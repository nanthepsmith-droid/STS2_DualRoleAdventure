using System.Globalization;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>本地席位在 UI 上的称呼种类（2026-11-18 §排期 6）。</summary>
internal enum SeatDisplayKind
{
    /// <summary>真人席位：<c>Player 1</c> / <c>Player 2</c> ……</summary>
    Human,

    /// <summary>瓦库托管席位：<c>Bot 1</c> / <c>Bot 2</c> ……</summary>
    Wakuu,
}

/// <summary>
/// 本地席位的**显示名**（2026-11-18 §排期 6）：瓦库 = <c>Bot N</c>，真人 = <c>Player N</c>。
///
/// <para><b>为什么不再用玩家昵称</b>：本地多控的每个席位都是同一台机器 / 同一个 Steam 账号伪造出来的，
/// 原版按 <c>PlatformUtil.GetPlayerNameRaw</c> 取名字 ⇒ 一桌 12 个人全都显示**同一个 Steam 昵称**，
/// 左侧玩家列表完全没法分辨（用户 2026-11-18：「这些玩家不能都用用户自己的 steamid」）。</para>
///
/// <para><b>为什么语言无关</b>：原来走 <c>LocalModText.RoleSlot</c> 的 `角色N` / `Player N` 是**调用时**
/// 按 <c>LocManager.Instance.Language</c> 分支，而战斗内左侧玩家列表的名牌是建节点那一刻的快照
/// （<c>NMultiplayerPlayerState._Ready</c> 只写一次）⇒ 切语言后会留着旧语言的称呼。</para>
///
/// <para><b>号位口径</b>：<c>N = 会话席位序 + 1</c>（<see cref="LocalSelfCoopContext.TryGetSlotIndex"/> →
/// <c>_localPlayerIds</c> 顺序，与 Tab 循环 / 小键盘切人**同序**）。两种称呼**共用同一套号位**：
/// 同号位的席位于是小键盘上是同一个键（例如 4 席 `Bot 1 / Bot 2 / Player 3 / Player 4`）。
/// **不要**按立绘左右或列表行号 —— 立绘会被重排（实测从左到右 = 角色4 | 2 | 3 | 1）；
/// 列表容器也是「我 + 其余」而非席位序。</para>
///
/// <para>本类只做字符串映射（纯逻辑、可单测）：**判定谁是本地席位 / 谁被瓦库托管**仍由调用方
/// （<see cref="LocalSelfCoopContext.TryGetSlotIndex"/> / <see cref="LocalSelfCoopContext.IsWakuuEnabled"/>）负责。</para>
/// </summary>
internal static class SeatDisplayNamePolicy
{
    /// <summary>
    /// 瓦库席位前缀（**语言无关**，大小写与空格固定）。
    /// 2026-11-18 用户口径：显示成 <c>Bot 1</c> / <c>Bot 2</c>（首字母大写 + 空格 + 号位），
    /// 不是全小写的 <c>bot1</c>。
    /// </summary>
    internal const string BotPrefix = "Bot ";

    /// <summary>真人席位前缀（语言无关）：显示成 <c>Player 1</c> / <c>Player 2</c>。</summary>
    internal const string PlayerPrefix = "Player ";

    /// <summary>某类席位的前缀。</summary>
    internal static string PrefixOf(SeatDisplayKind kind)
        => kind == SeatDisplayKind.Wakuu ? BotPrefix : PlayerPrefix;

    /// <summary>
    /// 会话席位下标（0 起）⇒ <c>Bot {N}</c> / <c>Player {N}</c>。
    /// 下界外（&lt; 0 = 不是本地席位）返回 null（调用方放行原版昵称）。
    /// </summary>
    internal static string? ResolveFromSlotIndex(int slotIndex, SeatDisplayKind kind)
    {
        return slotIndex < 0
            ? null
            : PrefixOf(kind) + (slotIndex + 1).ToString(CultureInfo.InvariantCulture);
    }
}
