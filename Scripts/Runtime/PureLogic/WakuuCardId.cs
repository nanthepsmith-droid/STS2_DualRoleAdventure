using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 卡牌 id 判定纯函数（从 LocalWakuuRestAutoChoice.IsBasicStrikeOrDefend 原样搬移）。
/// </summary>
internal static class WakuuCardId
{
    /// <summary>
    /// 基础打击/防御识别：卡 id 含 STRIKE/DEFEND（覆盖各角色变体与酒狐等 mod 命名）。
    /// </summary>
    public static bool IsBasicStrikeOrDefendId(string id)
    {
        if (id == null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        return IsStrikeId(id) || IsDefendId(id);
    }

    /// <summary>打击类基础卡识别：id 含 STRIKE（各角色变体、多级打击）。null 返回 false。</summary>
    public static bool IsStrikeId(string? id)
    {
        return id != null && id.ToUpperInvariant().Contains("STRIKE", StringComparison.Ordinal);
    }

    /// <summary>防御类基础卡识别：id 含 DEFEND（各角色变体、多级防御）。null 返回 false。</summary>
    public static bool IsDefendId(string? id)
    {
        return id != null && id.ToUpperInvariant().Contains("DEFEND", StringComparison.Ordinal);
    }
}
