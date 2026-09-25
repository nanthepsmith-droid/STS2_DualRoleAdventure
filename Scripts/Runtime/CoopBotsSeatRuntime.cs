using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「联机机器人（Co-op Bots）席位」的调度侧（POC）：
/// 配置 → 席位驱动集合 →（进局时）逐席接管 →（退局时）释放。
///
/// 生命周期（对齐 <see cref="LocalMultiControlRuntime"/> 的进局/退局钩子）：
/// <list type="bullet">
/// <item>启动：<see cref="LoadFromConfig"/> 把配置里的席位落进 <see cref="LocalSelfCoopContext"/>（仅记录，不接管）；</item>
/// <item>进局（<c>RunManager.Launch</c> 之后）：<see cref="ApplyOnRunLaunch"/> —— 先释放上一局遗留、再重读配置、
/// 然后对**本局真实存在**的席位调 <c>AutoPilot.Set(seat, true)</c>；</item>
/// <item>退局（<c>RunManager.CleanUp</c>）：<see cref="ReleaseOnRunCleanup"/> 只释放**本 mod 接管过**的席位
/// （玩家自己在 CB 面板上接管的席位不动）。</item>
/// </list>
///
/// 互斥约束（分析 §2.1 / §4.3）：同一席位不能同时被瓦库与联机机器人驱动 —— 见
/// <see cref="CoopBotsSeatPlan.ResolveDrivers"/>，冲突时**联机机器人优先**并打 WARN。
/// </summary>
internal static class CoopBotsSeatRuntime
{
    /// <summary>本 mod 亲手接管过的席位（退局只释放这些，避免误伤玩家在 CB 面板上的手动接管）。</summary>
    private static readonly HashSet<ulong> _handedOverByUs = new HashSet<ulong>();

    /// <summary>配置层入口：把 <c>coopBotsSeats</c> 文本落到席位驱动集合（不触碰 Co-op Bots 的运行期状态）。</summary>
    public static void LoadFromConfig(string source)
    {
        try
        {
            List<ulong> seats = CoopBotsSeatPlan.Parse(
                LocalWakuuAutopilotConfig.CoopBotsSeats,
                LocalSelfCoopContext.LocalPlayerIds);
            LocalSelfCoopContext.UseSavedCoopBotsPlayerIds(seats);
            LocalMultiControlLogger.Info(
                $"联机机器人席位配置: {CoopBotsSeatPlan.Format(seats)}（有效席位={seats.Count}）, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"联机机器人席位配置解析异常（已忽略，按无机器人处理）: {exception.Message}");
        }
    }

    /// <summary>
    /// 进局：逐席接管本局中标记为「联机机器人」的席位。
    /// 未安装 Co-op Bots 时只打一条日志（席位回落真人操作，行为与本 mod 未接入时一致）。
    /// </summary>
    public static void ApplyOnRunLaunch(RunState? runState)
    {
        try
        {
            // 上一局若是异常退出（没走 CleanUp），接管记录会残留在 CB 的静态集合里 —— 先清干净再重挂。
            ReleaseAll("run-launch-reset");

            LoadFromConfig("run-launch");

            // ⚠ 无论有没有配置「接管席位」都必须探测一次：上游 mod 自己加的**合成 Bot** 同样是
            // `AutoPilot.Drives(netId)` 的席位，而 R1 商店 ACK 死锁对「任何 CB 驱动的席位」都成立
            // （回环下 `expected` 会拿到我们的本地席位 ⇒ 永远等不到 ACK）。探测成功顺带挂上旁路补丁。
            // 2026-09-25 实机教训：旧写法在"没配 coopBotsSeats"时直接 return，导致装了 CB 也没挂补丁。
            bool available = CoopBotsAdapter.EnsureReady(force: true);
            List<ulong> configured = LocalSelfCoopContext.GetCoopBotsPlayerIdsSnapshot();
            if (configured.Count == 0)
            {
                LocalMultiControlLogger.Info(
                    $"联机机器人接管名单为空（coopBotsSeats 未配置）：本局不接管任何席位；"
                    + $"Co-op Bots 适配器={(available ? "可用（商店 ACK 旁路已挂载）" : "不可用")}。");
                return;
            }

            if (!available)
            {
                LocalMultiControlLogger.Warn(
                    $"联机机器人席位已配置（{CoopBotsSeatPlan.Format(configured)}）但 Co-op Bots 适配器不可用: "
                    + $"{CoopBotsAdapter.Describe}；这些席位本局按真人/瓦库原样，不接管。");
                return;
            }

            List<ulong> presentSeats = runState?.Players.Select((player) => player.NetId).ToList() ?? new List<ulong>();

            SeatDriverAssignment assignment = CoopBotsSeatPlan.ResolveDrivers(
                LocalSelfCoopContext.GetWakuuPlayerIdsSnapshot(),
                configured);
            if (assignment.Conflicts.Count > 0)
            {
                // 与瓦库名单冲突：按「联机机器人优先」收敛（分析 §4.3 的三态互斥）。
                LocalSelfCoopContext.UseSavedWakuuPlayerIds(assignment.Wakuu);
                LocalMultiControlLogger.Warn(
                    $"席位驱动冲突（同时勾选瓦库与联机机器人）: {CoopBotsSeatPlan.Format(assignment.Conflicts)} —— "
                    + "已按「联机机器人优先」处理，这些席位本局不再由瓦库托管。");
            }

            foreach (ulong seat in assignment.CoopBots)
            {
                if (!presentSeats.Contains(seat))
                {
                    LocalMultiControlLogger.Warn($"联机机器人席位不在本局玩家列表中，跳过接管: seat={seat}");
                    continue;
                }

                if (CoopBotsAdapter.TrySetHandedOver(seat, handedOver: true))
                {
                    _handedOverByUs.Add(seat);
                    LocalMultiControlLogger.Info(
                        $"联机机器人已接管席位: seat={seat}, 本局接管数={_handedOverByUs.Count}, "
                        + $"HandedOver=[{CoopBotsSeatPlan.Format(CoopBotsAdapter.HandedOverSnapshot())}]");
                }
                else
                {
                    LocalMultiControlLogger.Warn($"联机机器人接管席位失败（该席位仍由真人/瓦库驱动）: seat={seat}");
                }
            }

            LocalMultiControlLogger.Info(
                $"席位驱动分配（进局）: {CoopBotsSeatPlan.DescribeAssignment(presentSeats, ResolveDriverMode)}");
        }
        catch (Exception exception)
        {
            // 接管失败绝不能影响开局主流程。
            LocalMultiControlLogger.Warn($"联机机器人席位接管异常（已忽略，本局按原驱动）: {exception.Message}");
        }
    }

    /// <summary>退局：释放本 mod 接管过的席位（玩家手动接管的席位不动）。</summary>
    public static void ReleaseOnRunCleanup()
    {
        ReleaseAll("run-cleanup");
    }

    private static void ReleaseAll(string reason)
    {
        if (_handedOverByUs.Count == 0)
        {
            return;
        }

        List<ulong> seats = _handedOverByUs.OrderBy((seat) => seat).ToList();
        if (!CoopBotsAdapter.EnsureReady(force: false))
        {
            // CB 已经不可用了（例如它自己崩掉/被卸载），只剩本地记录要清。
            _handedOverByUs.Clear();
            LocalMultiControlLogger.Warn(
                $"释放联机机器人席位时适配器不可用（{CoopBotsAdapter.Describe}）: "
                + $"{CoopBotsSeatPlan.Format(seats)}，reason={reason}");
            return;
        }

        foreach (ulong seat in seats)
        {
            bool released = CoopBotsAdapter.TrySetHandedOver(seat, handedOver: false);
            LocalMultiControlLogger.Info(
                $"释放联机机器人席位: seat={seat}, 结果={(released ? "成功" : "失败（CB 侧未确认）")}, reason={reason}");
        }

        _handedOverByUs.Clear();
    }

    private static SeatDriverMode ResolveDriverMode(ulong seat)
    {
        return SeatDriverModes.Classify(
            LocalSelfCoopContext.IsWakuuEnabled(seat),
            LocalSelfCoopContext.IsCoopBotsDriven(seat));
    }
}
