#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 · **② 预设遗物**（2026-10-10 用户拍板：**A 方案 = 只挑"不依赖开战时机"的遗物**）。
///
/// 为什么必须挑：战神是**战斗中途中**加进来的，原版很多强力遗物（`Vajra` +1 力量 / `Anchor` 开战 10 格挡 /
/// `BagOfPreparation` 回合一多抽 / `Lantern` 回合 1 +1 能量 …）的效果挂在
/// **`BeforeCombatStart` / `AfterRoomEntered(CombatRoom)` / `TurnNumber &lt;= 1`** 这类钩子上 ——
/// 战神进场时那些钩子**早就跑完了** ⇒ 补挂遗物只会"显示有、不生效"（见 references 坑 **AE-5b**）。
///
/// 所以本表**只收**这两类（都验证过源码钩子）：
/// <list type="number">
/// <item><b>常驻数值修正</b>：`ModifyDamageAdditive` / `ModifyHpLostAfterOsty` 之类"每次结算时被问一次"的钩子；</item>
/// <item><b>逐动作 / 逐回合钩子</b>：`AfterCardPlayed` / `AfterDamageReceived` / `BeforeSideTurnEnd*` 之类
///   每个回合（或每次动作）都会再触发一次的钩子 —— 只要**没有** `TurnNumber &lt;= 1` 的分支就没问题。</item>
/// </list>
///
/// ⚠ 三个必须记住的口径：
/// <list type="bullet">
/// <item><b>加遗物用 `player.AddRelicInternal(...)`，绝不用 `RelicCmd.Obtain`</b>：后者第一步就
///   `runState.CurrentMapPointHistoryEntry?.GetEntry(NetId)` 写 `RelicChoices` —— 战神**不进 `runState.Players`**
///   （H1）⇒ 直接抛（同族教训见 references 坑 **AE-4b**）。</item>
/// <item><b>`AddRelicInternal` 不会替你调 `AfterObtained()`</b>（"获得时"效果）⇒ 由
///   <see cref="RunAfterObtainedAsync"/> 在生成收尾阶段 await（本表当前这几件都不覆盖它，
///   但留着这条链，日后加"获得时给最大生命"那类遗物时才不会静默失效）。</item>
/// <item><b>战神的遗物不会显示在界面上（预期，不是 bug）</b>：遗物槽 UI 只画 `LocalContext.IsMe` 那一席，
///   而战神刻意 `IsMe=False`；它的效果**不依赖 UI** ⇒ "看不见但生效"是正常状态。</item>
/// </list>
///
/// 件数阶梯 = 成本 − 1（<see cref="HellwarSummonPolicy.ResolvePresetRelicCount"/> 是单一事实源，
/// 启动自检会与本表的实际件数对账）：普通 **0** / 罕见 **1** / 稀有 **2** / 先古 **3**。
/// </summary>
internal static class HellwarWarGodRelics
{
    /// <summary>某档的预设遗物（canonical 模板；普通档 = 空 = 只保留它自己角色的初始遗物）。</summary>
    internal static IReadOnlyList<RelicModel> PresetRelicsOf(HellwarWarGodTier tier)
    {
        return tier switch
        {
            HellwarWarGodTier.Rare => RareRelics(),
            HellwarWarGodTier.Epic => EpicRelics(),
            HellwarWarGodTier.Ancient => AncientRelics(),
            _ => Array.Empty<RelicModel>(),
        };
    }

    /// <summary>日志 / 自检用一行摘要（`件数 + entry`）。</summary>
    internal static string DescribePresetRelics(HellwarWarGodTier tier)
    {
        IReadOnlyList<RelicModel> relics = PresetRelicsOf(tier);
        if (relics.Count == 0)
        {
            return "0 件";
        }

        return $"{relics.Count} 件: {string.Join(", ", relics.Select((relic) => relic.Id.Entry))}";
    }

    /// <summary>
    /// 把某档的预设遗物发给战神（**必须在 <see cref="HellwarWarGodFactory.AttachToRun"/> 之后**调 ——
    /// 那时 `player.RunState` 不再是 `NullRunState`，`TotalFloor` 才可读；遗物本身**不需要**像卡牌那样
    /// 补 `Owner` / 登记 run 表 —— `AddRelicInternal` 内部就设 Owner 并进 `player.Relics`）。
    ///
    /// 返回**实际发放**的遗物（已持有同 id 的会被跳过：战神身上本来就带角色初始遗物，撞了就不重复给）。
    /// 单件失败只记 WARN、不影响其余，也不回滚召唤（遗物是加值，不该因为一件遗物把整个召唤判失败）。
    /// </summary>
    internal static IReadOnlyList<RelicModel> GrantTo(Player player, HellwarWarGodTier tier, string tag)
    {
        List<RelicModel> granted = new();
        try
        {
            IReadOnlyList<RelicModel> canonicals = PresetRelicsOf(tier);
            if (canonicals.Count == 0)
            {
                return granted;
            }

            foreach (RelicModel canonical in canonicals)
            {
                try
                {
                    if (player.Relics.Any((existing) => existing.Id == canonical.Id))
                    {
                        LocalMultiControlLogger.Info(
                            $"{tag} 预设遗物跳过（该席位已有同件）: seat={player.NetId}, 遗物={canonical.Id.Entry}");
                        continue;
                    }

                    RelicModel relic = canonical.ToMutable();
                    relic.FloorAddedToDeck = Math.Max(1, player.RunState.TotalFloor);
                    player.AddRelicInternal(relic);
                    granted.Add(relic);
                }
                catch (Exception exception)
                {
                    LocalMultiControlLogger.Warn(
                        $"{tag} 预设遗物单件发放失败（已跳过）: 档位={tier}, 遗物={canonical.Id.Entry}, error={exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 预设遗物发放异常（本次不带遗物，召唤继续）: 档位={tier}, error={exception.Message}");
        }

        if (granted.Count > 0)
        {
            LocalMultiControlLogger.Info(
                $"{tag} ①c 预设遗物已发放: seat={player.NetId}, 档位={tier}, "
                + $"本次={granted.Count} 件[{string.Join(", ", granted.Select((relic) => relic.Id.Entry))}], "
                + $"该席位遗物总数={player.Relics.Count}");
        }

        return granted;
    }

    /// <summary>
    /// 补调"获得时"钩子（<c>RelicModel.AfterObtained()</c>）—— **`AddRelicInternal` 不会替你调它**
    /// （原版那条链是 `RelicCmd.Obtain` 调的）。本表当前这几件都不覆盖它（纯钩子型），
    /// 所以这一步目前是空转；留着是为了日后加"获得时给最大生命 / 复制卡"那类遗物时不静默失效。
    /// </summary>
    internal static async Task RunAfterObtainedAsync(IReadOnlyList<RelicModel> relics, string tag)
    {
        foreach (RelicModel relic in relics)
        {
            try
            {
                await relic.AfterObtained();
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 预设遗物的「获得时」钩子异常（已跳过）: 遗物={relic.Id.Entry}, error={exception.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // 内置预设遗物（A 方案：只挑"不依赖开战时机"的）
    // ------------------------------------------------------------------

    /// <summary>
    /// 罕见档（静默猎手皮，成本 2）＝ **1 件**：`Orichalcum` —— **回合结束时若无格挡 ⇒ 获得 6 格挡**。
    /// 靠 `BeforeSideTurnEndVeryEarly` / `BeforeSideTurnEnd` / `BeforeSideTurnStart`（**每回合都会再触发**，
    /// 无 `TurnNumber` 限制）⇒ 中途加人有效。给这档一个"防御地板"，配合它便宜的定位。
    /// </summary>
    private static RelicModel[] RareRelics() => new RelicModel[]
    {
        ModelDb.Relic<Orichalcum>(),
    };

    /// <summary>
    /// 稀有档（铁甲战士皮，成本 3）＝ **2 件**：
    /// <list type="bullet">
    /// <item>`MiniatureCannon`：**已升级的攻击牌 +3 伤害**（`ModifyDamageAdditive`，每次结算被问一次）
    ///   —— 与本档"预设卡组全升级"是**强协同**（14 张全升级 ⇒ 每次攻击都吃到 +3）；</item>
    /// <item>`SelfFormingClay`：**受到未格挡伤害 ⇒ 下回合 +3 格挡**（`AfterDamageReceived` → 施加 Power），
    ///   命中"肉盾"定位，AI 挨打就自动触发。</item>
    /// </list>
    /// </summary>
    private static RelicModel[] EpicRelics() => new RelicModel[]
    {
        ModelDb.Relic<MiniatureCannon>(),
        ModelDb.Relic<SelfFormingClay>(),
    };

    /// <summary>
    /// 先古档（摄政王皮，成本 4）＝ **3 件**（最强档）：
    /// <list type="bullet">
    /// <item>`PenNib`：每打 10 张攻击牌，**下一击伤害翻倍**（`BeforeCardPlayed` + `ModifyDamageMultiplicative`）；</item>
    /// <item>`Shuriken`：**同一回合打满 3 张攻击牌 ⇒ +1 力量**（`AfterCardPlayed` + 回合开始重置计数）；</item>
    /// <item>`TungstenRod`：**每次生命损失 −1**（`ModifyHpLostAfterOsty`，纯被动）
    ///   —— 与召唤时的【无实体】保命一个取向：先古档"更难死"。</item>
    /// </list>
    /// ⚠ 刻意**不放**有副作用的（如 `Brimstone` 每回合 +2 力量**同时也给敌人 +1 力量**）——
    /// 战神是"帮队友"的定位，任何"顺手加强敌人"的遗物在多控局里都是负收益。
    /// </summary>
    private static RelicModel[] AncientRelics() => new RelicModel[]
    {
        ModelDb.Relic<PenNib>(),
        ModelDb.Relic<Shuriken>(),
        ModelDb.Relic<TungstenRod>(),
    };
}
