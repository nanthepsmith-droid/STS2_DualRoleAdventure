#nullable enable

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「地狱战神」的**卡池注册验证卡**（2026-10-06，前置功课「mod 自定卡如何挂进卡池」的最小验证）。
///
/// 目的只有一个：验证下面这条链路真的通 ——
/// <list type="number">
/// <item>本类写在 mod 程序集里 ⇒ 被 <c>ReflectionHelper.GetSubtypesInMods&lt;AbstractModel&gt;</c> 扫到；</item>
/// <item><c>ModelDb.Init()</c> 自动 <c>Activator.CreateInstance</c> 建实例并进 <c>_contentById</c>；</item>
/// <item><c>Entry</c> 阶段 3 的 <c>ModHelper.AddModelToPool&lt;EventCardPool, 本类&gt;()</c> 登记（**必须早于游戏初始化**）；</item>
/// <item><c>EventCardPool.AllCards</c> 首次访问时由 <c>ModHelper.ConcatModelsFromMods</c> 拼接进池 ⇒ 进 <c>ModelDb.AllCards</c>。</item>
/// </list>
/// <b>不做效果</b>（用 <see cref="CardModel.OnPlay"/> 的基类空实现），
/// 本轮也**没有**任何给牌入口 —— 遗物 + 「开战给 3 张占位牌」属 ④ 的下一步。
///
/// ⚠ **口径（2026-10-06 用户点名，防回归）：mod 自定卡一律放 <c>EventCardPool</c>，
/// 绝不要放 <c>ColorlessCardPool</c>。** 理由（已逐条 grep 反编译源码核实）：
/// <list type="bullet">
/// <item><c>ColorlessCardPool</c> = 「一般方式可得」：商店 <c>MerchantInventory</c>、遗物（工具箱 / 橙子面团 /
///   大型卷轴 / 铅坠 / 破旧地毯 / 全明星）、药水（无色药水 / 宇宙调合物）、能力（光谱转移）、
///   事件（无尽传送带 / 脑蛭）都直接引用它 ⇒ 玩家能正常刷到我们的牌；</item>
/// <item><c>EventCardPool</c> 全游戏**零获取入口**（唯一的 <c>ModelDb.CardPool&lt;EventCardPool&gt;()</c>
///   出现在 <c>ModelDb.AllSharedCardPools</c> 列表本身，而该列表只被零调用点的 <c>UnlockState.CardPools</c> 使用）
///   —— 原版事件牌（神化 / 幻影 / 白噪声…）就是这么放的；</item>
/// <item>顺带安全：<c>CardFactory.GetDefaultTransformationOptions</c> 对 <c>Rarity == Event</c> 改走无色池、
///   且结果只保留 Common/Uncommon/Rare ⇒ 不会被「变化」类效果变出来。</item>
/// </list>
/// <b>卡牌库归属也是对的</b>：库里的「无色」筛选判据是 <c>c.Pool is ColorlessCardPool</c>（我们不在），
/// 「其它」筛选判据是**稀有度** ∈ Event/Token/Status/Curse/Quest（我们在）。
/// 注意 <c>EventCardPool.IsColorless => true</c> 与「看起来像无色牌」（<c>EnergyColorName = "colorless"</c> +
/// <c>card_frame_colorless</c> + 灰底）都只是游戏对非角色池的**观感/分类**，
/// 只在 <c>PrismaticGem</c>（池全无色则跳过）与 <c>HeirloomHammer</c>（从手牌里挑无色牌）用到，**不构成获取路径**。
/// </summary>
internal sealed class LocalWakuuHellGodPlaceholderCard : CardModel
{
    /// <summary>
    /// 费用 / 类型 / 稀有度 / 目标都是**占位值**（效果未设计，先固定成「1 费 · 技能 · 自身」）。
    ///
    /// 稀有度必须取 <see cref="CardRarity.Event"/>（**别改成 Common/Uncommon/Rare** —— 那会让它落进
    /// 「按稀有度随机」的奖励分支）：事件池**没有任何获取入口**会抽到它
    /// （与瓦库托管遗物进 <c>EventRelicPool</c> 同口径），不会污染奖励 / 商店 / 战斗生成
    /// （<c>CardFactory.FilterForCombat</c> 亦显式排除 Event）。完整口径与证据见类注释。
    /// </summary>
    public LocalWakuuHellGodPlaceholderCard()
        : base(1, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    /// <summary>
    /// 本 mod **没有 PCK**（工坊清单 <c>has_pck=false</c>），自带卡图资源不存在 ⇒
    /// 借原版事件池里已有的【Stack】立绘，避免 <see cref="CardModel.PortraitPath"/>
    /// 指向不存在的资源（遗物侧同样是借原版图标：<c>IconBaseName =&gt; "whispering_earring"</c>）。
    /// 走 <c>ModelDb.Card&lt;Stack&gt;()</c> 而不是硬拼字符串，改名 / 换池时不会静默写错路径。
    /// </summary>
    public override string PortraitPath => ModelDb.Card<Stack>().PortraitPath;
}
