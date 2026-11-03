using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 读符文候选的**稀有度**（v1.8.0，用户 2026-10-31 新口径：稀有度更高者优先）。
///
/// <para>全部走反射，**不引用海克斯 dll**（与 <see cref="MainModBridge"/> 同一姿态）：</para>
/// <list type="bullet">
/// <item><c>HextechCatalog.TryGetPlayerRuneRarity(RelicModel, out HextechRarityTier)</c>（公开 API，首选）；</item>
/// <item>兜底 <c>HextechRuneSelectionScreen.PresenterOptionRarity(int)</c>（internal，海克斯自己画卡时用的同一份判据）；</item>
/// <item>reroll 升档目标：<c>PresenterGoldenRerollActive/Rarity</c>（金色 reroll 升一档）、
///   保底首抽槽（<c>HextechRunePoolBuilder.IsGuaranteedFirstPickRune</c> + reroll 闭包里的 <c>stageRarity</c> = 本幕档位）。</item>
/// </list>
///
/// <para><b>失败姿态</b>：任何一环读不到 ⇒ 该项返回"未知"（<see cref="RuneRarityPolicy.UnknownRank"/>），
/// 选择逻辑自动退化成纯抓率、reroll 逻辑自动退化成 v1.7.x 的"三张全烂才 roll"。
/// 只 WARN 一次，绝不打断作答。</para>
/// </summary>
internal static class RuneRarityReader
{
    private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticAll = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const string CatalogTypeName = "HextechRunes.HextechCatalog";
    private const string PoolBuilderTypeName = "HextechRunes.HextechRunePoolBuilder";

    /// <summary>`TryGetPlayerRuneRarity(RelicModel, out HextechRarityTier)`（公开 API）。</summary>
    private static MethodInfo? _tryGetRuneRarity;

    /// <summary>`PresenterOptionRarity(int) → HextechRarityTier?`（internal 实例方法）。</summary>
    private static MethodInfo? _presenterOptionRarity;

    /// <summary>`PresenterGoldenRerollActive`（internal 实例属性）。</summary>
    private static PropertyInfo? _goldenRerollActive;

    /// <summary>`PresenterGoldenRerollRarity`（internal 实例属性，`HextechRarityTier?`）。</summary>
    private static PropertyInfo? _goldenRerollRarity;

    /// <summary>`HextechRunePoolBuilder.IsGuaranteedFirstPickRune(RelicModel)`（internal static）。</summary>
    private static MethodInfo? _isGuaranteedFirstPickRune;

    /// <summary>屏幕上的 `_rerollFunc`（reroll 闭包，里面捕获了本幕档位 `stageRarity`）。</summary>
    private static FieldInfo? _rerollFuncField;

    private static bool _warned;

    /// <summary>挂补丁时解析一次（缺项只 WARN，不抛）。</summary>
    internal static void Install(Type screenType)
    {
        try
        {
            Type? catalogType = screenType.Assembly.GetType(CatalogTypeName, throwOnError: false) ?? Entry.FindType(CatalogTypeName);
            _tryGetRuneRarity = catalogType?.GetMethod("TryGetPlayerRuneRarity", StaticAll);

            Type? poolBuilderType = screenType.Assembly.GetType(PoolBuilderTypeName, throwOnError: false) ?? Entry.FindType(PoolBuilderTypeName);
            _isGuaranteedFirstPickRune = poolBuilderType?.GetMethod("IsGuaranteedFirstPickRune", StaticAll);

            _presenterOptionRarity = screenType.GetMethod("PresenterOptionRarity", InstanceAll);
            _goldenRerollActive = screenType.GetProperty("PresenterGoldenRerollActive", InstanceAll);
            _goldenRerollRarity = screenType.GetProperty("PresenterGoldenRerollRarity", InstanceAll);
            _rerollFuncField = screenType.GetField("_rerollFunc", InstanceAll);
        }
        catch (Exception exception)
        {
            WarnOnce($"解析符文稀有度反射面失败（选择将退化成纯抓率）: {exception.GetType().Name}: {exception.Message}");
        }

        if (_tryGetRuneRarity == null && _presenterOptionRarity == null)
        {
            WarnOnce("符文稀有度读取面没解析到（HextechCatalog.TryGetPlayerRuneRarity / PresenterOptionRarity 都缺），"
                + "瓦库仍按抓率挑（行为与 v1.7.x 一致）。");
        }

        if (_goldenRerollActive == null || _goldenRerollRarity == null)
        {
            Log.Info($"{Entry.LogPrefix} 符文稀有度: 金色 reroll 面没解析到"
                + $"（active={_goldenRerollActive != null}, rarity={_goldenRerollRarity != null}）⇒ 不把它算作升档机会。");
        }

        // v1.8.0 就绪锚点（正向）：实机只看这一行就知道「升档机会」能不能算得出来。
        Log.Info($"{Entry.LogPrefix} 符文稀有度反射面: "
            + $"档位={(_tryGetRuneRarity != null ? "HextechCatalog" : _presenterOptionRarity != null ? "PresenterOptionRarity" : "缺")}, "
            + $"金色reroll={(_goldenRerollActive != null && _goldenRerollRarity != null ? "可读" : "缺")}, "
            + $"保底首抽={(_isGuaranteedFirstPickRune != null ? "可读" : "缺")}, "
            + $"本幕档位={(_rerollFuncField != null ? "可读" : "缺")}");
    }

    /// <summary>读每槽稀有度；返回是否**至少有一槽**读到了（false = 全未知 ⇒ 纯抓率）。</summary>
    internal static bool TryReadRanks(object screen, IReadOnlyList<RelicModel> options, out int[] ranks)
    {
        ranks = new int[options.Count];
        for (int i = 0; i < ranks.Length; i++)
        {
            ranks[i] = RuneRarityPolicy.UnknownRank;
        }

        if (_tryGetRuneRarity == null && _presenterOptionRarity == null)
        {
            return false;
        }

        bool any = false;
        for (int i = 0; i < options.Count; i++)
        {
            int rank = TryReadOne(screen, options[i], i);
            ranks[i] = rank;
            any |= rank >= 0;
        }

        return any;
    }

    /// <summary>
    /// 算每槽 **reroll 后可达的档位**（与海克斯自己的 <c>HextechRunePoolBuilder.GetRerollRarity</c> 同口径）：
    /// 金色 reroll 激活 ⇒ 所有槽升到 <c>UpgradedRarity</c>；否则只有「保底首抽符文」槽会用本幕档位，
    /// 其余槽普通 reroll **保持原档**。返回 true = 至少有一槽的可达档与当前档不同。
    /// </summary>
    internal static bool TryReadRerollTargets(
        object screen,
        IReadOnlyList<RelicModel> options,
        int[] currentRanks,
        out int[] targets,
        out string note)
    {
        targets = (int[])currentRanks.Clone();
        note = "普通 reroll 保持本档";
        bool changed = false;

        int? goldenRank = TryReadGoldenRank(screen);
        if (goldenRank.HasValue)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = goldenRank.Value;
            }

            note = $"金色 reroll → {RuneRarityPolicy.RankName(goldenRank.Value)}";
            return true;
        }

        int? stageRank = TryReadStageRarity(screen);
        if (stageRank.HasValue && _isGuaranteedFirstPickRune != null)
        {
            int hits = 0;
            for (int i = 0; i < options.Count && i < targets.Length; i++)
            {
                if (IsGuaranteedFirstPick(options[i]))
                {
                    targets[i] = stageRank.Value;
                    hits++;
                    changed = true;
                }
            }

            if (hits > 0)
            {
                note = $"保底首抽槽 → 本幕 {RuneRarityPolicy.RankName(stageRank.Value)}（{hits} 槽）";
            }
        }

        return changed;
    }

    // ---- 单项读取 ---------------------------------------------------------------------------------

    private static int TryReadOne(object screen, RelicModel? relic, int slot)
    {
        if (_tryGetRuneRarity != null && relic != null)
        {
            try
            {
                object?[] args = { relic, null };
                if (_tryGetRuneRarity.Invoke(null, args) is true)
                {
                    int rank = RankOf(args[1]);
                    if (rank >= 0)
                    {
                        return rank;
                    }
                }
            }
            catch (Exception exception)
            {
                WarnOnce($"读取符文稀有度失败（改用兜底/未知）: {Describe(exception)}");
            }
        }

        if (_presenterOptionRarity != null)
        {
            try
            {
                return RankOf(_presenterOptionRarity.Invoke(screen, new object[] { slot }));
            }
            catch (Exception exception)
            {
                WarnOnce($"读取符文稀有度（PresenterOptionRarity）失败: {Describe(exception)}");
            }
        }

        return RuneRarityPolicy.UnknownRank;
    }

    private static int? TryReadGoldenRank(object screen)
    {
        if (_goldenRerollActive == null || _goldenRerollRarity == null)
        {
            return null;
        }

        try
        {
            if (_goldenRerollActive.GetValue(screen) is not true)
            {
                return null; // 未激活 ⇒ reroll 不用升级档（仅"可以激活"不算）
            }

            int rank = RankOf(_goldenRerollRarity.GetValue(screen));
            return rank >= 0 ? rank : null;
        }
        catch (Exception exception)
        {
            WarnOnce($"读取金色 reroll 档位失败: {Describe(exception)}");
            return null;
        }
    }

    /// <summary>本幕符文档位（reroll 闭包捕获的 `stageRarity`）；读不到返回 null。</summary>
    private static int? TryReadStageRarity(object screen)
    {
        if (_rerollFuncField == null)
        {
            return null;
        }

        try
        {
            if (_rerollFuncField.GetValue(screen) is not Delegate func)
            {
                return null;
            }

            object? closure = func.Target;
            if (closure == null)
            {
                return null;
            }

            FieldInfo? stageField = closure.GetType().GetField("stageRarity", InstanceAll);
            if (stageField == null)
            {
                return null; // 闭包字段改名 ⇒ 只影响"保底首抽槽"这条升档机会
            }

            int rank = RankOf(stageField.GetValue(closure));
            return rank >= 0 ? rank : null;
        }
        catch (Exception exception)
        {
            WarnOnce($"读取本幕符文档位失败（保底首抽槽不算升档机会）: {Describe(exception)}");
            return null;
        }
    }

    private static bool IsGuaranteedFirstPick(RelicModel? relic)
    {
        if (_isGuaranteedFirstPickRune == null || relic == null)
        {
            return false;
        }

        try
        {
            return _isGuaranteedFirstPickRune.Invoke(null, new object?[] { relic }) is true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把 <c>HextechRarityTier</c>（或其 Nullable 的装箱值）换算成档位序号。</summary>
    private static int RankOf(object? value)
    {
        if (value == null)
        {
            return RuneRarityPolicy.UnknownRank;
        }

        try
        {
            string name = value.ToString() ?? string.Empty;
            if (name.IndexOf("Prismatic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RuneRarityPolicy.PrismaticRank;
            }

            if (name.IndexOf("Gold", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RuneRarityPolicy.GoldRank;
            }

            if (name.IndexOf("Silver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return RuneRarityPolicy.SilverRank;
            }

            int numeric = Convert.ToInt32(value);
            return numeric is >= RuneRarityPolicy.SilverRank and <= RuneRarityPolicy.PrismaticRank
                ? numeric
                : RuneRarityPolicy.UnknownRank;
        }
        catch
        {
            return RuneRarityPolicy.UnknownRank;
        }
    }

    private static string Describe(Exception exception)
    {
        Exception inner = exception is TargetInvocationException { InnerException: not null } tie
            ? tie.InnerException!
            : exception;
        return $"{inner.GetType().Name}: {inner.Message}";
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} {message}");
    }
}
