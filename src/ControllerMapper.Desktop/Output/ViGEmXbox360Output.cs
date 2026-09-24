using System.Runtime.InteropServices;
using ControllerMapper.Desktop.Core;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMapper.Desktop.Output;

/// <summary>
/// Feeds one virtual Xbox 360 controller through an already installed ViGEmBus.
/// This adapter never installs or updates a driver. The ViGEm software is retired;
/// this is an explicitly provisional backend pending distribution and hardware review.
/// </summary>
public sealed class ViGEmXbox360Output : IVirtualGamepadOutput
{
    private readonly object _sync = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool[]? _occupancyAtConnect;
    private bool _disposed;

    public bool IsConnected { get { lock (_sync) return _controller is not null; } }
    public int? XInputSlot { get; private set; }

    public void Connect()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_controller is not null)
            {
                return;
            }

            var before = ScanXInputSlots();
            if (before.All(occupied => occupied))
            {
                throw new InvalidOperationException("四个 XInput 插槽已满，无法安全识别虚拟手柄。");
            }

            ViGEmClient? client = null;
            IXbox360Controller? controller = null;
            var attached = false;
            try
            {
                client = new ViGEmClient();
                controller = client.CreateXbox360Controller();
                controller.AutoSubmitReport = false;
                controller.Connect();
                attached = true;
                controller.ResetReport();
                controller.SubmitReport();

                var slot = FindNewSlot(before);
                if (slot is null)
                {
                    throw new InvalidOperationException(
                        "无法唯一确认虚拟手柄的 XInput 插槽；已断开虚拟手柄以防输入回环。");
                }

                _client = client;
                _controller = controller;
                XInputSlot = slot;
                _occupancyAtConnect = ScanXInputSlots();
            }
            catch
            {
                if (attached && controller is not null)
                {
                    try { controller.ResetReport(); controller.SubmitReport(); } catch { }
                    try { controller.Disconnect(); } catch { }
                }

                try { (controller as IDisposable)?.Dispose(); } catch { }
                try { client?.Dispose(); } catch { }
                throw;
            }
        }
    }

    public void Submit(VirtualGamepadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.Buttons);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var controller = _controller ?? throw new InvalidOperationException("虚拟手柄尚未连接。");

            try
            {
                if (_occupancyAtConnect is null ||
                    !_occupancyAtConnect.SequenceEqual(ScanXInputSlots()))
                {
                    throw new InvalidOperationException(
                        "XInput 插槽占用状态已变化，虚拟手柄身份无法继续确认。");
                }

                ValidateAxes(report.Axes);
                controller.ResetReport();
                foreach (var button in report.Buttons)
                {
                    if (button is GamepadButton.None or GamepadButton.LeftTrigger or GamepadButton.RightTrigger)
                    {
                        continue;
                    }

                    controller.SetButtonState(MapButton(button), true);
                }

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(report.Axes.LeftX));
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxis(report.Axes.LeftY));
                controller.SetAxisValue(Xbox360Axis.RightThumbX, ToAxis(report.Axes.RightX));
                controller.SetAxisValue(Xbox360Axis.RightThumbY, ToAxis(report.Axes.RightY));
                controller.SetSliderValue(Xbox360Slider.LeftTrigger,
                    ToTrigger(report.Buttons.Contains(GamepadButton.LeftTrigger) ? 1f : report.Axes.LeftTrigger));
                controller.SetSliderValue(Xbox360Slider.RightTrigger,
                    ToTrigger(report.Buttons.Contains(GamepadButton.RightTrigger) ? 1f : report.Axes.RightTrigger));
                controller.SubmitReport();
            }
            catch
            {
                // Any invalid or failed update leaves the last submitted state unknown.
                // Best-effort neutral and device removal prevents a held output.
                DisconnectCore();
                throw;
            }
        }
    }

    public void Neutralize()
    {
        lock (_sync)
        {
            if (_controller is null)
            {
                return;
            }

            try
            {
                _controller.ResetReport();
                _controller.SubmitReport();
            }
            catch
            {
                DisconnectCore();
                throw;
            }
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            DisconnectCore();
        }
    }

    private void DisconnectCore()
    {
        var controller = _controller;
        var client = _client;
        _controller = null;
        _client = null;
        XInputSlot = null;
        _occupancyAtConnect = null;

        if (controller is not null)
        {
            try { controller.ResetReport(); controller.SubmitReport(); } catch { }
            try { controller.Disconnect(); } catch { }
            try { (controller as IDisposable)?.Dispose(); } catch { }
        }

        try { client?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisconnectCore();
        }
    }

    private static int? FindNewSlot(bool[] before)
    {
        // XInput provides no device identity. Require one stable newly occupied slot,
        // with no simultaneous removal; any ambiguity closes this backend.
        var deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline)
        {
            var after = ScanXInputSlots();
            if (before.Where((occupied, slot) => occupied && !after[slot]).Any())
            {
                return null;
            }

            var candidates = Enumerable.Range(0, 4)
                .Where(slot => !before[slot] && after[slot]).ToArray();
            if (candidates.Length > 1)
            {
                return null;
            }

            if (candidates.Length == 1)
            {
                Thread.Sleep(40);
                var confirm = ScanXInputSlots();
                if (after.SequenceEqual(confirm))
                {
                    return candidates[0];
                }
            }

            Thread.Sleep(40);
        }

        return null;
    }

    private static bool[] ScanXInputSlots()
    {
        var occupied = new bool[4];
        for (var slot = 0; slot < 4; slot++)
        {
            var result = XInputGetState((uint)slot, out _);
            if (result == 0)
            {
                occupied[slot] = true;
            }
            else if (result != 1167) // ERROR_DEVICE_NOT_CONNECTED
            {
                throw new InvalidOperationException($"XInput 插槽状态不可用（错误 {result}）。");
            }
        }

        return occupied;
    }

    private static Xbox360Button MapButton(GamepadButton button) => button switch
    {
        GamepadButton.A => Xbox360Button.A,
        GamepadButton.B => Xbox360Button.B,
        GamepadButton.X => Xbox360Button.X,
        GamepadButton.Y => Xbox360Button.Y,
        GamepadButton.LeftShoulder => Xbox360Button.LeftShoulder,
        GamepadButton.RightShoulder => Xbox360Button.RightShoulder,
        GamepadButton.Back => Xbox360Button.Back,
        GamepadButton.Start => Xbox360Button.Start,
        GamepadButton.LeftStick => Xbox360Button.LeftThumb,
        GamepadButton.RightStick => Xbox360Button.RightThumb,
        GamepadButton.DPadUp => Xbox360Button.Up,
        GamepadButton.DPadDown => Xbox360Button.Down,
        GamepadButton.DPadLeft => Xbox360Button.Left,
        GamepadButton.DPadRight => Xbox360Button.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button,
            "Xbox 360 虚拟手柄没有此按钮，不能输出独立背键。")
    };

    private static void ValidateAxes(GamepadAxes axes)
    {
        if (!float.IsFinite(axes.LeftX) || !float.IsFinite(axes.LeftY) ||
            !float.IsFinite(axes.RightX) || !float.IsFinite(axes.RightY) ||
            !float.IsFinite(axes.LeftTrigger) || !float.IsFinite(axes.RightTrigger))
        {
            throw new ArgumentOutOfRangeException(nameof(axes), "摇杆与扳机值必须为有限数。");
        }
    }

    private static short ToAxis(float value)
    {
        var clamped = Math.Clamp(value, -1f, 1f);
        return (short)MathF.Round(clamped < 0 ? clamped * 32768f : clamped * 32767f);
    }

    private static byte ToTrigger(float value) =>
        (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftX;
        public short LeftY;
        public short RightX;
        public short RightY;
    }
}
