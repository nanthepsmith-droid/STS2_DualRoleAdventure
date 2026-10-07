using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using LocalMultiControl.Scripts.Models.Cards;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    // r205：会话级可变状态收进 SelfCoopSessionState（R5-3）——「进 / 出大厅、进 / 退局、读档窗口」
    //       三条时序共用同一关闭出口（LeaveSession），复位矩阵可单测；对外调用点零改动
    //       （转发成员的名字与可见性保持原样）。
    // r206：手牌 UI 加入守卫扩到「非选牌期间」—— 共享手牌 UI 只显示受控席位的手牌，
    //       拦住瓦库（后台托管席位）的牌节点经取消出牌 / 回手牌路径漏进真人手牌（幽灵牌）。
    // r207：同一条守卫的两条分支都补"淡出回收"—— 只拦不回收会让卡面停在屏幕中间不动
    //       （实机：瓦库打【群情激愤】时真人那侧正在选牌，选牌分支把节点拦下却没人收）。
    // r208：瓦库四功能开工（第一件「净化」，默认关）—— 休息处给真人席位注入「净化」选项，
    //       先弹新造的局内「选玩家」选择器选目标瓦库，再走原版删牌界面删其最多 5 张牌；
    //       顺带把选择器守卫从"类型白名单"改成"按 WakuuSelectorRegistry 登记身份判"。
    // r209：r208 实机两个缺陷 —— ① 缺图标被 AssetCache 记 failed ⇒ RestSiteOption.Icon 抛异常，
    //       NRestSiteButton.Reload 在"设图标"处中断 ⇒ 按钮连名字都没设上（实机显示占位文字"Dig"）；
    //       改为 AssetPaths 返空 + 借用原版【烹饪】图标注册到本路径。
    //       ② 瓦库「作用域外牌组选牌自动作答」把净化选牌替真人答了（真人看不到删牌界面）⇒ 新增
    //       CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice 作用域，净化流程内压制。
    // r210：r209 的图标修法不彻底 —— 借原版图标拿到的实例是 AssetCache 的「missed cache 资产」，
    //       进房时 UnloadMissedCacheAssets 会 Dispose 它，缓存里的别名随即悬空 ⇒ Icon 返 null ⇒
    //       NThoughtBubbleVfx.SetTexture(null) 抛 NotImplementedException 冒穿 ChooseOption，
    //       整次「点选项」被中断（实机：净化要点两下）。改为在 LocalRestSiteOptionIcon 里
    //       用 GetImage() + ImageTexture.CreateFromImage 造**自持**纹理副本（抽成四功能共用工具）。
    // r211：r210 仍"没变化" —— 根因是 PreloadManager.LoadAssetSets 用「已缓存 − 本房间需求集」的**差集**
    //       决定卸载谁：r210 把 AssetPaths 覆写成空 ⇒ 我们的 icon 路径不在需求集里 ⇒ 注册好 5 行后就被
    //       卸载并 Dispose，Icon 又变 null。修法 = **不覆写 AssetPaths**（路径留在需求集里）+ 预加载前登记
    //       自持纹理（needLoaded 因已缓存而跳过它）⇒ 既不被卸、也不加载不存在的文件。
    // r212：再加一道兜底 —— 选项 IsEnabled 求值（建按钮时）时补一次图标注册，防时序意外。
    // r213：瓦库四功能第二件「我们联合」（默认关）—— 战斗界面加一个「我们联合」按钮（每场一次）：
    //       ① 从自己卡组**复制** 1 张 → 指定瓦库战斗手牌；② 从指定瓦库卡组**复制** 1 张 → 自己手牌
    //       （双向复制、主卡组不动）。建卡走 CombatState.CreateCard、塞手牌走 CardPileCmd.Add(PileType.Hand)，
    //       选牌走 CardSelectCmd.FromDeckGeneric + 两道防护（PushChoiceOwner / SuppressForHumanChoice）。
    // r213b：② 我们联合首测报「复制了没效果」—— 日志 `我们联合执行失败: Mutable model of type X used in incorrect place.`：
    //       建卡用 CombatState.CreateCard(卡组牌, 目标) 是错的（它要**规范模型**，内部 ToMutable() → AssertCanonical()
    //       会抛 MutableModelException），而喂 canonical 又会丢升级/附魔。改走
    //       CombatState.CloneCard(可变实例)（保留当前状态）+ CardModel.GiveToAnotherPlayer(目标) 改归属。
    // r215：用户报「打一半我和瓦库的立绘左右站位对调了」（不影响战斗）—— 原版站位只在
    //       NCombatRoom.CreateAllyNodes 里按 LocalContext.IsMe 排一次，本地多控下"谁是『我』"会漂。
    //       本轮只加**诊断**（不改行为）：入战站位快照 + 逐帧位移探针（LocalCreaturePositionProbe），
    //       等下一局复现时定位是"一开始就摆反"还是"中途被谁挪的"。
    // r216：瓦库四功能第三件「炼化」（默认关）—— 休息处给真人席位注入「炼化」选项：
    //       选目标瓦库 → 收编卡牌（档位 不限/20/5，至少 1 张）→ 选收编 1 件遗物（排除耳环/形态）
    //       → 按比例收编血量与血上限（GainMaxHp）→ 炼掉目标（SetMaxHpInternal(0) + Kill(force:true)）。
    //       席位不摘名单、不缩队、不做存档标记（用户拍板「炼化了≠死透了」）。
    //       同时把选玩家/选遗物弹层抽成共用基建 LocalWakuuChoiceOverlay（行为不变，日志锚点不变）。
    // r217：用户实机小测（r216）反馈 ——
    //  ① **战斗中两人立绘又换位**：r215 的位移探针这次采到了（快照反而一行没打：`NCombatRoom._Ready`
    //     那一刻 `CombatManager.IsInProgress` 还是 false ⇒ 旧实现静默 return）。实锤换位是
    //     **第三方 NinjaSlayer 的 `YamotoKokiAllyLayoutPatch`** 重排站位（它给自己的同伴排布局，
    //     先按 `LocalContext.IsMe` 给 `PositionPlayersAndPets` 的入参排序）；
    //     而原版落位是 `foreach(node) if (IsMe(node)) list.Insert(0, node)` —— 我们的 IsMe 放行口子
    //     让"瓦库也算 me"⇒ **两个 me 被 Insert(0) 反转相对次序** ⇒ 每次它重排就把两人对调。
    //     修法 = 把该调用方加进放行黑名单（维持原判 ⇒ 全场只有一个 me，排序确定、重排不再对调）；
    //     同时把入战快照判定放宽 + 首次采样兜底 + 跳过原因日志，防下次又"一片空白"。
    //  ② **炼化选遗物一页放不下** ⇒ 选玩家/选遗物/选药水共用的弹层加滚动（列表封顶 520px）。
    //  ③ 炼化加两个开关：`refineTakeVakuuAssets`（默认开；关掉则不吃瓦库的牌与遗物，
    //     给"日后把瓦库复活"留活路）、`refineTakeVakuuPotions`（默认开；自选 1 瓶药水后
    //     移除瓦库全部药水；真人药水栏满则整步跳过、不动它的药水）。
    // r218：用户小测 r217 的两条 ——
    //  ① **修 BUG**：药水"跳过"分支照样把瓦库药水删了（实机 10742 `炼化跳过药水收编: … 真人空位=False`
    //     紧接 10744 `移除瓦库药水=4`）。修法 = 把"拿走"与"移除"解耦：只有**真的拿走了 ≥1 瓶**
    //     才移除瓦库其余药水；跳过 / 一瓶不拿都**不动**它的药水。
    //  ② **药水改多选**（用户口径「瓦库都死了，我不是想拿几瓶就拿几瓶」）：共用弹层加**多选模式**
    //     （行可反复勾选、标题下显示 `已选 x/N`、确认按钮显示件数），选药水走
    //     `LocalWakuuPotionPicker.PickManyAsync(…, maxSelect)`，上限 = 真人药水栏**空位数**
    //     （与"战斗奖励拿药水"同口径），确认后没拿的也一并移除。
    // r219：用户两条口径 ——
    //  ① 「遗物要玩家拿走了瓦库就没有了；要防的是**无限强力遗物**；重复件本身是正常玩法」：
    //     遗物收编本来就是"先 RelicCmd.Remove 再授新实例"（真·拿走，不是复制），但**一旦移除被第三方前缀
    //     拦下**，继续授副本就等于白送一件 ⇒ 反复炼化同一件强力遗物可无限复制。故本轮加两道
    //     "先确认拿走、再给新的"守卫 + 证据日志：遗物 = Remove 后校验目标身上已无该遗物（未生效就放弃收编）；
    //     药水 = 改成**先全部移除（逐瓶校验）再授**，且只授"确实已移除"的那些。
    //     **不**做"玩家已有同件就过滤"（重复件是用户认可的正常玩法）。
    //  ② 「遗物也要能拿多件，且有多档（1/3/5/任意），不然最多拿 1 件收益太低、不如让瓦库活着」：
    //     新增档位 `refineRelicLimit`（**默认 any=不限**，循环 不限→5→3→1），选遗物改**多选弹层**
    //     （复用 r218 给药水做的多选模式 `PickIndexesAsync`），上限 = 档位与候选数取小；
    //     选项描述加 `{Relics}` 占位符。
    // r220：用户小测 r219 后两条 ——
    //  ① **炼化时"不能一张牌都不要"妨碍玩小卡组** ⇒ 卡组选牌界面 `Min` 1 → **0**（可一张不选）。
    //     代价：`Min=0` 后"确认 0 张"与"取消/关闭"都返回空表、无法区分 ⇒ 该步不再有"取消 = 中止"，
    //     空返回一律按"不拿牌、继续炼化"处理；想中止整次炼化请用选目标 / 选遗物 / 选药水三步的取消。
    //     选牌提示文案也写明"可以一张都不选，直接确认"。
    //  ② **两条日志措辞**（r219 实测时一度被误读）：`炼化未拿走任何药水（已确认「不拿」）` 现在只在
    //     "弹层里确认了 0 瓶"时打（整步跳过另有日志）；`RestSitePatch` 的 `休息区选项执行失败`
    //     改为 `休息区选项返回 false，不触发自动切人（我方自定义选项的取消属正常）`。
    // r221：r220 那局"炼化时一张牌都不选 ⇒ 直接退出炼化"的**根因 = 我自己漏删的一段代码**：
    //   `SelectCardsAsync` 里还留着 r219 时代的 `if (selected.Count == 0) return null;`，
    //   而 r220 已把签名改成非空、调用点直接读 `.Count` ⇒ 0 张时抛 NRE 冒穿 OnSelect
    //   （实机日志：`Player … chose cards []` 紧接 `NullReferenceException at RefineWakuuRestSiteOption.OnSelect()`）。
    //   修法 = 删掉那段（空表就是"不拿牌"）+ 给 OnSelect 包一层 try/catch 兜底（打异常全文 + 返回 false 不消费选项），
    //   并把炼化那一组新文件统一加 `#nullable enable`（项目级没开可空检查 ⇒ 这类"声明非空却返回 null"
    //   以前编译器不会提醒；开了之后构建门禁的 0 警告要求就是一道闸）。
    //   同轮确认：**站位修复实机通过**（快照 `source=first-tick` 只有一个 `IsMe=True`，
    //   位移探针全程只有等量抖动、再没出现"节点索引 1→0 / 0→1"的对调）。
    // r222：用户实机「炼化瓦库后**结束回合、敌方回合不会开始**」的根因定位与修复 ——
    //   `Combat #2 turn loop died … KeyNotFoundException: The given key 'Player' was not present in the dictionary`
    //   ← 第三方 **LexNinja2 的 `LexKelaSingleton.AfterSideTurnEnd`**：它按 `CurrentCombatState.Players`
    //   直接索引两个 `Dictionary<Player,bool>`（`_isActive`/`_usedLexKela`），而这两个键**只在
    //   该玩家"开始过回合"时写入** ⇒ 被炼化的瓦库**死在战斗外**、下一场战斗里一直在 Players 里却
    //   永不开始回合 ⇒ 键缺失 ⇒ 首次侧回合结束就抛 ⇒ 回合循环死掉。
    //   修法 = 新增 `LexNinja2KelaTurnEndGuardPatch`（延迟补丁：在它的 `AfterSideTurnEnd` 前把
    //   本场所有玩家在两个字典里补齐缺失键，补 false），只在本地多控会话生效、未装该 mod 只记日志；
    //   挂载点 = Entry 阶段 3 / 每次进局 / 战斗房间就绪（它的程序集常晚于本 mod 的 PatchAll）。
    //   另：入战快照新增 `已死席位=[…]`（这类"死在战斗外的席位"是第三方按回合登记状态的通用前提缺口）。
    // r223：r222 那局的两条 ERROR 定性（一条是我方副作用，已修；一条是游戏自身分支，记档）——
    //  ① **我方副作用（修）**：宝箱手势层 `NHandImageCollectionUpdateVisibilityPatch` 用
    //     `PeerInputSynchronizer.GetScreenType(playerId)` 探测屏幕类型，而它的实现是
    //     `GetOrCreateStateForPlayer(playerId).netScreenType` ⇒ **不存在就创建**；创建触发它自己的
    //     `StateAdded` ⇒ `NHandImageCollection.OnInputStateAdded` ⇒ `AddHand(playerId)`，而宝箱界面
    //     `Initialize` 已加过手势 ⇒ 游戏打 `[ERROR] Tried to add hand for player … twice!`
    //     （实机：炼化过的死席位 …327 没有输入状态，我们这次探测把它"造"了出来）。
    //     修法 = 改走**私有只读** `GetStateForPlayer(ulong)`（反射只解析一次）：拿不到状态就按"无屏幕类型"
    //     处理（调用方既有回退：用本机屏幕类型推断），**绝不产生副作用**；反射解析失败只 WARN 一次、
    //     退化为"不显示手势"（纯观感），换来不再触发那条游戏 ERROR。
    //  ② **游戏自身分支（记档，不改）**：进事件房间时游戏对每个玩家跑 `EventModel.BeginEvent`，
    //     碰到死席位会自己走 `if (player.Creature.IsDead) { Log.Error("The generic event death message
    //     should not appear!"); SetEventFinished(GENERIC.youAreDead.description); }` ⇒ 属"死者滞留"设计的
    //     **预期噪声**（vanilla 多人里"上一场死掉的队友"同类；游戏自己已把该席位的事件收尾），
    //     已记为复测契约"期望 0"的例外。
    //  同轮实证：LexKela 守卫**补键生效**（补键处数=2）且**回合循环正常**（两场战斗均能"结束回合 →
    //  自动补齐敌方回合就绪 → 敌方回合"）；整场站位**0 对调**；快照 `已死席位=[角色2]` 锚点生效。
    // r224~r226：④ 的前置功课 —— 验证「mod 自定卡如何挂进卡池」这条链路（当时叫「地狱战神验证」）。
    //  ① 一张占位卡 `Rarity=Event` 进 `EventCardPool`（该池全游戏**零获取入口**，不污染奖励/商店/战斗生成）；
    //     本项目**没有 PCK** ⇒ 覆写 PortraitPath 借原版卡立绘（同遗物借原版图标）。
    //  ② `ModHelper.AddModelToPool<EventCardPool, …>()` **必须早于游戏初始化**（卡池首次访问即冻结，
    //     再登记抛 `InvalidOperationException`）；卡类型本身由 `ModelDb.Init()` 经
    //     `GetSubtypesInMods<AbstractModel>` 自动扫到，**不用手动注册**。
    //  ③ 本地化运行期注入 `cards` 表（缺键会让 LocString 抛 LocException 冒穿渲染），
    //     自检锚点挂在**初始化早期**（r225 教训：挂"每次进局"的话，用户在主菜单开卡牌库就永远看不到锚点；
    //     r226 改为 `LocManager.Initialize()` 后置注入 + `ModelDb.Preload()` 后置自检，两个目标都进
    //     `OptionalPatchTargets`，缺了只 WARN）。
    // r227（2026-10-07）：**转正为功能首版「瓦库的爹」（遗物 + 三张占位牌）**。
    //  命名口径（2026-10-06 用户拍板）：「地狱战神」这个名字留给**战灵召唤**（临时玩家召唤，排期队尾）；
    //  本条（遗物 + 我挡/你攻/合体 三张占位牌）叫**瓦库的爹** ⇒ 代码里 `HellGod*` 一律改名 `Daddy*`
    //  （占位卡已"转正"成三张牌之一，于是旧验证卡删除）。
    //  ① 三张占位牌 `LocalWakuuDaddy{Shield,Focus,Merge}Card`：0 费技能、`Rarity=Event`、**效果留空**
    //     （打出去只消耗 0 费）；目标类型照提案 §5.1（我挡/合体 = AnyAlly，你攻 = AnyEnemy）；
    //     升级 = 加「保留」（`OnUpgrade → AddKeyword(Retain)`，原版 Anointed 同款）。
    //  ② 遗物 `LocalWakuuDaddyRelic` 进 `EventRelicPool`；效果 = **战斗开始时给这 3 张牌**
    //     （`AfterSideTurnStart` + `TurnNumber == 1`，原版 BigHat 同款写法）。
    //  ③ 发放：配置开关 `vakuuDaddy`（**默认关**）开时，开局给**真人席位**发一件（判据是纯逻辑
    //     `WakuuDaddyPolicy.ShouldGrantRelic`，瓦库席位不发 —— 它们已有托管遗物）。
    //  ④ 自检锚点改名 `[瓦库的爹验证]`（内容 = 三张牌 + 遗物 的 入池/本地化/立绘 四项）。
    private const string BuildMarker = "Revival v1.44.0 (game v0.111.0, marker=2026-10-07-r227)";

    private static Harmony? _harmony;

    /// <summary>
    /// 致命错误码（AGENTS.md §9）。日志 / 解析器 / issue 统一使用同一套 ID。
    /// </summary>
    private static class FatalCode
    {
        internal const string Config = "CONFIG001";      // 配置 / 记录器加载失败
        internal const string Model = "MODEL001";        // 遗物注册 / 本地化失败
        internal const string ClrRuntime = "CLR001";     // 进程架构 / 映像运行时版本不符
        internal const string ClrImage = "CLR002";       // 必需依赖 BadImageFormatException
        internal const string AsmAbi = "ASM002";         // 游戏程序集 ABI 版本漂移
        internal const string DepMissing = "DEP001";     // 必需依赖缺失
        internal const string DepLoad = "DEP002";        // 必需依赖加载失败
        internal const string PatchApply = "PATCH001";   // Harmony 应用中断
        internal const string PatchCritical = "PATCH002"; // Critical 补丁缺失
        internal const string PatchSelfTest = "PATCH003"; // 补丁自检无法执行
        internal const string Stage = "STG001";          // 阶段抛出未处理异常
    }

    /// <summary>
    /// 启动自检期望清单：这些目标必须被 Harmony 打上，否则说明被 PatchAll 静默跳过
    /// （本 mod 坑 1：类上缺类级 [HarmonyPatch] 时整个类被跳过且无任何报错）。
    ///
    /// 匹配口径（升级到完整名，消除同名类型 / 重载歧义）：
    ///   "Namespace.Type.Method"            完整类型名（清单标准写法，含命名空间）
    ///   "Namespace.Type.Method/2"          追加参数个数，用于区分重载（仅在确知补丁目标重载时写）
    /// 运行期会把「已打补丁方法」同时展开成 simple / full / simple+argc / full+argc 四种键，
    /// 所以写 full 一定能命中；写 full+argc 只在重载之间做精确区分。
    ///
    /// 门禁：tests/LocalMultiControl.Tests/ExpectedPatchTargetsTests.cs 会拿 sts2.dll 元数据
    /// 逐条核对（类型/方法存在、参数个数一致、格式合规）——写错在游戏更新或手误时**单测先红**，
    /// 不会等到实机才误报 Critical 缺失（那会 INIT_FAILED 让 mod 报红）。
    /// 维护口径：与 Scripts/Tools/patch_coverage.py 生成的 patch-coverage.md（pain/maintenance-docs/，无 git）一致。
    ///
    /// 【Critical】缺失 = 本地多控不可用 → 计入致命清单 → INIT_FAILED + 抛异常。
    /// </summary>
    internal static readonly string[] CriticalPatchTargets =
    {
        // ---- 选牌串行化 / 本地选牌判定（本地多控的核心，缺一个就会双角色同时选牌）----
        "MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.SelectCards",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHand",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHandForDiscard",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHandForUpgrade",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromChooseACardScreen",
        // FromCombatPile 有两个重载（参数 4 / 5），本 mod 两个都打了补丁 → 分别钉死签名，
        // 任意一个没打上就是真的漏了（r91 实机日志实证两条都在）。
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromCombatPile/4",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromCombatPile/5",
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.ShouldSelectLocalCard",
        // ---- 回合流程 / 切前台 ----
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetupPlayerTurn",
        "MegaCrit.Sts2.Core.Combat.CombatManager.DoTurnEnd",
        "MegaCrit.Sts2.Core.Combat.CombatManager.FlushPlayerHand",
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetReadyToEndTurn",
        "MegaCrit.Sts2.Core.Combat.CombatManager.SetReadyToBeginEnemyTurn",
        // ---- 击杀结算 ----
        "MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill",
        // ---- 事件 ----
        "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.BeginEvent",
        "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.ChooseLocalOption",
        // ---- 奖励归属（错归属 = 奖励给错角色）----
        "MegaCrit.Sts2.Core.Rewards.RewardsSet.Offer",
        "MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferCustom",
        "MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferForRoomEnd",
        "MegaCrit.Sts2.Core.Rooms.CombatRoom.OfferRoomEndRewards",
        "MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer.SelectLocalReward",
        // ---- 药水 / 动作队列 / 手牌变换 NetId 钉住 ----
        // TryToProcure 有 1 / 3 参数两个重载，补丁钉的是 3 参数那个（PotionModel, Player, int）
        "MegaCrit.Sts2.Core.Commands.PotionCmd.TryToProcure/3",
        "MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet.CombatEnded",
        "MegaCrit.Sts2.Core.Commands.CardCmd.Transform",
    };

    /// <summary>
    /// 【Optional】缺失只 WARN、不阻断加载：第三方联动、纯 UI 表现、瓦库自动化、个人偏好记录器。
    /// </summary>
    internal static readonly string[] OptionalPatchTargets =
    {
        // 第三方遗物联动
        "MegaCrit.Sts2.Core.Models.Relics.WhisperingEarring.AfterAutoPrePlayPhaseEnteredLate",
        // 纯 UI：结束回合按钮重评
        "MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton.CallReleaseLogic",
        // 瓦库：事件附魔自动作答（FromDeckForEnchantment 有三个重载，补丁钉的是
        // (IReadOnlyList<CardModel>, EnchantmentModel, int, CardSelectorPrefs) 那个）
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromDeckForEnchantment/4",
        // 个人记录器：整局胜负归因
        "MegaCrit.Sts2.Core.Runs.RunManager.OnEnded",
        // 个人记录器：真人事件点选
        "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.OptionButtonClicked",
        // 个人记录器：真人卡牌奖励点选
        "MegaCrit.Sts2.Core.Rewards.CardReward.OnSelect",
        // 个人记录器：事件网格选 N 入卡组
        "MegaCrit.Sts2.Core.Models.EventModel.SelectCardsToAddToDeckFromGrid",
        // 个人记录器：商店购买记录
        "MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry.OnTryPurchaseWrapper",
        // 个人记录器：真人删牌统计
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromDeckForRemoval",
        // 改进-2 / Phase 1：运行清理时同步清空选择器归属者注册表（缺了只会导致条目跨局残留）
        "MegaCrit.Sts2.Core.Commands.CardSelectCmd.Reset",
        // 改进-2 / 方案 D 队列路径出牌加速：瓦库队列出牌强制跳过卡牌堆演出（缺了只是少一份提速）
        "MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper",
        // BUG-20（r155）：整局进度写入的本地玩家识别校正（缺了 → 进度不写入 + 结算页无按钮）
        "MegaCrit.Sts2.Core.Saves.Managers.ProgressSaveManager.UpdateWithRunData",
        // BUG-20（r155）：结算页徽章保存兜底（缺了 → 结算页无按钮卡死）
        "MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen.NGameOverScreen.SaveBadgesToProgress",
        // 每日挑战（r156）：本地多控每日局禁止上传排行榜分数
        "MegaCrit.Sts2.Core.Daily.DailyRunUtility.UploadScore",
        // 每日挑战（r156）：出征前强制校正席位/角色/sender
        "MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen.OnEmbarkPressed",
        // ④ 瓦库的爹（r226）：内容本地化必须在**主菜单卡牌库/遗物库**之前注入（缺了渲染抛 LocException）
        "MegaCrit.Sts2.Core.Localization.LocManager.Initialize",
        // ④ 瓦库的爹（r226）：卡池冻结那一刻校验自定内容入池（缺了锚点 `[瓦库的爹验证]` 不出现）
        "MegaCrit.Sts2.Core.Models.ModelDb.Preload",
    };

    /// <summary>
    /// 严格模式：致命失败时向游戏上报（抛异常 → 主菜单显示 MOD_ERROR.ASSEMBLY_LOAD）。
    /// 置 LMC_INIT_STRICT=0/off/false 可临时降级为「只打 INIT_FAILED 不抛」，用于救急排查。
    /// </summary>
    private static readonly bool StrictMode = ResolveStrictMode();

    public static void Init()
    {
        var fatalFailures = new List<string>();

        LocalMultiControlLogger.Info("INIT_BEGIN");
        LocalMultiControlLogger.Info($"BUILD_ID {BuildMarker}");
        LocalMultiControlLogger.Info($"BUILD_IDENTITY {DescribeBuildIdentity()}");
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");

        // 阶段 1：运行期兼容性（PE/CLR/Assembly/依赖）
        RunStage(fatalFailures, "RUNTIME_COMPAT_CHECK",
            () => RuntimeCompatibilityCheck.Run(fatalFailures));

        // 阶段 2：配置与记录器
        RunStage(fatalFailures, "SERVICE_INIT", () =>
        {
            SafeAction(fatalFailures, FatalCode.Config, "配置重载", () => LocalWakuuAutopilotConfig.Reload("entry-init"));
            SafeAction(fatalFailures, FatalCode.Config, "个人记录器重载", () => LocalPersonalRecorder.Reload("entry-init"));
        });

        // 阶段 3：模型注册（遗物入池 / 本地化 / 可选第三方探测）
        RunStage(fatalFailures, "MODEL_REGISTRATION", () =>
        {
            RegisterWakuuRelicsToPool();
            RegisterWakuuDaddyContentToPool();
            SafeAction(fatalFailures, FatalCode.Model, "瓦库遗物本地化", () => LocalWakuuRelicLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "瓦库的爹内容本地化", () => LocalWakuuDaddyLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "瓦库休息区选项本地化", () => LocalWakuuRestSiteLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "瓦库联合选牌提示本地化", () => LocalWakuuUniteLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "瓦库炼化本地化", () => LocalWakuuRefineLocalization.Initialize());
            // 社区统计（SkadaHelper）为可选第三方依赖：探测失败只打日志，永不阻断
            WakuuSkadaAdapter.Probe();
            // 联机 AI 队友（Co-op Bots）同为可选第三方依赖：只探测 + 登记配置里的席位，绝不接管（进局时才接管）
            CoopBotsAdapter.Probe();
            // 第三方交互守卫（LexNinja2 / 蕾忍）：炼化把席位"死在战斗外"后，它的 LexKelaSingleton 会在
            // 侧回合结束时 KeyNotFound 打断回合循环 ⇒ 补键守卫。晚加载属常态，进局与战斗就绪时会重试。
            LexNinja2KelaTurnEndGuardPatch.TryApplyLate();
            SafeAction(fatalFailures, FatalCode.Config, "联机机器人席位配置", () => CoopBotsSeatRuntime.LoadFromConfig("entry-init"));
        });

        // 阶段 4：应用 Harmony 补丁
        RunStage(fatalFailures, "PATCH_APPLY", () =>
        {
            _harmony = new Harmony("sts2.dualroleadventure");
            try
            {
                if (PatchDomainMap.UseGroupedPatchAll)
                {
                    ApplyAllPatchGroups();
                }
                else
                {
                    // 回滚预案（实施方案 2.3）：整体关闭分组容错，回到旧 PatchAll 直跑。
                    _harmony.PatchAll();
                }
            }
            catch (Exception patchException)
            {
                // r38 防御 + 2.3 分组：Core 组失败即停时会走到这里（后续补丁组未应用）。
                // 已应用的补丁保留；缺失会在下方启动自检中报出。
                LocalMultiControlLogger.Error($"Harmony 补丁初始化中断（请结合启动自检缺失清单定位具体补丁）: {patchException}");
                fatalFailures.Add($"[{FatalCode.PatchApply}] Harmony 补丁应用中断: {patchException.GetType().Name}");
            }
        });

        // 阶段 5：补丁自检（Critical 缺失 → 致命）
        RunStage(fatalFailures, "PATCH_SELF_TEST", () => ValidatePatches(fatalFailures));

        // 阶段 6：选牌入口归属路由自检（改进-2 / Phase 1；只记录/告警，不致命）
        RunStage(fatalFailures, "SELECTOR_ROUTE_AUDIT", WakuuSelectorRouteAudit.Run);

        // 终态：INIT_OK / INIT_FAILED 二选一，互斥
        FinishInitialization(fatalFailures);
    }

    /// <summary>
    /// 启动自检：期望补丁清单 vs 实际已打补丁。
    /// Critical 缺失 → 致命（PATCH002）；Optional 缺失 → WARN。
    /// </summary>
    private static void ValidatePatches(ICollection<string> fatalFailures)
    {
        try
        {
            List<MethodBase> patchedList = _harmony!.GetPatchedMethods().ToList();
            LocalMultiControlLogger.Info($"Harmony 补丁统计: 已打补丁方法数={patchedList.Count}");

            // 同时构造简单名键与完整名键（含参数个数），清单可写 Type.Method 或 Namespace.Type.Method 或 Type.Method/argc
            var patchedKeys = new HashSet<string>(StringComparer.Ordinal);
            var signatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (MethodBase method in patchedList)
            {
                if (method.DeclaringType == null)
                {
                    continue;
                }

                string simple = $"{method.DeclaringType.Name}.{method.Name}";
                string full = $"{method.DeclaringType.FullName}.{method.Name}";
                int argCount = method.GetParameters().Length;
                patchedKeys.Add(simple);
                patchedKeys.Add(full);
                patchedKeys.Add($"{simple}/{argCount}");
                patchedKeys.Add($"{full}/{argCount}");

                // 同名方法被多个重载/同名类型命中 → 提示升级到签名级写法
                string signature = $"{full}/{argCount}";
                signatureCounts[signature] = signatureCounts.TryGetValue(signature, out int count) ? count + 1 : 1;

                if (method.Name.Contains("SelectCards") || method.Name.Contains("FromHand")
                    || method.Name.Contains("FromSimpleGrid") || method.Name.Contains("FromChooseACard")
                    || method.Name.Contains("FromCombatPile") || method.Name.Contains("ShouldSelectLocalCard"))
                {
                    LocalMultiControlLogger.Info($"  已打补丁: {method.DeclaringType.FullName}.{method.Name}/{argCount}");
                }
            }

            foreach (KeyValuePair<string, int> entry in signatureCounts.Where(pair => pair.Value > 1))
            {
                LocalMultiControlLogger.Warn($"启动自检: 同一签名被多次打补丁（第三方 mod 也可能是这里）: {entry.Key} x{entry.Value}");
            }

            List<string> missingCritical = CriticalPatchTargets.Where(target => !patchedKeys.Contains(target)).ToList();
            List<string> missingOptional = OptionalPatchTargets.Where(target => !patchedKeys.Contains(target)).ToList();

            foreach (string target in missingCritical)
            {
                LocalMultiControlLogger.Error($"[{FatalCode.PatchCritical}] Critical 补丁缺失(可能被 PatchAll 静默跳过): {target}");
                fatalFailures.Add($"[{FatalCode.PatchCritical}] Critical 补丁缺失: {target}");
            }

            foreach (string target in missingOptional)
            {
                LocalMultiControlLogger.Warn($"启动自检: Optional 补丁缺失（不影响可用性）: {target}");
            }

            int criticalOk = CriticalPatchTargets.Length - missingCritical.Count;
            int optionalOk = OptionalPatchTargets.Length - missingOptional.Count;
            LocalMultiControlLogger.Info(
                $"PATCH_RESULT critical={criticalOk}/{CriticalPatchTargets.Length} " +
                $"optional={optionalOk}/{OptionalPatchTargets.Length} total_patched={patchedList.Count}");

            if (missingCritical.Count == 0)
            {
                LocalMultiControlLogger.Info($"启动自检: {CriticalPatchTargets.Length} 个 Critical 补丁全部生效。");
            }
            else
            {
                LocalMultiControlLogger.Error(
                    $"启动自检: {missingCritical.Count}/{CriticalPatchTargets.Length} 个 Critical 补丁缺失，" +
                    "请用 Scripts/Tools/patch_coverage.py 重新生成覆盖清单核对。");
            }

            LogKeyPatchOwners(patchedList);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"启动自检执行失败: {exception}");
            fatalFailures.Add($"[{FatalCode.PatchSelfTest}] 补丁自检执行失败: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Harmony owner 审计（backlog：第三方 Patch 冲突检查）。
    ///
    /// 对每个「命中 Critical/Optional 清单的关键方法」打印该方法上的全部补丁 owner 与种类，
    /// 一眼区分「我们的补丁没执行」与「第三方补丁也打在这个方法上」（Koishi 等 mod 冲突场景）。
    /// 仅输出到日志，不影响初始化成败。
    /// </summary>
    private static void LogKeyPatchOwners(List<MethodBase> patchedList)
    {
        try
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (string target in CriticalPatchTargets.Concat(OptionalPatchTargets))
            {
                wanted.Add(target);
                // 签名级写法（.../参数个数）同时登记去掉后缀的写法，owner 审计按「方法」匹配
                int slash = target.LastIndexOf('/');
                if (slash > 0)
                {
                    wanted.Add(target.Substring(0, slash));
                }
            }

            int logged = 0;
            foreach (MethodBase method in patchedList)
            {
                if (method.DeclaringType == null)
                {
                    continue;
                }

                string simple = $"{method.DeclaringType.Name}.{method.Name}";
                string full = $"{method.DeclaringType.FullName}.{method.Name}";
                if (!wanted.Contains(simple) && !wanted.Contains(full))
                {
                    continue;
                }

                HarmonyLib.Patches? info = Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    continue;
                }

                var perOwner = new Dictionary<string, (int Prefix, int Postfix, int Transpiler, int Finalizer)>(StringComparer.Ordinal);
                void Count(IEnumerable<HarmonyLib.Patch> patches, string kind)
                {
                    foreach (HarmonyLib.Patch patch in patches)
                    {
                        string owner = string.IsNullOrEmpty(patch.owner) ? "(unknown)" : patch.owner;
                        if (!perOwner.TryGetValue(owner, out (int Prefix, int Postfix, int Transpiler, int Finalizer) tuple))
                        {
                            tuple = (0, 0, 0, 0);
                        }

                        switch (kind)
                        {
                            case "Prefix": tuple.Prefix++; break;
                            case "Postfix": tuple.Postfix++; break;
                            case "Transpiler": tuple.Transpiler++; break;
                            case "Finalizer": tuple.Finalizer++; break;
                        }
                        perOwner[owner] = tuple;
                    }
                }

                Count(info.Prefixes, "Prefix");
                Count(info.Postfixes, "Postfix");
                Count(info.Transpilers, "Transpiler");
                Count(info.Finalizers, "Finalizer");

                foreach (KeyValuePair<string, (int Prefix, int Postfix, int Transpiler, int Finalizer)> owner in perOwner)
                {
                    string counts =
                        $"P{owner.Value.Prefix}Po{owner.Value.Postfix}T{owner.Value.Transpiler}F{owner.Value.Finalizer}";
                    bool isSelf = owner.Key == "sts2.dualroleadventure";
                    if (isSelf)
                    {
                        LocalMultiControlLogger.Info($"  关键目标 {simple} — {owner.Key} [{counts}]");
                    }
                    else
                    {
                        LocalMultiControlLogger.Warn(
                            $"关键目标 {simple} 存在第三方补丁 owner「{owner.Key}」[{counts}]：若行为异常，" +
                            "先确认是不是它的补丁与我们冲突（Harmony 按优先级执行）。");
                    }
                }

                logged++;
            }

            if (logged > 0)
            {
                LocalMultiControlLogger.Info($"Harmony owner 审计完成: 关键目标 {logged} 个均已记录补丁归属。");
            }
        }
        catch (Exception exception)
        {
            // 审计是辅助诊断，绝不允许它反过来拖垮初始化
            LocalMultiControlLogger.Warn($"Harmony owner 审计失败（不影响加载）: {exception.Message}");
        }
    }

    /// <summary>
    /// 初始化终态收口：全绿 → INIT_OK；任一致命项 → INIT_FAILED，并在严格模式下抛异常上报游戏。
    /// 严格模式下抛出的异常会被 ModManager.CallModInitializer 捕获并写入 MOD_ERROR.ASSEMBLY_LOAD，
    /// 这是游戏侧唯一能表达「该 mod 加载失败」的机制（主菜单可见）。
    /// </summary>
    private static void FinishInitialization(List<string> fatalFailures)
    {
        if (fatalFailures.Count == 0)
        {
            LocalMultiControlLogger.Info("INIT_OK");
            LocalMultiControlLogger.Info("Mod 初始化完成（状态=OK）。");
            return;
        }

        LocalMultiControlLogger.Error($"INIT_FAILED fatal={fatalFailures.Count}");
        foreach (string failure in fatalFailures)
        {
            LocalMultiControlLogger.Error(failure);
        }

        LocalMultiControlLogger.Error(
            $"Mod 初始化失败（状态=FAILED）：{fatalFailures.Count} 项致命问题，本 mod 未处于可用状态。");

        if (!StrictMode)
        {
            LocalMultiControlLogger.Warn(
                "严格模式已关闭（LMC_INIT_STRICT=0）：仅记录 INIT_FAILED，未向游戏上报，mod 可能处于半损坏状态。");
            return;
        }

        throw new InvalidOperationException(
            $"LocalMultiControl 初始化失败（{fatalFailures.Count} 项致命问题）: {string.Join(" | ", fatalFailures)}");
    }

    private static void RunStage(ICollection<string> fatalFailures, string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"初始化阶段[{stage}] 抛出未处理异常: {exception}");
            fatalFailures.Add($"[{FatalCode.Stage}] 阶段 {stage} 未处理异常: {exception.GetType().Name}");
        }
    }

    private static void SafeAction(ICollection<string> fatalFailures, string code, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"[{code}] {what}失败: {exception}");
            fatalFailures.Add($"[{code}] {what}失败: {exception.GetType().Name}");
        }
    }

    private static bool ResolveStrictMode()
    {
        string? raw = Environment.GetEnvironmentVariable("LMC_INIT_STRICT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return !raw.Equals("0", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("false", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("off", StringComparison.OrdinalIgnoreCase)
               && !raw.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 结构化构建身份（BuildIdentity）：构建时由 csproj 生成 BuildIdentity.g.cs 编译进来。
    /// 与 BuildMarker（人工维护、可读）互为补充——这个回答「从哪个 commit、何时构建、工作区是否干净」。
    /// </summary>
    private static string DescribeBuildIdentity()
    {
        return $"commit={BuildIdentity.GitCommit} state={BuildIdentity.GitDirty} built={BuildIdentity.BuildTimeUtc}";
    }

    /// <summary>
    /// 分组应用全部 Harmony 补丁（维护性改进 2.3：PatchAll 分组隔离）。
    ///
    /// 按 <see cref="PatchDomainMap"/> 将补丁类归入 7 个域并逐组 try-catch：
    ///   - Core（本地多控运行基座）失败即停：异常上抛由 Init 兜底记录，不再应用后续补丁组；
    ///   - 其余组失败打 Error 并跳过，继续下一组；
    ///   - 未登记分组的补丁类打 Warn 并按「隔离组」兜底应用（应补登记到 PatchDomainMap）。
    /// </summary>
    private static void ApplyAllPatchGroups()
    {
        Assembly assembly = typeof(Entry).Assembly;
        // 与 Harmony PatchAll 的收集口径一致：不排除 abstract（静态补丁类编译为 abstract+sealed）。
        List<Type> allPatchTypes = assembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<HarmonyPatch>() != null)
            .ToList();

        var grouped = new Dictionary<PatchDomain, List<Type>>();
        var unregistered = new List<Type>();
        foreach (Type patchType in allPatchTypes)
        {
            PatchDomain? domain = PatchDomainMap.ResolveFor(patchType);
            if (domain == null)
            {
                unregistered.Add(patchType);
                continue;
            }

            if (!grouped.TryGetValue(domain.Value, out List<Type>? domainTypes))
            {
                domainTypes = new List<Type>();
                grouped[domain.Value] = domainTypes;
            }

            domainTypes.Add(patchType);
        }

        foreach (PatchDomain domain in PatchDomainMap.ApplyOrder)
        {
            if (grouped.TryGetValue(domain, out List<Type>? domainTypes))
            {
                ApplyPatchGroup(domain, domainTypes, failFast: domain == PatchDomain.Core);
            }
        }

        if (unregistered.Count > 0)
        {
            foreach (Type unregisteredType in unregistered)
            {
                LocalMultiControlLogger.Warn(
                    $"补丁类未登记分组（已按隔离组兜底应用，请登记到 PatchDomainMap）: {unregisteredType.FullName}");
            }

            ApplyPatchGroup(domain: null, unregistered, failFast: false);
        }
    }

    /// <summary>
    /// 应用单个补丁组。组内任一补丁失败时：
    ///   failFast=true（Core）→ 打 Error 并上抛，后续补丁组不再应用（错误基线上继续更危险）；
    ///   failFast=false → 打 Error 并跳过本组，继续其余组。
    /// </summary>
    private static void ApplyPatchGroup(PatchDomain? domain, IReadOnlyList<Type> patchTypes, bool failFast)
    {
        if (patchTypes.Count == 0)
        {
            return;
        }

        string groupName = domain?.ToString() ?? "Unregistered";
        try
        {
            foreach (Type patchType in patchTypes)
            {
                _harmony!.CreateClassProcessor(patchType).Patch();
            }

            LocalMultiControlLogger.Info($"补丁组[{groupName}] 应用完成：{patchTypes.Count} 类");
        }
        catch (Exception exception) when (failFast)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败（本组失败即停，后续补丁组不再应用）: {exception}");
            throw;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败，已跳过该组（其余组继续）: {exception}");
        }
    }

    /// <summary>
    /// 把瓦库托管遗物注册进事件遗物池（与原版低语耳环同池）。
    /// 不入池的遗物在 RelicModel.Pool 里会因 First() 找不到匹配而抛异常，
    /// 导致悬停/点开遗物描述时 UI 中断（表现为"未解锁"且无法退出描述页）。
    /// 事件遗物池没有任何随机奖励入口引用，注册后不会被随机抽到。
    /// </summary>
    private static void RegisterWakuuRelicsToPool()
    {
        try
        {
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuStarterRelic>();
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuFormRelic>();
            LocalMultiControlLogger.Info("已注册瓦库托管遗物到事件遗物池。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注册瓦库遗物到遗物池失败（遗物描述页可能异常）: {exception.Message}");
        }
    }

    /// <summary>
    /// ④「瓦库的爹」：三张占位牌注册进**事件卡池**、遗物注册进**事件遗物池**
    /// （与原版事件牌 / 瓦库托管遗物同池）。
    ///
    /// **必须在游戏初始化前**登记 —— 调用点 = mod 初始器（<see cref="Init"/> 阶段 3），
    /// 此时 <c>ModelDb.Init()</c> / <c>ModelDb.Preload()</c> 都还没跑；
    /// 池一旦被首次访问（Preload 会读 <c>ModelDb.AllCards</c>）就**冻结**，
    /// 之后再 <c>AddModelToPool</c> 会抛 <c>InvalidOperationException</c>（"it's too late!"）。
    ///
    /// 类型本身无需手动注册：<c>ModelDb.Init()</c> 经
    /// <c>ReflectionHelper.GetSubtypesInMods&lt;AbstractModel&gt;</c> 自动扫到本 mod 程序集里的模型。
    /// 是否真的进了池由 <see cref="WakuuDaddyContentProbe"/> 在**卡池冻结那一刻**报出来（这里读不了池）。
    ///
    /// ⚠ **卡池只能是 <see cref="EventCardPool"/>，绝不要改成 <c>ColorlessCardPool</c>** ——
    /// 后者是一般方式可得的（商店 / 无色药水 / 工具箱等遗物 / 多个事件都直接引用它）。
    /// 完整口径与证据见 <see cref="LocalWakuuDaddyShieldCard"/> 的类注释。
    /// </summary>
    private static void RegisterWakuuDaddyContentToPool()
    {
        try
        {
            ModHelper.AddModelToPool<EventCardPool, LocalWakuuDaddyShieldCard>();
            ModHelper.AddModelToPool<EventCardPool, LocalWakuuDaddyFocusCard>();
            ModHelper.AddModelToPool<EventCardPool, LocalWakuuDaddyMergeCard>();
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuDaddyRelic>();
            LocalMultiControlLogger.Info("已登记瓦库的爹内容到事件卡池/事件遗物池（实际入池校验见 [瓦库的爹验证]）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"登记瓦库的爹内容到卡池失败（卡牌库/遗物库/给牌可能异常）: {exception.Message}");
        }
    }
}
