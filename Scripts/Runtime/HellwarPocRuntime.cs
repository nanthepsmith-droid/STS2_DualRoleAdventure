#nullable enable
using System;
using System.IO;
using System.Linq;
using Godot;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 **Phase 0 POC** 运行时：在战斗里塞一个"哑战神"（战斗内临时玩家），**只验证三件事** ——
/// ① 立绘 / 血条出现；② 回合能正常推进（临时席位被"自动补齐敌方回合就绪"认领）；③ 战斗结束能清理干净、
/// 且不会被 <c>LocalContext</c> 当成"我"。
///
/// ⚠ 这是 **POC 调试入口**：不含遗物【地狱耳环】/ 计数 / 休息处【分裂精神】 / 召唤选择 UI / 预设卡组。
/// **正式玩法走 <see cref="HellwarSummonCombatRuntime"/>**（`hellwarGod` 开关 + 回合开始选择 + 消耗计数）；
/// 本类只在环境变量 <c>LMC_HELLWAR_POC=1</c> 或标志文件存在时生效（**默认关**，不开时完全静默）。
///
/// 生成 / 接入 / 清理链**全部委托** <see cref="HellwarWarGodFactory"/>（与 Phase 2 共用同一实现，
/// 免得两条链各写一份、日后只修一处）；本类只留"开关解析 + 每场一场只生成一个"的策略。
/// </summary>
internal static class HellwarPocRuntime
{
    /// <summary>POC 日志前缀（判读锚点靠它区分 POC 与正式召唤两条入口）。</summary>
    private const string LogTag = "[地狱战神POC]";

    /// <summary>本场战斗的 <see cref="CombatState"/> 引用（换战斗 = 引用变 ⇒ 复位"本场已生成"标记）。</summary>
    private static CombatState? _lastState;

    /// <summary>本场战斗是否已生成过（POC = 每场战斗一次）。</summary>
    private static bool _spawnedThisCombat;

    /// <summary>开关解析结果缓存（进程级，改完需重启游戏才变）。</summary>
    private static bool? _resolvedEnabled;

    /// <summary>开关来源（打日志用）。</summary>
    private static string _enabledSource = "未开启";

    /// <summary>标志文件路径：`%APPDATA%\SlayTheSpire2\hellwar_poc.enabled`（**与 Steam 启动方式无关**）。</summary>
    internal static string FlagFilePath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2",
        HellwarPocPolicy.FlagFileName);

    /// <summary>POC 开关：**环境变量 <c>LMC_HELLWAR_POC</c>** 或 **标志文件存在** 任一为真即开。</summary>
    internal static bool Enabled => _resolvedEnabled ??= ResolveEnabled();

    /// <summary>当前开关来源（"环境变量 …" / "标志文件 …" / "未开启"）。</summary>
    internal static string EnabledSource => _enabledSource;

    private static bool ResolveEnabled()
    {
        bool byEnv = HellwarPocPolicy.ParseEnvFlag(
            System.Environment.GetEnvironmentVariable(HellwarPocPolicy.EnvVarName));

        string? fileContent = null;
        try
        {
            string path = FlagFilePath;
            if (File.Exists(path))
            {
                fileContent = File.ReadAllText(path);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 读标志文件失败，按未开启处理: error={exception.Message}");
        }

        bool byFile = HellwarPocPolicy.ParseFlagFile(fileContent);
        if (byEnv)
        {
            _enabledSource = $"环境变量 {HellwarPocPolicy.EnvVarName}";
        }
        else if (byFile)
        {
            _enabledSource = $"标志文件 {FlagFilePath}";
        }

        return byEnv || byFile;
    }

    /// <summary>
    /// 启动时把开关状态打进日志。
    /// ⚠ 这条**必须常打**（开关关着也打）：否则"变量没配对"与"功能没生效"在日志上完全一样，
    /// 实机根本分不清（POC 阶段就这么判读）。
    /// </summary>
    internal static void LogSwitchStateAtStartup()
    {
        if (Enabled)
        {
            LocalMultiControlLogger.Info(
                $"{LogTag} 开关状态: on（来源={EnabledSource}）"
                + " —— POC 已启用：本地多控会话的战斗里会生成「哑战神」"
                + "（正式玩法请改用设置页的「战灵召唤」开关）");
            return;
        }

        LocalMultiControlLogger.Info(
            $"{LogTag} 开关状态: off（环境变量 {HellwarPocPolicy.EnvVarName} 未设置/off；标志文件不存在）"
            + $" —— 要开 POC：新建空文件 {FlagFilePath}（删掉即关）");
    }

    /// <summary>
    /// 玩家回合开始时的入口（由既有补丁 <c>CombatManagerSetupPlayerTurnForegroundPatch</c> 调用 ⇒ **不新增 Harmony 目标**）。
    /// 不在这里直接生成：回合建立过程中往 <c>CombatState.Players</c> 加人会扰动回合循环 ⇒ 延后一帧。
    /// </summary>
    /// <returns>
    /// true = 「这是**临时席位**（战神）的回合，调用方应当**立即返回**」—— 战神没有手牌 UI、也永远不进前台。
    /// false = 正常席位（或功能没开）。
    /// </returns>
    internal static bool OnPlayerTurnStart(Player? player, string source)
    {
        if (!Enabled)
        {
            return false;
        }

        // 战神自己的回合：先让工厂替它收口（它没有 AI ⇒ 无人 ready ⇒ 回合链卡死）。
        if (HellwarWarGodFactory.TryRequestWarGodTurnEnd(player, LogTag, source))
        {
            return true;
        }

        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        if (state == null)
        {
            return false;
        }

        if (!ReferenceEquals(state, _lastState))
        {
            // 换了一场战斗 ⇒ 复位"本场已生成"（POC 每场最多一个）。
            _lastState = state;
            _spawnedThisCombat = false;
            LocalMultiControlLogger.Info(
                $"{LogTag} 进入新战斗，已复位本场生成标记: source={source}, "
                + $"已有战神={HellwarWarGodFactory.CountWarGods(state)} 个");
        }

        if (_spawnedThisCombat)
        {
            return false;
        }

        bool live = CombatManager.Instance?.IsInProgress ?? false;
        int alive = HellwarWarGodFactory.CountWarGods(state);
        if (!HellwarPocPolicy.ShouldSpawn(Enabled, LocalSelfCoopContext.IsEnabled, live, _spawnedThisCombat, alive))
        {
            LocalMultiControlLogger.Info(
                $"{LogTag} 本轮不生成: source={source}, player={player?.NetId}, "
                + $"reason={HellwarPocPolicy.DescribeSkipReason(Enabled, LocalSelfCoopContext.IsEnabled, live, _spawnedThisCombat, alive)}");
            return false;
        }

        Callable.From(() => SpawnDeferred(state, source)).CallDeferred();
        return false;
    }

    /// <summary>延后一帧执行真正的生成（此时回合已建立、战斗必然在进行中）。</summary>
    private static void SpawnDeferred(CombatState state, string source)
    {
        try
        {
            if (_spawnedThisCombat || HellwarWarGodFactory.CountWarGods(state) > 0)
            {
                LocalMultiControlLogger.Info($"{LogTag} 生成已跳过（延后期间已存在）: source={source}");
                return;
            }

            if (!(CombatManager.Instance?.IsInProgress ?? false))
            {
                LocalMultiControlLogger.Warn($"{LogTag} 生成失败：战斗已不在进行中: source={source}");
                return;
            }

            CharacterModel? character = ResolveCharacterForPoc(state);
            if (character == null)
            {
                LocalMultiControlLogger.Warn($"{LogTag} 生成失败：拿不到可用角色模型: source={source}");
                return;
            }

            // POC 沿用 D7「普通档」预设（不升级卡组、不加血）。
            if (!HellwarWarGodFactory.TrySpawn(
                    state, character, HellwarWarGodPresets.Common, LogTag, out _))
            {
                return;
            }

            _spawnedThisCombat = true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 生成异常（POC 失败，功能未实机通过）: source={source}, error={exception}");
        }
    }

    /// <summary>
    /// 战后奖励发放**之前**清理（由既有补丁 <c>CombatRoomOfferRoomEndRewardsPatch</c> 调用 ⇒ 不新增 Harmony 目标）。
    /// 必须早于奖励生成：原版会遍历 <c>CombatState.Players</c> 发奖（设计文档 §5.8 / §13.3.1）。
    /// </summary>
    internal static void CleanupBeforeRewards(CombatState? state)
    {
        if (!Enabled)
        {
            return;
        }

        int removed = HellwarWarGodFactory.CleanupCombat(state, LogTag);
        if (removed > 0)
        {
            _spawnedThisCombat = false;
        }
    }

    /// <summary>退局兜底（由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 调用）：清掉所有临时席位与标记。</summary>
    internal static void ResetForRun(string reason)
    {
        if (_lastState == null && !HellwarWarGodFactory.HasTrackedWarGods)
        {
            return;
        }

        HellwarWarGodFactory.ResetRun(LogTag, reason);
        _spawnedThisCombat = false;
        _lastState = null;
    }

    /// <summary>POC 用的角色皮：优先沿用本局已有玩家的角色（立绘资源必然已加载），兜底铁甲战士。</summary>
    private static CharacterModel? ResolveCharacterForPoc(CombatState state)
    {
        CharacterModel? existing = state.Players.FirstOrDefault()?.Character;
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 兜底角色模型取用失败: error={exception.Message}");
            return null;
        }
    }
}
