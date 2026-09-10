namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管「视角策略」取值常量（改进-2 / Phase 0）。
///
/// 背景：本地多控下瓦库（自动托管角色）会因回合循环、Hook 入队、选牌兜底等原因把真人视角
/// 抢过去，多瓦库时来回跳。原本只有一个布尔开关 <c>backgroundMode</c>（后台托管 = 不切前台），
/// 粒度太粗；本档位把它细化为三档，默认 <see cref="Never"/>（不跟随），用户 2026-09-10 拍板。
/// </summary>
internal static class WakuuViewModes
{
    /// <summary>不跟随（默认）：瓦库全程不抢视角；仅保留两处防软锁兜底（见 <see cref="WakuuViewPolicy"/>）。</summary>
    public const string Never = "never";

    /// <summary>仅关键节点跟随：瓦库**回合开始**时切过去一次（看到轮到谁、抽了什么），日常出牌不跟随。</summary>
    public const string KeyNodes = "keyNodes";

    /// <summary>全程跟随：回合开始/结束、Hook 入队、出牌前都跟随（≈ 关闭「后台托管」的观感）。</summary>
    public const string Always = "always";

    public const string Default = Never;

    /// <summary>规范化取值（大小写/空白容忍）；非法值一律回退默认 <see cref="Never"/>。</summary>
    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "keynodes" => KeyNodes,
            "always" => Always,
            _ => Never,
        };
    }
}

/// <summary>
/// 视角切换的触发场景：决定「这次切前台是为什么」，策略纯函数据此判定。
/// 每个 Harmony 前缀 / 运行时入口使用与自己语义一致的场景，禁止用错（用错会让兜底失效或视角乱跳）。
/// </summary>
internal enum WakuuViewTrigger
{
    /// <summary>回合开始（<c>CombatManager.SetupPlayerTurn</c> 前缀）。</summary>
    TurnStart,

    /// <summary>回合结束 / 弃牌（<c>CombatManager.DoTurnEnd</c> / <c>FlushPlayerHand</c> 前缀）。</summary>
    TurnEnd,

    /// <summary>Hook 动作入队瞬间（<c>ActionQueueSynchronizer.EnqueueHookAction</c> 前缀）。</summary>
    HookEnqueue,

    /// <summary>瓦库出牌前的主动切视角（<c>LocalWakuuRelicRuntime.EnsureWakuuPerspective</c>）。</summary>
    ReactivePlay,

    /// <summary>
    /// 作用域外「需要真人处理」的选牌（<c>CardSelectForegroundSwitchPatch</c> 兜底）：
    /// **防软锁**，档位无效 —— 无全局选择器时必须切给真人，否则流程挂死。
    /// </summary>
    HumanInteractionChoice,

    /// <summary>
    /// 交互安全网超时救援（<c>LocalWakuuSafetyNet</c>）：**防软锁**，档位无效，必须切。
    /// 当前由安全网直接切前台，不经过本判定；枚举保留以固化语义、避免日后误用。
    /// </summary>
    SafetyNetStall,
}

/// <summary>
/// 瓦库托管「视角策略」判定纯函数（改进-2 / Phase 0）。
///
/// 单一事实来源：所有「是否为瓦库切换前台」的判定都收敛到这里，避免散落在
/// <c>CombatManagerTurnHookForegroundPatch</c> / <c>HookEnqueueForegroundPatch</c> /
/// <c>CardSelectForegroundSwitchPatch</c> / <c>LocalWakuuRelicRuntime.EnsureWakuuPerspective</c> /
/// <c>LocalWakuuSafetyNet</c> 各处各自为政（r105~r106 的教训：一处漏挂守卫就会吞掉真人操作）。
///
/// 不变式：
/// 1. **只治理「瓦库形态」角色**（<c>isWakuuFormPlayer</c>）——真人/未开启形态一律维持既有切前台行为；
/// 2. **后台托管（<c>backgroundMode</c>）关闭时档位不生效**（等价全程跟随），与旧版行为严格一致；
/// 3. **两处防软锁兜底在后台托管开启时不看档位**：安全网救援、作用域外真人交互选牌，该切就切。
/// </summary>
internal static class WakuuViewPolicy
{
    /// <summary>
    /// 是否**抑制**（= 不执行）这次为瓦库形态角色切换前台的动作。
    /// </summary>
    /// <param name="configuredMode">配置档位原始值（<see cref="WakuuViewModes"/>，内部会规范化）。</param>
    /// <param name="backgroundMode">「后台托管（不切前台）」总开关；关闭时档位不生效。</param>
    /// <param name="trigger">本次切换的语义场景。</param>
    /// <param name="isWakuuFormPlayer">目标角色是否处于【瓦库形态】托管。</param>
    /// <param name="willAutoAnswer">
    /// 仅 <see cref="WakuuViewTrigger.HumanInteractionChoice"/> 有意义：
    /// 这次选牌是否**会被自动作答、不弹 UI**（有全局选择器，或该入口对瓦库走「作用域外自动作答」，
    /// 见 <c>CardSelectWakuuTurnStartAutoAnswerPatch</c>）。为 true 时切换没有意义 → 抑制。
    /// </param>
    /// <returns>true = 抑制切换（保持当前视角）；false = 按既有逻辑切前台。</returns>
    public static bool ShouldSuppressSwitch(
        string? configuredMode,
        bool backgroundMode,
        WakuuViewTrigger trigger,
        bool isWakuuFormPlayer,
        bool willAutoAnswer = false)
    {
        // 不变式 1：只治理瓦库形态角色
        if (!isWakuuFormPlayer)
        {
            return false;
        }

        // 不变式 2：后台托管关闭 = 旧的「全程跟随」，档位不生效（放在兜底之前以严格保持向后兼容）
        if (!backgroundMode)
        {
            return false;
        }

        // 不变式 3：防软锁兜底不吃档位
        if (trigger == WakuuViewTrigger.SafetyNetStall)
        {
            return false;
        }

        if (trigger == WakuuViewTrigger.HumanInteractionChoice)
        {
            // 会被自动作答（不弹 UI）⇒ 切了也没意义（抑制）；
            // 不会被自动作答 ⇒ 必须切给真人处理（不抑制）。这是唯一允许「作用域外切前台」的路径。
            return willAutoAnswer;
        }

        return WakuuViewModes.Normalize(configuredMode) switch
        {
            // 全程跟随
            WakuuViewModes.Always => false,
            // 仅关键节点：回合开始跟随一次（看一眼后由调用方切回真人），其余抑制
            WakuuViewModes.KeyNodes => trigger != WakuuViewTrigger.TurnStart,
            // 不跟随（默认）：除上面两类兜底外一律抑制
            _ => true,
        };
    }

    /// <summary>
    /// 「仅关键节点」档位是否要在瓦库回合开始时「跳过去看一眼」（peek，随后由调用方自动切回真人）。
    /// 与 <see cref="ShouldSuppressSwitch"/> 分开暴露，是因为调用方需要知道「这次放行是不是 peek」，
    /// 以便安排自动切回；判定口径与档位表保持一致（只认 keyNodes + 后台托管 + 瓦库形态角色）。
    /// </summary>
    public static bool ShouldPeekAtTurnStart(string? configuredMode, bool backgroundMode, bool isWakuuFormPlayer)
    {
        if (!isWakuuFormPlayer || !backgroundMode)
        {
            return false;
        }

        return WakuuViewModes.Normalize(configuredMode) == WakuuViewModes.KeyNodes;
    }
}
