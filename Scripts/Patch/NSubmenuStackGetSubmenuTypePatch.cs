using System;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.UI;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 把瓦库托管设置页注册进原版子菜单栈（BaseLib 的 InjectModConfigSubmenuTypePatch 同款思路）：
/// 原版 GetSubmenuType(Type) 对未知类型直接抛 ArgumentException，
/// 这里用 Prefix 拦截：请求的是本 mod 的子菜单类型时，按栈懒建一个实例挂到该栈下并返回。
/// 主菜单（NMainMenuSubmenuStack）与局内暂停菜单（NRunSubmenuStack）各拦一份。
/// </summary>
[HarmonyPatch(typeof(NMainMenuSubmenuStack), "GetSubmenuType", new Type[] { typeof(Type) })]
public static class MainMenuStackRegisterWakuuConfigSubmenuPatch
{
    public static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        return SubmenuStackRegistry.TryGetOrCreate(__instance, type, ref __result);
    }
}

[HarmonyPatch(typeof(NRunSubmenuStack), "GetSubmenuType", new Type[] { typeof(Type) })]
public static class RunStackRegisterWakuuConfigSubmenuPatch
{
    public static bool Prefix(NRunSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        return SubmenuStackRegistry.TryGetOrCreate(__instance, type, ref __result);
    }
}

/// <summary>
/// 每个子菜单栈缓存一个瓦库托管设置页实例；以栈节点为键的弱表，栈销毁时条目自动失效。
/// </summary>
internal static class SubmenuStackRegistry
{
    private static readonly ConditionalWeakTable<Control, LocalWakuuConfigSubmenu> _instances = new();

    /// <returns>false 表示命中本 mod 类型并已写回 __result（拦截原方法）；true 表示放行原版逻辑。</returns>
    public static bool TryGetOrCreate(Control stack, Type type, ref NSubmenu result)
    {
        if (type != typeof(LocalWakuuConfigSubmenu))
        {
            return true;
        }

        if (!_instances.TryGetValue(stack, out LocalWakuuConfigSubmenu? submenu))
        {
            submenu = new LocalWakuuConfigSubmenu
            {
                Visible = false,
            };
            stack.AddChildSafely(submenu);
            _instances.Add(stack, submenu);
            LocalMultiControlLogger.Info($"已在 {stack.GetType().Name} 下创建瓦库托管设置页实例");
        }

        result = submenu;
        return false;
    }
}
