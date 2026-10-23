using System;
using System.Diagnostics;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// r312：启动耗时分段的「位置」探针 —— 回答「我们这一行日志出现在进程启动后的第几秒」。
///
/// 游戏日志没有行时间戳，所以分段只能靠"位置"落地：
///   <c>mod_init_begin</c> 的 since_process_start = 引擎启动 + 排在我们前面的那些 mod；
///   <c>mod_init_total</c> 的 since_process_start = 再加上我们自己的初始化；
///   到主菜单的总时长由游戏自报，减去上面两段 = 我们之后的部分。
///
/// 只读自身进程时间，拿不到就返回 -1（诊断绝不允许影响初始化）。
/// </summary>
internal static class StartupProfileProbe
{
    private static readonly DateTime? ProcessStart = ResolveProcessStart();

    internal static double SinceProcessStartMs()
    {
        DateTime? start = ProcessStart;
        return start.HasValue ? (DateTime.Now - start.Value).TotalMilliseconds : -1;
    }

    private static DateTime? ResolveProcessStart()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch
        {
            // 拿不到启动时间只是少一个数，不影响任何功能
            return null;
        }
    }
}
