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

        Label title = CreateLabel(LocalModText.Select("瓦 库 托 管", "V A K U U  A U T O P I L O T"), 42, new Color(1f, 0.95f, 0.75f));
        column.AddChild(title);
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel(
            LocalModText.Select(
                "改动立即保存到 vakuu_autopilot.json（总开关在下一局开局时生效）。",
                "Changes save to vakuu_autopilot.json immediately (the master toggle takes effect at the start of the next run)."),
            20, new Color(0.72f, 0.72f, 0.72f)));
        column.AddChild(CreateSpacer(10));

        AddToggleRow(column,
            LocalModText.Select("瓦库形态托管（总开关）", "Vakuu Form Autopilot (Master Toggle)"),
            LocalModText.Select(
                "开启后勾选瓦库的角色改发【瓦库形态】遗物并进入托管；关闭时保持原版\"永久低语耳环\"行为。",
                "When on, characters flagged as Vakuu receive the [Vakuu Form] relic and are autopiloted. When off, keeps the original \"Permanent Whispering Earring\" behavior."),
            () => LocalWakuuAutopilotConfig.UseVakuuForm,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("useVakuuForm", value));
        AddToggleRow(column,
            LocalModText.Select("打光所有手牌", "Play All Cards"),
            LocalModText.Select(
                "瓦库每回合自动出完所有可出牌；关闭时沿用原版低语耳环每回合最多 13 张的上限。",
                "Vakuu plays every playable card each turn. When off, keeps the vanilla Whispering Earring cap of 13 cards per turn."),
            () => LocalWakuuAutopilotConfig.PlayAllCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("playAllCards", value));
        AddToggleRow(column,
            LocalModText.Select("瓦库出牌加速（跳过卡牌堆动画）", "Fast Vakuu Plays (Skip Pile Animations)"),
            LocalModText.Select(
                "默认开。瓦库自动出牌时跳过「牌飞向出牌区 + 烟雾特效 + 各牌堆补间」与打出/收尾的两段固定等待（约 0.4~0.65 秒/张）。多瓦库是串行出牌的，每张牌的耗时会直接相加成整回合时长（实测约 1.0~1.4 秒/张），所以这是提速最明显的一项。下方两个「走动作队列」实验档的出牌同样吃本项（队列路径能跳过的只有收尾等待与结算堆补间，约省 0.15~0.3 秒/张）。关闭后恢复完整的出牌动画（与旧版观感一致）。",
                "On by default. Vakuu auto-plays skip the \"card flies to play area + smoke effect + pile tweens\" and two fixed waits (about 0.4-0.65s/card). Vakuu play serially, so per-card time adds up to the whole turn (about 1.0-1.4s/card measured), making this the biggest speedup. The two experimental \"action queue\" modes below also benefit (the queue path can only skip the settle wait and discard-pile tween, saving ~0.15-0.3s/card). Turning it off restores full play animations (as before)."),
            () => LocalWakuuAutopilotConfig.FastWakuuPlay,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("fastWakuuPlay", value));
        AddToggleRow(column,
            LocalModText.Select("【实验】瓦库出牌走动作队列", "[EXPERIMENTAL] Vakuu Plays via Action Queue"),
            LocalModText.Select(
                "默认关。**实验档**：开启后瓦库出牌不再用模组内联的自动出牌，而是像真人/远端玩家那样「把出牌动作排进自己的动作队列」（原版多人模式的同一条路径），换来多人模式的真实语义——某个瓦库在等自己的选牌时不挡住其他瓦库与真人。代价是三条刻意接受的语义变化：① 瓦库的出牌不再按「自动出牌」处理（虚无形态、佩尔之眼、不歇之巅等对自动出牌有特判的牌会开始把瓦库出牌算进去）；② 需要指定目标的牌若当刻解析不到目标，牌会留在手里（旧行为是打出去进弃牌堆）；③ 队列路径的演出比内联自动出牌略多（原版 PlayCardAction 自己没带跳过动画的参数，模组靠前缀补丁补上），**但仍然吃上面的「瓦库出牌加速」**：开着加速时队列出牌同样会跳过收尾固定等待与结算堆补间（约省 0.15~0.3 秒/张），只是「牌从手牌飞出」那段真人分支动画省不掉，所以仍比内联路径稍慢一点。关掉「瓦库出牌加速」即恢复完整演出。关闭本项即恢复既有行为；遇到任何异常请关掉本项并保留 godot.log。",
                "Off by default. **Experimental**: Vakuu plays no longer use the mod's inline auto-play; they enqueue plays into their own action queue like humans/remote players (the vanilla multiplayer path), gaining true multiplayer semantics — a Vakuu waiting on its own pick no longer blocks other Vakuu or humans. Three accepted trade-offs: 1) plays are no longer treated as \"auto-play\" (cards with special auto-play checks such as Void Form, Perr's Eye, Unresting Summit start counting Vakuu plays); 2) a targeted card stays in hand if no target resolves right now (old behavior played it into the discard); 3) the queue path shows slightly more animation than inline plays (vanilla PlayCardAction has no skip-anim flag; the mod adds one via a Prefix), but it **still benefits from \"Fast Vakuu Plays\" above**: it skips the settle wait and discard-pile tween (~0.15-0.3s/card); only the \"card flying out of hand\" human-branch animation remains, so it's slightly slower than the inline path. Turn off \"Fast Vakuu Plays\" to restore full animation. Turning this off restores prior behavior; if anything looks wrong, turn it off and keep godot.log."),
            () => LocalWakuuAutopilotConfig.WakuuPlayQueue,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("wakuuPlayQueue", value));
        AddToggleRow(column,
            LocalModText.Select("【实验】瓦库并发出牌（不互相等）", "[EXPERIMENTAL] Vakuu Concurrent Plays (No Mutual Waiting)"),
            LocalModText.Select(
                "默认关，**必须先开上面那项「瓦库出牌走动作队列」才生效**。这是上一项实验档的第二步（最终目标）：开启后多个瓦库不再排队等「全局出牌闸门」，一个瓦库在等自己的选牌时，其他瓦库与真人照常出牌（原版多人模式的真实语义）。代价：① 瓦库出牌期间不再把「当前玩家」钉在瓦库身上（出牌改由游戏动作泵执行、归属按角色分发），所以瓦库出牌的**前台视觉演出会更少**（更接近后台托管，伤害与效果照常）；② 视角档位设成「全程跟随」时，多个瓦库可能来回抢视角（建议配合默认的「不跟随」使用）。只想稳就先别开；遇到任何异常请关掉本项并保留 godot.log。",
                "Off by default; **requires \"Vakuu Plays via Action Queue\" above**. This is the second step of that experiment (end goal): multiple Vakuu no longer queue on the \"global play gate\" — a Vakuu waiting on its own pick no longer holds up other Vakuu or humans (true multiplayer semantics). Costs: 1) Vakuu plays no longer pin the \"current player\" to the Vakuu (plays run on the game action pump, attribution by character), so Vakuu plays have **less foreground visual flair** (closer to background autopilot; damage/effects work as normal); 2) with \"Always Follow\" camera, multiple Vakuu may fight over the camera (best used with the default \"Never\"). If you want stability first, leave it off; if anything looks wrong, turn it off and keep godot.log."),
            () => LocalWakuuAutopilotConfig.WakuuPlayOverlap,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("wakuuPlayOverlap", value));
        AddToggleRow(column,
            LocalModText.Select("后台托管（不切前台）", "Background Autopilot (No Camera Switch)"),
            LocalModText.Select(
                "瓦库回合不再强制切换到该角色视角，全程后台自动出牌与结束回合。关闭时下方「瓦库托管视角」档位不生效（等同全程跟随）。",
                "Vakuu turns no longer force the camera onto that character; all plays and turn-end happen in the background. When off, the \"Vakuu View Mode\" below is ignored (always follows)."),
            () => LocalWakuuAutopilotConfig.BackgroundMode,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("backgroundMode", value));
        column.AddChild(CreateViewModeRow(
            LocalModText.Select("瓦库托管视角", "Vakuu View Mode"),
            LocalModText.Select(
                "本档位细化「后台托管」的跟随程度，默认不跟随。**不跟随**：瓦库全程不抢视角，只保留两处防软锁兜底（作用域外需要你自己操作的选牌、安全网超时救援）；**仅关键节点**：瓦库回合开始时跳过去看一眼（看到轮到谁、抽了什么），约 1 秒后自动切回你自己，日常出牌不跟随；**全程跟随**：回合开始/结束、Hook 入队、瓦库出牌前都切过去（约等于关闭「后台托管」的观感）。仅当上方「后台托管（不切前台）」开启时生效。",
                "Finer control over \"Background Autopilot\" above (default: Never). **Never**: Vakuu never takes the camera, except two softlock guards (out-of-scope card picks that need you, and the safety-net timeout rescue); **Key Moments Only**: at Vakuu turn start, peek at that character (see whose turn it is and what's drawn), auto-switch back in ~1s; daily plays don't follow; **Always Follow**: switch over at turn start/end, hook enqueue, and before each Vakuu play (~like turning \"Background Autopilot\" off). Only effective when \"Background Autopilot (No Camera Switch)\" is on.")));
        AddToggleRow(column,
            LocalModText.Select("压制原版低语耳环", "Suppress Vanilla Earring"),
            LocalModText.Select(
                "持有【瓦库形态】时，局内再获得的原版低语耳环只保留 +1 能量，不再重复触发自动出牌。",
                "While holding [Vakuu Form], an in-run vanilla Whispering Earring only keeps its +1 energy and no longer re-triggers auto-play."),
            () => LocalWakuuAutopilotConfig.SuppressVanillaEarring,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("suppressVanillaEarring", value));
        AddToggleRow(column,
            LocalModText.Select("防止瓦库形态丢失", "Keep Vakuu Form Secure"),
            LocalModText.Select(
                "默认开。第三方遗物/事件（已见案例：东方「无底之胃」——拾起时吞噬初始遗物与先古遗物以外的全部遗物）会把【瓦库形态】一起移除，而托管判据只看是否持有该遗物，移除后瓦库会彻底停止自动操作。开启后：本 mod 会拦下对托管遗物的移除，并在遗物真的没了时按瓦库名单继续托管并补发。关闭 = 回到旧行为（可被移除，移除后瓦库停摆）。",
                "On by default. Third-party relics/events (seen: Touhou \"Bottomless Stomach\" — devours all relics except starter & ascend relics) can remove [Vakuu Form]; the autopilot check only looks at the relic, so Vakuu would stop entirely. When on, this mod blocks removal of the autopilot relic and, if it's ever gone, keeps autopiloting per the Vakuu list and re-issues it. Off = old behavior (can be removed; Vakuu stalls after removal)."),
            () => LocalWakuuAutopilotConfig.KeepWakuuFormRelic,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("keepWakuuFormRelic", value));

        column.AddChild(CreateSpacer(8));

        AddToggleRow(column,
            LocalModText.Select("火堆自动选择", "Auto-Choose at Rest Sites"),
            LocalModText.Select(
                "低血(<50%)优先睡觉；高血无遗物选项时升级非打击/防御的最后一张牌；全员满血时打击/防御也照升（全升完则睡觉）；有遗物选项（举重/挖掘等）则在睡觉以外随机；未满血且没得锻时愈合队友；帐篷多选时全拿。",
                "Sleep first at low HP (<50%); upgrade the last non-Strike/Defend card when high HP has no relic option; at full HP also upgrade Strikes/Defends (sleep once all are upgraded); with a relic option (Lift/Dig etc.) pick randomly among non-sleep options; heal teammates when unfit and no smithing; take all options at multi-choice tents."),
            () => LocalWakuuAutopilotConfig.AutoRestChoice,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoRestChoice", value));
        AddToggleRow(column,
            LocalModText.Select("战斗中自动用药水", "Auto-Use Potions in Combat"),
            LocalModText.Select(
                "默认关。按药水规则表逐药自动使用：血液/再生低血自用；果汁到手即喝；混沌药水填空位；力量等增益与火焰等攻击类精英/Boss战首回合；格挡/免伤类回合结束前按敌方意图伤害兜底；能量/迅捷/异蛇剩能量补牌；灰水/赌徒等定向消耗坏牌；故障机器人/储君/亡灵契约师/铁甲战士专属药水自动给对应队友，复制/超巨化优先给真人；污浊药水只在商人投掷；mod 药水普通战斗随机回合消耗。",
                "Off by default. Uses the potion rule table per potion: Blood/Regrowth self-use at low HP; Juice drank as soon as acquired; Chaos fills an empty slot; Strength-type buffs and Fire-type attacks on elite/boss fight first turns; Block/mitigation as a turn-end fallback based on enemy intent damage; Energy/Celerity/Isonade draw cards with leftover energy; Greywater/Gambler discard bad cards on purpose; class potions for Defect/Haveankh/Necrobinder/Ironclad auto-given to the matching teammate, Duplication/Gigantification to humans first; Dirty Water only thrown at the merchant; mod potions consumed on random turns in normal combat."),
            () => LocalWakuuAutopilotConfig.AutoUsePotions,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoUsePotions", value));
        AddToggleRow(column,
            LocalModText.Select("卡牌奖励自动领（最左）", "Auto-Claim Card Rewards (Leftmost)"),
            LocalModText.Select(
                "战后瓦库自己的卡牌奖励自动领最左边一张；金币与遗物同规则自动领取。",
                "Auto-claims the leftmost card of Vakuu's own post-combat card reward; gold and relics follow the same rule."),
            () => LocalWakuuAutopilotConfig.AutoClaimCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimCards", value));
        AddToggleRow(column,
            LocalModText.Select("金币/遗物奖励自动领", "Auto-Claim Gold/Relic Rewards"),
            LocalModText.Select(
                "战后瓦库的金币与遗物奖励自动领取。",
                "Auto-claims Vakuu's post-combat gold and relic rewards."),
            () => LocalWakuuAutopilotConfig.AutoClaimGoldRelics,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimGoldRelics", value));
        AddToggleRow(column,
            LocalModText.Select("药水奖励自动领取", "Auto-Claim Potion Rewards"),
            LocalModText.Select(
                "战后与事件中药水奖励自动领取。有空位直接领；满栏时先喝栏内鲜血药水腾位；否则奖励稀有度高于栏内最低才丢最低换领，等价或更低放弃。事件中的卡牌/金币/遗物奖励同上方开关。",
                "Auto-claims potion rewards after combat and in events. Grabs if there's a free slot; when full, drinks a Blood potion to free one; otherwise only replaces the lowest-rarity potion when the reward is higher rarity (equal or lower is skipped). Card/gold/relic rewards in events follow the toggles above."),
            () => LocalWakuuAutopilotConfig.AutoClaimPotions,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoClaimPotions", value));
        AddToggleRow(column,
            LocalModText.Select("事件自动选择", "Auto-Choose Events"),
            LocalModText.Select(
                "非共享事件按下方策略自动选择；触发战斗/小游戏/致死选项即停住等真人。",
                "Non-shared events are auto-chosen by the strategy below; events that trigger combat / minigames / lethal options stop and wait for a human."),
            () => LocalWakuuAutopilotConfig.AutoChooseEvents,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("autoChooseEvents", value));
        AddToggleRow(column,
            LocalModText.Select("社区统计辅助（需装 SkadaHelper）", "Community Stats Assist (Requires SkadaHelper)"),
            LocalModText.Select(
                "默认关。开启后瓦库的卡牌奖励与事件选项优先参考「皮皮军师: SkadaHelper」的社区大数据（卡牌：选取率 + 拿了之后的胜率增益；事件选项：胜率最高）。未装该 mod、查无数据或样本量不足时，静默回退到原来的最左 / 事件选项策略，行为与关闭时完全一致。",
                "Off by default. When on, Vakuu's card rewards and event options prefer \"Pipsi Strategist: SkadaHelper\" community data (cards: pick rate + win-rate gain when taken; events: highest win-rate option). If the mod isn't installed, no data, or the sample is too small, it silently falls back to the leftmost / event strategy — identical to being off."),
            () => LocalWakuuAutopilotConfig.SkadaAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("skadaAssist", value));
        AddToggleRow(column,
            LocalModText.Select("智能选牌优先级", "Smart Card Pick Priority"),
            LocalModText.Select(
                "默认关。开启后事件里的牌库删除 / 变化选牌按优先级表自动选取：删除优先 诅咒→状态→任务→打击→基础防御；变化优先变掉打击/防御并避开诅咒/状态/任务。关闭或场景未知时维持原选牌策略，行为与关闭时完全一致。",
                "Off by default. In events, deck-removal / transform picks follow a priority table: removal prefers Curse > Status > Quest > Strike > basic Defend; transform prefers Strikes/Defends and avoids Curses/Status/Quests. Off or unknown scenario keeps the existing pick strategy — identical to being off."),
            () => LocalWakuuAutopilotConfig.SmartPick,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("smartPick", value));
        AddToggleRow(column,
            LocalModText.Select("附魔智能选牌", "Smart Enchant Pick"),
            LocalModText.Select(
                "默认开。开启后瓦库附魔选牌按「原版附魔一览表」里填写的规则挑牌（例如：伶俐/墨影/灵巧选费用最低的牌，腐化/本能选伤害最高的攻击牌，注能选能抽 3 张以上的技能牌，克隆按是否持有不休陀螺走两套优先级）。关闭后回到原来的选牌策略。",
                "On by default. Vakuu enchant picks follow the rules in the \"vanilla enchant table\" (e.g. Sly/Ink/Swift picks the cheapest card; Corrupt/Instinct picks the highest-damage attack; Infuse picks a skill that draws 3+ cards; Clone uses two priority sets depending on whether you hold the Never-Tire Spinning Top). Turning it off returns to the old pick strategy."),
            () => LocalWakuuAutopilotConfig.SmartEnchant,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("smartEnchant", value));
        AddToggleRow(column,
            LocalModText.Select("个人偏好记录", "Personal Preference Recorder"),
            LocalModText.Select(
                "默认开。记录你自己点选的卡牌奖励与事件选项（只记真人决策，瓦库自动操作不记），写入 personal_stats.json，供「个人统计决策辅助」使用。只统计打完（胜/负）的局；进行中/半途退出的局不计。",
                "On by default. Records your own card-reward and event-option picks (human decisions only; Vakuu auto plays aren't recorded) into personal_stats.json, used by \"Personal Stats Assist\". Only finished runs (win/loss) count; in-progress / abandoned runs don't."),
            () => LocalWakuuAutopilotConfig.PersonalRecorder,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("personalRecorder", value));
        AddToggleRow(column,
            LocalModText.Select("个人统计决策辅助", "Personal Stats Assist"),
            LocalModText.Select(
                "默认关。开启后瓦库选牌/选事件优先参考你自己打出的个人统计（多人局优先参考多人局数据）：事件选项按你的「选择率」（遇到这个事件时你多选哪个）+ 胜率综合选取，用稳定 loc key 查表、不受界面语言影响；选牌按抓取率 + 拿了之后的胜率增益。个人样本不足或无倾向时回退社区统计与默认策略。样本越多越贴合你的打法（含 mod 卡）。",
                "Off by default. Vakuu card/event picks prefer your own statistics (multiplayer-run data first in multiplayer): events by your \"choose rate\" + win rate, matched via stable loc keys (language-independent); cards by take-rate + win-rate gain. Falls back to community stats and the default strategy when your sample is short or inconclusive. More samples fit your play better (incl. mod cards)."),
            () => LocalWakuuAutopilotConfig.PersonalAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("personalAssist", value));
        column.AddChild(CreatePersonalTierRow(
            LocalModText.Select("个人统计偏好档位", "Personal Stats Tier"),
            LocalModText.Select(
                "用个人统计做决策时按哪个切片查表。角色优先：先看本角色，不足再放宽（默认）；总量优先：角色样本不足时直接信跨角色总样本；只看角色：绝不用其他角色的数据兜底（适合角色专属牌）。影响瓦库卡牌奖励与事件选项两条决策链。",
                "Which slice of your personal stats to use when deciding. Character First: that character first, then relax (default); Volume First: trust cross-character totals when the character sample is short; Character Only: never use other characters' data as fallback (for character-specific cards). Affects both the card-reward and event decision chains.")));
        AddToggleRow(column,
            LocalModText.Select("商店自动买卡", "Auto-Buy at Shop"),
            LocalModText.Select(
                "默认关（Phase 4 实验）。开启后瓦库角色的商店视图打开时，自动买「社区统计胜率 ≥ 20% 且付完仍保留 ≥ 50 金币」的卡。这是下面两个子开关的总开关；删牌服务仍未做。",
                "Off by default (Phase 4 experimental). When a Vakuu character's shop view opens, auto-buys cards with community win rate >= 20% that keep >= 50 gold after purchase. This is the master switch for the two sub-options below; the card-removal service isn't automated yet."),
            () => LocalWakuuAutopilotConfig.ShopAssist,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssist", value));
        AddToggleRow(column,
            LocalModText.Select("商店买卡·无统计数据也买", "Shop: Buy Without Stats Too"),
            LocalModText.Select(
                "默认关。需「商店自动买卡」开启。商店里查不到社区统计胜率的卡（多为 mod 卡，皮皮军师未收录）也会按「付完仍保留 ≥ 50 金币」买入，否则无数据的卡静默跳过。",
                "Off by default; requires \"Auto-Buy at Shop\". Cards with no community win-rate data (mostly mod cards, not indexed by SkadaHelper) are also bought under the \"keep >= 50 gold\" rule; otherwise data-less cards are silently skipped."),
            () => LocalWakuuAutopilotConfig.ShopAssistBuyNoData,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssistBuyNoData", value));
        AddToggleRow(column,
            LocalModText.Select("商店自动买遗物", "Auto-Buy Relics at Shop"),
            LocalModText.Select(
                "默认关。需「商店自动买卡（总开关）」开启。遗物没有社区统计可查，只按「买得起且付完仍保留 ≥ 50 金币」买入（不会挑稀有度）。遗物获得时若触发选牌，由瓦库托管的自动作答处理。",
                "Off by default; requires the \"Auto-Buy at Shop\" master switch. Relics have no community stats, so they're bought purely by \"affordable and still >= 50 gold after paying\" (no rarity bias). If obtaining a relic triggers a card pick, the Vakuu auto-answer chain handles it."),
            () => LocalWakuuAutopilotConfig.ShopAssistBuyRelics,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssistBuyRelics", value));
        AddToggleRow(column,
            LocalModText.Select("商店自动买药水", "Auto-Buy Potions at Shop"),
            LocalModText.Select(
                "默认关。需「商店自动买卡（总开关）」开启。与遗物同一口径（按价格 + 付完保留 ≥ 50 金币），额外要求药水栏还有空位；栏位满了就停手。",
                "Off by default; requires the \"Auto-Buy at Shop\" master switch. Same rule as relics (price + keep >= 50 gold), plus a free potion slot is required; stops once the bar is full."),
            () => LocalWakuuAutopilotConfig.ShopAssistBuyPotions,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("shopAssistBuyPotions", value));
        column.AddChild(CreateStrategyRow(
            LocalModText.Select("事件选项策略", "Event Choice Strategy"),
            LocalModText.Select(
                "事件自动选择时挑哪个选项。很多事件一直选第一个会死，可切到最后一个或随机规避。",
                "Which event option to pick when auto-choosing. Many events kill you if you always take the first; switch to Last or Random to dodge."),
            "eventChoiceMode",
            () => LocalWakuuAutopilotConfig.EventChoiceMode));
        column.AddChild(CreateStrategyRow(
            LocalModText.Select("战斗内选牌策略", "In-Combat Card Pick Strategy"),
            LocalModText.Select(
                "效果类选牌（酒狐合成、开局遗物二选一、从手牌选 N 张、事件附魔/升级/变化选牌等）取哪张。默认最后；稀有度最高优先选 Ancient/Rare。卡牌奖励始终领最左。",
                "Which card to take for effect picks (Fox-Sake fusion, starter-relic choice, pick N from hand, event enchant/upgrade/transform picks, etc.). Default: last; Rarest prefers Ancient/Rare. Card rewards always take the leftmost card."),
            "cardPickMode",
            () => LocalWakuuAutopilotConfig.CardPickMode,
            allowRare: true));
        AddToggleRow(column,
            LocalModText.Select("涅奥开局自动选", "Auto-Choose NEOW Bonus"),
            LocalModText.Select(
                "允许自动选择涅奥（NEOW）开局奖励；默认关闭。",
                "Allows automatically choosing the NEOW (start) bonus; off by default."),
            () => LocalWakuuAutopilotConfig.NeowAutoChoose,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("neowAutoChoose", value));

        BuildOtherSection(column);

        column.AddChild(CreateSpacer(6));
        column.AddChild(CreateDivider(new Color(0.35f, 0.35f, 0.35f, 0.7f)));
        column.AddChild(CreateLabel(
            LocalModText.Select("点左下角返回按钮或再次打开本页可随时退出。", "Use the Back button (bottom-left) or reopen this page anytime to exit."),
            18, new Color(0.6f, 0.6f, 0.6f)));
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
        column.AddChild(CreateLabel(LocalModText.Select("其 它 设 置", "O T H E R   S E T T I N G S"), 34, new Color(1f, 0.95f, 0.75f)));
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel(
            LocalModText.Select(
                "以下功能与「瓦库形态托管（总开关）」无关：即使不开托管也按各自的开关生效，改动同样立即写入 vakuu_autopilot.json。",
                "These features are independent of \"Vakuu Form Autopilot (Master Toggle)\": each works by its own toggle even without autopilot. Changes also write to vakuu_autopilot.json immediately."),
            20, new Color(0.72f, 0.72f, 0.72f)));
        column.AddChild(CreateSpacer(10));

        AddToggleRow(column,
            LocalModText.Select("跨角色卡组（战后奖励）", "Cross-Character Card Offers (Post-Combat)"),
            LocalModText.Select(
                "默认关。开启后每个角色的战后卡牌奖励追加一组从「其他角色」卡池抽取的 3 选 1（原作者未完成的 v1.30 设计）。",
                "Off by default. Each character's post-combat card reward gets an extra 3-choose-1 drawn from other characters' pools (the original author's unfinished v1.30 design)."),
            () => LocalWakuuAutopilotConfig.ExtraCrossCharacterCardReward,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("extraCrossCharacterCardReward", value));
        AddToggleRow(column,
            LocalModText.Select("召唤物血量显示", "Summon HP Display"),
            LocalModText.Select(
                "默认开。本地多控时，顶部每个玩家的状态条旁显示其持有的全部召唤物/宠物血条（每只一行，含名字与血量，如亡灵契约师的奥斯蒂 Osty）。判据是「该玩家是否拥有召唤物」而非职业——奥斯蒂也可以由亡灵契约师为其他玩家召唤、或其它角色合法持有 Necrobinder 卡召唤；女王 mod 等走原版宠物体系的召唤物同样会显示。召唤物死亡后显示 0-X（置灰），提示需要复活/再召唤。",
                "On by default. In local multi-control, each top player bar also shows HP bars for all summons/pets that player holds (one line each, name + HP, e.g. Osty for Necrobinder). The judge is \"does this player have summons\", not class — Osty can be summoned for another player, or other classes can legally hold Necrobinder cards; Queen-mod summons through the vanilla pet system show too. Dead summons show 0-X (greyed) to prompt a revive/re-summon."),
            () => LocalWakuuAutopilotConfig.PetHpBadge,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("petHpBadge", value));
        AddToggleRow(column,
            LocalModText.Select("跳过他人回合开始抽牌演出", "Skip Other Players' Turn-Start Draw Animations"),
            LocalModText.Select(
                "默认关。本地多控下每回合开始会依次把前台切到每个真人玩家、逐个播完自动抽牌动画才轮到下一个，10 人以上时要等很久。开启后回合开始只保留「当前正在看的那位」的抽牌演出，其他人的回合开始不再切前台——原版对非本地玩家的抽牌本来就不做动画（只走数据），所以他们的抽牌瞬时生效，之后切到该角色时会立刻看到完整手牌。",
                "Off by default. In local multi-control each turn start switches the camera to every human player in turn and plays the auto-draw animation, which takes a while with 10+ players. When on, only the currently-watched player's draw animation plays; other players' turn starts no longer switch the camera — vanilla already draws no animation for non-local players (data-only), so their draws complete instantly and you see the full hand when you switch to them."),
            () => LocalWakuuAutopilotConfig.SkipTurnStartDrawAnim,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("skipTurnStartDrawAnim", value));
        AddToggleRow(column,
            LocalModText.Select("自有统计角标", "Own Stats Badge"),
            LocalModText.Select(
                "默认关。开启后在「奖励选牌卡/商店卡/事件选项按钮」的角标位置（见下方「自有统计角标位置」）显示你自己记录的总抓取率/总选择率（XX%），鼠标悬停弹出分幕首抓/重复抓取率、胜率的详情。只显示本地个人统计，与皮皮军师（SkadaHelper）社区统计 UI 分开、互不覆盖。",
                "Off by default. Shows your own recorded take-rate / choose-rate (XX%) in the badge corner of reward-pick cards / shop cards / event-option buttons (see \"Own Stats Badge Corner\" below); hovering pops up act-split first-take / repeat rates and win-rate details. Personal local stats only, kept separate from SkadaHelper's community UI."),
            () => LocalWakuuAutopilotConfig.StatBadge,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("statBadge", value));
        column.AddChild(CreateStatBadgeSourceRow(
            LocalModText.Select("统计角标数据来源", "Stats Badge Data Source"),
            LocalModText.Select(
                "角标百分比怎么算（**始终只显示一个数字**）：**仅个人**=只用你自己打出的统计；**个人+社区兜底**=该卡没有个人记录时才用皮皮军师的社区抓取率补足；**融合**=个人与社区按伪计数加权合成一个抓取率（个人样本越多越主导，个人 5 次以上即与社区平手以上）。社区数据来自皮皮军师（SkadaHelper）的数据接口；未装或查无数据时退回个人统计/0%。",
                "How the badge percentage is computed (**always one number**): **Personal Only** = your own stats only; **Personal + Community Fallback** = only when there's no personal record for the card, fill in SkadaHelper's community take-rate; **Blended** = personal & community weighted by pseudo-counts into one rate (more personal samples dominate; 5+ personal samples already tie or exceed the community). Community data comes from SkadaHelper's interface; if it's not installed or there's no data, falls back to personal stats / 0%.")));
        column.AddChild(CreateStatBadgeCornerRow(
            LocalModText.Select("自有统计角标位置", "Own Stats Badge Corner"),
            LocalModText.Select(
                "角标挂在目标（卡/事件选项）的哪个角，点右侧按钮在 左下 → 右下 → 右上 → 左上 之间循环。默认 **左下**——皮皮军师（SkadaHelper）会自己把社区统计标签画在卡的右侧，放右下会与它重叠并被本 mod 的顶层叠加盖住；若你想让两者挨在一起显示，可切到「右上/左上」自行比较。",
                "Which corner the badge sits on (cards / event options); click the button on the right to cycle Bottom-Left → Bottom-Right → Top-Right → Top-Left. Default **Bottom-Left** — SkadaHelper draws its own community label on the card's right side, so Bottom-Right would overlap and be covered by this mod's top-layer overlay; if you want them side by side, try \"Top-Right / Top-Left\".")));
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
            string next = NextChoiceMode(currentModeGetter(), allowRare);
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
            LocalWakuuAutopilotConfig.LastChoiceMode => LocalModText.Select("最后一个", "Last"),
            LocalWakuuAutopilotConfig.RandomChoiceMode => LocalModText.Select("随机", "Random"),
            LocalWakuuAutopilotConfig.RareChoiceMode => LocalModText.Select("稀有度最高", "Rarest"),
            _ => LocalModText.Select("第一个", "First"),
        };
    }

    /// <summary>
    /// 按「档位值」（语言无关的配置键）循环，不能按按钮显示文本循环——
    /// 显示文本已本地化，按文本判断在英文环境下会落到错误分支。
    /// </summary>
    private static string NextChoiceMode(string currentMode, bool allowRare)
    {
        if (allowRare)
        {
            return currentMode switch
            {
                LocalWakuuAutopilotConfig.FirstChoiceMode => LocalWakuuAutopilotConfig.LastChoiceMode,
                LocalWakuuAutopilotConfig.LastChoiceMode => LocalWakuuAutopilotConfig.RandomChoiceMode,
                LocalWakuuAutopilotConfig.RandomChoiceMode => LocalWakuuAutopilotConfig.RareChoiceMode,
                _ => LocalWakuuAutopilotConfig.FirstChoiceMode,
            };
        }

        return currentMode switch
        {
            LocalWakuuAutopilotConfig.FirstChoiceMode => LocalWakuuAutopilotConfig.LastChoiceMode,
            LocalWakuuAutopilotConfig.LastChoiceMode => LocalWakuuAutopilotConfig.RandomChoiceMode,
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
            LocalWakuuAutopilotConfig.VolumeFirstTier => LocalModText.Select("总量优先", "Volume First"),
            LocalWakuuAutopilotConfig.CharacterOnlyTier => LocalModText.Select("只看角色", "Character Only"),
            _ => LocalModText.Select("角色优先", "Character First"),
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
            LocalWakuuAutopilotConfig.ViewModeKeyNodes => LocalModText.Select("仅关键节点", "Key Moments Only"),
            LocalWakuuAutopilotConfig.ViewModeAlways => LocalModText.Select("全程跟随", "Always Follow"),
            _ => LocalModText.Select("不跟随", "Never"),
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
            WakuuStatBadgeSource.PersonalThenCommunity => LocalModText.Select("个人+社区兜底", "Personal + Community"),
            WakuuStatBadgeSource.Blended => LocalModText.Select("融合", "Blended"),
            _ => LocalModText.Select("仅个人", "Personal Only"),
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
            WakuuStatBadgeCorner.BottomRight => LocalModText.Select("右下", "Bottom-Right"),
            WakuuStatBadgeCorner.TopRight => LocalModText.Select("右上", "Top-Right"),
            WakuuStatBadgeCorner.TopLeft => LocalModText.Select("左上", "Top-Left"),
            _ => LocalModText.Select("左下", "Bottom-Left"),
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
