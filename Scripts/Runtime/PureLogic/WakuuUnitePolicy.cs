namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「我们联合」（瓦库四功能之三）的**纯判定**：战斗界面上那个按钮该不该显示 / 能不能点。
///
/// 分工（R0 规则）：本类只做「几个事实 → 一个结论」，**取数与副作用全在**
/// <see cref="LocalWakuuUniteRuntime"/>（上帝对象只做翻译）。这样"什么条件下能发动"
/// 有单测钉住，改动不会漂。
///
/// 语义（2026-10-06 用户拍板）：
/// <list type="bullet">
/// <item>触发形态 = **战斗内 HUD 按钮**（不是开战自动弹窗），**每场战斗一次**；</item>
/// <item>两步联动 = ① 自己卡组复制 1 张 → 指定瓦库手牌；② 指定瓦库卡组复制 1 张 → 自己手牌（**双向都是复制**）。</item>
/// </list>
/// </summary>
internal static class WakuuUnitePolicy
{
    /// <summary>
    /// 按钮是否显示且可点。任一条不满足都不显示（宁可不给按钮，也不要给出点了没反应的按钮）。
    /// </summary>
    /// <param name="featureEnabled">设置页「战斗联合（瓦库四功能）」开关。</param>
    /// <param name="coopSessionActive">本地多控回环会话 + 单人冒险模式（其余场景与本功能无关）。</param>
    /// <param name="combatInProgress">战斗进行中（非战斗时没有"战斗手牌"可注入）。</param>
    /// <param name="alreadyUsedThisCombat">本场战斗已发动过（每场一次；取消到没成功任何一步不算用掉）。</param>
    /// <param name="candidateCount">可联合的瓦库候选数（= 存活且卡组非空的瓦库形态席位）。</param>
    /// <param name="actorDeckCount">发起者（真人）自己卡组的牌数 —— 步骤①要从中选一张。</param>
    internal static bool ShouldShowButton(
        bool featureEnabled,
        bool coopSessionActive,
        bool combatInProgress,
        bool alreadyUsedThisCombat,
        int candidateCount,
        int actorDeckCount)
    {
        if (!featureEnabled || !coopSessionActive || !combatInProgress || alreadyUsedThisCombat)
        {
            return false;
        }

        return candidateCount > 0 && actorDeckCount > 0;
    }

    /// <summary>
    /// 本次发动是否算「用掉这一场的机会」：**至少成功一步**才算。
    /// 用户视角：点开看了看又全取消（或没牌可选）应当还能重来，不能因为一次误点丢掉整场机会。
    /// </summary>
    internal static bool ConsumesCombatChance(bool gaveToVakuu, bool tookFromVakuu)
    {
        return gaveToVakuu || tookFromVakuu;
    }
}
