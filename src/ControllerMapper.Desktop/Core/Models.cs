using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerMapper.Desktop.Core;

public enum GamepadButton
{
    None = 0,
    A, B, X, Y,
    LeftShoulder, RightShoulder,
    Back, Start, LeftStick, RightStick,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftPaddle, RightPaddle,
    LeftTrigger, RightTrigger
}

public readonly record struct GamepadAxes(
    float LeftX, float LeftY, float RightX, float RightY,
    float LeftTrigger, float RightTrigger)
{
    public static GamepadAxes Neutral => new(0, 0, 0, 0, 0, 0);
}

public sealed record DeviceDescriptor(
    string Id, string Name, string Kind, string Connection,
    string Capability, int? XInputSlot)
{
    public override string ToString() => $"{Name} ({Connection})";
}

public sealed record ControllerSnapshot(
    DeviceDescriptor Device,
    IReadOnlySet<GamepadButton> Buttons,
    GamepadAxes Axes,
    bool Healthy);

public interface IControllerInputService : IDisposable
{
    IReadOnlyList<DeviceDescriptor> Enumerate(int? excludedXInputSlot);
    ControllerSnapshot? Read(DeviceDescriptor device);
}

public sealed class MappingEntry
{
    public GamepadButton Source { get; set; }
    public GamepadButton Target { get; set; }
    public GamepadButton? SecondTarget { get; set; }
    public bool Shifted { get; set; }

    public override string ToString() =>
        $"{(Shifted ? "Shift + " : "")}{Source} → {Target}{(SecondTarget is { } second && second != GamepadButton.None ? " + " + second : "")}";
}

public sealed class Profile : ObservableObject
{
    private string _name = "默认配置";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public GamepadButton ShiftButton { get; set; } = GamepadButton.None;
    public GamepadButton TurboTrigger { get; set; } = GamepadButton.None;
    public GamepadButton TurboTarget { get; set; } = GamepadButton.None;
    public int TurboIntervalMs { get; set; } = 120;
    public List<MappingEntry> Mappings { get; set; } = [];
    public override string ToString() => Name;
}

public sealed class ProfileDocument
{
    public int SchemaVersion { get; set; } = 2;
    public string? SelectedProfileId { get; set; }
    public List<Profile> Profiles { get; set; } = [];
}
