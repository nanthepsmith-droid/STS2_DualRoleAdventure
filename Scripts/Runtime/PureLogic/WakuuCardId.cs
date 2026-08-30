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

        string upper = id.ToUpperInvariant();
        return upper.Contains("STRIKE", StringComparison.Ordinal)
            || upper.Contains("DEFEND", StringComparison.Ordinal);
    }
}
