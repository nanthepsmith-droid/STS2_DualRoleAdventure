using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管配置的磁盘数据模型（纯数据，无 IO 无游戏依赖）。
/// 字段名即 json 字段名（camelCase），默认值 = 原版低语耳环行为。
/// </summary>
internal sealed class WakuuConfigData
{
    public bool useVakuuForm { get; set; }

    public bool playAllCards { get; set; } = true;

    public bool backgroundMode { get; set; } = true;

    public bool suppressVanillaEarring { get; set; } = true;

    /// <summary>
    /// 防止【瓦库形态】/【永久低语耳环】被第三方效果移除（r83，默认开）。
    /// 实证：TouhouAncients【无底之胃】"吞噬初始遗物与先古遗物以外的全部遗物"会把【瓦库形态】
    /// 一并吃掉，而托管判据只看"是否持有遗物"→ 瓦库彻底停摆。
    /// 开启时：拦截 Player.RemoveRelicInternal 保住托管遗物，且判据在遗物缺失时按瓦库名单兜底并补发。
    /// 关闭时：回到 r82 及以前的行为（遗物可被正常移除，移除后瓦库停止自动操作）。
    /// </summary>
    public bool keepWakuuFormRelic { get; set; } = true;

    public bool autoClaimCards { get; set; } = true;

    public bool autoClaimGoldRelics { get; set; } = true;

    /// <summary>药水奖励自动领取（满栏按稀有度换药/先喝鲜血），默认开。</summary>
    public bool autoClaimPotions { get; set; } = true;

    public bool autoChooseEvents { get; set; } = true;

    public bool autoRestChoice { get; set; } = true;

    /// <summary>战斗中自动用药水：默认关（拍板：保守版写死规则，先观察）。</summary>
    public bool autoUsePotions { get; set; }

    public bool neowAutoChoose { get; set; }

    /// <summary>
    /// 社区统计辅助：读 SkadaHelper（皮皮军师）的社区大数据为卡牌奖励与事件选项加权（可行性分析 §8.2）。
    /// 默认关（关 = 纯最左 / 事件沿用 eventChoiceMode）；开启后若未安装 SkadaHelper 或查表无数据，
    /// 一律静默回退到与关闭时完全一致的行为。
    /// </summary>
    public bool skadaAssist { get; set; }

    /// <summary>
    /// 智能选牌优先级（可行性分析 §9.1/9.2）：开启后事件里的牌库删除/变化选牌按优先级表选取
    /// （删除优先 诅咒→状态→任务→打击→防御；变化优先变掉打击/防御并硬排除坏牌）。
    /// 默认关（关 = 纯 cardPickMode 策略，与既有行为一致）。
    /// </summary>
    public bool smartPick { get; set; }

    /// <summary>
    /// 附魔智能选牌：开启后附魔选牌按「原版附魔一览表」用户填写的规则表挑牌
    /// （每种附魔各有优先级，如腐化挑伤害最高的攻击牌、注能挑能抽 3 张以上的技能牌）。
    /// 默认开（用户已按表填写，关 = 回到纯 cardPickMode 策略）。
    /// 关闭或该附魔填了"维持现状"时行为与既有完全一致。
    /// </summary>
    public bool smartEnchant { get; set; } = true;

    /// <summary>
    /// 跨角色卡组：每个角色战后奖励追加一组从「其他角色」卡池抽取的 3 选 1 卡牌奖励
    /// （原作者未完成的 v1.30 设计，上游 GuyGinat 69c7d99 实现，奖励直接以接收者身份生成）。
    /// 默认关。
    /// </summary>
    public bool extraCrossCharacterCardReward { get; set; }

    /// <summary>
    /// 个人偏好记录器（默认开）：记录真人点选的卡牌奖励批次与事件选项（只记真人决策，
    /// 瓦库自动领/自动选不记），供三级决策链第①级使用。写 %APPDATA%\SlayTheSpire2\personal_stats.json。
    /// </summary>
    public bool personalRecorder { get; set; } = true;

    /// <summary>
    /// 个人统计决策辅助（默认关）：开启后瓦库选牌/选事件优先参考本机个人统计
    /// （多人局优先多人切片），个人样本不足或无倾向时回退社区统计（skadaAssist）→ 最左/最上。
    /// </summary>
    public bool personalAssist { get; set; }

    /// <summary>
    /// 个人统计偏好档位（Phase 1.5，默认 characterFirst，= v1 现状行为零变化）：
    /// characterFirst = 角色优先（①模式+角色→②模式→③角色→④全量，先保角色再放宽）；
    /// volumeFirst = 总量优先（跳过跨模式单角色档，样本集中在模式内与全量）；
    /// characterOnly = 只看角色（绝不用别的角色的数据兜底，适合角色专属牌）。
    /// 取值见 WakuuPersonalQuery.PersonalTier*（纯逻辑单一来源）。
    /// </summary>
    public string personalTier { get; set; } = WakuuPersonalQuery.PersonalTierCharacterFirst;

    /// <summary>
    /// 商店自动化（Phase 4，默认关）：瓦库角色商店界面打开时自动买卡——
    /// 卡牌按社区统计胜率 ≥ WakuuMerchantPicking.DefaultMinBuyWinRate（默认 0.2）
    /// 且支付后仍保留 ≥ DefaultGoldFloor（默认 50）金币才买。
    /// 遗物/药水与删牌服务的自动化后续增量（§9.3）。
    /// </summary>
    public bool shopAssist { get; set; }

    /// <summary>
    /// 商店自动买卡的补充项（默认关，2026-09-06 用户拍板）：开启后商店里
    /// 「查不到社区统计胜率」的卡（多为 mod 卡，SkadaHelper 未收录）也按
    /// 金币保底买入；关闭时无数据卡静默跳过（保持 r64 语义）。
    /// 仅 shopAssist 开启时生效。
    /// </summary>
    public bool shopAssistBuyNoData { get; set; }

    /// <summary>
    /// 自有统计角标（默认关，2026-09-07 用户拍板 Phase 4 增量）：开启后在
    /// 「奖励选牌卡 / 商店卡 / 事件选项按钮」右下角显示个人记录器算出的总抓取率/总选择率（XX%），
    /// 鼠标悬停时在 hover tip 下方追加一块自绘弹窗（分幕首抓/重复抓取率或分幕选择率 + 整体胜率）。
    /// 只显示本地个人统计，与皮皮军师/SkadaHelper 社区统计 UI 完全分开；无社区数据参与。
    /// </summary>
    public bool statBadge { get; set; }

    /// <summary>
    /// 自有统计角标在目标（卡/事件选项）内的位置：bottomRight / bottomLeft / topRight / topLeft。
    /// 默认 **bottomLeft**——皮皮军师（SkadaHelper）自己会把社区统计标签画在卡的右侧，
    /// 放右下会与它重叠并被我们的顶层 overlay 盖住。
    /// </summary>
    public string statBadgeCorner { get; set; } = WakuuStatBadgeCorner.BottomLeft;

    /// <summary>
    /// 统计角标的数据来源档位（默认 `personalOnly` 仅个人）：
    /// - `personalOnly`：只用自己打出来的个人统计；
    /// - `personalThenCommunity`：个人优先，该卡**没有个人记录**时才用社区抓取率补足；
    /// - `blended`：个人与社区按伪计数加权**融合成一个**抓取率（个人样本越多越主导）。
    /// 始终只显示一个百分比；社区数据来自皮皮军师（SkadaHelper）数据接口。
    /// </summary>
    public string statBadgeSource { get; set; } = WakuuStatBadgeSource.PersonalOnly;

    /// <summary>
    /// 召唤物血量显示（r86，默认开）：本地多控顶部的「玩家状态条」上，给每个拥有召唤物的玩家
    /// 在 HP 条旁显示其全部宠物/召唤物血条（Osty、以及任何走原版 AddPet/pet 体系的 mod 召唤物，
    /// 如 TheQueen/女王的 minion）。判据是玩家自己 PlayerCombatState.Pets，不依赖角色职业
    /// （Osty 可由亡灵契约师为别人召唤、其它角色也能合法持有 Necrobinder 卡召唤）。
    /// 一只召唤物一行迷你血条 + 名字与 HP 数字；死亡（尸体保留可复活阶段）显示 0-X 置灰。
    /// </summary>
    public bool petHpBadge { get; set; } = true;

    /// <summary>
    /// 跳过「其他人」的回合开始抽牌演出（改进-1，默认关）。
    /// 本地多控下回合开始会依次把前台切到每个真人玩家、逐个播完自动抽牌动画再切下一个，
    /// 满员（10+ 玩家）时要等很久。开启后回合开始**只保留当前正在看的那位**的演出：
    /// 其他人的回合开始不再切前台 —— 原版对非本地玩家（<c>LocalContext.IsMe == false</c>）的
    /// Draw→Hand 本来就不建卡牌节点、不做补间（见 CardPileCmd.GetTweenForCardsChangingPiles），
    /// 所以这些人的抽牌瞬时生效、数据照常，之后切到该角色时按手牌区重建 UI 即可看到手牌。
    /// 默认关（关 = 与既有观感完全一致）。
    /// </summary>
    public bool skipTurnStartDrawAnim { get; set; }

    /// <summary>
    /// 瓦库出牌加速（改进-2 / r117，**默认开**）：瓦库自动出牌时给 <c>CardCmd.AutoPlay</c> 传
    /// <c>skipCardPileVisuals: true</c>，跳过「牌飞向 Play 区 + 烟雾 VFX + 各牌堆补间」与两段固定等待
    /// （`CustomScaledWait(0.25~0.35)` / `(0.15~0.3)`，见 `CardModel.OnPlayWrapper`）。
    /// r116 埋点实测瓦库每张牌约 1.0~1.4s，多瓦库又是**串行**的（每张牌耗时直接相加成总时长），
    /// 所以这是收益最大的加速点；该参数是游戏官方给自动出牌场景（Havoc / 复制药水）用的，不改数据语义。
    /// 关闭 = 恢复完整出牌动画（观感与旧版一致）。
    /// </summary>
    public bool fastWakuuPlay { get; set; } = true;

    /// <summary>
    /// 【实验档】瓦库出牌走原生动作队列（改进-2 / 方案 D，**默认关**）：
    /// 开启后瓦库出牌不再用 inline 的 <c>CardCmd.AutoPlay</c>，而是像真人/远端玩家一样
    /// <c>new PlayCardAction(card, target)</c> 入**该瓦库自己的**动作队列
    /// （<c>ActionQueueSynchronizer.RequestEnqueue</c> —— 原版 <c>CardModel.EnqueueManualPlay</c> 就是这一行）。
    /// 换来的是多人模式的真实语义（"某人等自己的选择时，别人照常出牌"）；代价是三条刻意的语义迁移
    /// （见 <see cref="WakuuPlayQueuePolicy"/> 与方案 §12.2）：
    /// ① <c>isAutoPlay</c> 由 true 变 false（`VoidFormPower` / `PaelsEye` / `UnceasingTop` 等牌对瓦库出牌的统计与触发会变）；
    /// ② `Any` 类目标必须由调用方**构造前**解析好，解析不到则牌留在手牌（比 AutoPlay 的"进堆"更严格）；
    /// ③ <c>SpendResources</c> 由动作自己调用，外层**绝不能再花一次**（否则双重扣费）。
    /// 默认关 = 与既有行为完全一致。
    /// </summary>
    public bool wakuuPlayQueue { get; set; }

    /// <summary>
    /// 【实验档 · 第二步】瓦库并发出牌（改进-2 / 方案 D 的最终目标，**默认关**，仅在
    /// <see cref="wakuuPlayQueue"/> 开启时生效）。
    ///
    /// 开启后，出牌循环**不再抢占全局 1 槽的 `SelectorScopeGate`**，看门狗调度也不再因
    /// 「已有作用域在飞」被挡 —— 多个瓦库真正重叠："某个瓦库在等自己的选牌时，其他瓦库与真人照常出牌"
    /// （原版 `ActionQueueSet.GetReadyAction` 会跳过等选择的队列，这是多人模式的真实语义）。
    /// 两种选择各归各的路由由 Phase 1（r113）的 `WakuuSelectorRegistry` 承担。
    ///
    /// 代价与前提（详见方案 §12.11）：必须走队列路径（`wakuuPlayQueue` 开），否则闸门照旧；
    /// 并发时不再把 `LocalContext.NetId` 钉在瓦库身上（出牌由游戏全局单泵执行、归属走注册表分发），
    /// 因此瓦库出牌的**前台视觉**会更接近"后台托管"的观感。
    /// 默认关 = 与既有行为完全一致。
    /// </summary>
    public bool wakuuPlayOverlap { get; set; }

    /// <summary>
    /// 瓦库托管视角策略（改进-2 / Phase 0，默认 `never` **不跟随**，2026-09-10 用户拍板）：
    /// - `never`：瓦库全程不抢视角；只保留两处防软锁兜底（作用域外真人交互选牌、安全网超时救援）；
    /// - `keyNodes`：仅关键节点跟随 —— 瓦库**回合开始**时切过去一次（看到轮到谁、抽了什么），日常出牌不跟随；
    /// - `always`：全程跟随（回合开始/结束、Hook 入队、出牌前都切），≈ 关闭「后台托管」的观感。
    /// 仅当 `backgroundMode` 为 true 时生效；后台托管关闭时一律按 `always`（向后兼容）。
    /// 取值见 WakuuViewModes（纯逻辑单一来源）。
    /// </summary>
    public string wakuuViewMode { get; set; } = WakuuViewModes.Default;

    public string eventChoiceMode { get; set; } = WakuuChoiceModes.First;

    public string cardPickMode { get; set; } = WakuuChoiceModes.Last;

    /// <summary>战斗中决策大脑：heuristic=启发式（默认，即现有出牌逻辑）/ auto=自动探测可用求解器。</summary>
    public string wakuuBrain { get; set; } = WakuuBrainModes.Heuristic;
}

/// <summary>瓦库大脑模式取值常量（单一来源）。</summary>
internal static class WakuuBrainModes
{
    public const string Heuristic = "heuristic";

    public const string Auto = "auto";
}

/// <summary>
/// 配置的 JSON 解析/序列化纯函数（无 IO）。解析失败抛 JsonException，由调用方决定兜底策略。
/// </summary>
internal static class WakuuConfigJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析配置 json；输入 null 返回 null（沿用当前生效值的语义由调用方处理）。</summary>
    public static WakuuConfigData? Parse(string json)
    {
        return JsonSerializer.Deserialize<WakuuConfigData>(json, JsonOptions);
    }

    /// <summary>序列化为缩进 json 文本。</summary>
    public static string Serialize(WakuuConfigData data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }
}
