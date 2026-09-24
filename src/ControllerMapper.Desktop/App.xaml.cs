using System.IO;
using System.Windows;
using ControllerMapper.Desktop.Core;
using ControllerMapper.Desktop.Input;
using ControllerMapper.Desktop.Output;
using ControllerMapper.Desktop.UI;
using ControllerMapper.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ControllerMapper.Desktop;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var logDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDirectory, "controller-mapper-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();

            var registrations = new ServiceCollection();
            registrations.AddSingleton<ProfileStore>();
            registrations.AddSingleton<SafetyMonitor>();
            registrations.AddSingleton<IControllerInputService, WindowsControllerInputService>();
            registrations.AddSingleton<IVirtualGamepadOutput, ViGEmXbox360Output>();
            registrations.AddSingleton<ControllerEngine>();
            registrations.AddSingleton<MainViewModel>();
            registrations.AddSingleton<MainWindow>();
            _services = registrations.BuildServiceProvider();

            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI exception");
                _services.GetService<ControllerEngine>()?.Stop("应用异常，输出已释放");
                MessageBox.Show(args.Exception.Message, "Controller Mapper 发生错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application startup failed");
            MessageBox.Show(exception.Message, "Controller Mapper 启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _services?.GetService<ControllerEngine>()?.Dispose(); }
        finally
        {
            _services?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
