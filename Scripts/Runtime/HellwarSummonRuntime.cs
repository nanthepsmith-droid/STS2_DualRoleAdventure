#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）**Phase 1** 的编排层：休息处选项【分裂精神】的注入与遗物查询。
/// 数值换算在纯逻辑 <see cref="PureLogic.HellwarSummonPolicy"/>，选项本体在 <see cref="HellwarSplitSpiritOption"/>。
///
/// 关键纪律（与炼化 / 净化同源）：
/// <list type="bullet">
/// <item>**只注入真人席位** —— 注入到瓦库席位会被 <see cref="LocalWakuuRestAutoChoice"/> 当"第三方额外选项"随机点掉；</item>
/// <item>**注入必须幂等**（<c>Generate</c> 可能被多次调用）；</item>
/// <item>**图标必须在房间预加载之前注册**（<see cref="HellwarSplitSpiritOption.EnsureIconRegistered"/>，
///   且**绝不覆写 `AssetPaths`**，理由见 <see cref="LocalRestSiteOptionIcon"/>）。</item>
/// </list>
///
/// 与炼化 / 净化的差别：本选项**不需要"选目标"** —— 它作用于**自己**（给持有者自己的【地狱耳环】+1 计数）。
/// </summary>
internal static class HellwarSummonRuntime
{
    /// <summary>由 <c>RestSiteOptionPatch</c>（<c>RestSiteOption.Generate</c> 后缀）调用。</summary>
    internal static void TryInjectSplitSpiritOption(Player player, List<RestSiteOption> options)
    {
        if (player == null || options == null)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!LocalWakuuAutopilotConfig.HellwarGod)
        {
            return;
        }

        // 只给本地真人席位；瓦库席位与第三方席位一律不注入。
        if (!LocalSelfCoopContext.IsLocalSessionSeat(player.NetId)
            || LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return;
        }

        if (options.Any(option => option is HellwarSplitSpiritOption))
        {
            return;
        }

        LocalWakuuHellwarEarring? earring = FindEarring(player);
        if (earring == null)
        {
            return;
        }

        // D5 档位的一次性标记绝不跨休息处残留（上一处若最后一条选项被选掉，标记可能还没被消费）。
        earring.ResetPendingFreeRestAction();

        // 图标必须在房间预加载之前注册好（注入发生在 BeginRestSite，早于 Preloading 'RestSite Room'）。
        HellwarSplitSpiritOption.EnsureIconRegistered();

        options.Add(new HellwarSplitSpiritOption(player));
        LocalMultiControlLogger.Info(
            $"[地狱战神] 休息区已注入【分裂精神】选项: owner={player.NetId}, 计数={earring.SpiritCount}, "
            + $"扣血档位={LocalWakuuAutopilotConfig.HellwarSplitHpRatio}%, "
            + $"免费额外选项={LocalWakuuAutopilotConfig.HellwarSplitFree}");
    }

    /// <summary>取某席位身上的【地狱耳环】（没有则 null；异常一律吞掉返回 null）。</summary>
    internal static LocalWakuuHellwarEarring? FindEarring(Player? player)
    {
        try
        {
            return player?.Relics?.OfType<LocalWakuuHellwarEarring>().FirstOrDefault();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 读取【地狱耳环】失败: {exception.Message}");
            return null;
        }
    }
}
