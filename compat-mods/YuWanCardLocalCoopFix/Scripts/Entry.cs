using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace YuWanCardLocalCoopFix;

/// <summary>
/// YuWanCardLocalCoopFix —— 让猪猪 mod（YuWanCard）的【队友帮付】遗物在**本地多控**下可用的独立补丁 mod。
///
/// <para><b>为什么单独一个 mod</b>：病灶一半在猪猪自己的联机假设上（整条链路都用原版联机的定向
/// 网络消息 + 单一本机玩家判定），一半在主 mod 的回环服务语义上（定向 <c>SendMessage</c> 没有对端）。
/// 修法必须改猪猪的私有方法 ⇒ 属于「主 mod ↔ 第三方交互」补丁，按仓库口径进 <c>compat-mods/</c>，
/// 不进主 mod。</para>
///
/// <para>全程**反射 + 逐个挂补丁、失败只 WARN**：猪猪缺席或更新改名都只是让本补丁退化成「不干预」。</para>
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    internal const string LogPrefix = "[YuWanCardLocalCoopFix]";

    /// <summary>商店【队友帮付】按钮的补丁类（按钮创建 + 点击流程都在这里）。</summary>
    private const string ShopPatchTypeName = "YuWanCard.Patches.TeammatePayShopPatch";

    /// <summary>猪猪自己的「选择队友」弹层（带上真实金币，我们直接复用它，不重画 UI）。</summary>
    private const string SelectionPopupTypeName = "YuWanCard.UI.TeammateSelectionPopup";

    /// <summary>仅用于日志自检：这条链的处理器类存在与否（不挂补丁）。</summary>
    private const string MessageHandlerTypeName = "YuWanCard.Multiplayer.TeammatePayMessageHandler";

    private static Harmony? _harmony;
    private static bool _installed;
    private static bool _waitingLogged;
    private static readonly List<string> Patched = new();
    private static readonly List<string> Missing = new();

    public static void Init()
    {
        try
        {
            _harmony = new Harmony("sts2.yuwancard.localcoopfix");

            // 猪猪是「壳 dll + 运行时变体 dll」结构（YuWanCard.dll / lib/<ver>/YuWanCard.Content.dll），
            // 本补丁的 Init 可能早于 Content.dll 加载，那时类型还找不到。
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            TryInstall("init");

            if (!_installed)
            {
                Log.Info($"{LogPrefix} 等待猪猪 mod（YuWanCard）程序集加载后再挂补丁。");
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
            if (string.IsNullOrEmpty(name) || name.IndexOf("YuWan", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            Log.Info($"{LogPrefix} 检测到猪猪 mod 程序集加载: {name}，尝试挂补丁。");
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

        Type? shopPatchType = FindType(ShopPatchTypeName);
        Type? popupType = FindType(SelectionPopupTypeName);
        if (shopPatchType == null || popupType == null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Log.Info($"{LogPrefix} 猪猪 mod 类型尚未就绪（source={source}: "
                    + $"shopPatch={(shopPatchType != null)}, popup={(popupType != null)}），暂不挂补丁。");
            }

            return;
        }

        TeammatePayLocalCompat.Install(_harmony, shopPatchType, popupType, Patched, Missing);

        _installed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

        string level = Missing.Count == 0 ? "OK" : "DEGRADED";
        Log.Info($"{LogPrefix} INIT_{level}: patched=[{string.Join(",", Patched)}], "
            + $"missing=[{string.Join(",", Missing)}], source={source}");

        // 只做存在性自检（不挂补丁）：这条链的处理器若改名，说明猪猪重构过，值得在日志里留一条。
        Type? handlerType = FindType(MessageHandlerTypeName);
        Log.Info($"{LogPrefix} 自检: 消息处理器={(handlerType != null ? "在" : "缺（猪猪可能已重构）")}, "
            + $"可选队友弹层={(popupType != null ? "在" : "缺")}");
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
