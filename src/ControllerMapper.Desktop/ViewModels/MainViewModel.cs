using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerMapper.Desktop.Core;
using Microsoft.Win32;
using Serilog;

namespace ControllerMapper.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProfileStore _store;
    private readonly IControllerInputService _input;
    private readonly ControllerEngine _engine;
    private readonly DispatcherTimer _deviceRefreshTimer;
    private bool _automaticDeviceSelection;
    private bool _deviceSelectionExplicit;
    private DeviceDescriptor? _selectedDevice;
    private Profile? _selectedProfile;
    private GamepadButton _turboTrigger;
    private GamepadButton _turboTarget;
    private GamepadButton _shiftButton;
    private int _turboIntervalMs = 120;
    private string _statusText = "选择手柄与全局配置后手动启用。";
    private string _safetyText = "未启用";
    private string _capabilityText = "尚未选择设备。背键能力取决于设备和连接方式。";
    private string _deviceStatusText = "正在搜索设备…";
    private string _liveInputText = "无";
    private bool _isArmed;
    private GamepadButton _newMappingSource = GamepadButton.A;
    private GamepadButton _newMappingTarget = GamepadButton.B;
    private GamepadButton? _newMappingSecondTarget;
    private bool _newMappingShifted;
    private bool _loadingProfile;
    private bool _hasUnsavedChanges;
    private string _validationMessage = string.Empty;
    private string _mappingFeedback = string.Empty;
    private string _currentPage = "Home";
    private bool _isMappingEditorOpen;
    private bool _isMappingListOpen;
    private GamepadButton _editorShiftButton;

    public ObservableCollection<DeviceDescriptor> Devices { get; } = [];
    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<MappingEntry> Mappings { get; } = [];
    public ObservableCollection<GamepadButton> Buttons { get; } =
        new(Enum.GetValues<GamepadButton>().Where(button => button != GamepadButton.None));
    public ObservableCollection<GamepadButton> OptionalButtons { get; } = new(Enum.GetValues<GamepadButton>());
    public ObservableCollection<GamepadButton> OptionalOutputButtons { get; } =
        new(Enum.GetValues<GamepadButton>().Where(button => ProfileStore.IsOutput(button, allowNone: true)));
    public ObservableCollection<GamepadButton> OutputButtons { get; } =
        new(Enum.GetValues<GamepadButton>().Where(button => ProfileStore.IsOutput(button)));
    public ObservableCollection<InputButtonOption> InputButtonOptions { get; } =
        new(Enum.GetValues<GamepadButton>().Where(button => button != GamepadButton.None).Select(button => new InputButtonOption(button)));
    public ObservableCollection<InputButtonOption> OptionalInputButtonOptions { get; } =
        new(Enum.GetValues<GamepadButton>().Select(button => new InputButtonOption(button)));

    public IRelayCommand RefreshDevicesCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand ImportProfileCommand { get; }
    public IRelayCommand ExportProfileCommand { get; }
    public IRelayCommand ArmCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand AddMappingCommand { get; }
    public IRelayCommand<MappingEntry?> RemoveMappingCommand { get; }
    public IRelayCommand ClearSecondTargetCommand { get; }
    public IRelayCommand NewProfileCommand { get; }
    public IRelayCommand DuplicateProfileCommand { get; }
    public IRelayCommand DisableTurboCommand { get; }
    public IRelayCommand<GamepadButton> SelectSourceCommand { get; }
    public IRelayCommand<string> NavigateCommand { get; }
    public IRelayCommand GoBackCommand { get; }
    public IRelayCommand CloseOverlayCommand { get; }
    public IRelayCommand ShowMappingsCommand { get; }
    public IRelayCommand ApplyMappingCommand { get; }

    public MainViewModel(ProfileStore store, IControllerInputService input, ControllerEngine engine)
    {
        _store = store;
        _input = input;
        _engine = engine;
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        SaveProfileCommand = new RelayCommand(SaveProfile, () => !HasValidationError);
        ImportProfileCommand = new RelayCommand(ImportProfile);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => !HasValidationError);
        ArmCommand = new RelayCommand(Arm, () => SelectedDevice is not null && SelectedProfile is not null && !HasValidationError && !IsArmed);
        StopCommand = new RelayCommand(() => _engine.Stop("用户已手动停止"));
        AddMappingCommand = new RelayCommand(AddMapping);
        RemoveMappingCommand = new RelayCommand<MappingEntry?>(RemoveMapping);
        ClearSecondTargetCommand = new RelayCommand(() => NewMappingSecondTarget = null);
        NewProfileCommand = new RelayCommand(() => CreateProfile(false));
        DuplicateProfileCommand = new RelayCommand(() => CreateProfile(true));
        DisableTurboCommand = new RelayCommand(DisableTurbo);
        SelectSourceCommand = new RelayCommand<GamepadButton>(OpenMappingEditor);
        NavigateCommand = new RelayCommand<string>(page =>
        {
            if (page is not ("Home" or "Mapping" or "Turbo" or "Profiles" or "Device")) return;
            CurrentPage = page;
            CloseOverlays();
        });
        GoBackCommand = new RelayCommand(() => { CurrentPage = "Home"; CloseOverlays(); });
        CloseOverlayCommand = new RelayCommand(CloseOverlays);
        ShowMappingsCommand = new RelayCommand(() => { CloseOverlays(); IsMappingListOpen = true; });
        ApplyMappingCommand = new RelayCommand(ApplyMapping);
        _engine.StatusChanged += OnEngineStatusChanged;

        string? lastSelectedProfileId = null;
        try
        {
            var document = _store.LoadOrCreate();
            foreach (var profile in document.Profiles) Profiles.Add(profile);
            lastSelectedProfileId = document.SelectedProfileId;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to load profile document");
            Profiles.Add(new Profile());
            StatusText = "配置文件读取失败；已加载临时默认配置。请检查 JSON 文件。";
        }
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == lastSelectedProfileId) ?? Profiles[0];
        UpdateInputLabels();
        RefreshDevices();
        _deviceRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _deviceRefreshTimer.Tick += OnDeviceRefreshTick;
        _deviceRefreshTimer.Start();
    }

    public DeviceDescriptor? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                if (!_automaticDeviceSelection) _deviceSelectionExplicit = value is not null;
                if (_engine.IsArmed) _engine.Stop("设备已切换，输出已释放");
                CapabilityText = value?.Capability ?? "尚未选择设备。背键能力取决于设备和连接方式。";
                DeviceStatusText = value is null ? "未选择输入设备" : $"{value.Name} · {value.Connection}";
                UpdateInputLabels();
                OnPropertyChanged(nameof(InputLayoutText));
                OnPropertyChanged(nameof(HasDevice));
                ArmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                if (_engine.IsArmed) _engine.Stop("配置已切换，输出已释放");
                LoadProfile(value);
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(SavedProfileSummary));
                ValidateProfile();
            }
        }
    }

    public GamepadButton TurboTrigger
    {
        get => _turboTrigger;
        set { if (SetProperty(ref _turboTrigger, value)) UpdateProfile(profile => profile.TurboTrigger = value); }
    }

    public GamepadButton TurboTarget
    {
        get => _turboTarget;
        set { if (SetProperty(ref _turboTarget, value)) UpdateProfile(profile => profile.TurboTarget = value); }
    }

    public GamepadButton ShiftButton
    {
        get => _shiftButton;
        set { if (SetProperty(ref _shiftButton, value)) UpdateProfile(profile => profile.ShiftButton = value); }
    }

    public int TurboIntervalMs
    {
        get => _turboIntervalMs;
        set
        {
            value = Math.Clamp(value, ProfileStore.MinimumTurboIntervalMs, ProfileStore.MaximumTurboIntervalMs);
            if (SetProperty(ref _turboIntervalMs, value))
            {
                UpdateProfile(profile => profile.TurboIntervalMs = value);
                OnPropertyChanged(nameof(TurboRateText));
            }
        }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string SafetyText { get => _safetyText; private set => SetProperty(ref _safetyText, value); }
    public string CapabilityText { get => _capabilityText; private set => SetProperty(ref _capabilityText, value); }
    public string DeviceStatusText { get => _deviceStatusText; private set => SetProperty(ref _deviceStatusText, value); }
    public string LiveInputText { get => _liveInputText; private set => SetProperty(ref _liveInputText, value); }
    public bool IsArmed
    {
        get => _isArmed;
        private set { if (SetProperty(ref _isArmed, value)) ArmCommand.NotifyCanExecuteChanged(); }
    }
    public bool HasDevice => SelectedDevice is not null;
    public string CurrentPage
    {
        get => _currentPage;
        private set { if (SetProperty(ref _currentPage, value)) OnPropertyChanged(nameof(PageTitle)); }
    }
    public string PageTitle => CurrentPage switch
    {
        "Mapping" => "自定义按键配置", "Turbo" => "单键连发", "Profiles" => "管理配置文件",
        "Device" => "设备与运行", _ => "编辑您的配置文件"
    };
    public bool IsMappingEditorOpen { get => _isMappingEditorOpen; private set { if (SetProperty(ref _isMappingEditorOpen, value)) OnPropertyChanged(nameof(IsWorkspaceEnabled)); } }
    public bool IsMappingListOpen { get => _isMappingListOpen; private set { if (SetProperty(ref _isMappingListOpen, value)) OnPropertyChanged(nameof(IsWorkspaceEnabled)); } }
    public bool IsWorkspaceEnabled => !IsMappingEditorOpen && !IsMappingListOpen;
    public GamepadButton EditorShiftButton { get => _editorShiftButton; set => SetProperty(ref _editorShiftButton, value); }
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set { if (SetProperty(ref _hasUnsavedChanges, value)) OnPropertyChanged(nameof(SavedProfileSummary)); }
    }
    public string SavedProfileSummary => HasUnsavedChanges
        ? $"已创建 {Profiles.Count} 份配置 · 当前「{ProfileName}」有未保存修改"
        : $"已保存 {Profiles.Count} 份配置 · 当前：{ProfileName}";
    public string InputLayoutText => ControllerButtonLabels.InputLayout(SelectedDevice);
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationMessage);
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value)) OnPropertyChanged(nameof(HasValidationError));
            ArmCommand.NotifyCanExecuteChanged();
            SaveProfileCommand.NotifyCanExecuteChanged();
            ExportProfileCommand.NotifyCanExecuteChanged();
        }
    }
    public string MappingFeedback { get => _mappingFeedback; private set => SetProperty(ref _mappingFeedback, value); }
    public string TurboRateText => $"约 {1000d / TurboIntervalMs:0.#} 次 / 秒";
    public string ProfileName
    {
        get => SelectedProfile?.Name ?? string.Empty;
        set { UpdateProfile(profile => profile.Name = value); OnPropertyChanged(); OnPropertyChanged(nameof(SavedProfileSummary)); }
    }
    public GamepadButton NewMappingSource { get => _newMappingSource; set => SetProperty(ref _newMappingSource, value); }
    public GamepadButton NewMappingTarget { get => _newMappingTarget; set => SetProperty(ref _newMappingTarget, value); }
    public GamepadButton? NewMappingSecondTarget { get => _newMappingSecondTarget; set => SetProperty(ref _newMappingSecondTarget, value); }
    public bool NewMappingShifted { get => _newMappingShifted; set => SetProperty(ref _newMappingShifted, value); }

    private void CloseOverlays() { IsMappingEditorOpen = false; IsMappingListOpen = false; }

    private void OpenMappingEditor(GamepadButton button)
    {
        CloseOverlays();
        CurrentPage = "Mapping";
        NewMappingSource = button;
        NewMappingShifted = false;
        var mapping = Mappings.FirstOrDefault(item => item.Source == button && !item.Shifted);
        NewMappingTarget = mapping?.Target ?? (ProfileStore.IsOutput(button) ? button : GamepadButton.A);
        NewMappingSecondTarget = mapping?.SecondTarget;
        EditorShiftButton = ShiftButton;
        MappingFeedback = string.Empty;
        IsMappingEditorOpen = true;
    }

    private void ApplyMapping()
    {
        if (SelectedProfile is null) return;
        if (NewMappingShifted && EditorShiftButton == GamepadButton.None)
        {
            MappingFeedback = "请先选择 Shift 按钮。";
            return;
        }
        var entry = new MappingEntry { Source = NewMappingSource, Target = NewMappingTarget,
            SecondTarget = NewMappingSecondTarget, Shifted = NewMappingShifted };
        if (entry.Source == TurboTrigger)
        {
            MappingFeedback = "此输入键已用作连发触发键；请先更改或关闭单键连发。";
            return;
        }
        if (entry.Source == EditorShiftButton)
        {
            MappingFeedback = "此输入键已用作 Shift 层切换键；请改选另一个输入键。";
            return;
        }
        if (TurboTarget != GamepadButton.None &&
            (entry.Target == TurboTarget || entry.SecondTarget == TurboTarget))
        {
            MappingFeedback = "此输出键已用作连发目标；请先更改或关闭单键连发。";
            return;
        }
        var updated = SelectedProfile.Mappings.Where(item => item.Source != entry.Source || item.Shifted != entry.Shifted).ToList();
        updated.Add(entry);
        var candidate = new Profile { Id = SelectedProfile.Id, Name = SelectedProfile.Name,
            ShiftButton = EditorShiftButton, TurboTrigger = TurboTrigger, TurboTarget = TurboTarget,
            TurboIntervalMs = TurboIntervalMs, Mappings = updated };
        if (candidate.ShiftButton == GamepadButton.None && updated.Any(item => item.Shifted))
        {
            MappingFeedback = "已有 Shift 层映射，请保留 Shift 按钮或先移除对应规则。";
            return;
        }
        try { ProfileStore.Validate(new ProfileDocument { Profiles = [candidate] }); }
        catch (Exception exception) { MappingFeedback = exception.Message; return; }
        if (_engine.IsArmed) _engine.Stop("映射已修改，输出已释放");
        ShiftButton = EditorShiftButton;
        SelectedProfile.Mappings = updated;
        Mappings.Clear();
        foreach (var mapping in updated) Mappings.Add(mapping);
        HasUnsavedChanges = true;
        ValidateProfile();
        StatusText = "按键配置已更新。保存配置后可在下次启动时继续使用。";
        CloseOverlays();
    }

    private void UpdateProfile(Action<Profile> change)
    {
        if (_loadingProfile || SelectedProfile is null) return;
        if (_engine.IsArmed) _engine.Stop("配置已修改，输出已释放");
        change(SelectedProfile);
        HasUnsavedChanges = true;
        ValidateProfile();
    }

    private void ValidateProfile()
    {
        try
        {
            ProfileStore.Validate(new ProfileDocument { Profiles = Profiles.ToList() });
            if (Profiles.Any(profile => profile.ShiftButton == GamepadButton.None && profile.Mappings.Any(mapping => mapping.Shifted)))
                throw new InvalidOperationException("存在 Shift 层映射，请先设置 Shift 按钮。");
            ValidationMessage = string.Empty;
        }
        catch (Exception exception) { ValidationMessage = exception.Message; }
    }

    private void CreateProfile(bool duplicate)
    {
        if (Profiles.Count >= 100) { StatusText = "最多支持 100 个配置。"; return; }
        var source = duplicate ? SelectedProfile : null;
        var profile = new Profile
        {
            Name = source is null ? $"新配置 {Profiles.Count + 1}" : (source.Name.Length > 75 ? source.Name[..75] : source.Name) + " 副本",
            ShiftButton = source?.ShiftButton ?? GamepadButton.None,
            TurboTrigger = source?.TurboTrigger ?? GamepadButton.None,
            TurboTarget = source?.TurboTarget ?? GamepadButton.None,
            TurboIntervalMs = source?.TurboIntervalMs ?? 120,
            Mappings = source?.Mappings.Select(item => new MappingEntry
            {
                Source = item.Source, Target = item.Target, SecondTarget = item.SecondTarget, Shifted = item.Shifted
            }).ToList() ?? []
        };
        Profiles.Add(profile);
        SelectedProfile = profile;
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(SavedProfileSummary));
        StatusText = duplicate ? "已复制全局配置，修改后请保存。" : "已新建全局配置，可直接设置映射。";
    }

    private void DisableTurbo()
    {
        TurboTrigger = GamepadButton.None;
        TurboTarget = GamepadButton.None;
        StatusText = "单键连发已关闭，普通映射保留。";
    }

    private void LoadProfile(Profile? profile)
    {
        _loadingProfile = true;
        try
        {
            TurboTrigger = profile?.TurboTrigger ?? GamepadButton.None;
            TurboTarget = profile?.TurboTarget ?? GamepadButton.None;
            TurboIntervalMs = profile?.TurboIntervalMs ?? 120;
            ShiftButton = profile?.ShiftButton ?? GamepadButton.None;
            Mappings.Clear();
            if (profile is not null)
                foreach (var mapping in profile.Mappings) Mappings.Add(mapping);
            MappingFeedback = string.Empty;
        }
        finally { _loadingProfile = false; }
    }

    private void RefreshDevices()
    {
        try
        {
            var results = _input.Enumerate(_engine.VirtualXInputSlot)
                .OrderBy(device => ControllerButtonLabels.IsDualSense(device) ? 0 : 1).ToList();
            _automaticDeviceSelection = true;
            try
            {
                var presentIds = results.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var index = Devices.Count - 1; index >= 0; index--)
                    if (!presentIds.Contains(Devices[index].Id)) Devices.RemoveAt(index);
                foreach (var device in results)
                    if (!Devices.Any(existing => string.Equals(existing.Id, device.Id, StringComparison.OrdinalIgnoreCase)))
                        Devices.Add(device);

                var current = SelectedDevice is null ? null :
                    Devices.FirstOrDefault(device => string.Equals(device.Id, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase));
                var preferred = Devices.FirstOrDefault(ControllerButtonLabels.IsDualSense) ?? Devices.FirstOrDefault();
                if (current is null || (!_deviceSelectionExplicit && !ControllerButtonLabels.IsDualSense(current) &&
                    ControllerButtonLabels.IsDualSense(preferred)))
                {
                    _deviceSelectionExplicit = false;
                    SelectedDevice = preferred;
                }
                else if (!ReferenceEquals(current, SelectedDevice)) SelectedDevice = current;
            }
            finally { _automaticDeviceSelection = false; }
            if (SelectedDevice is null)
                DeviceStatusText = "未找到兼容手柄；连接 Xbox 或 DualSense 手柄后会自动检查";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Device enumeration failed");
            _engine.Stop("设备枚举失败，输出已释放");
            DeviceStatusText = "设备枚举失败";
            SafetyText = "监测状态不明，已停止";
        }
    }

    private void SaveProfile()
    {
        ValidateProfile();
        if (HasValidationError) { StatusText = $"无法保存：{ValidationMessage}"; return; }
        try
        {
            _store.Save(new ProfileDocument { Profiles = Profiles.ToList(), SelectedProfileId = SelectedProfile?.Id });
            HasUnsavedChanges = false;
            MappingFeedback = string.Empty;
            StatusText = $"已保存「{ProfileName}」；可在配置文件列表中查看。";
        }
        catch (Exception exception) { ShowError("保存配置失败", exception); }
    }

    private void ImportProfile()
    {
        var dialog = new OpenFileDialog { Filter = "JSON 配置文件|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var profile = _store.ImportSingleProfile(dialog.FileName);
            _store.Save(new ProfileDocument { Profiles = Profiles.Append(profile).ToList(), SelectedProfileId = profile.Id });
            Profiles.Add(profile);
            SelectedProfile = profile;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(SavedProfileSummary));
            StatusText = "配置导入成功";
        }
        catch (Exception exception) { ShowError("导入配置失败", exception); }
    }

    private void ExportProfile()
    {
        if (SelectedProfile is null) return;
        ValidateProfile();
        if (HasValidationError) { StatusText = $"无法导出：{ValidationMessage}"; return; }
        var dialog = new SaveFileDialog { Filter = "JSON 配置文件|*.json", FileName = "controller-profile.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _store.ExportSingleProfile(SelectedProfile, dialog.FileName);
            StatusText = "配置导出成功";
        }
        catch (Exception exception) { ShowError("导出配置失败", exception); }
    }

    private void AddMapping()
    {
        if (SelectedProfile is null) return;
        if (NewMappingShifted && ShiftButton == GamepadButton.None)
        {
            MappingFeedback = "请先选择 Shift 按钮，再添加 Shift 层映射。";
            return;
        }
        var entry = new MappingEntry
        {
            Source = NewMappingSource,
            Target = NewMappingTarget,
            SecondTarget = NewMappingSecondTarget,
            Shifted = NewMappingShifted
        };
        if (_engine.IsArmed) _engine.Stop("映射正在修改，输出已释放");
        SelectedProfile.Mappings.Add(entry);
        try
        {
            ProfileStore.Validate(new ProfileDocument { Profiles = [SelectedProfile] });
            Mappings.Add(entry);
            if (_engine.IsArmed) _engine.Stop("映射已修改，输出已释放");
            HasUnsavedChanges = true;
            MappingFeedback = "映射已添加，保存后可在下次启动时继续使用。";
            ValidateProfile();
        }
        catch (Exception exception)
        {
            SelectedProfile.Mappings.Remove(entry);
            MappingFeedback = $"无法添加：{exception.Message}";
        }
    }

    private void RemoveMapping(MappingEntry? entry)
    {
        if (entry is null || SelectedProfile is null) return;
        if (_engine.IsArmed) _engine.Stop("映射已修改，输出已释放");
        SelectedProfile.Mappings.Remove(entry);
        Mappings.Remove(entry);
        HasUnsavedChanges = true;
        MappingFeedback = "已移除映射。";
        ValidateProfile();
    }

    private void Arm()
    {
        ValidateProfile();
        if (HasValidationError) { StatusText = $"无法启用：{ValidationMessage}"; return; }
        if (SelectedDevice is null || SelectedProfile is null)
        {
            StatusText = "请先选择手柄和配置";
            return;
        }
        try
        {
            _store.Save(new ProfileDocument { Profiles = Profiles.ToList(), SelectedProfileId = SelectedProfile.Id });
            HasUnsavedChanges = false;
            _engine.Arm(SelectedDevice, SelectedProfile);
        }
        catch (Exception exception) { ShowError("无法启用虚拟手柄", exception); }
    }

    private void OnEngineStatusChanged(EngineStatus state)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => OnEngineStatusChanged(state));
            return;
        }
        IsArmed = state.IsArmed;
        StatusText = state.StatusText;
        SafetyText = state.SafetyText;
        LiveInputText = state.LiveInputText;
        DeviceStatusText = state.DeviceStatusText;
    }

    private void ShowError(string title, Exception exception)
    {
        Log.Error(exception, "{Action} failed", title);
        StatusText = $"{title}：{exception.Message}";
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UpdateInputLabels()
    {
        foreach (var option in InputButtonOptions) option.Label = ControllerButtonLabels.InputLabel(option.Button, SelectedDevice);
        foreach (var option in OptionalInputButtonOptions) option.Label = ControllerButtonLabels.InputLabel(option.Button, SelectedDevice);
    }

    private void OnDeviceRefreshTick(object? sender, EventArgs e) => RefreshDevices();

    public void Dispose()
    {
        _deviceRefreshTimer.Stop();
        _deviceRefreshTimer.Tick -= OnDeviceRefreshTick;
        _engine.StatusChanged -= OnEngineStatusChanged;
    }
}
