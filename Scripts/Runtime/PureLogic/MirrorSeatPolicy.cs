using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 共享镜像的席位判据（r147）。
///
/// 本 mod 的"共享"语义只适用于**本地多控自己的席位**：被第三方（Co-op Bots）加进来的合成 Bot
/// 是独立队友 —— 它的金币 / 遗物 / 药水 / 藏宝图 / 卡牌既不该复制给我们，我们也不该复制给它。
///
/// 这条规则**两头都要看**。r146 只补了"目标"一头（不把我们的东西发给 Bot），
/// 于是 Bot 自己打出来的奖励照样被复制给两个真人 —— 实机（2026-09-25 第一幕）最典型的是它
/// 「七咒之戒」的额外战斗掉落遗物（含第一幕 BOSS 的千咒卷轴，8 件 × 2 人，且是直接塞进背包、
/// 真人连"要不要拿"都没得选）与每场战斗的金币（还被 Co-op Bots 的金币作弊一起放大了 3 倍）。
///
/// 抽成纯函数是为了把"来源与目标都必须是我们自己的席位"这一条钉进单测，避免第三次踩同一个坑。
/// </summary>
internal static class MirrorSeatPolicy
{
    /// <summary>来源席位是否属于本地多控会话（不是就不该发起任何镜像）。</summary>
    internal static bool IsMirrorableSource(ulong sourceNetId, IReadOnlyCollection<ulong> localSeatNetIds)
    {
        return sourceNetId != 0UL && localSeatNetIds.Contains(sourceNetId);
    }

    /// <summary>
    /// 是否应把 <paramref name="sourceNetId"/> 这次获得镜像给 <paramref name="targetNetId"/>：
    /// 两头都得是本地席位，且不能是自己镜给自己。
    /// </summary>
    internal static bool ShouldMirrorTo(ulong sourceNetId, ulong targetNetId, IReadOnlyCollection<ulong> localSeatNetIds)
    {
        return sourceNetId != 0UL
            && targetNetId != 0UL
            && sourceNetId != targetNetId
            && localSeatNetIds.Contains(sourceNetId)
            && localSeatNetIds.Contains(targetNetId);
    }
}
