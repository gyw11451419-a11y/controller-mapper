using ControllerMapper.Desktop.Core;

namespace ControllerMapper.Desktop.Output;

public sealed record VirtualGamepadReport(
    IReadOnlySet<GamepadButton> Buttons,
    GamepadAxes Axes)
{
    public static VirtualGamepadReport Neutral =>
        new(new HashSet<GamepadButton>(), GamepadAxes.Neutral);
}

public interface IVirtualGamepadOutput : IDisposable
{
    bool IsConnected { get; }
    int? XInputSlot { get; }
    void Connect();
    void Submit(VirtualGamepadReport report);
    void Neutralize();
    void Disconnect();
}
