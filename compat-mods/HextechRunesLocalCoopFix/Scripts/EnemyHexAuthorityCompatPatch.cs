using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 海克斯「**敌人 hex 调整面板**」在本地多控下的**归属**补丁（本 mod 的 ⑥ / ⑦）。
///
/// <para><b>病灶</b>（用户 2026-10-08 实测：「每层给敌方 buff 可以 roll 一次，roll 完点确定却还是之前那个」）：
/// 海克斯每幕开局 roll「本幕怪物 hex」，并让**权威席位**在符文界面里顺手 reroll / 移除。
/// 它判「这一屏要不要给可调面板」用的是 <c>IsLocalPlayer(runManager, syncContext.AuthorityPlayer)</c>
/// —— 本 mod 的补丁 ① 把**每个本地席位**都判成「本机玩家」⇒ 两席的符文界面**都**拿到可调面板，
/// 而「最终值是哪一份」由 <c>SendEnemyHexAdjustment</c> 的 <c>syncContext.FinalSent</c> 决定：
/// **谁先作答，谁的 final 生效**（第二个作答那屏的 reroll 被静默丢弃；它显示的又是自己那份，
/// 因为原版只在「面板不可调」的那一屏才挂远端镜像）。</para>
///
/// <para><b>做法</b>（把「只有权威席位可调」这条原版语义在单进程里还原出来，不接远端镜像）：</para>
/// <list type="number">
/// <item>⑥ <c>CreateEnemyHexAdjustmentOptionsForSelection</c> **后缀**：这一屏的席位 ≠ **可调席位**时，
///   把刚建好的 options 改成**只读**（<c>ControlsEnabled=false</c> + <c>RerollFunc/Changed/ScreenCreated</c>
///   全清空）⇒ 面板还在（能看到本幕 hex），但没有 reroll 按钮、也不会往同步里发任何东西。
///   「可调席位」= 权威席位**若是真人**就是它，否则是**第一个真人本地席位**（见 <see cref="ResolveControllerSeat"/>：
///   权威席位常常就是【后台托管的瓦库席位】，把可调权给它等于真人在自己那屏只能看不能动 —— v1.2.1 修的就是这条）；</item>
/// <item>⑦ <c>CompleteLocalEnemyHexAdjustmentSync</c> **前缀**：只读那一屏**不发 final**
///   （否则它会用「初始值」把权威席位刚调好的结果覆盖掉）——判据走界面自己的私有字段
///   <c>_enemyHexControlsEnabled</c>（**实例准确**，不依赖任何"归属猜测"）。</item>
/// </list>
///
/// <para><b>刻意不做远端镜像</b>：原版「非权威客户端只读镜像」靠
/// <c>ReceiveEnemyHexAdjustments</c> 等一条**远端**消息。本地多控是单进程回环，权威席位是自己发自己的选择，
/// 原版不回环给自己的客户端 ⇒ 那个 <c>RemoteReceiveTask</c> 很可能永远等不到 ⇒ **软锁**
/// （比现在这个显示 bug 严重得多）。所以只读屏只显示**创建时的那份快照**，不追随后续改动。</para>
///
/// <para><b>失败姿态</b>：两处补丁 + 3 个反射成员（<c>_enemyHexControlsEnabled</c> /
/// <c>CurrentMonsterHexSlots</c> / <c>RemoteReceiveTask</c>）**要么全上、要么全不上**
/// （<see cref="_ready"/>）——半挂状态会造出"只读屏照样发 final"这种更糟的组合；
/// 海克斯改名 / 更新 ⇒ 本文件整体退化为「不干预」，只留一条 WARN。</para>
/// </summary>
internal static class EnemyHexAuthorityCompatPatch
{
    private const int ForSelectionParameterCount = 10;
    private const int CompleteSyncParameterCount = 3;

    private static readonly HashSet<string> LoggedKeys = new();

    /// <summary>整套是否武装完毕：两处补丁都挂上 + 所有反射成员都解析到。</summary>
    private static bool _ready;

    /// <summary><c>CreateEnemyHexAdjustmentOptionsForSelection</c> 里 <c>syncContext</c> / <c>selection</c> 的实参下标。</summary>
    private static int _syncContextArgIndex = -1;
    private static int _selectionArgIndex = -1;

    /// <summary>要改成只读的 4 个 options 属性（<c>init</c> 访问器可用反射调，改不动就整体退化）。</summary>
    private static readonly List<PropertyInfo> ReadOnlyProperties = new();

    /// <summary><c>HextechRuneSelectionScreen._enemyHexControlsEnabled</c>：这一屏的面板**是不是可调**。</summary>
    private static FieldInfo? _controlsEnabledField;

    /// <summary><c>HextechRuneSelectionScreen.CurrentMonsterHexSlots</c>：这一屏有没有敌人 hex 槽位。</summary>
    private static PropertyInfo? _slotsProperty;

    /// <summary><c>EnemyHexAdjustmentSyncContext.RemoteReceiveTask</c>：原版"等远端镜像"分支的信号。</summary>
    private static PropertyInfo? _remoteReceiveTaskProperty;

    internal static void Install(
        Harmony harmony,
        Type coordinatorType,
        Type screenType,
        List<string> patched,
        List<string> missing)
    {
        Reset();

        MethodInfo? forSelection = FindMethod(coordinatorType, "CreateEnemyHexAdjustmentOptionsForSelection", ForSelectionParameterCount);
        MethodInfo? completeSync = FindMethod(coordinatorType, "CompleteLocalEnemyHexAdjustmentSync", CompleteSyncParameterCount);
        if (forSelection == null || completeSync == null)
        {
            missing.Add($"敌人hex面板归属（CreateEnemyHexAdjustmentOptionsForSelection={forSelection != null}, "
                + $"CompleteLocalEnemyHexAdjustmentSync={completeSync != null}）");
            Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板归属补丁：目标方法没找齐，跳过"
                + "（保留旧行为 = 两席都能调，「后作答那屏的 reroll 不生效」）。");
            return;
        }

        _syncContextArgIndex = IndexOfParameter(forSelection, "syncContext");
        _selectionArgIndex = IndexOfParameter(forSelection, "selection");
        if (_syncContextArgIndex < 0 || _selectionArgIndex < 0)
        {
            missing.Add("敌人hex面板归属（参数名 syncContext / selection 对不上）");
            Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板归属补丁：参数名对不上（海克斯可能已更新），跳过。");
            return;
        }

        Type optionsType = forSelection.ReturnType;
        foreach (string memberName in new[] { "ControlsEnabled", "RerollFunc", "Changed", "ScreenCreated" })
        {
            PropertyInfo? property = FindPropertyInHierarchy(optionsType, memberName);
            if (property == null)
            {
                missing.Add($"敌人hex面板归属（{optionsType.Name}.{memberName} 未找到）");
                Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板归属补丁：{optionsType.Name}.{memberName} 未找到，跳过整套。");
                return;
            }

            ReadOnlyProperties.Add(property);
        }

        _controlsEnabledField = FindFieldInHierarchy(screenType, "_enemyHexControlsEnabled");
        _slotsProperty = FindPropertyInHierarchy(screenType, "CurrentMonsterHexSlots");
        _remoteReceiveTaskProperty = FindPropertyInHierarchy(completeSync.GetParameters()[1].ParameterType, "RemoteReceiveTask");
        if (_controlsEnabledField == null || _slotsProperty == null || _remoteReceiveTaskProperty == null)
        {
            missing.Add($"敌人hex面板归属（界面字段 _enemyHexControlsEnabled={_controlsEnabledField != null}, "
                + $"CurrentMonsterHexSlots={_slotsProperty != null}, RemoteReceiveTask={_remoteReceiveTaskProperty != null}）");
            Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板归属补丁：界面字段没解析齐，跳过整套"
                + "（半挂状态会造出「只读屏照样发 final」这种更糟的组合）。");
            return;
        }

        try
        {
            harmony.Patch(
                forSelection,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(EnemyHexAuthorityCompatPatch), nameof(EnemyHexOptionsPostfix))));
            patched.Add($"{coordinatorType.Name}.CreateEnemyHexAdjustmentOptionsForSelection");
        }
        catch (Exception exception)
        {
            missing.Add("HextechRuneSelectionCoordinator.CreateEnemyHexAdjustmentOptionsForSelection");
            Log.Warn($"{Entry.LogPrefix} 挂补丁失败: CreateEnemyHexAdjustmentOptionsForSelection: "
                + $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        try
        {
            harmony.Patch(
                completeSync,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(EnemyHexAuthorityCompatPatch), nameof(CompleteLocalEnemyHexSyncPrefix))));
            patched.Add($"{coordinatorType.Name}.CompleteLocalEnemyHexAdjustmentSync");
        }
        catch (Exception exception)
        {
            missing.Add("HextechRuneSelectionCoordinator.CompleteLocalEnemyHexAdjustmentSync");
            Log.Warn($"{Entry.LogPrefix} 挂补丁失败: CompleteLocalEnemyHexAdjustmentSync: "
                + $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        _ready = true;
        Log.Info($"{Entry.LogPrefix} 敌人 hex 调整面板：已按「只有权威席位可调」收口"
            + $"（非权威席位的面板变只读、不发 final；**不做**远端镜像）。"
            + $"syncContext 实参下标={_syncContextArgIndex}, selection 实参下标={_selectionArgIndex}");
    }

    /// <summary>
    /// ⑥ 后缀：这一屏不是权威席位 ⇒ 把它那份 options 改成**只读**
    /// （面板照旧出现，但没有 reroll、也不往同步里发任何调整）。
    /// </summary>
    private static void EnemyHexOptionsPostfix(object[] __args, object? __result)
    {
        if (!_ready || __result == null || !MainModBridge.IsLocalCoopRun())
        {
            return;
        }

        try
        {
            // 首次命中打一条：这条**有**就说明后缀真的挂上了（没有它 + 也没有只读化日志 = 这一环被 JIT 内联跳过，报维护者）。
            LogOnce("postfix-alive", "敌人 hex 调整面板归属：后缀已生效（用于确认补丁没被内联跳过）。");

            if (_syncContextArgIndex >= __args.Length || _selectionArgIndex >= __args.Length)
            {
                return;
            }

            object? syncContext = __args[_syncContextArgIndex];
            object? selection = __args[_selectionArgIndex];
            if (syncContext == null || selection == null)
            {
                return;
            }

            Player? authority = GetPlayerMember(syncContext, "AuthorityPlayer");
            Player? seat = GetPlayerMember(selection, "Player");
            if (seat == null)
            {
                return;
            }

            Player? controller = ResolveControllerSeat(seat, authority);
            if (controller == null || seat.NetId == controller.NetId)
            {
                return; // 这一屏就是「可调的那一屏」⇒ 原样
            }

            foreach (PropertyInfo property in ReadOnlyProperties)
            {
                // ControlsEnabled ⇒ false；RerollFunc / Changed / ScreenCreated ⇒ null
                // （界面构造时 `_enemyHexControlsEnabled = ControlsEnabled || RerollFunc != null`，两个都要清）。
                property.SetValue(__result, property.PropertyType == typeof(bool) ? false : null);
            }

            LogOnce(
                $"readonly#{seat.NetId}",
                $"敌人 hex 调整面板：非可调席位改为**只读**（只有可调席位能 reroll / 移除，避免「先作答那屏说了算」）: "
                + $"席位={MainModBridge.SeatLabelById(seat.NetId)}, 可调席位={MainModBridge.SeatLabelById(controller.NetId)}, "
                + $"原权威={MainModBridge.SeatLabelById(authority?.NetId ?? 0UL)}");
        }
        catch (Exception exception)
        {
            // 改不动就整体退化：**绝不能留半改状态**（那会造出「面板可调但不发送」的怪屏）。
            _ready = false;
            Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板只读化失败 ⇒ 本补丁整体退化为「不干预」: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// ⑦ 前缀：**只读**那一屏不发 final（否则它会用创建时的初始值把权威席位刚调好的结果覆盖掉）。
    /// 判据 = 界面自己的 <c>_enemyHexControlsEnabled</c>（实例准确）；没有面板的屏 / 权威屏 / 还有远端镜像要等的屏一律放行。
    /// </summary>
    private static bool CompleteLocalEnemyHexSyncPrefix(object? syncContext, object? screen, ref Task __result)
    {
        if (!_ready || syncContext == null || screen == null || !MainModBridge.IsLocalCoopRun())
        {
            return true;
        }

        try
        {
            LogOnce("prefix-alive", "敌人 hex 调整面板归属：final 收口前缀已生效（用于确认补丁没被内联跳过）。");

            if (_controlsEnabledField!.GetValue(screen) is not bool controlsEnabled || controlsEnabled)
            {
                return true; // 这一屏的面板可调（= 权威席位）⇒ 走原版：final 就该由它发
            }

            if (!HasEnemyHexSlots(screen))
            {
                return true; // 这一屏压根没有敌人 hex 面板（锻造 / 自选池 / 提示屏）⇒ 不在本次收口范围内
            }

            if (_remoteReceiveTaskProperty!.GetValue(syncContext) != null)
            {
                return true; // 原版「等远端镜像」那条路还有东西要等 ⇒ 交回原版，别把等待吞掉
            }

            __result = Task.CompletedTask;
            LogOnce(
                "no-final",
                "敌人 hex 调整面板：只读那一屏不发 final（否则会用初始值覆盖权威席位刚调好的结果）。");
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 敌人 hex 面板 final 收口判断失败，按原版继续: "
                + $"{exception.GetType().Name}: {exception.Message}");
            return true;
        }
    }

    /// <summary>
    /// 「这一场战斗里，敌人 hex 面板该由谁可调」——原版语义是**权威席位**，但本地多控有两处失真：
    /// <list type="number">
    /// <item>两席都判「本机」⇒ 谁都可调 ⇒ 先作答者胜（本补丁要修的就是这条）；</item>
    /// <item><b>权威席位可能就是【后台托管的瓦库席位】</b>：它根本不是"会去点这个面板的人"，
    ///   而且它的符文界面由 ⑤ 自动作答（1.2s）**必定早于真人** ⇒ 若把可调权给它，
    ///   真人那一屏就只有预览没按钮（r238 实测：用户「海克斯我也不能 roll 了」，日志
    ///   `席位=角色1, 权威=角色2`，而角色2 正是被 ⑤ 自动作答的托管瓦库）。</item>
    /// </list>
    /// ⇒ 规则：<b>权威席位是真人就给它</b>；否则给<b>第一个真人本地席位</b>（按 run 内顺序，稳定）；
    /// 一个真人都没有（全托管）就退回权威席位 = 不干预。
    /// </summary>
    private static Player? ResolveControllerSeat(Player seat, Player? authority)
    {
        if (authority != null
            && MainModBridge.IsLocalSeat(authority.NetId)
            && !MainModBridge.IsAutomatedWakuuSeat(authority.NetId))
        {
            return authority;
        }

        foreach (Player candidate in seat.RunState.Players)
        {
            if (MainModBridge.IsLocalSeat(candidate.NetId) && !MainModBridge.IsAutomatedWakuuSeat(candidate.NetId))
            {
                return candidate;
            }
        }

        return authority;
    }

    private static bool HasEnemyHexSlots(object screen)
    {
        object? slots = _slotsProperty!.GetValue(screen);
        return slots switch
        {
            null => false,
            ICollection collection => collection.Count > 0,
            IEnumerable enumerable => enumerable.Cast<object?>().Any(),
            _ => false,
        };
    }

    private static Player? GetPlayerMember(object instance, string memberName)
    {
        Type type = instance.GetType();
        object? value = FindPropertyInHierarchy(type, memberName)?.GetValue(instance)
            ?? FindFieldInHierarchy(type, memberName)?.GetValue(instance);
        return value as Player;
    }

    private static int IndexOfParameter(MethodInfo method, string parameterName)
    {
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, parameterName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Reset()
    {
        _ready = false;
        _syncContextArgIndex = -1;
        _selectionArgIndex = -1;
        ReadOnlyProperties.Clear();
        _controlsEnabledField = null;
        _slotsProperty = null;
        _remoteReceiveTaskProperty = null;
    }

    private static MethodInfo? FindMethod(Type type, string methodName, int parameterCount)
    {
        List<MethodInfo> candidates = type
            .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == methodName)
            .ToList();

        return candidates.FirstOrDefault(m => m.GetParameters().Length == parameterCount)
            ?? (candidates.Count == 1 ? candidates[0] : null);
    }

    private static FieldInfo? FindFieldInHierarchy(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? FindPropertyInHierarchy(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static void LogOnce(string key, string message)
    {
        lock (LoggedKeys)
        {
            if (!LoggedKeys.Add(key))
            {
                return;
            }
        }

        Log.Info($"{Entry.LogPrefix} {message}");
    }
}
