using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalModText
{
    private const string EnglishLanguageCode = "eng";
    private const string UnknownSlotLabel = "?";

    public static bool IsEnglish => LocManager.Instance.Language == EnglishLanguageCode;

    public static string Select(string zh, string en)
    {
        return IsEnglish ? en : zh;
    }

    public static string RoleSlot(string slotLabel)
    {
        return slotLabel == UnknownSlotLabel
            ? Select("未知角色", "Unknown Player")
            : Select($"角色{slotLabel}", $"Player {slotLabel}");
    }

    public static string RestartRoomButton => Select("重启房间", "Restart Room");
    public static string RestartRoomFailed => Select("重启失败，请手动继续游戏", "Restart failed, please continue manually");

    public static string LocalSelfCoopCardTitle => Select("单人多角色", "Local Multi-Control");
    public static string LocalSelfCoopCardDescription => Select(
        "在本机创建2~12名可切换角色，进行本地协作。\n进入后可用 +/- 调整人数。",
        "Create 2-12 switchable local players on this machine.\nUse +/- to change player count.");

    public static string EnteredLocalSelfCoopHint => Select(
        "已进入本地多角色：按 +/- 调整人数（2~12）",
        "Local multi-control enabled: use +/- to set player count (2-12)");

    /// <summary>联机菜单「单人多角色」卡片下方的小按钮：本地多角色自定义模式（官方自定义入口保持原样）。</summary>
    public static string LocalCustomSelfCoopButton => Select("本地·自定义模式", "Local Custom Run");

    /// <summary>联机菜单「单人多角色」卡片下方的小按钮：本地多角色每日挑战（官方每日入口保持原样）。</summary>
    public static string LocalDailySelfCoopButton => Select("本地·每日挑战", "Local Daily Run");

    public static string EnteredDailySelfCoopHint => Select(
        "已进入本地多角色每日挑战：按 +/- 调整人数（2~4）",
        "Local daily run enabled: use +/- to set player count (2-4)");

    public static string GhostHandsOn => Select(
        "队友手牌已显示（Ctrl+方向键 调整位置，F8 关闭）",
        "Teammate hands shown (Ctrl+Arrows to move, F8 to hide)");

    public static string GhostHandsOff => Select(
        "队友手牌已隐藏（F8 重新显示）",
        "Teammate hands hidden (F8 to show)");

    public static string LobbyEditingSlot(string slotLabel)
    {
        return Select($"大厅编辑角色：{RoleSlot(slotLabel)}", $"Lobby Editor: {RoleSlot(slotLabel)}");
    }

    public static string ControlledSlot(string slotLabel)
    {
        return Select($"控制角色：{RoleSlot(slotLabel)}", $"Controlled Character: {RoleSlot(slotLabel)}");
    }

    public static string LocalPlayerCount(int count)
    {
        return Select($"本地人数：{count}", $"Local Players: {count}");
    }

    public static string RandomCharacterNotSupported => Select(
        "本地多控暂不支持随机角色",
        "Random character is not supported in local multi-control");

    public static string RestSiteAllChosen => Select(
        "休息区选择完成：所有可选角色都已选择",
        "Rest site complete: all selectable players finished");

    public static string RestSiteFocusHint => Select(
        "休息区提示：若未显示选项，请先按 R 或 ] 切换一次角色",
        "Rest site tip: if options are missing, press R or ] to switch once");

    /// <summary>通用「取消」按钮文案（选玩家选择器等自建弹层用）。</summary>
    public static string Cancel => Select("取消", "Cancel");

    // ---- 瓦库四功能（2026-10-05 开工）----

    /// <summary>净化：选玩家选择器的标题。</summary>
    public static string PurifyPickTitle => Select("净化 · 选择目标瓦库", "Purify · Choose a Vakuu");

    /// <summary>净化：休息区选项名（注入 rest_site_ui 表的 OPTION_LMC_PURIFY.name）。</summary>
    public static string PurifyOptionName => Select("净化", "Purify");

    /// <summary>
    /// 净化：休息区选项描述（注入 rest_site_ui 表的 OPTION_LMC_PURIFY.description）。
    /// `{Amount}` 是游戏 LocString 的占位符，由 PurifyWakuuRestSiteOption 用 Add("Amount", …) 填充 —— 
    /// 所以这里**必须是普通字符串字面量**，不能写成 C# 插值。
    /// </summary>
    public static string PurifyOptionDescription => Select(
        "删除目标瓦库卡组中最多 {Amount} 张牌。",
        "Remove up to {Amount} cards from the chosen Vakuu's deck.");

    /// <summary>我们联合：战斗界面按钮文案。</summary>
    public static string UniteButtonName => Select("我们联合", "Unite");

    /// <summary>我们联合 ①（自己卡组 → 瓦库手牌）：选玩家选择器标题。</summary>
    public static string UniteGivePickTitle => Select("我们联合 · 选择目标瓦库", "Unite · Choose a Vakuu");

    /// <summary>我们联合 ②（瓦库卡组 → 自己手牌）：选玩家选择器标题。</summary>
    public static string UniteTakePickTitle => Select("我们联合 · 选择要复制的瓦库", "Unite · Choose a Vakuu to copy from");

    /// <summary>
    /// 我们联合 ①：选牌提示（从真人自己卡组里选 1 张复制给瓦库）。
    /// 注入到游戏 <c>card_selection</c> 表（键 <c>LMC_UNITE_GIVE</c>），见 <see cref="LocalWakuuUniteLocalization"/>。
    /// </summary>
    public static string UniteGivePrompt => Select("选择要复制给瓦库的牌", "Choose a card to copy to the Vakuu");

    /// <summary>
    /// 我们联合 ②：选牌提示（从瓦库卡组里选 1 张复制给真人）。
    /// 注入到游戏 <c>card_selection</c> 表（键 <c>LMC_UNITE_TAKE</c>）。
    /// </summary>
    public static string UniteTakePrompt => Select("选择要从瓦库复制的牌", "Choose a card to copy from the Vakuu");

    // ---- 瓦库四功能 · ③ 炼化（2026-10-06 开工）----

    /// <summary>炼化：选玩家选择器的标题。</summary>
    public static string RefinePickTitle => Select("炼化 · 选择目标瓦库", "Refine · Choose a Vakuu");

    /// <summary>炼化：休息区选项名（注入 rest_site_ui 表的 OPTION_LMC_REFINE.name）。</summary>
    public static string RefineOptionName => Select("炼化", "Refine");

    /// <summary>
    /// 炼化：休息区选项描述（注入 rest_site_ui 表的 OPTION_LMC_REFINE.description）。
    /// `{Cards}` / `{Relics}` / `{Ratio}` 是游戏 LocString 的占位符，由 <see cref="RefineWakuuRestSiteOption"/>
    /// 用 Add(...) 填充 —— 所以这里**必须是普通字符串字面量**，不能写成 C# 插值。
    /// </summary>
    public static string RefineOptionDescription => Select(
        "炼化一个瓦库：收编其 {Ratio} 的血量与血上限、自选最多 {Cards} 张牌与 {Relics} 遗物、任取它的药水，随后把它炼掉（血量与血上限清零，不可撤销）。",
        "Refine a Vakuu: absorb {Ratio} of its HP and Max HP, take up to {Cards} cards, {Relics} relics and any of its potions, then melt it down (HP and Max HP are zeroed; irreversible).");

    /// <summary>
    /// 炼化：卡组选牌提示（注入 card_selection 表，键 LMC_REFINE_CARDS）。
    /// r220 起**可以一张都不选**（用户：不能一张都不要会妨碍玩小卡组）—— 提示里写明，免得玩家以为必须选。
    /// </summary>
    public static string RefineCardsPrompt => Select(
        "选择要收编的牌（可以一张都不选，直接确认）",
        "Choose cards to absorb (you may take none — just confirm)");

    /// <summary>炼化：选遗物选择器的标题。</summary>
    public static string RefineRelicPickTitle => Select("炼化 · 选择要收编的遗物", "Refine · Choose a relic to absorb");

    /// <summary>炼化：选药水选择器的标题（任选 1 瓶拿走，其余全删）。</summary>
    public static string RefinePotionPickTitle => Select("炼化 · 选择要收编的药水", "Refine · Choose a potion to absorb");

    /// <summary>炼化：收编血量的比例档位按钮文案。</summary>
    public static string RefineHpRatioLabel(string ratioKey)
    {
        return ratioKey switch
        {
            WakuuRefinePolicy.HpRatioHalf => Select("一半（50%）", "Half (50%)"),
            WakuuRefinePolicy.HpRatioQuarter => Select("四分之一（25%）", "Quarter (25%)"),
            _ => Select("全部（100%）", "All (100%)"),
        };
    }

    /// <summary>炼化：卡组张数上限档位按钮文案。</summary>
    public static string RefineCardLimitLabel(string limitKey)
    {
        return limitKey switch
        {
            WakuuRefinePolicy.CardLimit20 => Select("20 张", "20 cards"),
            WakuuRefinePolicy.CardLimit5 => Select("5 张", "5 cards"),
            _ => Select("不限", "Unlimited"),
        };
    }

    /// <summary>炼化：遗物件数上限档位按钮文案（1 / 3 / 5 / 不限）。</summary>
    public static string RefineRelicLimitLabel(string limitKey)
    {
        return limitKey switch
        {
            WakuuRefinePolicy.RelicLimit1 => Select("1 件", "1 relic"),
            WakuuRefinePolicy.RelicLimit3 => Select("3 件", "3 relics"),
            WakuuRefinePolicy.RelicLimit5 => Select("5 件", "5 relics"),
            _ => Select("不限", "Unlimited"),
        };
    }

    // ---- 战灵召唤（原 ④「地狱战神」，#21）· Phase 1：遗物【地狱耳环】+ 休息处【分裂精神】----
    // 2026-10-09 开工。Phase 1 只做"计数"这条线（遗物 + 火堆换计数 + 纯逻辑档位），战斗内召唤见后续阶段。

    /// <summary>遗物【地狱耳环】的名称。</summary>
    public static string HellwarEarringTitle => Select("地狱耳环", "Hell Earring");

    /// <summary>遗物【地狱耳环】的描述（Phase 2 起召唤已实装；罕见 / 稀有 / 先古档仍是后续阶段）。</summary>
    public static string HellwarEarringDescription => Select(
        "休息处的【分裂精神】可以为它增加计数；战斗中每个回合开始可消耗计数召唤一个【普通】战神（更强档位在后续阶段实装）。消耗的计数在战斗结束后自动补满。",
        "The 【Split Spirit】 option at rest sites charges it; at the start of each combat turn you may spend charges to summon a 【Common】 war god (stronger tiers come in later phases). Spent charges refill after combat.");

    /// <summary>
    /// 遗物【地狱耳环】的风味文本（2026-10-09 用户指定，逐字取自《海虎》的旁白段）。
    /// 用户给了多段候选，选这一段是因为它**直接点题"地狱"**且读起来像遗物风味（其余候选更像台词，
    /// 更适合留给后续的战斗内召唤 UI）。
    /// </summary>
    public static string HellwarEarringFlavor => Select(
        "达摩经记载，当战纹现于强者面上，便是人成神的时候。但若战纹现于狂者的额上又如何？……可能这就是地狱开始的时候了。",
        "The Damo Sutra says: when battle-marks surface on a strong man's face, that is the hour a man becomes a god. But what if those marks surface on a madman's brow? …Perhaps that is when hell begins.");

    /// <summary>【分裂精神】休息区选项名（注入 rest_site_ui 表 <c>OPTION_LMC_SPLIT_SPIRIT.name</c>）。</summary>
    public static string SplitSpiritOptionName => Select("分裂精神", "Split Spirit");

    /// <summary>
    /// 【分裂精神】休息区选项描述（注入 rest_site_ui 表 <c>OPTION_LMC_SPLIT_SPIRIT.description</c>）。
    /// <c>{Hp}</c> / <c>{Ratio}</c> 是游戏 LocString 的占位符，由 <see cref="HellwarSplitSpiritOption"/>
    /// 用 <c>Add(...)</c> 填充 —— 所以这里**必须是普通字符串字面量**，不能写成 C# 插值。
    /// </summary>
    public static string SplitSpiritOptionDescription => Select(
        "消耗 {Hp} 点生命（当前血量的 {Ratio}），为【地狱耳环】增加 1 个计数。",
        "Spend {Hp} HP ({Ratio} of your current HP) to add 1 charge to the Hell Earring.");

    /// <summary>【分裂精神】扣血档位按钮文案（不扣血 / 10% / 15% / 20%）。</summary>
    public static string HellwarSplitHpRatioLabel(string ratioKey)
    {
        return ratioKey switch
        {
            HellwarSummonPolicy.SplitHpRatioNone => Select("不扣血（0%）", "No cost (0%)"),
            HellwarSummonPolicy.SplitHpRatio10 => Select("10%", "10%"),
            HellwarSummonPolicy.SplitHpRatio20 => Select("20%", "20%"),
            _ => Select("15%", "15%"),
        };
    }

    /// <summary>「每幕自动获得计数」数量档位的按钮文案（1 / 2 / 3）。</summary>
    public static string HellwarActGainLabel(string amountKey)
    {
        return amountKey switch
        {
            "1" => Select("1 个", "1"),
            "3" => Select("3 个", "3"),
            _ => Select("2 个", "2"),
        };
    }

    // ---- 战灵召唤 · Phase 2：回合开始的召唤选择 UI ----

    /// <summary>回合开始「召唤选择」弹层的标题（<see cref="LocalWakuuChoiceOverlay"/>）。</summary>
    public static string HellwarSummonPickTitle => Select(
        "召唤战神（消耗计数）",
        "Summon a War God (spends charges)");

    /// <summary>战神档位的显示名（D7：普通 / 罕见 / 稀有 / 先古）。</summary>
    public static string HellwarTierName(HellwarWarGodTier tier)
    {
        return tier switch
        {
            HellwarWarGodTier.Rare => Select("罕见", "Rare"),
            HellwarWarGodTier.Epic => Select("稀有", "Epic"),
            HellwarWarGodTier.Ancient => Select("先古", "Ancient"),
            _ => Select("普通", "Common"),
        };
    }

    /// <summary>召唤选择里"某一档"的选项文案（例：召唤【普通】战神（消耗 1 计数））。</summary>
    public static string HellwarSummonOptionLabel(string tierName, int cost)
    {
        return Select(
            $"召唤【{tierName}】战神（消耗 {cost} 计数）",
            $"Summon a 【{tierName}】 war god (costs {cost})");
    }

    // ---- 战灵召唤 · Phase 2b：两个"大招"（R16【无尽战神】/ R17【终极九连环】）----

    /// <summary>
    /// 【无尽战神】选项文案（D18：**必须显示实际会召唤几个** —— 会被剩余栏位截断）。
    /// </summary>
    public static string HellwarEndlessOptionLabel(int count, int cost)
    {
        return Select(
            $"【无尽战神】把空位补满：实际召唤 {count} 个随机战神（消耗 {cost} 计数·本场战斗限 1 次）",
            $"【Endless War Gods】 fill every free slot: summons {count} random war gods (costs {cost} · once per combat)");
    }

    /// <summary>
    /// 【终极九连环】选项文案（**永久扣计数上限**是这条的关键信息，必须写明）。
    /// </summary>
    public static string HellwarNineChainOptionLabel(int count, int cost)
    {
        return Select(
            $"【终极九连环】召唤 {count} 个先古战神（**永久**消耗 {cost} 点计数上限·本场战斗限 1 次）",
            $"【Ultimate Nine-Chain】 summons {count} ancient war gods (**permanently** spends {cost} charges from your cap · once per combat)");
    }

    // ---- 战灵召唤 · Phase 3：战神托管出牌（R11）----

    /// <summary>
    /// 战神（临时席位）出牌收尾台词（R11 用户口径：「战神台词改『唔』」）。
    /// 注入 <c>relics</c> 表（键见 <see cref="LocalWakuuHellwarLocalization.WarGodLineKey"/>），
    /// 由 <see cref="LocalWakuuRelicRuntime"/> 按席位分流取用。
    /// </summary>
    public static string HellwarWarGodLine => Select("唔", "Mmm.");

    /// <summary>「休息处选项滚动条」的开关说明用标题（设置页）。</summary>
    public static string RestSiteOptionScrollTitle => Select(
        "休息处选项横向滚动条（默认开）",
        "Rest-Site Option Scrollbar (default ON)");

    // ---- 瓦库四功能 · ④「瓦库的爹」（遗物 + 我挡/你攻/合体 三张牌）----
    // 2026-10-07 从「卡池注册验证」转正为功能首版，r228 实装三张牌的效果。命名口径（2026-10-06 用户拍板）：
    // 「地狱战神」留给战灵召唤（临时玩家召唤）；本条叫「瓦库的爹」。

    /// <summary>遗物【瓦库的爹】的名称（占位名，正式名字用户另有安排）。</summary>
    public static string DaddyRelicTitle => Select("瓦库的爹", "Vakuu's Daddy");

    /// <summary>遗物【瓦库的爹】的描述：战斗开始时给三张牌。</summary>
    public static string DaddyRelicDescription => Select(
        "战斗开始时，获得【我挡】【你攻】【合体】各一张。",
        "At the start of combat, gain one 【I Block】, 【You Attack】 and 【Merge】.");

    /// <summary>遗物【瓦库的爹】的风味文本。</summary>
    public static string DaddyRelicFlavor => Select("爹来摆平。", "Daddy handles it.");

    /// <summary>牌【我挡】的名称。</summary>
    public static string DaddyShieldCardTitle => Select("我挡", "I Block");

    /// <summary>牌【我挡】的描述（照提案 §5.1；r228 实装后去掉"未实现"脚注）。</summary>
    public static string DaddyShieldCardDescription => Select(
        "选择一个队友：本回合它受到的攻击伤害改由你承受，但你只承受一半。抽 1 张牌。",
        "Choose an ally: attack damage it takes this turn is redirected to you, but only half of it. Draw 1 card.");

    /// <summary>牌【你攻】的名称。</summary>
    public static string DaddyFocusCardTitle => Select("你攻", "You Attack");

    /// <summary>牌【你攻】的描述（照提案 §5.1）。</summary>
    public static string DaddyFocusCardDescription => Select(
        "选择一个敌人：本回合所有瓦库优先攻击它。抽 1 张牌。",
        "Choose an enemy: every Vakuu prioritizes attacking it this turn. Draw 1 card.");

    /// <summary>牌【合体】的名称。</summary>
    public static string DaddyMergeCardTitle => Select("合体", "Merge");

    /// <summary>牌【合体】的描述（照提案 §5.1）。</summary>
    public static string DaddyMergeCardDescription => Select(
        "选择一个瓦库队友：它的手牌与能量都归你，本回合你抽牌时随机从双方抽牌堆取、弃牌时随机进其中一方的弃牌堆。抽 1 张牌。",
        "Choose a Vakuu ally: its hand and energy become yours; this turn your draws come from either draw pile at random, and your discards go to either discard pile at random. Draw 1 card.");

    /// <summary>状态（【我挡】挂在被挡队友身上）的名称。</summary>
    public static string DaddyShieldPowerTitle => Select("我挡（承伤转移）", "I Block (redirected)");

    /// <summary>状态（【我挡】）的描述：回合结束时消失，说明白只对攻击伤害与"一半"。</summary>
    public static string DaddyShieldPowerDescription => Select(
        "本回合受到的攻击伤害改由施加者承受，且只有一半。敌方回合结束时消失。",
        "Attack damage taken this turn is borne by the applier instead, and only half of it. Removed at the end of the enemy turn.");

    public static string GlobalWakuuLabel => Select("全瓦库", "All Vakuu");

    public static string VakuuControlsPlayer(string slotLabel)
    {
        return Select($"瓦库将接管角色{slotLabel}的操作", $"Vakuu will control {RoleSlot(slotLabel)}");
    }

    public static string VakuuReleasedPlayer(string slotLabel)
    {
        return Select($"瓦库已停止接管角色{slotLabel}的操作", $"Vakuu stopped controlling {RoleSlot(slotLabel)}");
    }

    public static string VakuuControlsAllPlayers()
    {
        return Select("瓦库将接管所有角色的操作", "Vakuu will control all characters");
    }

    public static string VakuuReleasedAllPlayers()
    {
        return Select("瓦库已停止接管所有角色的操作", "Vakuu stopped controlling all characters");
    }
}
