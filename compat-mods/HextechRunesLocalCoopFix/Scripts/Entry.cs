using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// HextechRunesLocalCoopFix —— 让海克斯大乱斗（HextechRunes）的「逐席位符文选择」在本地多控下可用的独立补丁 mod。
///
/// 为什么单独一个 mod：病灶在海克斯自己的「本机玩家」判定上（它按原版联机假设只有一个本机玩家），
/// 修法必须改它的私有方法 —— 属于第三方专属兼容代码，按仓库惯例不塞进主 mod。
/// 这里全程**反射 + 逐个挂补丁、失败只 WARN**：海克斯缺席或更新改名都只是让本补丁退化成「不干预」。
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    internal const string LogPrefix = "[HextechRunesLocalCoopFix]";

    private const string CoordinatorTypeName = "HextechRunes.HextechRuneSelectionCoordinator";
    private const string ScreenTypeName = "HextechRunes.HextechRuneSelectionScreen";

    private static Harmony? _harmony;
    private static bool _installed;
    private static bool _waitingLogged;
    private static readonly List<string> Patched = new();
    private static readonly List<string> Missing = new();

    public static void Init()
    {
        try
        {
            _harmony = new Harmony("sts2.hextechrunes.localcoopfix");

            // 先挂监听再试一次：海克斯是「壳 dll + 运行时变体 dll」结构，
            // 本补丁的 Init 可能早于它的变体程序集加载，那时类型还找不到。
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            TryInstall("init");

            if (!_installed)
            {
                Log.Info($"{LogPrefix} 等待海克斯（HextechRunes）程序集加载后再挂补丁。");
            }
        }
        catch (Exception exception)
        {
            // 第三方兼容补丁绝不允许连累游戏启动：只记日志，不抛。
            Log.Error($"{LogPrefix} INIT_ERROR: {exception}");
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (_installed)
        {
            return;
        }

        try
        {
            string? name = args.LoadedAssembly.GetName().Name;
            if (string.IsNullOrEmpty(name) || name.IndexOf("Hextech", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            Log.Info($"{LogPrefix} 检测到海克斯程序集加载: {name}，尝试挂补丁。");
            TryInstall($"assembly-load:{name}");
        }
        catch
        {
            // 监听回调里的异常一律吞掉（不能影响游戏自身的程序集加载流程）。
        }
    }

    private static void TryInstall(string source)
    {
        if (_installed || _harmony == null)
        {
            return;
        }

        Type? coordinatorType = FindType(CoordinatorTypeName);
        Type? screenType = FindType(ScreenTypeName);
        if (coordinatorType == null || screenType == null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Log.Info($"{LogPrefix} 符文选择类型尚未就绪（source={source}: "
                    + $"coordinator={(coordinatorType != null)}, screen={(screenType != null)}），暂不挂补丁。");
            }

            return;
        }

        HextechRuneSelectionCompatPatch.Install(_harmony, coordinatorType, screenType, Patched, Missing);
        _installed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

        string level = Missing.Count == 0 ? "OK" : "DEGRADED";
        Log.Info($"{LogPrefix} INIT_{level}: patched=[{string.Join(",", Patched)}], "
            + $"missing=[{string.Join(",", Missing)}], source={source}");

        // ⑤ 瓦库（后台托管）席位的符文界面自动作答：解析界面私有字段，没解析到只让这一环退化。
        bool autoAnswerReady = WakuuRuneAutoAnswer.Install(screenType);
        Log.Info($"{LogPrefix} 瓦库席位符文界面自动作答: "
            + $"{(autoAnswerReady ? "已就绪（后台托管瓦库席位由我们代选，真人席位照旧自己点）" : "未就绪（字段解析失败，真人手点）")}。");
    }

    internal static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
                // 个别程序集 GetType 会抛，跳过继续找。
            }
        }

        return null;
    }
}
