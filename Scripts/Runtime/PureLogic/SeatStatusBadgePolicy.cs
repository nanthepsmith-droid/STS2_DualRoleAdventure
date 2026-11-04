using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// QOL-3（2026-11-04）：顶部「玩家状态条」上的**席位状态角标**行规划（纯逻辑，可直接单测）。
///
/// <para>只做两件事：把「污染 / 沙坑」的层数拼成一行行文本；0 层不显示（避免空行占位）。
/// 顺序固定为 污染 → 沙坑（与战斗内的观感一致：先自己身上的，再 boss 那边属于自己那份的）。</para>
///
/// <para>⚠ 这两个数字都必须是**该玩家自己那一份**：
/// 污染 = 他身上的 <c>TaintedPower</c>；沙坑 = 敌方 creature 上 <c>Target == 该玩家</c> 的
/// <c>SandpitPower</c> 实例（每玩家一份，不能读"当前前台那份"）。</para>
/// </summary>
internal static class SeatStatusBadgePolicy
{
    internal const string TaintedLabel = "污染";

    internal const string SandpitLabel = "沙坑";

    /// <summary>拼行（0 层跳过；负数按 0 处理）。</summary>
    internal static List<string> BuildLabels(int tainted, int sandpit)
    {
        List<string> labels = new(2);

        if (tainted > 0)
        {
            labels.Add($"{TaintedLabel} {tainted}");
        }

        if (sandpit > 0)
        {
            labels.Add($"{SandpitLabel} {sandpit}");
        }

        return labels;
    }
}
