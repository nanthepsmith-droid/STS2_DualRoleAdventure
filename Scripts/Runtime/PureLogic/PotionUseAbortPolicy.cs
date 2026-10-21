using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 自动用药「异常路径收尾」的判定纯函数（§待查-11，2026-10-21）。
///
/// 背景（实机 marker r298，`godot2026-10-21T20.52.32` 长局）：
/// <c>瓦库自动用药异常（跳过该瓶）: potion=FIRE_POTION, error=Nullable object must have a value.</c>
/// 紧接着原版 <c>Tried to pop model RELIC.LOCAL_WAKUU_FORM_RELIC … but POTION.FIRE_POTION … was on the top
/// of the stack instead! (Stack size: 2)</c>。
///
/// 已定性的根因（不是目标为空）：那一按发生在本局**正在退出到主菜单**时
/// （同段日志 `[BGM探针] StopMusic … 调用方=&lt;CloseToMenu&gt;` → 随后 `RunManager.CleanUp(graceful=True)`）。
/// 原版 <c>PotionModel.OnUseWrapper</c> 在 `PushModel(this)` 之后还有 ~0.5s 的投掷动画 `await`，
/// 跨过清理点后 <c>LocalContext.NetId</c> 已被置空，再走到
/// <c>new BranchingPlayerChoiceContext(this, LocalContext.NetId.Value, …)</c> ⇒ `Nullable&lt;ulong&gt;.Value`
/// 抛 <c>InvalidOperationException</c>；而它的 <c>PopModel</c> 在异常路径上永远不执行 ⇒
/// **药水模型留在 choice context 栈顶**，之后同一个 hook 里原版遗物模型的 pop 就错位。
///
/// 本文件只放两条与游戏类型无关的判定，便于单测：
/// ① 要不要继续弹「我自己」的残留模型（原版 <c>PopModel</c> 在栈顶不是自己时只打 ERROR 且**不弹**，
///    所以只能在自己确实是栈顶时调用它）；
/// ② 异常详情怎么拼成一行（带类型名与栈帧，替代原来只打 `Message` 的做法）。
/// 「是不是清理期预期中止」沿用既有口径 <see cref="WakuuTeardownPolicy"/>，不在这里重复实现。
/// </summary>
internal static class PotionUseAbortPolicy
{
    /// <summary>
    /// 单次收尾最多弹几层。正常路径只会残留 1 层（`OnUseWrapper` 只 push 一次自己的模型）；
    /// 上限只为防御「取栈/弹栈行为意外变化」时的死循环，不是预期用量。
    /// </summary>
    public const int DefaultMaxResidualPops = 4;

    /// <summary>
    /// 是否再弹一层残留药水模型：**栈顶必须就是这瓶药水**（引用相等）且未超过上限。
    /// 栈顶是别人时一律不动 —— 宁可留着，也不要给原版制造第二次错位。
    /// </summary>
    /// <param name="topIsPotion">`ReferenceEquals(choiceContext.LastInvolvedModel, potion)`。</param>
    /// <param name="poppedSoFar">本次收尾已经弹出的层数。</param>
    /// <param name="maxPops">上限，默认 <see cref="DefaultMaxResidualPops"/>。</param>
    public static bool ShouldPopResidualModel(bool topIsPotion, int poppedSoFar, int maxPops = DefaultMaxResidualPops)
    {
        return topIsPotion && poppedSoFar >= 0 && poppedSoFar < maxPops;
    }

    /// <summary>
    /// 把异常压成一行日志：`类型名: 消息 | 栈帧…`（截断到 <paramref name="maxChars"/>）。
    /// 只打 `Message` 会把「谁抛的」这条最重要的线索丢掉（§待查-11 正是这样被卡了一轮）。
    /// </summary>
    public static string DescribeException(Exception exception, int maxChars = 900)
    {
        if (exception == null)
        {
            return "unknown";
        }

        string text = $"{exception.GetType().Name}: {exception.Message}";

        string? stack = exception.StackTrace;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            string flattened = stack!
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace('\n', ' ')
                .Replace("   ", " ", StringComparison.Ordinal)
                .Trim();
            text = $"{text} | {flattened}";
        }

        if (maxChars > 0 && text.Length > maxChars)
        {
            text = string.Concat(text.AsSpan(0, maxChars), "…");
        }

        return text;
    }
}
