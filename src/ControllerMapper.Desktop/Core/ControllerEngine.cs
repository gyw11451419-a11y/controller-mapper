using System.Diagnostics;
using ControllerMapper.Desktop.Output;

namespace ControllerMapper.Desktop.Core;

public sealed record EngineStatus(
    bool IsArmed, string StatusText, string SafetyText,
    string LiveInputText, string DeviceStatusText);

public sealed class ControllerEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly IControllerInputService _input;
    private readonly IVirtualGamepadOutput _output;
    private readonly SafetyMonitor _safety;
    private CancellationTokenSource? _cancellation;
    private DeviceDescriptor? _device;
    private Profile? _profile;
    private bool _triggerWasDown;
    private bool _requireTriggerRelease = true;
    private long _pressTimestamp;
    private IntPtr _lastForegroundWindow;
    private EngineStatus? _lastStatus;
    private bool _disposed;

    public event Action<EngineStatus>? StatusChanged;

    public ControllerEngine(IControllerInputService input, IVirtualGamepadOutput output, SafetyMonitor safety)
    {
        _input = input;
        _output = output;
        _safety = safety;
    }

    public bool IsArmed
    {
        get { lock (_gate) return _cancellation is not null; }
    }

    public int? VirtualXInputSlot
    {
        get { lock (_gate) return _output.XInputSlot; }
    }

    public void Arm(DeviceDescriptor device, Profile profile)
    {
        ProfileStore.Validate(new ProfileDocument { Profiles = [profile] });
        lock (_gate)
        {
            ThrowIfDisposed();
            HaltLocked("已停止", "手动停止", false);
            var assessment = _safety.Assess();
            if (assessment.RequiresManualResume)
                throw new InvalidOperationException(assessment.Description);

            try
            {
                _output.Connect();
                var virtualSlot = _output.XInputSlot
                    ?? throw new InvalidOperationException("无法确认虚拟 XInput 槽位，已阻止输入回环。");
                if (device.XInputSlot == virtualSlot)
                    throw new InvalidOperationException("选中的手柄是虚拟输出设备，已阻止输入回环。");
                var available = _input.Enumerate(virtualSlot);
                if (!available.Any(candidate => candidate.Id == device.Id))
                    throw new InvalidOperationException("所选实体手柄已断开或无法安全识别。");

                _device = device;
                _profile = profile;
                _triggerWasDown = false;
                _requireTriggerRelease = true;
                _pressTimestamp = 0;
                _lastForegroundWindow = assessment.ForegroundWindow;
                _output.Neutralize();
                _cancellation = new CancellationTokenSource();
                var token = _cancellation.Token;
                _ = Task.Run(() => RunAsync(token), token);
                PublishLocked(new EngineStatus(true, "全局映射已启用；连发须重新按下触发键",
                    assessment.Description, "无", $"{device.Name} → 虚拟 Xbox 360 (XInput {virtualSlot})"));
            }
            catch
            {
                _output.Neutralize();
                _output.Disconnect();
                _device = null;
                _profile = null;
                throw;
            }
        }
    }

    public void Stop(string reason = "用户已停止")
    {
        lock (_gate)
        {
            HaltLocked("已停止", reason, true);
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                lock (_gate)
                {
                    if (_cancellation is null || cancellation.IsCancellationRequested) break;
                    try { TickLocked(); }
                    catch (Exception exception)
                    {
                        HaltLocked("连发异常", $"{exception.GetType().Name}：输出已释放，需手动恢复", true);
                        break;
                    }
                }
                await Task.Delay(10, cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void TickLocked()
    {
        var device = _device!;
        var profile = _profile!;
        var assessment = _safety.Assess();
        if (assessment.RequiresManualResume)
        {
            HaltLocked("安全门关闭", assessment.Description, true);
            return;
        }

        if (!assessment.Allowed)
        {
            _output.Neutralize();
            _lastForegroundWindow = IntPtr.Zero;
            _requireTriggerRelease = true;
            _triggerWasDown = false;
            _pressTimestamp = 0;
            PublishLocked(new EngineStatus(true, "等待可识别的前台窗口", assessment.Description,
                "无", device.ToString()));
            return;
        }

        if (_lastForegroundWindow != assessment.ForegroundWindow)
        {
            _output.Neutralize();
            _lastForegroundWindow = assessment.ForegroundWindow;
            _requireTriggerRelease = true;
            _triggerWasDown = false;
            _pressTimestamp = 0;
            PublishLocked(new EngineStatus(true, "前台窗口已切换，输出已归零；连发请松开后重按",
                assessment.Description, "无", device.ToString()));
            return;
        }

        var snapshot = _input.Read(device);
        if (snapshot is null || !snapshot.Healthy)
        {
            HaltLocked("设备断开或读取失败", "输出已释放，需手动恢复", true);
            return;
        }

        var live = snapshot.Buttons.Count == 0
            ? "无"
            : string.Join("、", snapshot.Buttons.OrderBy(button => button).Select(button => button.ToString()));

        var triggerDown = snapshot.Buttons.Contains(profile.TurboTrigger);
        if (!triggerDown) _requireTriggerRelease = false;
        if (triggerDown && !_triggerWasDown && !_requireTriggerRelease)
            _pressTimestamp = Stopwatch.GetTimestamp();
        _triggerWasDown = triggerDown;

        var baseReport = ControllerReportComposer.Compose(snapshot, profile);
        var pulseOn = false;
        if (triggerDown && !_requireTriggerRelease && _pressTimestamp != 0)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(_pressTimestamp).TotalMilliseconds;
            var pulseWidthMs = Math.Min(30, profile.TurboIntervalMs / 2);
            if (elapsedMs % profile.TurboIntervalMs < pulseWidthMs)
                pulseOn = true;
        }

        var report = triggerDown
            ? ControllerReportComposer.ApplyTurboPulse(baseReport, profile.TurboTarget, pulseOn)
            : baseReport;
        _output.Submit(report);
        PublishLocked(new EngineStatus(true,
            profile.TurboTrigger == GamepadButton.None ? "映射运行中（未设置连发）" :
            triggerDown && !_requireTriggerRelease ? "单键连发运行中" : "映射运行中，等待按住触发键",
            assessment.Description, live, device.ToString()));
    }

    private void HaltLocked(string status, string safety, bool publish)
    {
        var prior = _cancellation;
        _cancellation = null;
        prior?.Cancel();
        try { _output.Neutralize(); }
        catch { /* A disconnected driver cannot receive neutral reports. */ }
        try { _output.Disconnect(); }
        catch { }
        _device = null;
        _profile = null;
        _pressTimestamp = 0;
        _lastForegroundWindow = IntPtr.Zero;
        _triggerWasDown = false;
        _requireTriggerRelease = true;
        if (publish)
            PublishLocked(new EngineStatus(false, status, safety, "无", "未连接虚拟输出"));
    }

    private void PublishLocked(EngineStatus status)
    {
        if (status == _lastStatus) return;
        _lastStatus = status;
        StatusChanged?.Invoke(status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ControllerEngine));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            HaltLocked("已停止", "应用正在关闭", false);
            _disposed = true;
            _output.Dispose();
            _input.Dispose();
        }
    }
}
