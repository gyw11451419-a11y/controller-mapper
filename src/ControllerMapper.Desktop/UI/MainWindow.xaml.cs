using ControllerMapper.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace ControllerMapper.Desktop.UI;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x5B, 0x9D, 0xEB), ApplicationTheme.Dark, false);
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsMappingEditorOpen) && viewModel.IsMappingEditorOpen)
                Dispatcher.BeginInvoke(new Action(() => MappingSourceSelector.Focus()));
            if (args.PropertyName == nameof(MainViewModel.IsMappingListOpen) && viewModel.IsMappingListOpen)
                Dispatcher.BeginInvoke(new Action(() => MappingListClose.Focus()));
        };
        Closing += (_, args) => ConfirmUnsavedChanges(viewModel, args);
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;

    private void OnToggleMaximizeWindow(object sender, RoutedEventArgs args) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object sender, RoutedEventArgs args) => Close();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed) return;
        // Interactive header controls keep their own click handling.
        for (var element = args.OriginalSource as DependencyObject;
             element is not null && element != WindowDragRegion;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is ButtonBase or TextBoxBase or Selector or RangeBase) return;
        }

        args.Handled = true;
        if (args.ClickCount == 2)
        {
            OnToggleMaximizeWindow(sender, args);
            return;
        }
        DragMove();
    }

    private void ConfirmUnsavedChanges(MainViewModel viewModel, CancelEventArgs args)
    {
        if (!viewModel.HasUnsavedChanges) return;
        var choice = MessageBox.Show(this, "配置有尚未保存的修改。是否保存后退出？",
            "保存配置", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) args.Cancel = true;
        else if (choice == MessageBoxResult.Yes)
        {
            viewModel.SaveProfileCommand.Execute(null);
            args.Cancel = viewModel.HasUnsavedChanges;
        }
    }
}

