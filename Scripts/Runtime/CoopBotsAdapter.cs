using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 第三方 mod「Co-op Bots / 联机机器人」（Harmony id <c>cn.xiwa.sts2.coopbots</c>，程序集 <c>CoopBots</c>，
/// 分析基线 v0.39.0）的反射适配器。
///
/// 设计约束（与 <see cref="WakuuSkadaAdapter"/> 同款，对应《Co-op_Bots联机队友兼容可行性分析》§1.3 / §4.4）：
/// <list type="bullet">
/// <item>**可选依赖**：不做编译期引用（第三方 dll 不在 <c>Compile Remove</c> 之外的引用面内），全部走反射；
/// 未安装 / 改名 / 版本漂移一律「不可用」，**永不判致命**（AGENTS.md §9）。</item>
/// <item>**反射集中在本文件**：第三方结构变化只影响这里，业务侧只认 <see cref="TrySetHandedOver"/> /
/// <see cref="Drives"/> 这几个语义方法。</item>
/// <item>**接管真实席位**：只用 <c>AutoPilot.Set(netId, true)</c>（席位保留原 netId / Player / 角色，
/// 运行 schema 不变）；绝不触碰 <c>LobbyBotService.Add</c> 的合成 Bot 路径（回环下不可用，见分析 §2.3）。</item>
/// <item>**只改驱动归属**：不读也不改它的内部状态，只调它自己公开的 <c>public static</c> 接口。</item>
/// </list>
///
/// 为什么需要重探：本 mod 的 ModInitializer 可能**早于** CoopBots 的程序集加载，
/// 启动探测会失败（与 SkadaHelper 的实测结论一致），所以 <see cref="EnsureReady"/> 支持强制重探，
/// 由 <see cref="CoopBotsSeatRuntime"/> 在每次进局时再探一次。
/// </summary>
internal static class CoopBotsAdapter
{
    private const string AutoPilotTypeName = "CoopBots.AutoPilot";
    private const string DrivesMethodName = "Drives";
    private const string IsAutopilotedMethodName = "IsAutopiloted";
    private const string SetMethodName = "Set";
    private const string ClearMethodName = "Clear";
    private const string HandedOverPropertyName = "HandedOver";

    /// <summary>类型探测失败后的重试间隔（与 SkadaHelper 适配器同口径：探到即短路，查不到才等冷却）。</summary>
    private const long ProbeRetryIntervalMs = 2_000;

    private static readonly object Sync = new object();

    private static bool _probed;
    private static bool _ready;
    private static long _nextProbeAtMs;
    private static string _describe = "未探测";

    private static MethodInfo? _drives;
    private static MethodInfo? _isAutopiloted;
    private static MethodInfo? _set;
    private static MethodInfo? _clear;
    private static MethodInfo? _handedOverGetter;

    /// <summary>适配器是否可用（只用于日志诊断；业务判定一律看各方法返回值）。</summary>
    public static bool IsReady
    {
        get
        {
            lock (Sync)
            {
                return _ready;
            }
        }
    }

    /// <summary>已识别到的 Co-op Bots 程序集描述（日志用；未就绪时说明原因）。</summary>
    public static string Describe
    {
        get
        {
            lock (Sync)
            {
                return _describe;
            }
        }
    }

    /// <summary>
    /// 启动探测（Entry 阶段 3）：只打一条状态日志，任何失败都不抛、不影响 mod 初始化。
    /// </summary>
    public static void Probe()
    {
        try
        {
            if (!EnsureReady(force: true))
            {
                LocalMultiControlLogger.Info(
                    "CoopBots 适配器未就绪：未安装「Co-op Bots / 联机机器人」或其程序集晚于本 mod 加载"
                    + "（已验证属常见情况，每次进局会再探一次；席位回落真人/瓦库，不致命）。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"CoopBots 启动探测异常，联机机器人接管不可用: {exception.Message}");
        }
    }

    /// <summary>
    /// 探测并缓存 <c>CoopBots.AutoPilot</c> 的反射成员。
    /// force=true 立即探测（启动 / 每次进局）；force=false 受冷却间隔约束（类型一旦识别成功即短路）。
    /// </summary>
    public static bool EnsureReady(bool force)
    {
        lock (Sync)
        {
            if (_ready)
            {
                return true;
            }

            long now = Environment.TickCount64;
            if (!force && _probed && now < _nextProbeAtMs)
            {
                return false;
            }

            _probed = true;
            _nextProbeAtMs = now + ProbeRetryIntervalMs;

            try
            {
                Type? autoPilotType = AccessTools.TypeByName(AutoPilotTypeName);
                if (autoPilotType == null)
                {
                    return false;
                }

                _drives = AccessTools.Method(autoPilotType, DrivesMethodName, new[] { typeof(ulong) });
                _isAutopiloted = AccessTools.Method(autoPilotType, IsAutopilotedMethodName, new[] { typeof(ulong) });
                _set = AccessTools.Method(autoPilotType, SetMethodName, new[] { typeof(ulong), typeof(bool) });
                _clear = AccessTools.Method(autoPilotType, ClearMethodName, Type.EmptyTypes);
                _handedOverGetter = AccessTools.Property(autoPilotType, HandedOverPropertyName)?.GetGetMethod(nonPublic: false);

                _describe = DescribeAssembly(autoPilotType);
                _ready = _drives != null && _set != null;
                if (!_ready)
                {
                    LocalMultiControlLogger.Warn(
                        $"CoopBots 已加载但接口签名不匹配（疑似版本漂移）: {_describe}；"
                        + "联机机器人接管不可用，瓦库/真人路径不受影响。");
                    return false;
                }

                // 迟到的 Co-op Bots：R1 商店 ACK 旁路补丁在这里补挂（PatchAll 时目标还不存在）。
                Patch.CoopBotsShopAckPatch.TryApplyLate();
                LocalMultiControlLogger.Info(
                    $"CoopBots 适配器就绪: {_describe}（可用 AutoPilot.Set 逐席接管真实席位）。");
                return true;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"CoopBots 类型探测异常，联机机器人接管不可用: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 接管 / 释放一个席位（<c>AutoPilot.Set(netId, handedOver)</c>）。
    /// 未就绪、调用抛异常一律返回 false（调用方据此打日志并保持原驱动）。
    /// </summary>
    public static bool TrySetHandedOver(ulong netId, bool handedOver)
    {
        if (netId == 0 || !EnsureReady(force: false))
        {
            return false;
        }

        // AutoPilot.Set 的返回类型是 void：没有抛异常即视为「已送达」。
        return InvokeVoid(_set, new object?[] { netId, handedOver });
    }

    /// <summary>该席位是否由机器逻辑作答（<c>AutoPilot.Drives</c>，含合成 Bot）。未就绪返回 false。</summary>
    public static bool Drives(ulong netId)
    {
        if (netId == 0 || !EnsureReady(force: false))
        {
            return false;
        }

        return InvokeBool(_drives, netId);
    }

    /// <summary>该席位是否「真人席位被接管」（<c>AutoPilot.IsAutopiloted</c>）。未就绪返回 false。</summary>
    public static bool IsAutopiloted(ulong netId)
    {
        if (netId == 0 || !EnsureReady(force: false))
        {
            return false;
        }

        return InvokeBool(_isAutopiloted, netId);
    }

    /// <summary>当前被接管的席位集合快照（<c>AutoPilot.HandedOver</c>）。读不到返回空表。</summary>
    public static List<ulong> HandedOverSnapshot()
    {
        List<ulong> seats = new List<ulong>();
        if (!EnsureReady(force: false) || _handedOverGetter == null)
        {
            return seats;
        }

        try
        {
            object? raw = _handedOverGetter.Invoke(null, null);
            if (raw is System.Collections.IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item is ulong seat && seat != 0)
                    {
                        seats.Add(seat);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"CoopBots 读取 HandedOver 失败（已忽略）: {Unwrap(exception).Message}");
        }

        return seats;
    }

    /// <summary>清空全部接管记录（<c>AutoPilot.Clear</c>）。会连带清掉玩家自己在 CB 面板上的接管，慎用。</summary>
    public static bool Clear()
    {
        if (!EnsureReady(force: false) || _clear == null)
        {
            return false;
        }

        return InvokeVoid(_clear, null);
    }

    /// <summary>调 `bool X(ulong netId)` 形状的静态方法（取不到/异常一律 false）。</summary>
    private static bool InvokeBool(MethodInfo? method, ulong netId)
    {
        if (method == null)
        {
            return false;
        }

        try
        {
            return method.Invoke(null, new object?[] { netId }) is bool value && value;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"CoopBots 接口调用异常: {method.Name}, error={Unwrap(exception).Message}");
            return false;
        }
    }

    /// <summary>调返回 void 的静态方法（<c>Set</c> / <c>Clear</c>）：**没有异常即算成功**。</summary>
    private static bool InvokeVoid(MethodInfo? method, object?[]? args)
    {
        if (method == null)
        {
            return false;
        }

        try
        {
            method.Invoke(null, args);
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"CoopBots 接口调用异常: {method.Name}, error={Unwrap(exception).Message}");
            return false;
        }
    }

    /// <summary>
    /// 程序集描述（程序集名 + 版本 + dll 文件名）。诊断用：确认加载的是工坊里哪一版。
    /// 取不到的部分直接省略，绝不让诊断信息本身抛异常。
    /// </summary>
    private static string DescribeAssembly(Type autoPilotType)
    {
        try
        {
            Assembly assembly = autoPilotType.Assembly;
            string name = assembly.GetName().Name ?? "CoopBots";
            string version = assembly.GetName().Version?.ToString() ?? "未知版本";
            string fileName = string.Empty;
            try
            {
                fileName = System.IO.Path.GetFileName(assembly.Location);
            }
            catch (Exception)
            {
                fileName = string.Empty;
            }

            return string.IsNullOrEmpty(fileName)
                ? $"assembly={name}, version={version}"
                : $"assembly={name}, version={version}, file={fileName}";
        }
        catch (Exception)
        {
            return "assembly=CoopBots（描述信息读取失败）";
        }
    }

    private static Exception Unwrap(Exception exception)
    {
        return exception is TargetInvocationException { InnerException: not null } outer
            ? outer.InnerException ?? exception
            : exception;
    }
}
