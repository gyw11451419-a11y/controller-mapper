using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ControllerMapper.Desktop.Core;

public enum SafetyGateState
{
    Safe,
    ForegroundUnknown,
    AntiCheatDetected,
    MonitoringFailed
}

public readonly record struct SafetyAssessment(
    SafetyGateState State, string Description, IntPtr ForegroundWindow = default)
{
    public bool Allowed => State == SafetyGateState.Safe;
    public bool RequiresManualResume => State is SafetyGateState.AntiCheatDetected or SafetyGateState.MonitoringFailed;
}

public sealed class SafetyMonitor
{
    // Exact process-name matches only. This is a best-effort list, not a guarantee of detection.
    private static readonly HashSet<string> KnownAntiCheatProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64",
        "vgc", "xigncode3", "xhunter1"
    };

    private SafetyAssessment _lastProcessScan = new(SafetyGateState.MonitoringFailed, "安全进程监测尚未开始");
    private long _lastScanTimestamp;

    public SafetyAssessment Assess()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastScanTimestamp == 0 || Stopwatch.GetElapsedTime(_lastScanTimestamp, now).TotalMilliseconds >= 500)
        {
            _lastProcessScan = ScanProcesses();
            _lastScanTimestamp = now;
        }

        if (!_lastProcessScan.Allowed) return _lastProcessScan;

        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
                return new(SafetyGateState.ForegroundUnknown, "无法确定前台窗口，已释放输出");
            return new(SafetyGateState.Safe, "全局模式：安全监测正常", window);
        }
        catch
        {
            return new(SafetyGateState.ForegroundUnknown, "前台窗口监测失败，已释放输出");
        }
    }

    private static SafetyAssessment ScanProcesses()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    var name = process.ProcessName;
                    if (KnownAntiCheatProcesses.Contains(name))
                        return new(SafetyGateState.AntiCheatDetected, $"检测到 {name}，连发已停止；需手动恢复");
                }
                return new(SafetyGateState.Safe, "安全进程监测正常");
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch
        {
            return new(SafetyGateState.MonitoringFailed, "安全进程监测失败，连发已停止；需手动恢复");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

}
