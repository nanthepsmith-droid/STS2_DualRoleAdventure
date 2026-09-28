namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「远端选择」兜底时要回哪种空结果（BUG-23 方案 A，2026-09-28）。
///
/// `PlayerChoiceResult` 是**类型化**的：`AsIndex()` / `AsIndexes()` / `AsCombatCards()` /
/// `AsDeckCards()` / `AsPlayerId()` 遇到不匹配的 `ChoiceType` 一律抛 `InvalidOperationException`
/// ⇒ 兜底时必须给出调用方真正需要的类型，否则只是把"卡死"换成"异常"。
/// </summary>
internal enum PlayerChoiceEmptyResultKind
{
    /// <summary>`AsIndex()` / `AsIndexes()`（`FromSimpleGrid*` / `FromChooseACardScreen` / 遗物三选一 / 卡牌奖励 / 第三方自绘）。</summary>
    Index,

    /// <summary>`AsCombatCards()`（`FromHand*` / `FromCombatPile*`）。</summary>
    CombatCard,

    /// <summary>`AsDeckCards()`（`FromDeck*`）。</summary>
    DeckCard,

    /// <summary>`AsPlayerId()`（`MendRestSiteOption` 的"指定一名玩家"）。</summary>
    Player,
}

/// <summary>
/// 「这个 `WaitForRemoteChoice` 调用方是谁、需要哪种结果类型」的纯逻辑（BUG-23；可单测）。
///
/// 为什么要它：`PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId)` 这一层看不到调用方，
/// 而返回值类型必须与调用方后面的 `As*()` 一致。于是我们做两件事，两件都是纯字符串判定 ⇒ 进 PureLogic：
/// <list type="number">
/// <item><b>还原真实调用方</b>：异步方法在栈上只露出编译器生成的状态机
///   （`&lt;FromSimpleGrid&gt;d__89` + `MoveNext`）⇒ 要还原成（`CardSelectCmd`, `FromSimpleGrid`）；
///   非状态机的 `MoveNext`（异步机制内部帧）则要丢掉；</item>
/// <item><b>映射结果类型</b>：按 `sts2src` 里每个 `WaitForRemoteChoice` 调用点紧跟的 `As*()`
///   建表（见 <see cref="Classify"/> 的表），**未知调用方默认 `Index`**
///   —— 第三方自绘选牌实测都是 indexes（沙耶 mod `PigmentCellCard` → `SelectCenteredBranchCards`），
///   而"猜错类型会抛异常"的风险由日志里的 `调用方=…` 兜住（下轮按日志补表）。</item>
/// </list>
/// </summary>
internal static class PlayerChoiceCallerClassifier
{
    internal const string UnknownType = "unknown";
    internal const string UnknownMethod = "unknown";

    /// <summary>选择同步器自身（栈上永远会先看到它，必须跳过）。</summary>
    internal const string SynchronizerTypeName = "PlayerChoiceSynchronizer";

    internal const string WaitForRemoteChoiceMethod = "WaitForRemoteChoice";

    /// <summary>兜底补丁类简名（栈上也会出现，必须跳过）。</summary>
    internal const string FallbackPatchTypeName = "PlayerChoiceSynchronizerRemoteChoiceFallbackPatch";

    /// <summary>
    /// 把一帧栈帧还原成「真实调用方（类型名，方法名）」。
    /// 返回 false = 这一帧不该被当作调用方（异步机制内部帧、或信息不足），继续往上找。
    ///
    /// 规则：<list type="bullet">
    /// <item>`&lt;方法名&gt;d__NN`（编译器状态机）⇒ 用外层类型 + 剥出的方法名；</item>
    /// <item>其它带 `MoveNext` 的帧 ⇒ 丢掉（不是业务方法）；</item>
    /// <item>普通方法帧 ⇒ 原样返回。</item>
    /// </list>
    /// </summary>
    internal static bool TryNormalizeFrame(
        string? declaringTypeName,
        string? declaringOuterTypeName,
        string? methodName,
        out string callerType,
        out string callerMethod)
    {
        callerType = UnknownType;
        callerMethod = UnknownMethod;

        if (string.IsNullOrEmpty(declaringTypeName) || string.IsNullOrEmpty(methodName))
        {
            return false;
        }

        int angle = declaringTypeName.IndexOf('>');
        bool isStateMachine = declaringTypeName[0] == '<' && angle > 1;
        if (isStateMachine)
        {
            callerMethod = declaringTypeName.Substring(1, angle - 1);
            callerType = string.IsNullOrEmpty(declaringOuterTypeName) ? declaringTypeName : declaringOuterTypeName!;
            return true;
        }

        if (string.Equals(methodName, "MoveNext", System.StringComparison.Ordinal))
        {
            // 非状态机的 MoveNext（异步机制内部帧）
            return false;
        }

        callerType = declaringTypeName;
        callerMethod = methodName;
        return true;
    }

    /// <summary>这一帧是不是"选择同步器 / 本补丁自己"（要跳过的内部帧）。</summary>
    internal static bool IsInternalFrame(string callerType, string callerMethod)
    {
        return string.Equals(callerType, SynchronizerTypeName, System.StringComparison.Ordinal)
            || string.Equals(callerType, FallbackPatchTypeName, System.StringComparison.Ordinal)
            || string.Equals(callerMethod, WaitForRemoteChoiceMethod, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 按调用方映射它需要的结果类型。表来自 `sts2src`（逐个 `WaitForRemoteChoice` 调用点核对）：
    /// <code>
    /// CardSelectCmd.FromHand* / FromCombatPile*            → AsCombatCards()  → CombatCard
    /// CardSelectCmd.FromDeck*                              → AsDeckCards()    → DeckCard
    /// CardSelectCmd.FromSimpleGrid* / FromChooseACardScreen → AsIndex(es)      → Index
    /// RelicSelectCmd.FromChooseARelicScreen                → AsIndex()        → Index
    /// CardReward.OnSelect                                  → AsIndexOrNull()  → Index
    /// MendRestSiteOption.OnSelect                          → AsPlayerId()     → Player
    /// 其它（含第三方自绘选牌）                              → 默认 Index
    /// </code>
    /// </summary>
    internal static PlayerChoiceEmptyResultKind Classify(string callerType, string callerMethod)
    {
        if (string.Equals(callerType, "MendRestSiteOption", System.StringComparison.Ordinal))
        {
            return PlayerChoiceEmptyResultKind.Player;
        }

        if (string.Equals(callerType, "CardSelectCmd", System.StringComparison.Ordinal))
        {
            if (StartsWith(callerMethod, "FromHand") || StartsWith(callerMethod, "FromCombatPile"))
            {
                return PlayerChoiceEmptyResultKind.CombatCard;
            }

            if (StartsWith(callerMethod, "FromDeck"))
            {
                return PlayerChoiceEmptyResultKind.DeckCard;
            }
        }

        // FromSimpleGrid* / FromChooseACardScreen / RelicSelectCmd / CardReward / 未知（含第三方自绘）
        return PlayerChoiceEmptyResultKind.Index;
    }

    private static bool StartsWith(string value, string prefix)
    {
        return value.StartsWith(prefix, System.StringComparison.Ordinal);
    }
}
