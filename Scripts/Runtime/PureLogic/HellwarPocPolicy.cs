#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 战灵召唤 **Phase 0 POC**（战斗内临时玩家「战神」）的**纯逻辑**：临时席位号段 / 环境开关解析 / 是否该生成。
///
/// 设计口径（设计文档 #21 §7.1 拍板附录，D2 / D3）：
/// - 临时席位用**独立高位段** <see cref="TempSeatBase"/>（`0x1000` 起），与正式席位 `1..12` 天然隔离
///   ⇒ 所有 `Take(DesiredLocalPlayerCount)` 的"正式席位"用法**看不见**它，
///   而 `SeatRegistry.IsLocalSeat`（看整张表）**看得见**它 ⇒ 回合自动就绪会认它
///   （不认就会让 `PlayersReadyToBeginEnemyTurn.Count == State.Players.Count` 这条门卡死）。
/// - 也因为这个隔离，它天然不会撞 Steam ID、不会被存档标记（save tag）采集。
///
/// 本类**不碰任何 Godot / 游戏类型**，便于单测（见 `tests/LocalMultiControl.Tests/HellwarPocPolicyTests.cs`）。
/// </summary>
internal static class HellwarPocPolicy
{
    /// <summary>POC 开关的环境变量名（值为 `1` / `true` / `on` 视为开）。</summary>
    internal const string EnvVarName = "LMC_HELLWAR_POC";

    /// <summary>
    /// POC 开关的**标志文件**名（放在 `%APPDATA%\SlayTheSpire2\` 下）。
    /// 为什么还要这个：环境变量在"从 Steam 启动"的链路上很难生效
    /// （游戏是 Steam 的子进程，只继承 Steam 启动那一刻的环境；而直启 exe 会被游戏的 Steamworks 初始化挡掉）。
    /// 标志文件与启动方式无关 ⇒ **建文件即开、删文件即关**。
    /// </summary>
    internal const string FlagFileName = "hellwar_poc.enabled";

    /// <summary>临时席位号段起点（含）。</summary>
    internal const ulong TempSeatBase = 0x1000UL;

    /// <summary>临时席位号段终点（不含）。</summary>
    internal const ulong TempSeatLimit = 0x2000UL;

    /// <summary>是不是临时席位（POC 哨兵 id 段）。</summary>
    internal static bool IsTemporarySeat(ulong netId) => netId >= TempSeatBase && netId < TempSeatLimit;

    /// <summary>第 <paramref name="index"/> 个临时席位 id（**只有 POC 那个"每场固定一个"的场景**能用它：
    /// 正式召唤要一次多个 / 会有空洞 ⇒ 必须用 <see cref="ResolveFreeSeatId"/>）。</summary>
    internal static ulong SeatIdAt(int index) => TempSeatBase + (ulong)Math.Max(0, index);

    /// <summary>
    /// 在临时席位段里取**第一个没被占用的**席位号（<paramref name="occupied"/> = 已占用的确切 id 集合）。
    ///
    /// ⚠ 别拿 <see cref="IsTemporarySeat"/> 当"这个号被占了吗"用 —— 它是**范围谓词**
    /// （整个 `0x1000..0x1FFF` 都返回 true）⇒ 逐个试号时会**全部**判成"已占用"，
    /// 循环走到底只能兜底返回段起点 ⇒ **每个战神都拿到同一个 id**（r245 实机踩中，
    /// 表现为"第二个战神不会被打"：一批按 NetId 取数的路径永远解析到第一个）。
    /// 本方法用**确切集合成员判断**，把这条坑钉成可单测的纯逻辑。
    /// </summary>
    internal static ulong ResolveFreeSeatId(IReadOnlyCollection<ulong>? occupied)
    {
        for (ulong offset = 0; offset < TempSeatLimit - TempSeatBase; offset++)
        {
            ulong candidate = TempSeatBase + offset;
            if (occupied == null || !occupied.Contains(candidate))
            {
                return candidate;
            }
        }

        // 号段耗尽（16 屏位，现实中只会出现在"没清理干净"的病态场景）⇒ 兜底复用段起点。
        return TempSeatBase;
    }

    /// <summary>解析环境变量开关（`1` / `true` / `on`，大小写不敏感；其余/空白 = 关）。</summary>
    internal static bool ParseEnvFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim();
        return value == "1"
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析标志文件（`%APPDATA%\SlayTheSpire2\hellwar_poc.enabled`）：
    /// - <paramref name="rawContent"/> 为 <c>null</c> = **文件不存在** ⇒ 关；
    /// - 空文件 / 空白 = **开**（"新建一个空文件"就是最省事的开法）；
    /// - 内容为 `0` / `false` / `off` ⇒ 显式关（便于用同一个文件表达"临时关掉"）；
    /// - 其余内容 ⇒ 开。
    /// </summary>
    internal static bool ParseFlagFile(string? rawContent)
    {
        if (rawContent == null)
        {
            return false;
        }

        string value = rawContent.Trim();
        if (value.Length == 0)
        {
            return true;
        }

        return !(value == "0"
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>是否该在本场战斗生成 POC 战神（**全部**条件都满足才生成）。</summary>
    internal static bool ShouldSpawn(
        bool pocEnabled,
        bool sessionEnabled,
        bool combatLive,
        bool alreadySpawnedThisCombat,
        int aliveWarGods)
        => pocEnabled && sessionEnabled && combatLive && !alreadySpawnedThisCombat && aliveWarGods <= 0;

    /// <summary>跳过生成时的一句话原因（打日志用 ⇒ 实机能一眼看出卡在哪一步）。</summary>
    internal static string DescribeSkipReason(
        bool pocEnabled,
        bool sessionEnabled,
        bool combatLive,
        bool alreadySpawnedThisCombat,
        int aliveWarGods)
    {
        if (!pocEnabled) { return "poc-disabled"; }
        if (!sessionEnabled) { return "session-disabled"; }
        if (!combatLive) { return "combat-not-live"; }
        if (alreadySpawnedThisCombat) { return "already-spawned-this-combat"; }
        if (aliveWarGods > 0) { return "war-god-already-alive"; }
        return "spawn";
    }
}
