using CommunityToolkit.Mvvm.ComponentModel;
using ControllerMapper.Desktop.Core;

namespace ControllerMapper.Desktop.ViewModels;

public sealed class InputButtonOption(GamepadButton button) : ObservableObject
{
    private string _label = button.ToString();

    public GamepadButton Button { get; } = button;
    public string Label { get => _label; set => SetProperty(ref _label, value); }
}

public static class ControllerButtonLabels
{
    public static bool IsDualSense(DeviceDescriptor? device) =>
        device?.Kind is "DualSense" or "DualSense Edge";

    public static string InputLayout(DeviceDescriptor? device) => device?.Kind switch
    {
        "DualSense Edge" => "已识别 DualSense Edge HID：显示 PlayStation 输入按键；独立背键仍需实机验证。输出保持虚拟 Xbox 360 按键。",
        "DualSense" => "已识别 DualSense HID：显示 PlayStation 输入按键。输出保持虚拟 Xbox 360 按键。",
        "Xbox" => "已识别 XInput：显示 Xbox 按键。XInput 无法证明实体手柄型号；输出为虚拟 Xbox 360。",
        _ => "尚未识别输入手柄，暂显示 Xbox 按键；连接手柄后会自动更新。"
    };

    public static string InputLabel(GamepadButton button, DeviceDescriptor? device)
    {
        if (!IsDualSense(device))
            return XboxLabel(button);

        return button switch
        {
            GamepadButton.A => "✕ · 叉键",
            GamepadButton.B => "○ · 圆键",
            GamepadButton.X => "□ · 方键",
            GamepadButton.Y => "△ · 三角键",
            GamepadButton.LeftShoulder => "L1 · 左肩键",
            GamepadButton.RightShoulder => "R1 · 右肩键",
            GamepadButton.LeftTrigger => "L2 · 左扳机",
            GamepadButton.RightTrigger => "R2 · 右扳机",
            GamepadButton.Back => "Create · 创建",
            GamepadButton.Start => "Options · 选项",
            GamepadButton.LeftStick => "L3 · 左摇杆按下",
            GamepadButton.RightStick => "R3 · 右摇杆按下",
            GamepadButton.LeftPaddle => device?.Kind == "DualSense Edge" ? "左背键（待验证）" : "左背键（仅 Edge）",
            GamepadButton.RightPaddle => device?.Kind == "DualSense Edge" ? "右背键（待验证）" : "右背键（仅 Edge）",
            _ => XboxLabel(button)
        };
    }

    public static string XboxLabel(GamepadButton button) => button switch
    {
        GamepadButton.None => "未设置",
        GamepadButton.LeftShoulder => "LB · 左肩键",
        GamepadButton.RightShoulder => "RB · 右肩键",
        GamepadButton.LeftTrigger => "LT · 左扳机",
        GamepadButton.RightTrigger => "RT · 右扳机",
        GamepadButton.Back => "View · 选择",
        GamepadButton.Start => "Menu · 开始",
        GamepadButton.LeftStick => "LS · 左摇杆按下",
        GamepadButton.RightStick => "RS · 右摇杆按下",
        GamepadButton.DPadUp => "↑ · 十字键上",
        GamepadButton.DPadDown => "↓ · 十字键下",
        GamepadButton.DPadLeft => "← · 十字键左",
        GamepadButton.DPadRight => "→ · 十字键右",
        GamepadButton.LeftPaddle => "左背键（仅 Edge）",
        GamepadButton.RightPaddle => "右背键（仅 Edge）",
        _ => button.ToString()
    };
}
