namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 「预设战神的额外血上限」怎么算（r289；2026-10-20 用户拍板）。
///
/// 口径：<c>bonusHp</c> **正数 = 加血上限、负数 = 减血上限**，两者都把**当前血补满到新上限**
/// （否则会显示成"半血出生"）。负数**必须收在最少 1 点**：
/// 原版 <c>Creature.SetMaxHpInternal</c> 对负数直接抛 <c>ArgumentException</c>
/// （`amount must be non-negative`），而 0 上限的怪会立刻进死亡流程 ⇒ 下限取 1 最稳。
///
/// 为什么要单开纯逻辑：归一（<see cref="HellwarPresetPolicy.Normalize"/>）阶段**拿不到角色基础血量**
/// （普通档的战神可能用 mod 角色的皮），下限只能在施加时算 ⇒ 判据要可单测。
/// </summary>
internal static class HellwarPresetHpPolicy
{
    /// <summary>血上限的硬下限（负数扣到 0 及以下时收在这里）。</summary>
    internal const int MinimumMaxHp = 1;

    /// <summary>0 = 不调整（继承 / 显式 0）⇒ 调用方不必走 SetMaxHp 那条链。</summary>
    internal static bool ShouldApply(int bonusHp) => bonusHp != 0;

    /// <summary>
    /// 把「角色基础血上限 + 预设额外血」算成最终上限：正负都直接用，只在低于
    /// <see cref="MinimumMaxHp"/> 时收住。用 <see cref="long"/> 中转，防极端值溢出。
    /// </summary>
    internal static int ResolveMaxHp(int baseMaxHp, int bonusHp)
    {
        if (bonusHp == 0)
        {
            return baseMaxHp;
        }

        long target = (long)baseMaxHp + bonusHp;
        if (target < MinimumMaxHp)
        {
            return MinimumMaxHp;
        }

        return target > int.MaxValue ? int.MaxValue : (int)target;
    }

    /// <summary>这次调整是否被下限收住（打日志用，说明"扣得比血还多"）。</summary>
    internal static bool WasClampedToMinimum(int baseMaxHp, int bonusHp)
        => (long)baseMaxHp + bonusHp < MinimumMaxHp;
}
