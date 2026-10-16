#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>一条预设的**可用性**判定结果：<see cref="Usable"/>=false 时 <see cref="Reason"/> 是人话原因。</summary>
internal readonly record struct HellwarPresetAvailabilityResult(bool Usable, string Reason)
{
    internal static readonly HellwarPresetAvailabilityResult Ok = new(true, string.Empty);
}

/// <summary>
/// 预设的**可用性判据**（纯逻辑）：这条预设引用的东西在**本机**存不存在。
///
/// <para><b>为什么要它</b>：预设是"跨机器"传的（随 mod 分发 / 别人分享 json）。写预设的人装了某个 mod，
/// 用 <c>skin=那个 mod 的角色</c> + <c>那群 mod 卡</c>；**没装那个 mod 的玩家**拿到这份预设时，
/// 旧行为是"逐项降级"——召出一个**皮肤变召唤者、卡组变角色初始、遗物少几件**的残缺战神，
/// 玩家不知道发生了什么。改成：**引用了本机没有的内容 ⇒ 这条预设直接跳过**（不进候选、不进随机池），
/// 其它预设照常。语义见用户 2026-10-12 的口径。</para>
///
/// <para><b>判据（严格）</b>——任一项缺失即不可用：
/// <list type="bullet">
/// <item><b>角色皮</b>：写了具体角色（非空、非 <c>summoner</c>）而本机没有 ⇒ 不可用；</item>
/// <item><b>卡组</b>：写了文本 ⇒ **每一张**都必须在（缺一张即不可用）；<c>null</c> = 继承该档内置（永远是本体卡）⇒ 不检查；
///   空串 = 不替换（用角色初始卡组）⇒ 由上面的角色检查兜住；</item>
/// <item><b>遗物</b>：写了列表 ⇒ **每一件**都必须在；<c>null</c> = 继承 ⇒ 不检查；空列表 = 一件不加 ⇒ 不检查。</item>
/// </list></para>
///
/// <para>⚠ **附魔不算**：附魔缺失只影响那一张牌的附加效果（卡照给，既有行为 = 跳过附魔 + WARN），
/// 降级很轻，不值得为它整条丢弃——否则第三方 mod 的附魔会让预设大面积消失。</para>
///
/// <para>本类不碰游戏类型：角色/卡/遗物的存在性由调用方以委托注入（便于单测）。</para>
/// </summary>
internal static class HellwarPresetAvailability
{
    /// <summary>「召唤者自身角色」的保留皮肤键（= <c>HellwarWarGodPresets.SkinOfSummoner</c>，此处不引用运行时类型）。</summary>
    internal const string SkinOfSummoner = "summoner";

    /// <summary>原因里最多列几个缺失 id（避免日志刷屏）。</summary>
    private const int MaxListed = 6;

    internal static HellwarPresetAvailabilityResult Evaluate(
        string? skin,
        string? deckText,
        IReadOnlyList<string>? relicEntries,
        Func<string, bool> characterExists,
        Func<string, bool> cardExists,
        Func<string, bool> relicExists)
    {
        string skinKey = (skin ?? string.Empty).Trim();
        if (skinKey.Length > 0 && !string.Equals(skinKey, SkinOfSummoner, StringComparison.OrdinalIgnoreCase))
        {
            if (!SafeExists(characterExists, skinKey))
            {
                return new HellwarPresetAvailabilityResult(false, $"角色皮「{skinKey}」本机没有（多半是没装那个 mod）");
            }
        }

        if (deckText != null && deckText.Trim().Length > 0)
        {
            IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse(deckText);
            if (entries == null)
            {
                // 全是分隔符之类的退化输入：**既不是"缺 mod 内容"，也不该由本判据整条丢弃**
                //（与 HellwarPresetPolicy.Normalize 的口径一致 —— 那里只记问题、运行时回落该档内置卡组）。
                return HellwarPresetAvailabilityResult.Ok;
            }

            List<string> missing = new();
            foreach (HellwarWarGodDeckEntry entry in entries)
            {
                if (!SafeExists(cardExists, entry.Entry))
                {
                    missing.Add(entry.Entry);
                }
            }

            if (missing.Count > 0)
            {
                return new HellwarPresetAvailabilityResult(
                    false,
                    $"卡组里有 {missing.Count} 张本机没有（多半是没装那个 mod）: {Describe(missing)}");
            }
        }

        if (relicEntries is { Count: > 0 })
        {
            List<string> missing = new();
            foreach (string entry in relicEntries)
            {
                string token = (entry ?? string.Empty).Trim();
                if (token.Length > 0 && !SafeExists(relicExists, token))
                {
                    missing.Add(token);
                }
            }

            if (missing.Count > 0)
            {
                return new HellwarPresetAvailabilityResult(
                    false,
                    $"遗物里有 {missing.Count} 件本机没有（多半是没装那个 mod）: {Describe(missing)}");
            }
        }

        return HellwarPresetAvailabilityResult.Ok;
    }

    /// <summary>查表异常按"不存在"处理（宁可不展示，也不要把坏内容召出来）。</summary>
    private static bool SafeExists(Func<string, bool> exists, string entry)
    {
        try
        {
            return exists(entry);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(IReadOnlyList<string> missing)
    {
        StringBuilder builder = new();
        int shown = Math.Min(missing.Count, MaxListed);
        for (int i = 0; i < shown; i++)
        {
            if (i > 0)
            {
                builder.Append('/');
            }

            builder.Append(missing[i]);
        }

        if (missing.Count > shown)
        {
            builder.Append($"…等 {missing.Count} 条");
        }

        return builder.ToString();
    }
}
