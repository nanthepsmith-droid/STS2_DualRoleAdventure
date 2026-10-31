using System;
using System.Collections.Generic;
using System.Threading;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ThirdPartyHotPathFix.Scripts.Gates;

/// <summary>
/// 四家第三方 mod 的「这家的机制到底在不在场」判定。
///
/// 背景（2026-10-31 采样，先量后猜）：
/// - `Koishi.SealAllRelicPatch` / `figure_Saya.GeneralizedResistanceSuppressionPatch` /
///   `newsanguo.AbilitySuppressionPatch` 三家都挂在 <c>CombatState.IterateHookListeners</c> 的**后缀**上，
///   每次 hook 分发都要把**全部** ~500 个 listener 逐个 `is` 判型 + 查能力 / 扫手牌
///   （合计 ≈ 9.5% 主线程，其中大半是 `is` 判型与 LINQ 分配）。
/// - `YUICore.YuiRelicTriggerReplay.Wrap` 对**每个遗物 listener** 建一个 scope 并写两次 `AsyncLocal`
///   ⇒ 主线程 **10.7%** 全在 `ExecutionContext.SetLocalValue`（12 席 ≈ 120 个遗物 × 每秒数千次分发）。
///
/// 关键事实：这四条链在「机制不在场」时是**纯空转**（逐条 yield 原样返回），
/// 所以只要证明不在场，就可以整条跳过 —— 语义等价，收益是这 20% 的主线程时间。
///
/// 判据（全部来自反编译源码，见各 patch 的注释）：
/// - Koishi：任何生物身上有 <c>SealAllPower</c>；
/// - Saya：任何生物身上有 <c>GeneralizedResistancePower</c>；
/// - newsanguo：任一玩家手牌里有 <c>AntiMagicField</c>，或任一敌人身上有 <c>IronCavalryPower</c>；
/// - YUI：任一玩家的遗物实现了 <c>IAfterRelicTriggeredHook</c> 或 <c>IRelicTriggerRepeater</c>。
///
/// **判不出来时一律保守（不跳过，保持第三方原行为）** —— 性能补丁绝不能改变玩法。
/// </summary>
internal static class ArmedGates
{
    private static readonly CombatSnapshotCache CombatCache = new();
    private static readonly YuiRelicCache YuiCache = new();

    private static readonly VerdictCounter KoishiVerdict = new();
    private static readonly VerdictCounter SayaVerdict = new();
    private static readonly VerdictCounter NewsanguoVerdict = new();
    private static readonly VerdictCounter YuiVerdict = new();

    private static readonly Dictionary<Type, bool> RelicTypeInterest = new();

    private static long _powerVersion;

    /// <summary>
    /// 能力版本监听的安装结果。**为 false 时三个战斗内闸门一律不跳过**：
    /// 快照只有在「能力增删能立刻反映」时才是可信的，否则「新挂了 SealAll 却没人知道」
    /// 会让第三方过滤链被错误跳过 = 改变玩法（性能补丁绝不能干这个）。
    /// </summary>
    internal static bool PowerVersionWatchInstalled;

    /// <summary>
    /// 能力版本号 +1。由 <c>Creature.ApplyPowerInternal</c> / <c>RemovePowerInternal</c> 的后缀调用
    /// （这两个是原版**唯一**的能力增删入口，见 `Creature.cs`）⇒ 快照永远精确，不靠时间窗口。
    /// </summary>
    internal static void BumpPowerVersion() => Interlocked.Increment(ref _powerVersion);

    /// <summary>「机制在场」的具体来源（只用于日志，便于下次一眼看出为什么没跳过）。</summary>
    internal enum ArmedReason
    {
        None = 0,
        PowerOnCreature,
        HanzhongCardInHand,
        IronCavalryPower,
        RelicImplementsHook,
        Unknown,
    }

    /// <summary>true = 可以跳过 Koishi 的遗物过滤链。</summary>
    internal static bool ShouldSkipKoishiSealAll()
    {
        bool available = EnsureCombatSnapshot(out bool koishi, out _, out _);
        if (!available)
        {
            return Record(KoishiVerdict, "Koishi SealAll 遗物过滤", armed: true, ArmedReason.Unknown);
        }

        return Record(
            KoishiVerdict,
            "Koishi SealAll 遗物过滤",
            koishi,
            koishi ? ArmedReason.PowerOnCreature : ArmedReason.None);
    }

    /// <summary>true = 可以跳过 figure_Saya 的减益过滤链。</summary>
    internal static bool ShouldSkipSayaResistance()
    {
        bool available = EnsureCombatSnapshot(out _, out bool saya, out _);
        if (!available)
        {
            return Record(SayaVerdict, "figure_Saya 泛抗性减益过滤", armed: true, ArmedReason.Unknown);
        }

        return Record(
            SayaVerdict,
            "figure_Saya 泛抗性减益过滤",
            saya,
            saya ? ArmedReason.PowerOnCreature : ArmedReason.None);
    }

    /// <summary>true = 可以跳过 newsanguo 的能力压制过滤链。</summary>
    internal static bool ShouldSkipNewsanguo()
    {
        bool available = EnsureCombatSnapshot(out _, out _, out bool ironCavalry);
        if (!available)
        {
            return Record(NewsanguoVerdict, "newsanguo 能力压制过滤", armed: true, ArmedReason.Unknown);
        }

        if (ironCavalry)
        {
            return Record(NewsanguoVerdict, "newsanguo 能力压制过滤", armed: true, ArmedReason.IronCavalryPower);
        }

        bool hanzhong = AnyPlayerHoldsHanzhong();
        return Record(
            NewsanguoVerdict,
            "newsanguo 能力压制过滤",
            hanzhong,
            hanzhong ? ArmedReason.HanzhongCardInHand : ArmedReason.None);
    }

    /// <summary>true = 可以跳过 YUICore 的遗物触发复读链（Wrap）。</summary>
    internal static bool ShouldSkipYuiRelicReplay()
    {
        bool armed = YuiArmed();
        return Record(
            YuiVerdict,
            "YUICore 遗物触发复读链",
            armed,
            armed ? ArmedReason.RelicImplementsHook : ArmedReason.None);
    }

    /// <summary>
    /// 记录一次判定。返回 true = 跳过（第三方过滤链整条不跑）。
    /// 只在结论翻转时打一行日志（判定本身每秒上万次，不能刷屏）。
    /// </summary>
    private static bool Record(VerdictCounter counter, string label, bool armed, ArmedReason reason)
    {
        bool skip = !armed;
        if (counter.Record(skip))
        {
            string detail = skip ? "机制不在场 → 跳过整条过滤链" : $"机制在场（{Describe(reason)}）→ 保留第三方原逻辑";
            Log.Info($"[HotPathFix] {label}：{detail}（{counter.Format()}）");
        }

        return skip;
    }

    private static string Describe(ArmedReason reason) => reason switch
    {
        ArmedReason.PowerOnCreature => "场上有生物带这家能力",
        ArmedReason.HanzhongCardInHand => "有玩家手牌里躺着汉中",
        ArmedReason.IronCavalryPower => "敌人身上有铁骑",
        ArmedReason.RelicImplementsHook => "有玩家的遗物实现了 YUI 的触发/复读接口",
        ArmedReason.Unknown => "判不出来（不在战斗 / 没装能力版本监听）",
        _ => "未知",
    };

    // ── 战斗内快照（Koishi / Saya / newsanguo 铁骑）──────────────────────────

    /// <summary>
    /// 取战斗内快照。返回 false = 当前不在战斗里 / 拿不到战斗状态 / 能力版本监听没装上
    /// （此时调用方按「机制在场」处理，绝不擅自跳过）。
    /// </summary>
    private static bool EnsureCombatSnapshot(out bool koishi, out bool saya, out bool ironCavalry)
    {
        if (!PowerVersionWatchInstalled)
        {
            koishi = false;
            saya = false;
            ironCavalry = false;
            return false;
        }

        CombatState? state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            CombatCache.Reset();
            koishi = false;
            saya = false;
            ironCavalry = false;
            return false;
        }

        IReadOnlyList<Creature> allies = state.Allies;
        IReadOnlyList<Creature> enemies = state.Enemies;
        long version = Interlocked.Read(ref _powerVersion);

        if (CombatCache.TryGet(state, version, allies.Count, enemies.Count, out koishi, out saya, out ironCavalry))
        {
            return true;
        }

        bool scannedKoishi = false;
        bool scannedSaya = false;
        bool scannedIron = false;
        ScanPowers(allies, ref scannedKoishi, ref scannedSaya, ref scannedIron);
        ScanPowers(enemies, ref scannedKoishi, ref scannedSaya, ref scannedIron);

        CombatCache.Commit(state, version, allies.Count, enemies.Count, scannedKoishi, scannedSaya, scannedIron);
        koishi = scannedKoishi;
        saya = scannedSaya;
        ironCavalry = scannedIron;
        return true;
    }

    /// <summary>
    /// 一次遍历同时判三家：全部命中就提前返回。
    /// 用 <c>GetType() == 类型</c> 精确比对（这三家的能力都是 sealed / 非实例型，
    /// 反编译确认没有子类），比反射调用 `GetPower&lt;T&gt;()` 便宜且不分配。
    /// </summary>
    private static void ScanPowers(IReadOnlyList<Creature> creatures, ref bool koishi, ref bool saya, ref bool iron)
    {
        Type? sealAll = VendorTypes.SealAllPower;
        Type? resistance = VendorTypes.GeneralizedResistancePower;
        Type? ironCavalry = VendorTypes.IronCavalryPower;

        for (int i = 0; i < creatures.Count; i++)
        {
            if (koishi && saya && iron)
            {
                return;
            }

            Creature creature = creatures[i];
            IReadOnlyList<PowerModel> powers = creature.Powers;
            for (int j = 0; j < powers.Count; j++)
            {
                Type powerType = powers[j].GetType();
                if (!koishi && sealAll != null && powerType == sealAll)
                {
                    koishi = true;
                }

                if (!saya && resistance != null && powerType == resistance)
                {
                    saya = true;
                }

                if (!iron && ironCavalry != null && powerType == ironCavalry)
                {
                    iron = true;
                }
            }
        }
    }

    /// <summary>
    /// newsanguo 的「汉中」判据：任一玩家**手牌**里有 <c>AntiMagicField</c>。
    /// 手牌内容会随抽牌 / 弃牌变化且不走能力增删入口 ⇒ 这一项**不进快照**、每次现扫
    /// （12 席 × 手牌 ≈ 80 次类型判定，远低于被它省下的整条过滤链遍历）。
    /// </summary>
    private static bool AnyPlayerHoldsHanzhong()
    {
        Type? antiMagicField = VendorTypes.AntiMagicFieldCard;
        if (antiMagicField == null)
        {
            return true;
        }

        CombatState? state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return true;
        }

        IReadOnlyList<Creature> allies = state.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            Player? player = allies[i].Player;
            PlayerCombatState? combatState = player?.PlayerCombatState;
            if (combatState == null)
            {
                continue;
            }

            IReadOnlyList<CardModel> cards = combatState.Hand.Cards;
            for (int j = 0; j < cards.Count; j++)
            {
                if (antiMagicField.IsInstanceOfType(cards[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ── YUICore 遗物快照 ────────────────────────────────────────────────────

    /// <summary>
    /// 遗物快照。**战斗内以 `CombatState.Allies` 为准**（不是 `RunState.Players`）：
    /// 战灵召唤的临时席位不在 `runState.Players` 里，但它们的遗物**确实**会被
    /// `IterateHookListeners` 收进去 —— 只看 RunState 会漏掉战神身上的 YUI 遗物。
    /// </summary>
    private static bool YuiArmed()
    {
        CombatState? combat = CombatManager.Instance.DebugOnlyGetState();
        if (combat != null)
        {
            IReadOnlyList<Creature> allies = combat.Allies;
            int relicCount = 0;
            for (int i = 0; i < allies.Count; i++)
            {
                Player? player = allies[i].Player;
                if (player != null)
                {
                    relicCount += player.Relics.Count;
                }
            }

            if (YuiCache.TryGet(combat, relicCount, out bool cachedInCombat))
            {
                return cachedInCombat;
            }

            bool armedInCombat = ScanCombatRelics(allies);
            YuiCache.Commit(combat, relicCount, armedInCombat);
            return armedInCombat;
        }

        RunState? run = RunManager.Instance.DebugOnlyGetState();
        if (run == null)
        {
            YuiCache.Reset();
            return true;
        }

        IReadOnlyList<Player> players = run.Players;
        int total = 0;
        for (int i = 0; i < players.Count; i++)
        {
            total += players[i].Relics.Count;
        }

        if (YuiCache.TryGet(run, total, out bool cached))
        {
            return cached;
        }

        bool armed = ScanRelics(players);
        YuiCache.Commit(run, total, armed);
        return armed;
    }

    private static bool ScanCombatRelics(IReadOnlyList<Creature> allies)
    {
        for (int i = 0; i < allies.Count; i++)
        {
            Player? player = allies[i].Player;
            if (player != null && HasInterestingRelic(player.Relics))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ScanRelics(IReadOnlyList<Player> players)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (HasInterestingRelic(players[i].Relics))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 这串遗物里有没有实现 YUICore 两个接口的。
    /// 「类型 → 是否相关」缓存永久有效（遗物的接口实现是类型级常量，不会变），
    /// 所以热路径上每个遗物只是一次字典命中。
    /// </summary>
    private static bool HasInterestingRelic(IReadOnlyList<RelicModel> relics)
    {
        Type? notifyHook = VendorTypes.AfterRelicTriggeredHook;
        Type? repeater = VendorTypes.RelicTriggerRepeater;
        if (notifyHook == null && repeater == null)
        {
            return true;
        }

        for (int i = 0; i < relics.Count; i++)
        {
            RelicModel relic = relics[i];
            Type relicType = relic.GetType();
            if (!RelicTypeInterest.TryGetValue(relicType, out bool interested))
            {
                interested = (notifyHook != null && notifyHook.IsInstanceOfType(relic))
                    || (repeater != null && repeater.IsInstanceOfType(relic));
                RelicTypeInterest[relicType] = interested;
            }

            if (interested)
            {
                return true;
            }
        }

        return false;
    }
}
