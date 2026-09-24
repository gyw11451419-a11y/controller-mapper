using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using ControllerMapper.Desktop.Core;
using HidSharp;

namespace ControllerMapper.Desktop.Input;

/// <summary>
/// Polls XInput 1.4 slots and known DualSense HID report formats. HID report layouts are
/// based on Sony's hid-playstation driver; no Windows hardware/firmware combination is
/// claimed to have been validated by this implementation.
/// </summary>
public sealed class WindowsControllerInputService : IControllerInputService
{
    private const int SonyVendorId = 0x054C;
    private const int DualSenseProductId = 0x0CE6;
    private const int EdgeProductId = 0x0DF2;
    private const int HidReadTimeoutMs = 25;

    private readonly object _sync = new();
    private readonly Dictionary<string, HidDevice> _hidDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HidStream> _hidStreams = new(StringComparer.OrdinalIgnoreCase);
    private int? _excludedXInputSlot;
    private bool _disposed;

    public IReadOnlyList<DeviceDescriptor> Enumerate(int? excludedXInputSlot)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _excludedXInputSlot = excludedXInputSlot;
            var result = new List<DeviceDescriptor>();
            for (var slot = 0; slot < 4; slot++)
            {
                if (slot == excludedXInputSlot)
                {
                    continue;
                }

                try
                {
                    if (XInputGetState((uint)slot, out _) == 0)
                    {
                        result.Add(new DeviceDescriptor(
                            $"xinput:{slot}", $"Xbox 手柄 · 插槽 {slot + 1}", "Xbox", "XInput",
                            "XInput 仅标识插槽，无法证明该插槽是实体手柄；虚拟输出插槽必须排除。", slot));
                    }
                }
                catch (DllNotFoundException)
                {
                    break;
                }
                catch (EntryPointNotFoundException)
                {
                    break;
                }
            }

            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var hid in DeviceList.Local.GetHidDevices(SonyVendorId))
                {
                    try
                    {
                        if (hid.ProductID is not (DualSenseProductId or EdgeProductId))
                        {
                            continue;
                        }

                        // The known full gamepad reports are 64 bytes (USB) or 78 bytes
                        // (Bluetooth). Other interfaces/reports are deliberately omitted.
                        var reportLength = hid.GetMaxInputReportLength();
                        if (reportLength is not (64 or 78))
                        {
                            continue;
                        }

                        var path = hid.DevicePath;
                        if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                        {
                            continue;
                        }

                        _hidDevices[path] = hid;
                        var edge = hid.ProductID == EdgeProductId;
                        result.Add(new DeviceDescriptor(
                            path,
                            edge ? "DualSense Edge 手柄" : "DualSense 手柄",
                            edge ? "DualSense Edge" : "DualSense",
                            reportLength == 64 ? "USB（按 HID 报告长度推断）" : "蓝牙（按 HID 报告长度推断）",
                            edge
                                ? "协议定义独立背键位；此设备和固件尚需实测，只有完整 HID 报告中的独立位会作为背键。"
                                : "此型号无 Edge 独立背键；仅能识别报告中的逻辑按键。",
                            null));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (NotSupportedException) { }
                }

                foreach (var path in _hidDevices.Keys.Where(path => !seen.Contains(path)).ToArray())
                {
                    CloseStream(path);
                    _hidDevices.Remove(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return result;
        }
    }

    public ControllerSnapshot? Read(DeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            if (device.XInputSlot is int slot)
            {
                return ReadXInput(device, slot);
            }

            return ReadDualSense(device);
        }
    }

    private ControllerSnapshot? ReadXInput(DeviceDescriptor device, int slot)
    {
        if (slot is < 0 or > 3 || slot == _excludedXInputSlot ||
            device.Id != $"xinput:{slot}")
        {
            return null;
        }

        try
        {
            if (XInputGetState((uint)slot, out var state) != 0)
            {
                return null;
            }

            var gamepad = state.Gamepad;
            var buttons = new HashSet<GamepadButton>();
            Add(buttons, gamepad.Buttons, 0x1000, GamepadButton.A);
            Add(buttons, gamepad.Buttons, 0x2000, GamepadButton.B);
            Add(buttons, gamepad.Buttons, 0x4000, GamepadButton.X);
            Add(buttons, gamepad.Buttons, 0x8000, GamepadButton.Y);
            Add(buttons, gamepad.Buttons, 0x0100, GamepadButton.LeftShoulder);
            Add(buttons, gamepad.Buttons, 0x0200, GamepadButton.RightShoulder);
            Add(buttons, gamepad.Buttons, 0x0020, GamepadButton.Back);
            Add(buttons, gamepad.Buttons, 0x0010, GamepadButton.Start);
            Add(buttons, gamepad.Buttons, 0x0040, GamepadButton.LeftStick);
            Add(buttons, gamepad.Buttons, 0x0080, GamepadButton.RightStick);
            Add(buttons, gamepad.Buttons, 0x0001, GamepadButton.DPadUp);
            Add(buttons, gamepad.Buttons, 0x0002, GamepadButton.DPadDown);
            Add(buttons, gamepad.Buttons, 0x0004, GamepadButton.DPadLeft);
            Add(buttons, gamepad.Buttons, 0x0008, GamepadButton.DPadRight);
            // Trigger mappings use a fixed half-press threshold; analogue values stay in Axes.
            if (gamepad.LeftTrigger >= 128) buttons.Add(GamepadButton.LeftTrigger);
            if (gamepad.RightTrigger >= 128) buttons.Add(GamepadButton.RightTrigger);

            return new ControllerSnapshot(device, buttons,
                new GamepadAxes(
                    NormalizeXInputAxis(gamepad.LeftX), NormalizeXInputAxis(gamepad.LeftY),
                    NormalizeXInputAxis(gamepad.RightX), NormalizeXInputAxis(gamepad.RightY),
                    gamepad.LeftTrigger / 255f, gamepad.RightTrigger / 255f), true);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    private ControllerSnapshot? ReadDualSense(DeviceDescriptor device)
    {
        if (!_hidDevices.TryGetValue(device.Id, out var hid) ||
            hid.ProductID is not (DualSenseProductId or EdgeProductId))
        {
            return null;
        }

        if (!_hidStreams.TryGetValue(device.Id, out var stream))
        {
            if (!hid.TryOpen(out stream) || stream is null)
            {
                return null;
            }

            stream.ReadTimeout = HidReadTimeoutMs;
            _hidStreams[device.Id] = stream;
        }

        try
        {
            var report = new byte[hid.GetMaxInputReportLength()];
            var count = stream.Read(report, 0, report.Length);
            return TryParseDualSenseReport(device, hid.ProductID == EdgeProductId,
                report.AsSpan(0, count), out var snapshot) ? snapshot : null;
        }
        catch (TimeoutException)
        {
            // No fresh report means the physical state is unknown. The caller must
            // release any held virtual output instead of reusing a stale snapshot.
            return null;
        }
        catch (IOException)
        {
            CloseStream(device.Id);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            CloseStream(device.Id);
            return null;
        }
        catch (ObjectDisposedException)
        {
            CloseStream(device.Id);
            return null;
        }
    }

    internal static bool TryParseDualSenseReport(
        DeviceDescriptor device, bool edge, ReadOnlySpan<byte> report,
        out ControllerSnapshot? snapshot)
    {
        snapshot = null;
        int offset;
        if (report.Length == 64 && report[0] == 0x01)
        {
            offset = 1;
        }
        else if (report.Length == 78 && report[0] == 0x31 && HasValidBluetoothCrc(report))
        {
            offset = 2;
        }
        else
        {
            // The short Bluetooth 0x01 report omits Edge paddle state; reject it. Full USB reports passed DSE validation; Bluetooth is not guaranteed.
            return false;
        }

        var first = report[offset + 7];
        var second = report[offset + 8];
        var third = report[offset + 9];
        var buttons = new HashSet<GamepadButton>();
        Add(buttons, first, 0x20, GamepadButton.A); // Cross
        Add(buttons, first, 0x40, GamepadButton.B); // Circle
        Add(buttons, first, 0x10, GamepadButton.X); // Square
        Add(buttons, first, 0x80, GamepadButton.Y); // Triangle
        Add(buttons, second, 0x01, GamepadButton.LeftShoulder);
        Add(buttons, second, 0x02, GamepadButton.RightShoulder);
        Add(buttons, second, 0x10, GamepadButton.Back); // Create
        Add(buttons, second, 0x20, GamepadButton.Start); // Options
        Add(buttons, second, 0x40, GamepadButton.LeftStick);
        Add(buttons, second, 0x80, GamepadButton.RightStick);
        if (edge)
        {
            Add(buttons, third, 0x40, GamepadButton.LeftPaddle);
            Add(buttons, third, 0x80, GamepadButton.RightPaddle);
        }
        if (report[offset + 4] >= 128) buttons.Add(GamepadButton.LeftTrigger);
        if (report[offset + 5] >= 128) buttons.Add(GamepadButton.RightTrigger);

        switch (first & 0x0F)
        {
            case 0: buttons.Add(GamepadButton.DPadUp); break;
            case 1: buttons.Add(GamepadButton.DPadUp); buttons.Add(GamepadButton.DPadRight); break;
            case 2: buttons.Add(GamepadButton.DPadRight); break;
            case 3: buttons.Add(GamepadButton.DPadRight); buttons.Add(GamepadButton.DPadDown); break;
            case 4: buttons.Add(GamepadButton.DPadDown); break;
            case 5: buttons.Add(GamepadButton.DPadDown); buttons.Add(GamepadButton.DPadLeft); break;
            case 6: buttons.Add(GamepadButton.DPadLeft); break;
            case 7: buttons.Add(GamepadButton.DPadLeft); buttons.Add(GamepadButton.DPadUp); break;
        }

        snapshot = new ControllerSnapshot(device, buttons,
            new GamepadAxes(
                NormalizeHidStick(report[offset]), NormalizeHidStickY(report[offset + 1]),
                NormalizeHidStick(report[offset + 2]), NormalizeHidStickY(report[offset + 3]),
                report[offset + 4] / 255f, report[offset + 5] / 255f), true);
        return true;
    }

    private static bool HasValidBluetoothCrc(ReadOnlySpan<byte> report)
    {
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(report[^4..]);
        uint crc = 0xFFFFFFFF;
        crc = FeedCrc(crc, 0xA1);
        foreach (var value in report[..^4])
        {
            crc = FeedCrc(crc, value);
        }

        return ~crc == expected;
    }

    private static uint FeedCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }

        return crc;
    }

    private static float NormalizeXInputAxis(short value) =>
        value < 0 ? value / 32768f : value / 32767f;

    private static float NormalizeHidStick(byte value) =>
        Math.Clamp((value - 127.5f) / 127.5f, -1f, 1f);

    private static float NormalizeHidStickY(byte value) => -NormalizeHidStick(value);

    private static void Add(HashSet<GamepadButton> buttons, int value, int mask, GamepadButton button)
    {
        if ((value & mask) != 0)
        {
            buttons.Add(button);
        }
    }

    private void CloseStream(string path)
    {
        if (_hidStreams.Remove(path, out var stream))
        {
            stream.Dispose();
        }
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
            foreach (var stream in _hidStreams.Values)
            {
                stream.Dispose();
            }

            _hidStreams.Clear();
            _hidDevices.Clear();
        }
    }

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
