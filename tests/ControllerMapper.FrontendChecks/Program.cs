using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ControllerMapper.Desktop;
using ControllerMapper.Desktop.Core;
using ControllerMapper.Desktop.Output;
using ControllerMapper.Desktop.UI;
using ControllerMapper.Desktop.ViewModels;

internal static class Program
{
    private static readonly string Root = Path.GetFullPath("artifacts/frontend-v2-qa");
    private static int checks;
    [STAThread]
    private static int Main()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("CONTROLLER_MAPPER_DATA_DIRECTORY", Path.Combine(Root, "data-" + Guid.NewGuid().ToString("N")));
            var bindingLog = new StringWriter();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingLog));
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            var app = new App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var input = new FakeInput();
            var output = new FakeOutput();
            using var engine = new ControllerEngine(input, output, new SafetyMonitor());
            var store = new ProfileStore();
            var vm = new MainViewModel(store, input, engine);
            var legacyFile = Path.Combine(Root, "legacy-v1.json");
            File.WriteAllText(legacyFile, """
                {"SchemaVersion":1,"Profiles":[{"Id":"legacy","Name":"旧配置","ExecutablePath":"C:\\Games\\Old.exe"}]}
                """);
            var migrated = store.ReadDocument(legacyFile);
            Check(migrated.SchemaVersion == 2 && migrated.Profiles[0].Name == "旧配置", "V1 profile migrates to global schema");
            var migratedExport = Path.Combine(Root, "legacy-v2.json");
            store.ExportSingleProfile(migrated.Profiles[0], migratedExport);
            Check(!File.ReadAllText(migratedExport).Contains("ExecutablePath"), "Global export removes obsolete application path");
            var simulatedDevice = new DeviceDescriptor("test", "测试手柄", "Xbox", "XInput", "模拟设备", 0);
            var buttonSwap = new Profile { Mappings = [new MappingEntry { Source = GamepadButton.A, Target = GamepadButton.B }] };
            var swapped = ControllerReportComposer.Compose(
                new ControllerSnapshot(simulatedDevice, new HashSet<GamepadButton> { GamepadButton.A }, GamepadAxes.Neutral, true), buttonSwap);
            Check(swapped.Buttons.SetEquals([GamepadButton.B]), "Ordinary button remapping replaces virtual output");
            var analogueTrigger = new ControllerSnapshot(simulatedDevice,
                new HashSet<GamepadButton> { GamepadButton.LeftTrigger },
                GamepadAxes.Neutral with { LeftTrigger = 0.75f }, true);
            var passthrough = ControllerReportComposer.Compose(analogueTrigger, new Profile());
            Check(passthrough.Buttons.Count == 0 && passthrough.Axes.LeftTrigger == 0.75f,
                "Unmapped trigger keeps analogue travel");
            var triggerMap = new Profile { Mappings = [new MappingEntry { Source = GamepadButton.LeftTrigger, Target = GamepadButton.A }] };
            ProfileStore.Validate(new ProfileDocument { Profiles = [triggerMap] });
            var remappedTrigger = ControllerReportComposer.Compose(analogueTrigger, triggerMap);
            Check(remappedTrigger.Buttons.SetEquals([GamepadButton.A]) && remappedTrigger.Axes.LeftTrigger == 0f,
                "Mapped trigger emits button and suppresses original virtual trigger");
            var belowThreshold = ControllerReportComposer.Compose(analogueTrigger with
            {
                Buttons = new HashSet<GamepadButton>(), Axes = GamepadAxes.Neutral with { LeftTrigger = 0.25f }
            }, triggerMap);
            Check(belowThreshold.Buttons.Count == 0 && belowThreshold.Axes.LeftTrigger == 0f,
                "Mapped trigger does not leak partial analogue input");
            var triggerTarget = new Profile { Mappings = [new MappingEntry { Source = GamepadButton.A, Target = GamepadButton.RightTrigger }] };
            ProfileStore.Validate(new ProfileDocument { Profiles = [triggerTarget] });
            var toTrigger = ControllerReportComposer.Compose(
                new ControllerSnapshot(simulatedDevice, new HashSet<GamepadButton> { GamepadButton.A }, GamepadAxes.Neutral, true), triggerTarget);
            Check(toTrigger.Buttons.Contains(GamepadButton.RightTrigger), "Button can map to virtual trigger output");
            var noPulse = ControllerReportComposer.ApplyTurboPulse(passthrough, GamepadButton.LeftTrigger, false);
            var pulse = ControllerReportComposer.ApplyTurboPulse(passthrough, GamepadButton.LeftTrigger, true);
            Check(noPulse.Axes.LeftTrigger == 0f && !noPulse.Buttons.Contains(GamepadButton.LeftTrigger) &&
                  pulse.Axes.LeftTrigger == 0f && pulse.Buttons.Contains(GamepadButton.LeftTrigger),
                  "Turbo trigger target releases between pulses");
            var window = new MainWindow(vm);
            var content = (FrameworkElement)window.Content;
            content.DataContext = vm;
            Check(!vm.HasDevice && !vm.ArmCommand.CanExecute(null), "No device cannot arm");
            Check(!vm.HasUnsavedChanges, "Initial configuration clean");
            Render(content, 1260, 760, "01-home.png");
            vm.NavigateCommand.Execute("Mapping");
            Render(content, 1260, 760, "02-controller.png");
            Check(Descendants<Button>(content).Count(b => b.Tag is GamepadButton) == 18, "All 18 controller callouts render, including triggers");
            var buttonX = Descendants<Button>(content).Single(b => Equals(b.Tag, GamepadButton.X));
            Check(buttonX.Command is not null, "Schematic command bound");
            ((IInvokeProvider)new ButtonAutomationPeer(buttonX).GetPattern(PatternInterface.Invoke)!).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(vm.NewMappingSource == GamepadButton.X && vm.IsMappingEditorOpen && !vm.IsWorkspaceEnabled, "Callout opens modal editor");
            Check(Descendants<Button>(content).Any(button => Equals(button.Content, "添加 / 修改键位")),
                  "Visible remapping entry exists alongside controller callouts");
            var oldShift = vm.ShiftButton;
            vm.EditorShiftButton = GamepadButton.RightShoulder;
            vm.CloseOverlayCommand.Execute(null);
            Check(vm.ShiftButton == oldShift && vm.IsWorkspaceEnabled, "Cancel leaves Shift configuration unchanged");
            vm.NewMappingTarget = GamepadButton.A;
            vm.AddMappingCommand.Execute(null);
            Check(vm.Mappings.Count == 1 && vm.HasUnsavedChanges, "Add mapping and dirty flag");
            vm.AddMappingCommand.Execute(null);
            Check(vm.Mappings.Count == 1 && vm.MappingFeedback.Contains("无法添加"), "Duplicate mapping rejected inline");
            vm.NewMappingSource = GamepadButton.Y;
            vm.NewMappingTarget = GamepadButton.B;
            vm.NewMappingShifted = true;
            vm.AddMappingCommand.Execute(null);
            Check(vm.Mappings.Count == 1 && vm.MappingFeedback.Contains("Shift"), "Shift requires modifier");
            vm.ShiftButton = GamepadButton.LeftShoulder;
            vm.NewMappingSecondTarget = GamepadButton.RightShoulder;
            vm.AddMappingCommand.Execute(null);
            Check(vm.Mappings.Count == 2, "Shift combination mapping added");
            var original = vm.SelectedProfile!;
            vm.DuplicateProfileCommand.Execute(null);
            Check(vm.Profiles.Count == 2 && vm.Mappings.Count == 2 && !ReferenceEquals(original.Mappings[0], vm.SelectedProfile!.Mappings[0]), "Duplicate deep copies mappings");
            vm.ProfileName = "动作游戏 · 自定义";
            vm.NewMappingShifted = false;
            vm.NewMappingSecondTarget = null;
            vm.NewMappingSource = GamepadButton.A;
            vm.NewMappingTarget = GamepadButton.B;
            vm.AddMappingCommand.Execute(null);
            vm.SaveProfileCommand.Execute(null);
            Check(!vm.HasUnsavedChanges && store.ReadDocument(store.StoragePath).Profiles[1].Name == vm.ProfileName, "Profile name and mappings persist");
            vm.SelectSourceCommand.Execute(GamepadButton.X);
            vm.NewMappingTarget = GamepadButton.Y;
            vm.ApplyMappingCommand.Execute(null);
            Check(vm.Mappings.Count == 3 && vm.Mappings.Single(m => m.Source == GamepadButton.X).Target == GamepadButton.Y && !vm.IsMappingEditorOpen, "Editor replaces existing source without duplicates");
            vm.SelectSourceCommand.Execute(GamepadButton.X);
            vm.EditorShiftButton = GamepadButton.None;
            vm.ApplyMappingCommand.Execute(null);
            Check(vm.IsMappingEditorOpen && vm.ShiftButton == GamepadButton.LeftShoulder, "Cannot orphan existing Shift mappings");
            vm.EditorShiftButton = GamepadButton.LeftShoulder;
            vm.NewMappingSecondTarget = GamepadButton.Y;
            vm.ApplyMappingCommand.Execute(null);
            Check(vm.IsMappingEditorOpen && vm.Mappings.Count == 3 && vm.Mappings.Single(m => m.Source == GamepadButton.X).SecondTarget is null, "Invalid editor change leaves prior mapping intact");
            Render(content, 1260, 760, "03-mapping-editor.png");
            vm.CloseOverlayCommand.Execute(null);
            vm.ShowMappingsCommand.Execute(null);
            Render(content, 1260, 760, "04-mapping-list.png");
            vm.CloseOverlayCommand.Execute(null);
            vm.TurboTrigger = GamepadButton.RightPaddle;
            Check(vm.HasValidationError && !vm.SaveProfileCommand.CanExecute(null), "Incomplete turbo blocks saving");
            vm.TurboTarget = GamepadButton.DPadUp;
            Check(!vm.HasValidationError, "Valid turbo pair clears warning");
            var mappingCountBeforeConflict = vm.Mappings.Count;
            vm.SelectSourceCommand.Execute(GamepadButton.B);
            vm.NewMappingTarget = GamepadButton.DPadUp;
            vm.ApplyMappingCommand.Execute(null);
            Check(vm.IsMappingEditorOpen && vm.Mappings.Count == mappingCountBeforeConflict &&
                  vm.MappingFeedback.Contains("连发目标"), "Mapping conflict names the occupied turbo output");
            vm.CloseOverlayCommand.Execute(null);
            vm.TurboIntervalMs = 50;
            Check(vm.TurboRateText.Contains("20"), "Rate display follows interval");
            vm.TurboIntervalMs = 120;
            vm.NavigateCommand.Execute("Turbo");
            Render(content, 1260, 760, "05-turbo.png");
            vm.DisableTurboCommand.Execute(null);
            Check(vm.TurboTrigger == GamepadButton.None && vm.TurboTarget == GamepadButton.None && !vm.HasValidationError, "Disable turbo resets both fields");
            vm.NavigateCommand.Execute("Device");
            Render(content, 1000, 660, "06-small-device.png");
            vm.NavigateCommand.Execute("Mapping");
            Render(content, 1000, 660, "07-small-mapping.png");
            vm.NavigateCommand.Execute("Profiles");
            Render(content, 1260, 760, "08-profiles.png");
            vm.GoBackCommand.Execute(null);
            Check(vm.CurrentPage == "Home", "Back returns to profile hub");
            vm.RemoveMappingCommand.Execute(vm.Mappings[0]);
            Check(vm.Mappings.Count == 2 && original.Mappings.Count == 2, "Removal isolated to current profile");
            vm.ProfileName = "";
            Check(vm.HasValidationError && !vm.SaveProfileCommand.CanExecute(null), "Blank profile name rejected");
            vm.ProfileName = "动作游戏 · 自定义";
            input.Connected = true;
            vm.RefreshDevicesCommand.Execute(null);
            Check(vm.HasDevice && vm.ArmCommand.CanExecute(null), "Arm available with device and valid global config");
            vm.NewProfileCommand.Execute(null);
            Check(vm.Mappings.Count == 0 && vm.Profiles.Count == 3, "New profile empty");
            vm.NavigateCommand.Execute("Mapping");
            Render(content, 1260, 760, "10-remap-entry.png");
            var remapButton = Descendants<Button>(content).Single(button => Equals(button.Content, "添加 / 修改键位"));
            ((IInvokeProvider)new ButtonAutomationPeer(remapButton).GetPattern(PatternInterface.Invoke)!).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(vm.IsMappingEditorOpen && vm.NewMappingSource == GamepadButton.A, "Visible remap button opens editor");
            vm.NewMappingTarget = GamepadButton.B;
            var confirmMapping = Descendants<Button>(content).Single(button => Equals(button.Content, "确认映射"));
            ((IInvokeProvider)new ButtonAutomationPeer(confirmMapping).GetPattern(PatternInterface.Invoke)!).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!vm.IsMappingEditorOpen && vm.Mappings.Count == 1 && vm.Mappings[0].Target == GamepadButton.B,
                  "Visible editor confirms ordinary button remapping");
            var newProfileId = vm.SelectedProfile!.Id;
            vm.SaveProfileCommand.Execute(null);
            Check(vm.Profiles.Count == 3 && vm.SavedProfileSummary.Contains("已保存 3 份配置") &&
                  store.ReadDocument(store.StoragePath).SelectedProfileId == newProfileId,
                  "Saved profile is immediately listed and selected");
            Check(vm.SelectedDevice?.Kind == "Xbox" && vm.InputButtonOptions.Single(option => option.Button == GamepadButton.A).Label == "A",
                  "Xbox input shows Xbox labels");
            Check(vm.InputButtonOptions.Single(option => option.Button == GamepadButton.LeftTrigger).Label.StartsWith("LT"),
                  "Xbox trigger is selectable");
            input.SonyConnected = true;
            PumpFor(TimeSpan.FromSeconds(2.3));
            Check(vm.SelectedDevice?.Kind == "DualSense" && vm.InputButtonOptions.Single(option => option.Button == GamepadButton.A).Label.StartsWith("✕"),
                  "Automatic hotplug scan selects PlayStation input labels");
            vm.NavigateCommand.Execute("Mapping");
            Render(content, 1260, 760, "09-dualsense-mapping.png");
            Check(Equals(Descendants<Button>(content).Single(button => Equals(button.Tag, GamepadButton.A)).Content, "✕ · 叉键"),
                  "Controller callout follows detected device");
            Check(Equals(Descendants<Button>(content).Single(button => Equals(button.Tag, GamepadButton.LeftTrigger)).Content, "L2 · 左扳机"),
                  "DualSense trigger callout follows detected device");
            vm.SelectedDevice = vm.Devices.Single(device => device.Kind == "Xbox");
            vm.RefreshDevicesCommand.Execute(null);
            Check(vm.SelectedDevice?.Kind == "Xbox", "Explicit device choice survives refresh");
            input.Connected = false;
            vm.RefreshDevicesCommand.Execute(null);
            Check(vm.SelectedDevice?.Kind == "DualSense", "Disconnected device falls back to connected controller");
            vm.Dispose();
            using var reloaded = new MainViewModel(store, input, engine);
            Check(reloaded.SelectedProfile?.Id == newProfileId && reloaded.Profiles.Count == 3,
                  "Saved selection restores after reopening");
            Check(output.ConnectCount == 0, "Frontend tests never connect virtual driver");
            PresentationTraceSources.DataBindingSource.Flush();
            File.WriteAllText(Path.Combine(Root, "bindings.log"), bindingLog.ToString());
            Check(string.IsNullOrWhiteSpace(bindingLog.ToString()), "No binding errors or warnings");
            File.WriteAllText(Path.Combine(Root, "result.txt"), $"PASS: {checks} checks; 10 WPF renders; no hardware output.\n");
            Console.WriteLine($"PASS: {checks} checks; 10 WPF renders; no hardware output.");
            app.Shutdown();
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(Root, "failure.txt"), exception.ToString()); Console.Error.WriteLine(exception); return 1; }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement content, int width, int height, string name)
    {
        content.Width = width; content.Height = height;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Root, name)); encoder.Save(stream);
    }
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private sealed class FakeInput : IControllerInputService
    {
        public bool Connected { get; set; }
        public bool SonyConnected { get; set; }
        public IReadOnlyList<DeviceDescriptor> Enumerate(int? excluded)
        {
            var devices = new List<DeviceDescriptor>();
            if (Connected) devices.Add(new("test-xbox", "测试 Xbox 手柄", "Xbox", "XInput", "模拟设备，仅用于界面验证。", 0));
            if (SonyConnected) devices.Add(new("test-sony", "测试 DualSense 手柄", "DualSense", "HID", "模拟设备，仅用于界面验证。", null));
            return devices;
        }
        public ControllerSnapshot? Read(DeviceDescriptor device) => null;
        public void Dispose() { }
    }
    private sealed class FakeOutput : IVirtualGamepadOutput
    {
        public int ConnectCount;
        public bool IsConnected => false;
        public int? XInputSlot => null;
        public void Connect() { ConnectCount++; throw new InvalidOperationException("Hardware forbidden in UI test"); }
        public void Submit(VirtualGamepadReport report) => throw new InvalidOperationException("Hardware forbidden in UI test");
        public void Neutralize() { }
        public void Disconnect() { }
        public void Dispose() { }
    }
}


