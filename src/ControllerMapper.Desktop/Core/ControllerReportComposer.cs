using ControllerMapper.Desktop.Output;

namespace ControllerMapper.Desktop.Core;

/// <summary>
/// Composes the virtual controller's ordinary mapping state. Trigger input is treated
/// as pressed at 50% travel for mappings, while an unmapped trigger keeps its analogue value.
/// A trigger used as a mapping source or modifier is removed from analogue passthrough.
/// </summary>
public static class ControllerReportComposer
{
    public static VirtualGamepadReport ApplyTurboPulse(VirtualGamepadReport ordinaryReport,
        GamepadButton target, bool pulseOn)
    {
        if (target == GamepadButton.None) return ordinaryReport;
        var buttons = new HashSet<GamepadButton>(ordinaryReport.Buttons);
        buttons.Remove(target);
        if (pulseOn) buttons.Add(target);
        var axes = ordinaryReport.Axes;
        if (target == GamepadButton.LeftTrigger) axes = axes with { LeftTrigger = 0f };
        if (target == GamepadButton.RightTrigger) axes = axes with { RightTrigger = 0f };
        return new VirtualGamepadReport(buttons, axes);
    }

    public static VirtualGamepadReport Compose(ControllerSnapshot snapshot, Profile profile)
    {
        var physical = snapshot.Buttons;
        var shiftDown = profile.ShiftButton != GamepadButton.None && physical.Contains(profile.ShiftButton);
        MappingEntry? MappingFor(GamepadButton source) =>
            profile.Mappings.FirstOrDefault(item => item.Source == source && item.Shifted == shiftDown)
            ?? profile.Mappings.FirstOrDefault(item => item.Source == source && !item.Shifted);

        var axes = snapshot.Axes with
        {
            LeftTrigger = ConsumesTrigger(GamepadButton.LeftTrigger) ? 0f : snapshot.Axes.LeftTrigger,
            RightTrigger = ConsumesTrigger(GamepadButton.RightTrigger) ? 0f : snapshot.Axes.RightTrigger
        };
        var outputButtons = new HashSet<GamepadButton>();
        foreach (var source in physical)
        {
            if (source == profile.ShiftButton || source == profile.TurboTrigger) continue;
            var mapping = MappingFor(source);
            if (mapping is null)
            {
                // Unmapped triggers remain analogue in Axes; no digital output is added.
                if (source is not (GamepadButton.LeftTrigger or GamepadButton.RightTrigger) &&
                    ProfileStore.IsOutput(source)) outputButtons.Add(source);
            }
            else
            {
                outputButtons.Add(mapping.Target);
                if (mapping.SecondTarget is { } second && second != GamepadButton.None)
                    outputButtons.Add(second);
            }
        }

        return new VirtualGamepadReport(outputButtons, axes);

        bool ConsumesTrigger(GamepadButton trigger) =>
            trigger == profile.ShiftButton || trigger == profile.TurboTrigger || MappingFor(trigger) is not null;
    }
}
