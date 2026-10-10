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
    // r228（2026-10-07）：**三张牌的效果实装**（r227 只做了框架）。
    //  ① 【我挡】= 给队友挂 `LocalWakuuDaddyShieldPower`：`ModifyUnblockedDamageTarget` 把攻击伤害转给施牌者
    //     （照原版 `DieForYouPower`，那是原版唯一的伤害重定向）+ `ModifyDamageMultiplicative` 返回 0.5
    //     （照 `GuardedPower`）⇒ "转移 + 只担一半"；敌方回合结束自删（照 `CoveredPower`）。
    //     该状态**故意不可见 + 关 VFX**：mod 没有 PCK ⇒ 图标固定取 power_atlas 里不存在的资源，
    //     可见状态下 `NPower.Reload` 会直接 ResourceLoader.Load 它 ⇒ 每次施加都留一条引擎 ERROR（见类注释）。
    //  ② 【你攻】= `WakuuDaddyCombatState.SetFocus` 登记集火目标，两个瓦库大脑解析 `AnyEnemy` 时优先取它
    //     （原版侧"敌人/玩家选目标"没有任何钩子可用，而瓦库的目标选择本来就在我们手里）。
    //  ③ 【合体】= 瓦库手牌改归属进施牌者手牌（`CardPileCmd.GiveToAnotherPlayer`）+ 能量搬运
    //     （`PlayerCmd.LoseEnergy`/`GainEnergy`）+ 本回合混抽/混弃（`WakuuDaddyMergePilePatch`：
    //     Draw 前缀把随机来源的牌搬进自己抽牌堆顶部后放行原版流程；DiscardAndDraw 前缀只改归属，
    //     让原版自己按 owner 落堆）。
    //  ④ 三张牌都补「抽 1 张牌」；「本回合」登记统一在**玩家侧回合结束**清空（`LocalWakuuDaddyRelic`），
    //     战斗结束再兜一次。
    // r229（2026-10-07，r228 首测后的两处修复）：
    //  ① 【我挡】"转过来的伤害不能格挡" ⇒ 重定向发生在被挡者格挡结算**之后**，承伤者天然是直接掉血；
    //     在 `ModifyHpLostAfterOsty`（重定向之后那条钩子）里用原版同一个 `Creature.DamageBlockInternal`
    //     补一次承伤者自己的格挡（`_redirectPending` 一次性标记确保只对该笔重定向生效，不会把直接挨打的
    //     那笔算两遍 —— `CreatureCmd.Damage:290→291` 之间没有 await，可安全用实例字段）。
    //     ⚠ 已知副作用：被挡者自己的格挡仍会先吃掉一部分（无法从钩子里绕过）。
    //  ② 【合体】"牌进手牌不显示、切人才出来" ⇒ `CardPileCmd.GiveToAnotherPlayer` 依赖"桌上已有的卡节点"，
    //     而瓦库的手牌节点不在共享手牌 UI 里 ⇒ 取不到节点就整个 return，什么都不画。改成
    //     `CardModel.GiveToAnotherPlayer`（改归属）+ `CardPileCmd.Add(牌, Hand)`（正常流程会建节点并挂进手牌）。
    // r230（2026-10-07，r229 首测后的关键修复）：
    //  🔴 **`CardModel.Pile` 是按 Owner 反查的**（`Pile => _owner?.Piles.FirstOrDefault(p => p.Cards.Contains(this))`）
    //     ⇒ 「跨玩家搬牌」的顺序是**铁律**：**先 `RemoveFromCurrentPile`（此时 Owner 还是原主、Pile 正确）→ 再
    //     `GiveToAnotherPlayer` 改归属 → 再 `CardPileCmd.Add`**。r229 我为了修"牌不显示"把顺序写成"先改归属再 Add"，
    //     结果改完归属 `Pile` 立刻变 null、`Add` 以为"牌不在任何牌堆里"而**跳过摘除** ⇒ 同一张牌同时挂在
    //     原主手牌 + 新主手牌上（一个实例两个牌堆）⇒ 瓦库把这张"自己的牌"每回合重打 60 次（撞护栏上限）、
    //     卡牌节点暴涨（实机泄漏 6.1 万 CanvasItem）= 严重卡顿（用户口径「超级卡」）。已按铁律修好。
    //  ① `LocalWakuuDaddyMergeCard` 手牌收编：先摘 → 改归属 → `CardPileCmd.Add(牌, Hand)`（顺序铁律 + 正常视觉路径）。
    //  ② 「合体混弃」：不要再"弃牌前改归属让原版按新 owner 落堆"（同一个坑）⇒ 改成 `DiscardAndDraw` **后置**：
    //     原版正常弃完，再把牌搬去掷硬币选中的那一方弃牌堆（先摘 → 改归属 → AddInternal，全同步内部方法）。
    //  ③ 兜底：瓦库自动出牌的手牌读数过滤掉"不属于它的牌"（`ResolveAutoplayHand`），命中打一条限流 WARN
    //     —— 即使将来再出现"牌没搬干净"，最坏也只是它不碰那张牌，而不会变成无限重打。
    // r231（2026-10-07，r230 实机日志判读后的小补）：
    //  ① **退局复位枢纽补登**：`WakuuDaddyCombatState.ResetForRun("run-cleanup")`（挂进 `OnRunCleanup`，与
    //     我们联合同款）—— 本登记处是进程级静态，而"战斗中途退出"既不走玩家侧回合结束、也不走战斗结束
    //     ⇒ 残留链接会在**下一局**同 NetId 的席位对上复活（判读 r230 那局日志时发现：那局中途退出，
    //     全程没有 `本回合登记已清空`）。
    //  ② `合体混抽` / `合体混弃` 两条日志补 `round=` —— r230 判读时正是"没写回合号"导致要额外反推
    //     那两次混抽属于哪一回合（是不是跨回合残留）。纯日志，无行为变化。
    // r240（2026-10-09）**战灵召唤 Phase 1**（原 ④「地狱战神」，design doc #21 §8）—— 只做"计数"这条线，
    // 战斗内召唤留到 Phase 2：
    //  ① 新遗物【地狱耳环】`Scripts/Models/Relics/LocalWakuuHellwarEarring.cs`：计数用原版原生三件套
    //     （`ShowCounter` + `DisplayAmount` + `[SavedProperty] int SpiritCount` ⇒ 存档往返与格子数字都白得）；
    //     兼 D5 档位落点（覆写 `ShouldDisableRemainingRestSiteOptions`，照原版 `MiniatureTent`）。
    //  ② 休息处新选项【分裂精神】`HellwarSplitSpiritOption`（注入点在 `RestSitePatch`，只给真人席位）：
    //     **先扣当前血量的档位百分比（默认 15%，向下取整，clamp ≥1 不扣死）→ 再给耳环 +1 计数**；
    //     扣血走 `CreatureCmd.SetCurrentHp`（非战斗环境安全，避开 `SetMaxHp(0)` 的 Log.Error 老坑）。
    //  ③ 纯逻辑 `HellwarSummonPolicy`（强度→成本 D7 / 可召唤判据 R6 / 扣血档位 D13）+ 单测；
    //     配置三项 `hellwarGod` / `hellwarSplitHpRatio`(10/15/20) / `hellwarSplitFree`（设置页三行）。
    //  ④ 内容挂点 `HellwarContentPatch`（LocManager 就绪后注入本地化 + ModelDb.Preload 后打"已就绪"锚点），
    //     与「瓦库的爹」同款；本轮**不新增目标方法**但新增补丁类 ⇒ 已登记 `PatchDomainMap` 与 targets 基线。
    // r241（2026-10-09，Phase 1 实机关单后的用户追加）：
    //  ① 🔴 **休息处选项横向滚动条**（用户：「选项太多了，导致有时候根本放不下」）——
    //     原版 `%ChoicesContainer` 是写死 799px 宽的居中 HBoxContainer，按钮 247px + 间隔 100px
    //     ⇒ **5 个选项就开始溢出**（实机 7 个时两端按钮被挤出屏幕、点不到）。做法：**只在真的溢出时**
    //     把它包进横向 `ScrollContainer`（`ScrollMode.Auto` ⇒ 不溢出时与原版逐像素一致；溢出时给可拖动 / 可滚轮
    //     的滚动条 + `FollowFocus` 让手柄也够得到）。两条硬要求（见 RestSiteOptionScrollRuntime 类注释）：
    //     **必须给 HBox 设 `CustomMinimumSize.X`**（否则超出的按钮会被 ScrollContainer 裁掉、永远够不到）、
    //     **包装层要向下加高**给滚动条留一行（否则按钮底部被切）。开关 `restSiteOptionScroll`（默认开）——
    //     用户口径「可能与其它 mod 功能重合，需要加个开关」。
    //  ② 「每进入新的一幕自动获得计数」（用户担心攒不出大招的量）：开关 `hellwarActGain`（默认关）+
    //     数量档位 `hellwarActGainAmount`（1 / **2（默认）** / 3）。落点 = **遗物自己的原版钩子 `AfterActEntered`**
    //     （零新增 Harmony 目标）+ 发放点补一次；**幂等靠存档字段 `LastRewardedActIndex`**（进局与进第 1 幕会
    //     两次经过同一条路、读档还会重进当前幕 ⇒ 没这条水位就会重复发）。
    //  ③ 【分裂精神】扣血档位**加「不扣血（0%）」**（用户追加），四档循环 0% → 10% → 15%（默认）→ 20%。
    //  ④ 【地狱耳环】风味文本按用户给的候选换掉（取"达摩经…战纹…地狱开始的时候"那段：直接点题且像遗物风味）。
    // r242（2026-10-09，r241 首测的三条症状全部由**同一个根因**造成）：
    //  用户：「滚动条起始处有问题，移到最左边什么选项都没有，移到 1/3 的位置才出现第一个选项，并且看不全选项文字，点不了选项」。
    //  🔴 **根因**：日志 `需要宽=7183, 可用宽=799, **buttons=21**` —— 游戏 `UpdateRestSiteOptions` 换选项时用
    //     `QueueFree` 销毁旧按钮，**那一帧旧按钮还在子节点列表里** ⇒ 我按 `GetChildCount()` 算宽度把 3 代 ×7 个
    //     全算了 ⇒ HBox 被撑到 7183 宽，而它 `alignment = Center` ⇒ **左侧多出约 2427px 空白**
    //     （"滚到最左什么都没有、1/3 处才见到第一个选项"），可见内容被夹在 `clip_contents` 边缘 ⇒ **文字看不全、
    //     且裁剪区外的部分不收鼠标 ⇒ 点不到**。
    //  ✅ 修法三条：① `ResolveRequiredWidth` **只数活着的选项按钮**（`IsQueuedForDeletion()` 过滤 + 只认 `NRestSiteButton`）；
    //     ② **自愈**：挂 `ChildEnteredTree` / `ChildExitingTree`（旧按钮**真的**退出树才触发）⇒ 下一帧重算内容宽，
    //     游戏自己调 `UpdateRestSiteOptions`（选完选项后的重排）也能跟上；
    //     ③ 溢出判据从"超过原版容器 799px"改成"**超过屏幕可用宽**（视口宽 − 2×24）"——
    //     原版 3~5 个选项本来就摆得下（3 个 = 941、5 个 = 1635，都在 1920 屏幕内）⇒ 不再无谓改变原版观感；
    //     真要滚时窗口宽取 `min(内容宽+12, 屏幕可用)`（能一屏看多少看多少）。
    // r243（2026-10-09，r242 复测后剩下的两条症状：**点不了** + **文字不完整**）——
    //  日志佐证：r242 局 `需要宽=2328, buttons=7`（算术已对）但**「休息区选择完成」0 条**（一个选项都没点成）。
    //  ① 🔴 **点不了 = Reparent 打断了按钮的 `AnimateIn`**：`rest_site_button.tscn` 里按钮是
    //     `mouse_filter = 2 (Ignore)`，唯一把它翻成 `Stop` 的是 `NRestSiteButton.AnimateIn()` 里
    //     `await tween.AwaitFinished(_cts.Token)` **之后**那行；而 `Reparent` 必然走一次 exit/enter tree
    //     ⇒ 按钮 `_ExitTree()` 先 `_cts.Cancel()` ⇒ 那行不执行 ⇒ 收不到 `MouseEntered` ⇒
    //     `NClickableControl` 的按下/松开都要求 `IsFocused`（由 hover 驱动）⇒ **整个按钮点不动**
    //     （r241 只有"没被包层的那个休息处"能点，正是这个原因）。修法 = 包完自己补 `MouseFilter = Stop`
    //     （`RepairButtonClickability`，并把"修了几个"打进日志）。
    //  ② **文字不完整 = 选项名字挂在按钮下方**：`%Label` 是 `offset_top = offset_bottom = 165`（在按钮外的
    //     下方、靠"无人裁剪"才看得见），而包装层 `clip_contents = true` 把下半截裁掉、滚动条还正好压在文字上
    //     （挡字又挡点击）。修法 = 包装层向下**先留名字带（按主题字号+描边算）再留滚动条那一行**
    //     ⇒ 滚动条落在文字下面（`ResolveLabelBand`）。
    // r244（2026-10-09）**战灵召唤 Phase 2 —— 召唤闭环**（R3/R4/R7，design doc #21 §8）：
    //  ① 把 Phase 0 POC 的生成 / 接入 / 清理链抽成 `HellwarWarGodFactory`（POC 与正式召唤共用同一实现，
    //     免得两条链各写一份、日后只修一处）：`Player.CreateForNewRun`（= D7「普通档」：召唤者角色自身的
    //     初始遗物 + 初始卡组，运行期读 ⇒ 自动兼容 mod 角色）→ `AttachToRun`（RunState/种子/**卡组 Owner**/
    //     run 卡牌表）→ `state.AddPlayer` → `ResetCombatState` + `PopulateCombatState`（**战斗态**）→
    //     `CombatManager.AddCreature` + `NCombatRoom.AddCreature`（**立绘/血条**）→ `AfterCreatureAdded` 收尾；
    //     席位号从 `0x1000` 起取**第一个空闲**（Phase 2b 一次召唤多个会有空洞，不能再拿"当前个数"当索引）。
    //  ② 新 `HellwarSummonCombatRuntime`：真人席位**回合开始**弹召唤选择（复用 `LocalWakuuChoiceOverlay`，
    //     并压制瓦库自动作答 + 钉住归属者两道）⇒ **消耗计数** ⇒ 生成完整战神。挂点仍在既有补丁
    //     `CombatManagerSetupPlayerTurnForegroundPatch`/`CombatRoomOfferRoomEndRewardsPatch` ⇒ **不新增 Harmony 目标**。
    //  ③ **R3 口径 A（计数是"每场战斗的预算"）落地**：遗物【地狱耳环】的 `SpiritCount` = 累计**上限**（存档），
    //     新增**不存档**的 `CombatSpentSpirit`（本场已花）；遗物格子显示 / 判据一律用 `AvailableSpirit = 上限 − 已花`；
    //     召唤**先扣后生成、生成失败退回**；战后（发奖之前）把战神摘出战斗 + 已花清零 = **自动补满**（日志 `计数已自动补满`）。
    //  ④ 判据全部走纯逻辑 `HellwarSummonPolicy`（`ResolveAvailableSpirit`/`CanSpendSpirit`/`ShouldOfferSummon`）+ 单测
    //     （把"每回合最多 1 个（D6）""满 12 不弹（R6）""买不起就不弹空弹层"钉住）。
    //  ⚠ 本阶段战神仍**不出牌**（托管出牌是 Phase 3），它的回合由 `TryRequestWarGodTurnEnd` 自动收口（同 POC）。
    // r245（2026-10-09，r244 实机首测翻车）**"召唤战神后不能结束回合"**：
    //  🔴 根因 = 「结束回合」的完成判据是 `PlayersReadyToEndTurn.Count == State.Players.Count`
    //     （`CombatManager.cs:1063`；本仓不是 `NetGameType.Singleplayer`，拿不到 `:1073` 的 SP 旁路），
    //     而这个集合是 `StartTurn` 在**回合开始那一帧**把"不在 `playersStartingTurn` 里"的玩家逐个预填的
    //     （`CombatManager.cs:802-813`）；Phase 2 的战神要**等真人点完召唤选择**才加进 `State.Players`
    //     ⇒ 早过了那一圈 ⇒ 战神永远不进集合 ⇒ 分母永远多一个 ⇒ 卡死。
    //     （Phase 0 POC 是"回合开始 +1 帧"就生成，落在那圈**之前**，所以从没踩到。）
    //  ✅ 修法 = 加完人**立刻** `SetReadyToEndTurn(warGod, canBackOut:false)`（幂等），
    //     收在 `HellwarWarGodFactory.MarkReadyToEndTurnIfMidPlayerTurn`，日志锚点 `③b 战神已补登「本回合已结束回合」`。
    //  ⚠ 这是**第二道**就绪门：gate ① `PlayersReadyToEndTurn`（结束回合）/ gate ②
    //     `PlayersReadyToBeginEnemyTurn`（进入敌方回合，r238 踩过、由 `CombatManagerReadyEnemyTurnPatch` 认临时席位）——
    //     **两道都要认临时席位**。通用教训 → references 坑 **AE-3**。
    // r246（2026-10-09，r245 实机判读：用户「其他没问题，但是**我召唤的第二个战神不会被打**」）—— 两个根因：
    //  ① 🔴 **两个战神拿到同一个 NetId**（日志铁证 `…自动补齐敌方回合就绪: mirrored=…,4096,4096`）：
    //     `NextSeatId` 拿 `LocalSelfCoopContext.IsTemporarySeat` 当"这个号被占了吗"用 ——
    //     而它是**范围谓词**（整个 `0x1000..0x1FFF` 都返回 true）⇒ 逐个试号全判成"已占用" ⇒ 循环走到底
    //     兜底返回段起点 ⇒ **永远 0x1000**。修法 = 纯逻辑 `HellwarPocPolicy.ResolveFreeSeatId(确切占用集合)`
    //     （占用集合 = 已登记临时席位 ∪ 在册战神 ∪ 战斗内现有玩家）+ 单测钉住"三个战神必须三号"。
    //  ② 🔴 **战神「不会被打」的真因**：`CreatureCmd.Damage` 的**受击结算**会
    //     `CurrentMapPointHistoryEntry.GetEntry(受击者.NetId)` 记 `DamageTaken`，而这张表是按
    //     `runState.Players` 建的（战神不在其中）⇒ `GetEntry` **抛** `InvalidOperationException
    //     ("Player with ID 4096 not found in player stats for this run history!")`
    //     ⇒ 那次攻击整体失败（第三方兜一句 `怪物招式 STRONG_PUNCH_MOVE 执行失败（已兜住）`）
    //     ⇒ 敌方根本打不到战神。修法 = 照抄 `RunState.AddPlayerDebug` 那一步
    //     `CurrentMapPointHistoryEntry?.PlayerStats.Add(new PlayerMapPointHistoryEntry { PlayerId = … })`，
    //     清理时按 `PlayerId` 摘掉（**不进 `runState.Players`** 这条 H1 不变）。通用教训 → references 坑 **AE-4**。
    // r247（2026-10-09）**战灵召唤 Phase 2b —— 两个"大招" + D7 预设档**（doc §7.3 / §7.3.1 / §8 Phase 2b）：
    //  ① **D7 预设档表**（`PureLogic/HellwarWarGodPresets`）：普通 = 召唤者自身角色（自动兼容 mod 角色，不变）；
    //     罕见 / 稀有 / 先古 = 预设表里指定的**原版角色皮**（静默猎手 / 铁甲战士 / 摄政王）+ **卡组全升级**
    //     （先古额外 +25 血上限）。⚠ 刻意**不硬编码任何卡牌 id**：`ModelDb.Character<T>()` + `StartingDeck`
    //     是编译期可校验的引用；强度差由"卡组是否全升级"表达（首版不挂额外遗物 —— 战神是中途加人的，
    //     `Akabeko` / `Anchor` / `Lantern` 那类"开战时机"遗物根本不会触发）。四档因此**都可单点召唤**（1/2/3/4）。
    //  ② 【无尽战神】R16（整包 **8**，扣**本场预算**，战后补满）：**每个补位各掷一次**随机取「普通/罕见/稀有」
    //     三档之一（**先古不进随机池** D17），**补满到 12 席**、超员截断（D18）；**每场限 1 次**（D15）。
    //  ③ 【终极九连环】R17（整包 **12**，**永久扣计数上限** `SpiritCount`，2026-10-09 用户二轮修订 ——
    //     "开出来就赢" ⇒ 用"永久消耗上限"定价，玩家自然留到最后一场）：召唤先古战神；不足 12 时**部分成功**
    //     （每 3 点永久消耗换 1 个先古、至少 1 个、门槛 3 点）；按剩余栏位截断；**每场限 1 次**。
    //  ④ 两个大招都进**同一个召唤选择 UI**（D14）——文案里带"实际会召唤几个"（D18）与"永久消耗"（R17）；
    //     买不起 / 本场已用则**不出现**（不弹空弹层）。批量生成之间**错开 ~0.1s**（一次建 N 个立绘节点会掉帧）。
    //  ⑤ 扣费纪律沿用 AE-2：**先扣后生成、一个都没生成就全额退回**（大招连"本场次数"一起退还）。
    //  ⚠ 本阶段战神仍**不出牌**（托管出牌是 Phase 3）⇒ 档位强度的"体感"要等 Phase 3；
    //     现在先古档已生效的部分 = 更肉（+25 血）+ 更占敌人攻击次数。
    // r248（2026-10-09，r247 实机「一切都好正常」后用户对【终极九连环】的预期修正）：
    //  用户口径：「我的预期是**固定召唤 9 个，除非到栏位上限**。也就是说想要用这招，**计数至少能召唤 9 个**，
    //  然后再**根据实际召唤的数量**计算消耗的量」⇒ 取消原来"每 3 点 1 个"的部分成功，改成：
    //  ① **门槛 = 整包价 12**（= "至少够召唤 9 个"，`NineChainMinSpirit` 由 3 改成 12）；
    //  ② **固定召唤 9 个**，只有剩余栏位不足才截断（`ResolveNineChainSummonCount(freeSlots)`）；
    //  ③ **消耗按实际召唤数折算整包价**：`round(12 × 实际 / 9)`（9 个 = 12；被截断到 k 个就只扣 `round(12k/9)`）；
    //  ④ 少生了几个（生成失败 / 中途满员）⇒ **按实际数量退差价**（差额 `RefundPermanentSpirit`）。
    //  单测把 9 个 = 12 点的自洽性与各 k 的折算值都钉住了。
    // r249（2026-10-10）**战灵召唤 Phase 3 —— 战神进瓦库托管名单 + 台词「唔」**（设计文档 §8 Phase 3 / R11）：
    //  ① **"名单"落法 = 显式认临时席位，不塞遗物**：`LocalWakuuRelicRuntime.IsAutoplayHosted(player)`
    //     = 持接管遗物 **或** `LocalSelfCoopContext.IsTemporarySeat`（纯逻辑 `WakuuAutoplaySeatPolicy`，有单测）。
    //     看门狗候选处（`TryAutoEndTurnForRelicControlledPlayer`）改用它；`TryScheduleWatchdog` 对临时席位
    //     不再因 `no-wakuu-relic` 早退。
    //  ② **出牌链去遗物依赖**：`RunWatchdogAsync` / `ExecuteBeforePlayPhaseStartAsync` 的 `relic` 改**可空**，
    //     内部一律 `Player owner = relic?.Owner ?? player`；`relic.Flash()` → `relic?.Flash()`。
    //     🔴 临时席位的 choiceContext 必须走**显式 owner** 重载：原来那个重载在 `source == null` 时会把 Owner
    //     **静默**解析成"战斗里第一个玩家"（`HookPlayerChoiceContext.cs:97-106`）⇒ 选牌永不本地入队（挂死），
    //     而 `HookPlayerChoiceContextLocalPatch` 对临时席位不生效、救不回来（references 坑 AE-6）。
    //  ③ **出牌上限**：战神与瓦库形态同款"**全量出牌**"（`UsesFullPlayLimit`，护栏 60；玩家关掉「打光所有手牌」
    //     时回落 13）—— 否则先古那一大把牌打不完，Phase 2b 的档位强度无从体现。
    //  ④ **回合生命周期**：战神回合开始**不再立即收口**（`HellwarSummonCombatRuntime.OnPlayerTurnStart` 只
    //     返回 true = 别切前台），改由 `HellwarWarGodFactory.TryEndIdleWarGodTurns`（每帧 tick，挂在
    //     `TryAutoEndTurnForRelicControlledPlayer`）= "活着 + 未 ready + 手牌无可出牌 ⇒ EndTurn"，
    //     并包裹 `WakuuTurnEndOrigin.BeginModIssuedEnd()`（收口后又拿到新牌会被放行继续打）。
    //     ⚠ 当回合**不进托管**（召唤当回合手牌为空，且 ③b `MarkReadyToEndTurnIfMidPlayerTurn` 保留）⇒
    //       r244「不能结束回合」不回归；下一回合 `StartTurn` 会把战神纳入 `playersStartingTurn` ⇒ 正常抽牌 + 托管。
    //  ⑤ **台词「唔」**：`relics` 表注入 `LMC_HELLWAR_WARGOD.line`（`LocalWakuuHellwarLocalization.WarGodLineKey`
    //     + `LocalModText.HellwarWarGodLine`），`ResolveAutoplayLine` 按席位分流 + `LocString.Exists` 兜底。
    //  ⑥ 顺带：`EnsureWakuuPerspective` 对临时席位早退（原来会白打一条"切换视角 → 0x1000"的错日志）。
    // r250（2026-10-10，r249 实机首测修复）**战神出牌必抛 + 【无尽战神】先古开关**：
    //  ① 🔴 **r249 实机真因**：战神**不进 `runState.Players`**（H1），而出牌路径上大量 UI / 状态刷新会调
    //     `LocalContext.GetMe(runState)`（`IPlayerCollection` 重载，原实现 `?? throw`）⇒ 每次出牌必抛
    //     `Local player not found in player collection.`（日志铁证：`瓦库评分出牌: player=4096, card=…`
    //     紧接 `瓦库选择器作用域异常退出` / `瓦库看门狗重启失败: … error=Local player not found in player collection.`）
    //     ⇒ 牌一张打不出去 + 战神永不 ready ⇒ **真人无法结束回合**（r249 用户实机现象）。
    //     修法 = 新补丁 `LocalContextTemporarySeatGetMePatch`：`NetId` 是临时席位、且在册、且原集合
    //     查不到时返回在册战神（`HellwarWarGodFactory.FindSpawnedWarGod`）；三条收窄 ⇒ 原版/真人/瓦库语义不动。
    //  ② 【无尽战神】新增开关 `hellwarEndlessAncient`（默认关，用户 2026-10-10 追加）：开启后随机池
    //     加入【先古】（原为【九连环】专属）；池口径收进 `HellwarWarGodPresets.ResolveRandomPool(bool)`。
    // r251（2026-10-10，r250 实机反馈）**战神出牌不再锁死真人**：
    //  用户「战神出牌导致我不能出牌」。根因 = 战神（临时席位）在 `IsVakuuFormMode` 上为 false
    //  ⇒ `WakuuPlayQueuePolicy.DecidePath` 把它判成 **inline 路径** ⇒ ① 抢全局 1 槽 `SelectorScopeGate`、
    //  ② `RunWatchdogAsync` 把 **`LocalContext.NetId` 钉成战神**；而战神是"全量出牌"（打到没牌为止，
    //  多张 × 每张约 1s）⇒ 那几秒里原版把"本机玩家"当成战神 ⇒ 真人手牌被当成别人的牌、点不出牌。
    //  修法 = 新判据 `LocalWakuuRelicRuntime.UsesHostedPlayPath(player)`（= 瓦库形态 **或** 临时席位；
    //  纯逻辑 `WakuuAutoplaySeatPolicy.UsesHostedPlayPath`，有单测）替换**出牌路径决策**那 4 处对
    //  `IsVakuuFormMode` 的使用（3 处 `DecidePath` + 1 处 `ShouldSkipCardPileVisuals`）
    //  ⟹ 战神改走**队列 + 并发档**：不抢闸门、不钉上下文（出牌由游戏全局动作泵执行、归属走选择器注册表）。
    //  `CardPlayVisualsSkipPatch` 同步改用同一判据（战神队列出牌也跳过卡牌堆演出）。
    //  ⚠ 刻意**不动** `IsVakuuFormMode` 本身（它被选牌/奖励/事件等一大批自动作答入口复用，扩大它会误开那些口子）。
    // r252（2026-10-10，r251 实机反馈）**战神补登记网络卡牌 id（队列路径才走通）**：
    //  用户「战神不出牌，我也不能结束回合」。r251 日志铁证：`瓦库评分出牌: player=4096, card=…YI_CUT`
    //  紧接 `瓦库看门狗重启失败: … error=Card CARD.LEX_NINJA2_CARD_YI_CUT (48902160) could not be found
    //  in combat ID database!`（每 2 秒 7 次、0 条 `战神已自动结束回合`）。
    //  真因 = `NetCombatCardDb.StartCombat(players)` **只在开战时按 `state.Players` 登记一次**
    //  （登记 + 订阅牌堆 `ContentsChanged`），而战神是**途中**加进来的、且刻意不进 `runState.Players`
    //  ⇒ 它的牌从未登记 ⇒ 队列路径 `NetCombatCard.FromCard(card)` → `GetCardId` 抛
    //  ⇒ 牌一张都入不了队 ⇒ 战神永不 ready ⇒ 「结束回合」分母多一个（r251/r252 同族：**"不进集合"
    //  的实体要把按 id 查它的公共设施补齐**，与坑 AE-7 同根因家族）。
    //  修法 = `LocalWakuuRelicRuntime.RegisterNetworkCardId`：入队前用官方公开的
    //  `NetCombatCardDb.Instance.IdCardForTesting(card)` 补登记这一张（幂等；失败只记一次 WARN）。
    // r253（2026-10-10，r252 实机反馈）**战神回归 inline（不钉上下文）+ 召唤保命【无实体】**：
    //  ① r252 实机铁证 `瓦库出牌入队执行异常: player=4096, card=NEUTRALIZE,
    //     error=Tried to get local action queue for nonexistent player with ID 4096!` ——
    //     队列档对战神**根本走不通**（动作队列按正式玩家建；上一轮的 `NetCombatCardDb` 未登记是同一层）
    //     ⇒ 战神只能走 **inline**。而 inline 原本必须钉 `LocalContext`（r250 实机因此锁死真人）。
    //     合订口径 ⇒ **战神 = inline + 不钉上下文**：`RunWatchdogAsync` 的钉扎条件对临时席位短路
    //     （选牌那一刻的短钉扎由 `PlayerChoiceSynchronizerPatch` 自己负责，窗口是毫秒级，不锁真人）；
    //     三处 `DecidePath` 恢复 `IsVakuuFormMode`（战神 ⇒ InlineAutoPlay）。
    //  ② 用户要求「要么战神一来就能打牌、要么召唤时先给它一层无实体，不然高压环境还没打牌直接死」
    //     ⇒ 实装 **召唤时给战神 1 层 `IntangiblePower`（无实体）**
    //     （`HellwarWarGodFactory.ApplySummonProtectionAsync`）：它把 HP 损失压到 1（**立即生效**）、
    //     层数在**敌方回合结束**递减 ⇒ 保住"召唤当回合 + 接着那个敌方回合"。
    //     （"当回合就能打牌"要中途补抽起手牌 + 去掉落地立即 ready，会重新引入回合卡住风险 ⇒ 本轮先走保命方案。）
    // r254（2026-10-10，用户「继续干活」= 第七十八段 ④ 的下一步 ①）**手工预设卡组（配置文件可自定义）**：
    //  战神卡组不再"原版角色初始卡组 + 全升级"，改成**手工挑的列表**（用户口径："手工 id 列表 + 开放到配置文件"）。
    //  ① **内置默认**（`HellwarWarGodDecks`）：罕见/稀有/先古各 14 张，用 `ModelDb.Card<T>()` **类型引用**写死
    //     （编译期可校验；**不用字符串 id** —— 拼错会静默召不出东西，与 D7 首版"零硬编码"取舍一致）；
    //     挑卡口径 = 只挑**托管 AI 好驾驭的线性牌**（罕见=毒+格挡+敏捷 / 稀有=易伤+力量+大格挡 /
    //     先古=攒星+花星里"有星就纯赚"的那半）；**普通档不替换**（仍用召唤者自身角色初始卡组 ⇒ 兼容 mod 角色）。
    //  ② **配置覆盖**（`hellwarDeckCommon/Rare/Epic/Ancient`，默认空 = 内置默认）：逗号/空白分隔的
    //     `ModelId.Entry`（`CARD.` 前缀可省、大小写不敏感），末尾 `+` = 这一张单独升级，写几遍就是几张；
    //     认不出的 id 跳过 + WARN、一条都没认出来则回落角色初始卡组。解析层 = 纯逻辑 `HellwarWarGodDeckPlan`。
    //  ③ **落点** `HellwarWarGodFactory.ApplyPresetDeck`：必须在 `AttachToRun` / `PopulateCombatState` **之前**
    //     替换卡组（那两步分别补 `Owner`+登记 run 卡牌表、把卡组克隆进抽牌堆）；做法照抄 `PopulateStartingDeck`
    //     的原语（`ToMutable()` → `Deck.AddInternal(..., silent: true)`），Owner/登记仍交给 `AttachToRun`。
    //  ④ 锚点：启动自检 `[地狱战神] 预设卡组已就绪: 档位=…, 内容=…`（同时是给玩家的配置模板）+
    //     召唤时 `①a 预设卡组已替换: … 来源=内置默认/配置文件`。
    private const string BuildMarker = "Revival v1.45.0 (game v0.111.0, marker=2026-10-10-r254)";

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
        // ④ 瓦库的爹（r228）：【合体】混抽（Draw 的 4 参数重载；2 参数重载会转发到它）
        "MegaCrit.Sts2.Core.Commands.CardPileCmd.Draw/4",
        // ④ 瓦库的爹（r228）：【合体】混弃（缺了只是弃牌不混堆，不影响弃牌本身）
        "MegaCrit.Sts2.Core.Commands.CardCmd.DiscardAndDraw",
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
        // 战灵召唤 Phase 0 POC：开机就把开关状态打出来（关着也打 ⇒ 实机能区分"没配变量"与"功能没生效"）。
        HellwarPocRuntime.LogSwitchStateAtStartup();

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
            RegisterWakuuHellwarContentToPool();
            SafeAction(fatalFailures, FatalCode.Model, "瓦库遗物本地化", () => LocalWakuuRelicLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "瓦库的爹内容本地化", () => LocalWakuuDaddyLocalization.Initialize());
            SafeAction(fatalFailures, FatalCode.Model, "战灵召唤内容本地化", () => LocalWakuuHellwarLocalization.Initialize());
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
    /// <summary>
    /// 战灵召唤（原 ④「地狱战神」，#21）：遗物【地狱耳环】注册进**事件遗物池**
    /// （与瓦库托管遗物 / 【瓦库的爹】同池）。
    ///
    /// 纪律与 <see cref="RegisterWakuuDaddyContentToPool"/> 完全相同：**必须在游戏初始化前**登记
    /// （池一旦被 <c>ModelDb.Preload</c> 首次访问即冻结）；类型本身无需手动注册（<c>ModelDb.Init</c> 反射扫本程序集）。
    /// 是否真的进了池由 <see cref="Patch.HellwarContentPatch"/> 在 <c>ModelDb.Preload</c> 后打锚点。
    /// </summary>
    private static void RegisterWakuuHellwarContentToPool()
    {
        try
        {
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuHellwarEarring>();
            LocalMultiControlLogger.Info("已登记战灵召唤遗物【地狱耳环】到事件遗物池（就绪锚点在 ModelDb.Preload 之后）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"登记战灵召唤遗物到遗物池失败（遗物描述页可能异常）: {exception.Message}");
        }
    }

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
