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
