#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 一个档位的「预设战神」内容（**纯数据**；角色模型由运行时按 <see cref="SkinKey"/> 解析成
/// <c>CharacterModel</c> —— 纯逻辑层不碰任何游戏类型，便于单测）。
///
/// <para>⚠ 预设战神 v2（r269 起，2026-10-11 用户拍板「档位 × 角色预设」二维）：后四个可选参数承载
/// **<c>hellwar_presets.json</c> 里的自定义预设** —— 内置四档全部走默认值（零行为变化）：
/// <see cref="PresetId"/>/<see cref="DisplayName"/> 空 = 内置默认；
/// <see cref="DeckText"/> = 预设自带卡组文本（格式同 <c>hellwarDeck*</c>；null = 走旧链：配置键 → 内置默认）；
/// <see cref="RelicEntries"/> = 预设自带遗物 id（null = 内置遗物表）。</para>
/// </summary>
/// <param name="Tier">档位（普通 / 罕见 / 稀有 / 先古）。</param>
/// <param name="SkinKey">角色皮键：<see cref="HellwarWarGodPresets.SkinOfSummoner"/> = 召唤者自身角色；
/// 其余 = 预设表键或 <c>CharacterModel.Id.Entry</c>（大小写不敏感）。</param>
/// <param name="UpgradeDeck">是否把初始卡组**全升级**（最强的"零硬编码"强化手段，见 D7 首版口径）。</param>
/// <param name="BonusHp">额外血上限（0 = 用角色初始血量）。</param>
/// <param name="PresetId">预设 id（内置默认 = 空串）。</param>
/// <param name="DisplayName">预设显示名（内置默认 = 空串）。</param>
/// <param name="DeckText">预设自带卡组文本（null = 走旧链）。</param>
/// <param name="RelicEntries">预设自带遗物 id 列表（null = 内置遗物表；空列表 = 一件不加）。</param>
internal sealed record HellwarWarGodPreset(
    HellwarWarGodTier Tier,
    string SkinKey,
    bool UpgradeDeck,
    int BonusHp,
    string PresetId = "",
    string DisplayName = "",
    string? DeckText = null,
    IReadOnlyList<string>? RelicEntries = null)
{
    /// <summary>单点召唤这一档的成本（D7 成本表）。</summary>
    internal int Cost => HellwarSummonPolicy.ResolveSummonCost(Tier);

    /// <summary>这是不是内置默认（无自定义预设内容）。</summary>
    internal bool IsBuiltin => PresetId.Length == 0;
}

/// <summary>
/// 战灵召唤 **D7 预设档表**（Phase 2b：罕见 / 稀有 / 先古 三档落地）。
///
/// **首版口径（2026-10-09，我定；后续要调只改这张表）**：
/// <list type="bullet">
/// <item><b>普通</b>：皮肤 = **召唤者自身角色**，卡组 = 该角色初始卡组（原样）—— 与 D7 口径一致，
///   **自动兼容 mod 角色**（这是唯一一档会用 mod 内容的）；</item>
/// <item><b>罕见 / 稀有 / 先古</b>：皮肤 = 指定**原版角色**（静默猎手 / 铁甲战士 / 摄政王），
///   卡组 = 该角色初始卡组**全升级**（先古额外 +25 血上限）——
///   ⚠ **刻意不硬编码任何卡牌 / 遗物 id**：`ModelDb.Character<T>()` + `CharacterModel.StartingDeck`
///   是编译期可校验的引用，改卡池 / 加 mod 都不会静默失效（硬编码 id 一旦拼错就整档召不出来）。</item>
/// </list>
///
/// ⚠ 为什么**首版不挂"额外遗物"**：战神是**战斗中途中途加进来**的，而原版很多强力遗物的效果挂在
/// **开战时机钩子**上（`Akabeko` / `Anchor` / `BagOfPreparation` / `Lantern` … 都是"每场战斗开始时"），
/// 中途加人**不会触发**那些钩子 ⇒ 挂了也只显示、不生效（甚至误导）。⇒ 强度差先由"**卡组是否全升级**"
/// 表达；"挑不依赖开战时机的遗物"留到 Phase 3（托管出牌）之后按实际效果补。
///
/// ⚠ 另一个已知边界：本阶段战神**还不出牌**（托管出牌 = Phase 3）⇒ 档位强度体感要等 Phase 3
/// （先古的血量 +25 现在就有效：更耐打、更占敌人攻击次数）。
/// </summary>
internal static class HellwarWarGodPresets
{
    /// <summary>皮肤键：用**召唤者自己的角色模型**（普通档；自动兼容 mod 角色）。</summary>
    internal const string SkinOfSummoner = "summoner";

    /// <summary>皮肤键：静默猎手（罕见档）。</summary>
    internal const string SkinSilent = "silent";

    /// <summary>皮肤键：铁甲战士（稀有档）。</summary>
    internal const string SkinIronclad = "ironclad";

    /// <summary>皮肤键：摄政王（先古档）。</summary>
    internal const string SkinRegent = "regent";

    /// <summary>先古档的额外血上限（比该角色初始血量更肉）。</summary>
    internal const int AncientBonusHp = 25;

    internal static readonly HellwarWarGodPreset Common =
        new(HellwarWarGodTier.Common, SkinOfSummoner, UpgradeDeck: false, BonusHp: 0);

    internal static readonly HellwarWarGodPreset Rare =
        new(HellwarWarGodTier.Rare, SkinSilent, UpgradeDeck: true, BonusHp: 0);

    internal static readonly HellwarWarGodPreset Epic =
        new(HellwarWarGodTier.Epic, SkinIronclad, UpgradeDeck: true, BonusHp: 0);

    internal static readonly HellwarWarGodPreset Ancient =
        new(HellwarWarGodTier.Ancient, SkinRegent, UpgradeDeck: true, BonusHp: AncientBonusHp);

    /// <summary>全部档位（按成本从低到高）。</summary>
    internal static readonly HellwarWarGodPreset[] All = { Common, Rare, Epic, Ancient };

    /// <summary>
    /// 【无尽战神】的**默认随机池** = 普通 / 罕见 / 稀有（D17：**先古不进随机池** —— 那是【终极九连环】的专属）。
    /// 玩家开启「无尽战神允许开出先古」后改用 <see cref="RandomPoolWithAncient"/>（2026-10-10 用户追加）。
    /// </summary>
    internal static readonly HellwarWarGodPreset[] RandomPool = { Common, Rare, Epic };

    /// <summary>【无尽战神】在"允许开出先古"开关下的随机池（四档全进）。</summary>
    internal static readonly HellwarWarGodPreset[] RandomPoolWithAncient = { Common, Rare, Epic, Ancient };

    /// <summary>按开关取【无尽战神】的随机池（单一来源：运行时与单测共用）。</summary>
    internal static HellwarWarGodPreset[] ResolveRandomPool(bool includeAncient)
        => includeAncient ? RandomPoolWithAncient : RandomPool;

    /// <summary>取某档的预设（非法值兜普通档）。</summary>
    internal static HellwarWarGodPreset Resolve(HellwarWarGodTier tier)
    {
        foreach (HellwarWarGodPreset preset in All)
        {
            if (preset.Tier == tier)
            {
                return preset;
            }
        }

        return Common;
    }

    /// <summary>
    /// 随机池取一个（把随机数收成一个纯函数参数 ⇒ 可单测；运行时用 `RunState.Rng.CombatTargets.NextInt(n)` 传进来）。
    /// </summary>
    internal static HellwarWarGodPreset PickRandom(int roll, bool includeAncient)
    {
        HellwarWarGodPreset[] pool = ResolveRandomPool(includeAncient);
        if (pool.Length == 0)
        {
            return Common;
        }

        int index = ((roll % pool.Length) + pool.Length) % pool.Length;
        return pool[index];
    }

    /// <summary>某档是否在【无尽战神】的随机池里（默认池不含先古；<paramref name="includeAncient"/> 开时含）。</summary>
    internal static bool IsInRandomPool(HellwarWarGodTier tier, bool includeAncient)
    {
        foreach (HellwarWarGodPreset preset in ResolveRandomPool(includeAncient))
        {
            if (preset.Tier == tier)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>诊断用一行（日志里能直接看出"这一档是什么内容"）。</summary>
    internal static string Describe(HellwarWarGodPreset preset)
    {
        string source = preset.IsBuiltin ? "内置默认" : $"预设={preset.DisplayName}({preset.PresetId})";
        return $"{preset.Tier}({source}, 皮肤={preset.SkinKey}, 全升级={preset.UpgradeDeck}, "
            + $"额外血={preset.BonusHp}, 成本={preset.Cost})";
    }

    /// <summary>全部档位的一行摘要（启动自检 / 日志用）。</summary>
    internal static string DescribeAll()
    {
        List<string> lines = new();
        foreach (HellwarWarGodPreset preset in All)
        {
            lines.Add(Describe(preset));
        }

        return string.Join(" | ", lines);
    }
}
