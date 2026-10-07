using System;
using MegaCrit.Sts2.Core.Logging;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// 统一日志与诊断开关。
///
/// `STS2_PRELOAD_STALL_GUARD=quiet` 关掉流程诊断（只保留兜底与停滞告警）；
/// `report` 保留诊断但**不强制放行**；不设 = 诊断 + 强制放行。
/// </summary>
internal static class PreloadStallLog
{
    private static string? _mode;

    /// <summary>运行模式原始取值（读一次并缓存；环境变量不会中途变）。</summary>
    internal static string Mode
    {
        get
        {
            if (_mode == null)
            {
                try
                {
                    _mode = (Environment.GetEnvironmentVariable("STS2_PRELOAD_STALL_GUARD") ?? string.Empty).Trim();
                }
                catch
                {
                    _mode = string.Empty;
                }
            }

            return _mode;
        }
    }

    /// <summary>流程诊断开关（`quiet` 关闭）。</summary>
    internal static bool DiagnosticsEnabled => !string.Equals(Mode, "quiet", StringComparison.OrdinalIgnoreCase);

    /// <summary>流程诊断日志（战斗开始推进链的打点）。</summary>
    internal static void Probe(string message)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        try
        {
            Log.Info($"[PreloadStallGuard][流程] {message}");
        }
        catch
        {
            // 诊断日志失败不影响流程
        }
    }
}
