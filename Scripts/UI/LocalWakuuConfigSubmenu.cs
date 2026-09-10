using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace LocalMultiControl.Scripts.UI;

/// <summary>
/// 瓦库托管设置页：一个真正的 NSubmenu 子菜单（BaseLib 的 NModConfigSubmenu 同款思路），
/// 由注入按钮经 PushSubmenuType 推入原版子菜单栈，观感与游戏自带设置页一致：
/// 无黑幕弹层、无浮动圆角面板，原生返回按钮 + 居中内容列。
/// 开关控件沿用游戏原生外观勾选框（LocalWakuuConfigTickbox），
/// 改动经 LocalWakuuAutopilotConfig.TrySetAndSave 即时写回 vakuu_autopilot.json。
/// </summary>
internal sealed partial class LocalWakuuConfigSubmenu : NSubmenu
{
    /// <summary>滚动条槽宽（沿用 BaseLib NNativeScrollableContainer 的 ScrollbarGutterWidth）。</summary>
    private const float ScrollbarGutterWidth = 60f;

    private const float ScrollbarInset = 64f;

    private LocalWakuuConfigTickbox? _firstToggle;

    private Control? _clipper;

    private VBoxContainer? _column;

    private CenterContainer? _scrollContent;

    public LocalWakuuConfigSubmenu()
    {
        // NSubmenu 是全屏 Control；由子菜单栈负责 Visible 切换
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.End;
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override Control? InitialFocusedControl => _firstToggle;

    /// <summary>
    /// 注意：NSubmenu 基类约定子类不要调 base._Ready()，改为直接调 ConnectSignals()；
    /// ConnectSignals 会按节点名 "BackButton" 找原生返回按钮并接上 _stack.Pop()，
    /// 因此返回按钮必须先于本调用加入子树。
    /// </summary>
    public override void _Ready()
    {
        AddChild(CreateBackButton());
        ConnectSignals();
        BuildContent();
        // 返回按钮移到子节点末尾（置顶），避免被全屏滚动容器遮挡而无法点击
        MoveChild(GetNode("BackButton"), -1);
        LocalMultiControlLogger.Info("瓦库托管设置子菜单已构建");
    }

    /// <summary>
    /// 修复「首次点进设置页内容不显示，只有退出按钮和滚动条，退出重进才显示」。
    ///
    /// 根因：本页实例在子菜单栈下是懒建并缓存的，创建时 Visible=false，
    /// BuildContent/_Ready 阶段 clipper 尚未完成布局（FullRect 尺寸为 0），
    /// OnScrollContentResized 会把 _scrollContent 宽压成 1px，内容列被裁剪到不可见。
    /// 重进时缓存实例已带上一轮布局好的 clipper 尺寸，故能正常显示。
    ///
    /// 对策：每次显示（含首次）都延迟一帧重算滚动内容尺寸，等 clipper 拿到真实尺寸后再排版，
    /// 保证首次进入内容即可见；同时保留 Resized 事件兜底覆盖后续窗口缩放。
    /// </summary>
    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();
        CallDeferred(nameof(RecomputeScrollContentDeferred));
    }

    private void RecomputeScrollContentDeferred()
    {
        if (!IsInsideTree() || !IsInstanceValid(this))
        {
            return;
        }

        OnScrollContentResized();
    }

    private static NBackButton CreateBackButton()
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(
            SceneHelper.GetScenePath("ui/back_button"), null, ResourceLoader.CacheMode.Reuse);
        NBackButton backButton = scene != null
            ? scene.Instantiate<NBackButton>()
            : new NBackButton();
        backButton.Name = "BackButton";
        return backButton;
    }

    private void BuildContent()
    {
        // 滚动容器：沿用游戏原生 NScrollableContainer + ui/scrollbar 场景，
        // 天然支持鼠标拖拽/滚轮、键盘上下、手柄与焦点跟随滚动（与古明地恋/BaseLib 同思路）。
        NScrollableContainer scrollArea = new()
        {
            Name = "ScrollArea",
            // 裁剪子节点，配合 Mask 形成可视滚动区域
            ClipChildren = ClipChildrenMode.AndDraw,
        };
        scrollArea.SetAnchorsPreset(LayoutPreset.FullRect);

        // Mask/Clipper：裁剪可视区，右侧留出滚动条槽
        TextureRect mask = new()
        {
            Name = "Mask",
            ClipChildren = ClipChildrenMode.AndDraw,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        mask.SetAnchorsPreset(LayoutPreset.FullRect);

        Control clipper = new()
        {
            Name = "Clipper",
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        clipper.SetAnchorsPreset(LayoutPreset.FullRect);
        clipper.OffsetRight = -ScrollbarGutterWidth;
        mask.AddChild(clipper);

        // 滚动条（游戏原生场景，含 %Handle 唯一名）
        PackedScene? scrollbarScene = ResourceLoader.Load<PackedScene>(
            SceneHelper.GetScenePath("ui/scrollbar"), null, ResourceLoader.CacheMode.Reuse);
        NScrollbar scrollbar = scrollbarScene != null
            ? scrollbarScene.Instantiate<NScrollbar>()
            : new NScrollbar();
        scrollbar.Name = "Scrollbar";
        scrollbar.SetAnchorsPreset(LayoutPreset.RightWide, false);
        scrollbar.OffsetLeft = -48f;
        scrollbar.OffsetRight = 0f;
        scrollbar.OffsetTop = ScrollbarInset;
        scrollbar.OffsetBottom = -ScrollbarInset;

        scrollArea.AddChild(mask);
        scrollArea.AddChild(scrollbar);
        // Mask/Scrollbar 就绪后再入树，确保 NScrollableContainer._Ready 能取到 Scrollbar 子节点
        AddChild(scrollArea);

        // 内容列（所有开关/策略行）
        VBoxContainer column = new()
        {
            Name = "Column",
        };
        column.AddThemeConstantOverride("separation", 14);

        Label title = CreateLabel("瓦 库 托 管", 42, new Color(1f, 0.95f, 0.75f));
        column.AddChild(title);
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel(
            "改动立即保存到 vakuu_autopilot.json（总开关在下一局开局时生效）。",
            20, new Color(0.72f, 0.72f, 0.72f)));
        column.AddChild(CreateSpacer(10));

        AddToggleRow(column,
            "瓦库形态托管（总开关）",
            "开启后勾选瓦库的角色改发【瓦库形态】遗物并进入托管；关闭时保持原版\"永久低语耳环\"行为。",
            () => LocalWakuuAutopilotConfig.UseVakuuForm,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("useVakuuForm", value));
        AddToggleRow(column,
            "打光所有手牌",
            "瓦库每回合自动出完所有可出牌；关闭时沿用原版低语耳环每回合最多 13 张的上限。",
            () => LocalWakuuAutopilotConfig.PlayAllCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("playAllCards", value));
        AddToggleRow(column,
            "后台托管（不切前台）",
            "瓦库回合不再强制切换到该角色视角，全程后台自动出牌与结束回合。关闭时下方「瓦库托管视角」档位不生效（等同全程跟随）。",
            () => LocalWakuuAutopilotConfig.BackgroundMode,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("backgroundMode", value));
        column.AddChild(CreateViewModeRow(
            "瓦库托管视角",
            "本档位细化「后台托管」的跟随程度，默认不跟随。**不跟随**：瓦库全程不抢视角，只保留两处防软锁兜底（作用域外需要你自己操作的选牌、安全网超时救援）；**仅关键节点**：瓦库回合开始时跳过去看一眼（看到轮到谁、抽了什么），约 1 秒后自动切回你自己，日常出牌不跟随；**全程跟随**：回合开始/结束、Hook 入队、瓦库出牌前都切过去（约等于关闭「后台托管」的观感）。仅当上方「后台托管（不切前台）」开启时生效。"));
        AddToggleRow(column,
            "压制原版低语耳环",
            "持有【瓦库形态】时，局内再获得的原版低语耳环只保留 +1 能量，不再重复触发自动出牌。",
            () => LocalWakuuAutopilotConfig.SuppressVanillaEarring,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("suppressVanillaEarring", value));
        AddToggleRow(column,
            "防止瓦库形态丢失",
            "默认开。第三方遗物/事件（已见案例：东方「无底之胃」——拾起时吞噬初始遗物与先古遗物以外的全部遗物）会把【瓦库形态】一起移除，而托管判据只看是否持有该遗物，移除后瓦库会彻底停止自动操作。开启后：本 mod 会拦下对托管遗物的移除，并在遗物真的没了时按瓦库名单继续托管并补发。关闭 = 回到旧行为（可被移除，移除后瓦库停摆）。",
            () => LocalWakuuAutopilotConfig.KeepWakuuFormRelic,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("keepWakuuFormRelic", value));

        column.AddChild(CreateSpacer(8));

        AddToggleRow(column,
            "火堆自动选择",
            "低血(<50%)优先睡觉；高血无遗物选项时升级非打击/防御的最后一张牌；全员满血时打击/防御也照升（全升完则睡觉）；有遗物选项（举重/挖掘等）则在睡觉以外随机；未满血且没得锻时愈合队友；帐篷多选时全拿。",
            () => LocalWakuuAutopilotConfig.AutoRestChoice,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoRestChoice", value));
        AddToggleRow(column,
            "战斗中自动用药水",
            "默认关。按药水规则表逐药自动使用：血液/再生低血自用；果汁到手即喝；混沌药水填空位；力量等增益与火焰等攻击类精英/Boss战首回合；格挡/免伤类回合结束前按敌方意图伤害兜底；能量/迅捷/异蛇剩能量补牌；灰水/赌徒等定向消耗坏牌；故障机器人/储君/亡灵契约师/铁甲战士专属药水自动给对应队友，复制/超巨化优先给真人；污浊药水只在商人投掷；mod 药水普通战斗随机回合消耗。",
            () => LocalWakuuAutopilotConfig.AutoUsePotions,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoUsePotions", value));
        AddToggleRow(column,
            "卡牌奖励自动领（最左）",
            "战后瓦库自己的卡牌奖励自动领最左边一张；金币与遗物同规则自动领取。",
            () => LocalWakuuAutopilotConfig.AutoClaimCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimCards", value));
        AddToggleRow(column,
            "金币/遗物奖励自动领",
            "战后瓦库的金币与遗物奖励自动领取。",
            () => LocalWakuuAutopilotConfig.AutoClaimGoldRelics,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimGoldRelics", value));
        AddToggleRow(column,
            "药水奖励自动领取",
            "战后与事件中药水奖励自动领取。有空位直接领；满栏时先喝栏内鲜血药水腾位；否则奖励稀有度高于栏内最低才丢最低换领，等价或更低放弃。事件中的卡牌/金币/遗物奖励同上方开关。",
            () => LocalWakuuAutopilotConfig.AutoClaimPotions,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimPotions", value));
        AddToggleRow(column,
            "事件自动选择",
            "非共享事件按下方策略自动选择；触发战斗/小游戏/致死选项即停住等真人。",
            () => LocalWakuuAutopilotConfig.AutoChooseEvents,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoChooseEvents", value));
        AddToggleRow(column,
            "社区统计辅助（需装 SkadaHelper）",
            "默认关。开启后瓦库的卡牌奖励与事件选项优先参考「皮皮军师: SkadaHelper」的社区大数据（卡牌：选取率 + 拿了之后的胜率增益；事件选项：胜率最高）。未装该 mod、查无数据或样本量不足时，静默回退到原来的最左 / 事件选项策略，行为与关闭时完全一致。",
            () => LocalWakuuAutopilotConfig.SkadaAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("skadaAssist", value));
        AddToggleRow(column,
            "智能选牌优先级",
            "默认关。开启后事件里的牌库删除 / 变化选牌按优先级表自动选取：删除优先 诅咒→状态→任务→打击→基础防御；变化优先变掉打击/防御并避开诅咒/状态/任务。关闭或场景未知时维持原选牌策略，行为与关闭时完全一致。",
            () => LocalWakuuAutopilotConfig.SmartPick,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("smartPick", value));
        AddToggleRow(column,
            "附魔智能选牌",
            "默认开。开启后瓦库附魔选牌按「原版附魔一览表」里填写的规则挑牌（例如：伶俐/墨影/灵巧选费用最低的牌，腐化/本能选伤害最高的攻击牌，注能选能抽 3 张以上的技能牌，克隆按是否持有不休陀螺走两套优先级）。关闭后回到原来的选牌策略。",
            () => LocalWakuuAutopilotConfig.SmartEnchant,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("smartEnchant", value));
        AddToggleRow(column,
            "个人偏好记录",
            "默认开。记录你自己点选的卡牌奖励与事件选项（只记真人决策，瓦库自动操作不记），写入 personal_stats.json，供「个人统计决策辅助」使用。只统计打完（胜/负）的局；进行中/半途退出的局不计。",
            () => LocalWakuuAutopilotConfig.PersonalRecorder,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("personalRecorder", value));
        AddToggleRow(column,
            "个人统计决策辅助",
            "默认关。开启后瓦库选牌/选事件优先参考你自己打出的个人统计（多人局优先参考多人局数据）；个人样本不足或无倾向时回退社区统计与默认策略。样本越多越贴合你的打法（含 mod 卡）。",
            () => LocalWakuuAutopilotConfig.PersonalAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("personalAssist", value));
        column.AddChild(CreatePersonalTierRow(
            "个人统计偏好档位",
            "用个人统计做决策时按哪个切片查表。角色优先：先看本角色，不足再放宽（默认）；总量优先：角色样本不足时直接信跨角色总样本；只看角色：绝不用其他角色的数据兜底（适合角色专属牌）。影响瓦库卡牌奖励与事件选项两条决策链。"));
        AddToggleRow(column,
            "商店自动买卡",
            "默认关（Phase 4 实验）。开启后瓦库角色的商店视图打开时，自动买「社区统计胜率 ≥ 20% 且付完仍保留 ≥ 50 金币」的卡。遗物/药水与删牌服务自动化尚未做。",
            () => LocalWakuuAutopilotConfig.ShopAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssist", value));
        AddToggleRow(column,
            "商店买卡·无统计数据也买",
            "默认关。需「商店自动买卡」开启。商店里查不到社区统计胜率的卡（多为 mod 卡，皮皮军师未收录）也会按「付完仍保留 ≥ 50 金币」买入，否则无数据的卡静默跳过。",
            () => LocalWakuuAutopilotConfig.ShopAssistBuyNoData,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssistBuyNoData", value));
        column.AddChild(CreateStrategyRow(
            "事件选项策略",
            "事件自动选择时挑哪个选项。很多事件一直选第一个会死，可切到最后一个或随机规避。",
            "eventChoiceMode",
            () => LocalWakuuAutopilotConfig.EventChoiceMode));
        column.AddChild(CreateStrategyRow(
            "战斗内选牌策略",
            "效果类选牌（酒狐合成、开局遗物二选一、从手牌选 N 张、事件附魔/升级/变化选牌等）取哪张。默认最后；稀有度最高优先选 Ancient/Rare。卡牌奖励始终领最左。",
            "cardPickMode",
            () => LocalWakuuAutopilotConfig.CardPickMode,
            allowRare: true));
        AddToggleRow(column,
            "涅奥开局自动选",
            "允许自动选择涅奥（NEOW）开局奖励；默认关闭。",
            () => LocalWakuuAutopilotConfig.NeowAutoChoose,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("neowAutoChoose", value));

        BuildOtherSection(column);

        column.AddChild(CreateSpacer(6));
        column.AddChild(CreateDivider(new Color(0.35f, 0.35f, 0.35f, 0.7f)));
        column.AddChild(CreateLabel("点左下角返回按钮或再次打开本页可随时退出。", 18, new Color(0.6f, 0.6f, 0.6f)));
        column.AddChild(CreateSpacer(ScrollbarInset));

        // 滚动内容：CenterContainer 水平居中内容列；宽 = 裁剪区宽、高 = 内容高，供 NScrollableContainer 计算滚动范围
        CenterContainer scrollContent = new()
        {
            Name = "ContentPanel",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        scrollContent.SetAnchorsPreset(LayoutPreset.TopLeft, false);
        scrollContent.AddChild(column);

        // 关键顺序：先把内容加入 clipper（使其有 parent），再 SetContent。
        // NScrollableContainer.SetContent → UpdatePadding → UpdateScrollLimitBottom → ScrollViewportSize
        // 会访问 _content.GetParent<Control>()，parent 缺失会抛 NullReferenceException。
        clipper.AddChild(scrollContent);
        scrollArea.SetContent(scrollContent);
        _clipper = clipper;
        _column = column;
        _scrollContent = scrollContent;

        // 内容/裁剪区尺寸变化时同步滚动范围（列自动换行导致高度变化、窗口缩放导致宽度变化）
        column.Resized += OnScrollContentResized;
        clipper.Resized += OnScrollContentResized;
        scrollContent.Resized += OnScrollContentResized;

        // 首个布局帧后校准一次滚动内容尺寸（此时列宽已定，自动换行高度才准确）
        OnScrollContentResized();
    }

    /// <summary>
    /// 「其它设置」分区（r83）：与瓦库托管无强关联的开关集中放到页面末尾，并用分隔线隔开，
    /// 避免和上方「瓦库托管」区块混在一起——这些功能即便关闭瓦库形态总开关也照常按自身开关生效。
    /// </summary>
    private void BuildOtherSection(VBoxContainer column)
    {
        column.AddChild(CreateSpacer(18));
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel("其 它 设 置", 34, new Color(1f, 0.95f, 0.75f)));
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel(
            "以下功能与「瓦库形态托管（总开关）」无关：即使不开托管也按各自的开关生效，改动同样立即写入 vakuu_autopilot.json。",
            20, new Color(0.72f, 0.72f, 0.72f)));
        column.AddChild(CreateSpacer(10));

        AddToggleRow(column,
            "跨角色卡组（战后奖励）",
            "默认关。开启后每个角色的战后卡牌奖励追加一组从「其他角色」卡池抽取的 3 选 1（原作者未完成的 v1.30 设计）。",
            () => LocalWakuuAutopilotConfig.ExtraCrossCharacterCardReward,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("extraCrossCharacterCardReward", value));
        AddToggleRow(column,
            "召唤物血量显示",
            "默认开。本地多控时，顶部每个玩家的状态条旁显示其持有的全部召唤物/宠物血条（每只一行，含名字与血量，如亡灵契约师的奥斯蒂 Osty）。判据是「该玩家是否拥有召唤物」而非职业——奥斯蒂也可以由亡灵契约师为其他玩家召唤、或其它角色合法持有 Necrobinder 卡召唤；女王 mod 等走原版宠物体系的召唤物同样会显示。召唤物死亡后显示 0-X（置灰），提示需要复活/再召唤。",
            () => LocalWakuuAutopilotConfig.PetHpBadge,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("petHpBadge", value));
        AddToggleRow(column,
            "跳过他人回合开始抽牌演出",
            "默认关。本地多控下每回合开始会依次把前台切到每个真人玩家、逐个播完自动抽牌动画才轮到下一个，10 人以上时要等很久。开启后回合开始只保留「当前正在看的那位」的抽牌演出，其他人的回合开始不再切前台——原版对非本地玩家的抽牌本来就不做动画（只走数据），所以他们的抽牌瞬时生效，之后切到该角色时会立刻看到完整手牌。",
            () => LocalWakuuAutopilotConfig.SkipTurnStartDrawAnim,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("skipTurnStartDrawAnim", value));
        AddToggleRow(column,
            "自有统计角标",
            "默认关。开启后在「奖励选牌卡/商店卡/事件选项按钮」的角标位置（见下方「自有统计角标位置」）显示你自己记录的总抓取率/总选择率（XX%），鼠标悬停弹出分幕首抓/重复抓取率、胜率的详情。只显示本地个人统计，与皮皮军师（SkadaHelper）社区统计 UI 分开、互不覆盖。",
            () => LocalWakuuAutopilotConfig.StatBadge,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("statBadge", value));
        column.AddChild(CreateStatBadgeSourceRow(
            "统计角标数据来源",
            "角标百分比怎么算（**始终只显示一个数字**）：**仅个人**=只用你自己打出的统计；**个人+社区兜底**=该卡没有个人记录时才用皮皮军师的社区抓取率补足；**融合**=个人与社区按伪计数加权合成一个抓取率（个人样本越多越主导，个人 5 次以上即与社区平手以上）。社区数据来自皮皮军师（SkadaHelper）的数据接口；未装或查无数据时退回个人统计/0%。"));
        column.AddChild(CreateStatBadgeCornerRow(
            "自有统计角标位置",
            "角标挂在目标（卡/事件选项）的哪个角，点右侧按钮在 左下 → 右下 → 右上 → 左上 之间循环。默认 **左下**——皮皮军师（SkadaHelper）会自己把社区统计标签画在卡的右侧，放右下会与它重叠并被本 mod 的顶层叠加盖住；若你想让两者挨在一起显示，可切到「右上/左上」自行比较。"));
    }

    /// <summary>
    /// 根据内容列的实际最小尺寸与裁剪区宽度，校准滚动内容（宽/高），从而让 NScrollableContainer
    /// 的滚动范围与滚动条正确。列高超过裁剪区高时 NScrollableContainer 自动显示滚动条。
    /// </summary>
    private void OnScrollContentResized()
    {
        if (_scrollContent == null || _column == null || _clipper == null)
        {
            return;
        }

        float width = Mathf.Max(1f, _clipper.Size.X);
        float height = Mathf.Max(1f, Mathf.Ceil(_column.GetMinimumSize().Y));
        _scrollContent.Size = new Vector2(width, height);
    }

    /// <summary>
    /// 策略切换行（通用）：右侧按钮在 第一个 → 最后一个 → 随机 [→ 稀有度最高] 间循环
    /// （allowRare=false 时为三档，用于事件选项策略），
    /// 按钮文本显示当前值，点击经 TrySetAndSaveString 即时写回 json。
    /// </summary>
    private Control CreateStrategyRow(string title, string description, string configKey, Func<string> currentModeGetter, bool allowRare = false)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalSimpleTextButton modeButton = new()
        {
            ButtonText = GetChoiceModeDisplayText(currentModeGetter()),
            FontSize = 24,
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter
        };
        modeButton.CustomMinimumSize = new Vector2(220f, 64f);
        modeButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            string next = NextChoiceMode(modeButton.ButtonText, allowRare);
            if (LocalWakuuAutopilotConfig.TrySetAndSaveString(configKey, next))
            {
                modeButton.ButtonText = GetChoiceModeDisplayText(currentModeGetter());
                LocalMultiControlLogger.Info($"策略已切换: {configKey}={next}");
            }
        }));
        row.AddChild(modeButton);
        return row;
    }

    private static string GetChoiceModeDisplayText(string mode)
    {
        return mode switch
        {
            LocalWakuuAutopilotConfig.LastChoiceMode => "最后一个",
            LocalWakuuAutopilotConfig.RandomChoiceMode => "随机",
            LocalWakuuAutopilotConfig.RareChoiceMode => "稀有度最高",
            _ => "第一个",
        };
    }

    private static string NextChoiceMode(string currentDisplayText, bool allowRare)
    {
        if (allowRare)
        {
            return currentDisplayText switch
            {
                "第一个" => LocalWakuuAutopilotConfig.LastChoiceMode,
                "最后一个" => LocalWakuuAutopilotConfig.RandomChoiceMode,
                "随机" => LocalWakuuAutopilotConfig.RareChoiceMode,
                _ => LocalWakuuAutopilotConfig.FirstChoiceMode,
            };
        }

        return currentDisplayText switch
        {
            "第一个" => LocalWakuuAutopilotConfig.LastChoiceMode,
            "最后一个" => LocalWakuuAutopilotConfig.RandomChoiceMode,
            _ => LocalWakuuAutopilotConfig.FirstChoiceMode,
        };
    }

    /// <summary>
    /// 个人统计偏好档位切换行（三档：角色优先 → 总量优先 → 只看角色，循环）。
    /// 档位取值经 TrySetAndSaveString("personalTier", ...) 即时写回 json。
    /// </summary>
    private Control CreatePersonalTierRow(string title, string description)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalSimpleTextButton tierButton = new()
        {
            ButtonText = GetPersonalTierDisplayText(LocalWakuuAutopilotConfig.PersonalTier),
            FontSize = 24,
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter
        };
        tierButton.CustomMinimumSize = new Vector2(220f, 64f);
        tierButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            string next = NextPersonalTier(LocalWakuuAutopilotConfig.PersonalTier);
            if (LocalWakuuAutopilotConfig.TrySetAndSaveString("personalTier", next))
            {
                tierButton.ButtonText = GetPersonalTierDisplayText(LocalWakuuAutopilotConfig.PersonalTier);
                LocalMultiControlLogger.Info($"个人统计偏好档位已切换: {next}");
            }
        }));
        row.AddChild(tierButton);
        return row;
    }

    private static string GetPersonalTierDisplayText(string tier)
    {
        return tier switch
        {
            LocalWakuuAutopilotConfig.VolumeFirstTier => "总量优先",
            LocalWakuuAutopilotConfig.CharacterOnlyTier => "只看角色",
            _ => "角色优先",
        };
    }

    private static string NextPersonalTier(string tier)
    {
        return tier switch
        {
            LocalWakuuAutopilotConfig.CharacterFirstTier => LocalWakuuAutopilotConfig.VolumeFirstTier,
            LocalWakuuAutopilotConfig.VolumeFirstTier => LocalWakuuAutopilotConfig.CharacterOnlyTier,
            _ => LocalWakuuAutopilotConfig.CharacterFirstTier,
        };
    }

    /// <summary>
    /// 「瓦库托管视角」策略切换行（三档：不跟随 → 仅关键节点 → 全程跟随，循环，改进-2 Phase 0）。
    /// 默认不跟随；取值经 TrySetAndSaveString("wakuuViewMode", ...) 即时写回 json。
    /// 仅当「后台托管（不切前台）」开启时生效（关闭时一律按全程跟随，向后兼容）。
    /// </summary>
    private Control CreateViewModeRow(string title, string description)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalSimpleTextButton viewButton = new()
        {
            ButtonText = GetViewModeDisplayText(LocalWakuuAutopilotConfig.ViewMode),
            FontSize = 24,
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter
        };
        viewButton.CustomMinimumSize = new Vector2(260f, 64f);
        viewButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            string next = NextViewMode(LocalWakuuAutopilotConfig.ViewMode);
            if (LocalWakuuAutopilotConfig.TrySetAndSaveString("wakuuViewMode", next))
            {
                viewButton.ButtonText = GetViewModeDisplayText(LocalWakuuAutopilotConfig.ViewMode);
                LocalMultiControlLogger.Info($"瓦库托管视角已切换: {next}");
            }
        }));
        row.AddChild(viewButton);
        return row;
    }

    private static string GetViewModeDisplayText(string mode)
    {
        return mode switch
        {
            LocalWakuuAutopilotConfig.ViewModeKeyNodes => "仅关键节点",
            LocalWakuuAutopilotConfig.ViewModeAlways => "全程跟随",
            _ => "不跟随",
        };
    }

    private static string NextViewMode(string mode)
    {
        return mode switch
        {
            LocalWakuuAutopilotConfig.ViewModeNever => LocalWakuuAutopilotConfig.ViewModeKeyNodes,
            LocalWakuuAutopilotConfig.ViewModeKeyNodes => LocalWakuuAutopilotConfig.ViewModeAlways,
            _ => LocalWakuuAutopilotConfig.ViewModeNever,
        };
    }

    /// <summary>
    /// 统计角标数据来源切换行（三档：仅个人 → 个人+社区兜底 → 融合，循环）。
    /// 取值经 TrySetAndSaveString("statBadgeSource", ...) 即时写回 json。
    /// </summary>
    private Control CreateStatBadgeSourceRow(string title, string description)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalSimpleTextButton sourceButton = new()
        {
            ButtonText = GetStatBadgeSourceDisplayText(LocalWakuuAutopilotConfig.StatBadgeSource),
            FontSize = 24,
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter
        };
        sourceButton.CustomMinimumSize = new Vector2(260f, 64f);
        sourceButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            string next = NextStatBadgeSource(LocalWakuuAutopilotConfig.StatBadgeSource);
            if (LocalWakuuAutopilotConfig.TrySetAndSaveString("statBadgeSource", next))
            {
                sourceButton.ButtonText = GetStatBadgeSourceDisplayText(LocalWakuuAutopilotConfig.StatBadgeSource);
                LocalMultiControlLogger.Info($"统计角标数据来源已切换: {next}");
            }
        }));
        row.AddChild(sourceButton);
        return row;
    }

    private static string GetStatBadgeSourceDisplayText(string source)
    {
        return source switch
        {
            WakuuStatBadgeSource.PersonalThenCommunity => "个人+社区兜底",
            WakuuStatBadgeSource.Blended => "融合",
            _ => "仅个人",
        };
    }

    private static string NextStatBadgeSource(string source)
    {
        return source switch
        {
            WakuuStatBadgeSource.PersonalOnly => WakuuStatBadgeSource.PersonalThenCommunity,
            WakuuStatBadgeSource.PersonalThenCommunity => WakuuStatBadgeSource.Blended,
            _ => WakuuStatBadgeSource.PersonalOnly,
        };
    }

    /// <summary>
    /// 自有统计角标位置切换行（四档：左下 → 右下 → 右上 → 左上，循环）。
    /// 默认左下：皮皮军师（SkadaHelper）会自己把社区统计标签画在卡的右侧，放右下会与它重叠。
    /// 取值经 TrySetAndSaveString("statBadgeCorner", ...) 即时写回 json。
    /// </summary>
    private Control CreateStatBadgeCornerRow(string title, string description)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalSimpleTextButton cornerButton = new()
        {
            ButtonText = GetStatBadgeCornerDisplayText(LocalWakuuAutopilotConfig.StatBadgeCorner),
            FontSize = 24,
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter
        };
        cornerButton.CustomMinimumSize = new Vector2(220f, 64f);
        cornerButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
        {
            string next = NextStatBadgeCorner(LocalWakuuAutopilotConfig.StatBadgeCorner);
            if (LocalWakuuAutopilotConfig.TrySetAndSaveString("statBadgeCorner", next))
            {
                cornerButton.ButtonText = GetStatBadgeCornerDisplayText(LocalWakuuAutopilotConfig.StatBadgeCorner);
                LocalMultiControlLogger.Info($"自有统计角标位置已切换: {next}");
            }
        }));
        row.AddChild(cornerButton);
        return row;
    }

    private static string GetStatBadgeCornerDisplayText(string corner)
    {
        return corner switch
        {
            WakuuStatBadgeCorner.BottomRight => "右下",
            WakuuStatBadgeCorner.TopRight => "右上",
            WakuuStatBadgeCorner.TopLeft => "左上",
            _ => "左下",
        };
    }

    private static string NextStatBadgeCorner(string corner)
    {
        return corner switch
        {
            WakuuStatBadgeCorner.BottomLeft => WakuuStatBadgeCorner.BottomRight,
            WakuuStatBadgeCorner.BottomRight => WakuuStatBadgeCorner.TopRight,
            WakuuStatBadgeCorner.TopRight => WakuuStatBadgeCorner.TopLeft,
            _ => WakuuStatBadgeCorner.BottomLeft,
        };
    }

    private void AddToggleRow(VBoxContainer column, string title, string description, Func<bool> getter, Action<bool> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalWakuuConfigTickbox tickbox = new(getter, setter)
        {
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter：勾选框在行内垂直居中
        };
        _firstToggle ??= tickbox;
        row.AddChild(tickbox);

        column.AddChild(row);
    }

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static ColorRect CreateDivider(Color color)
    {
        return new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(0f, 2f),
        };
    }

    private static Control CreateSpacer(float height)
    {
        return new Control { CustomMinimumSize = new Vector2(0f, height) };
    }
}
