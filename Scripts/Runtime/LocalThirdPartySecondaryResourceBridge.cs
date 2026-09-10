using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// RitsuLib「次级资源」战斗UI 的切角色补桥（第三方兼容；**全反射 + 缓存 + try/catch**，未装 RitsuLib 时静默跳过）。
///
/// 背景（实机问题）：RitsuLib 的次级资源计数器（LexNinja2 的**蕾克拉**等）挂在 `NCombatUi` 上。
/// 反编译 `STS2RitsuLib.Combat.SecondaryResources` 可知：
/// - `SecondaryResourceCombatUiStateTracker` **只在 `CombatStateChanged` 时**用 `LocalContext.GetMe(state)`
///   刷新归属（原版联机语义下「本地玩家」固定）；本 mod 切前台不触发该事件；
/// - `NSecondaryResourceCounter.Refresh(player)` 里 `_hasBeenMaterial` 是**粘滞标记**
///   （曾经有过该资源就常显），`Refresh` 不会复位它——只有公共 `Bind(player)` 在**绑定玩家变化时**才复位。
///
/// 于是出现两种错法：切到没有该资源的角色后计数器仍显示；或者入战时残留上一个角色的绑定。
/// 修法：每次重建战斗UI（切角色/入战）后
/// ① 调用 RitsuLib 官方入口 `SecondaryResourceUiRuntime.UpdateCombatUi(combatUi, player)`
///    （让挂载回调重算数值/位置/样式）；
/// ② 再对子树里每个 `NSecondaryResourceCounter` 显式 `Bind(player, autoRefresh: true)`，
///    确保粘滞标记随玩家变化被复位（否则只靠 ① 可能只是 `Refresh`，可见性不会更新）。
/// 反射失败/类型改名/未安装都只记一次日志，绝不影响本 mod 与战斗流程。
/// </summary>
internal static class LocalThirdPartySecondaryResourceBridge
{
    private const string RuntimeTypeName = "STS2RitsuLib.Combat.SecondaryResources.SecondaryResourceUiRuntime";
    private const string CounterTypeName = "STS2RitsuLib.Combat.SecondaryResources.NSecondaryResourceCounter";
    private const string UpdateMethodName = "UpdateCombatUi";
    private const string BindMethodName = "Bind";

    private static bool _resolved;
    private static MethodInfo? _updateCombatUi;
    private static Type? _counterType;
    private static MethodInfo? _bind;
    private static FieldInfo? _boundPlayerField;
    private static FieldInfo? _hasBeenMaterialField;
    private static bool _failureLogged;

    /// <summary>每个战斗UI实例上一次记录过的玩家（避免每次切人都刷日志）。</summary>
    private static readonly Dictionary<ulong, ulong> _lastLoggedPlayer = new();

    /// <summary>把第三方次级资源战斗UI同步到指定玩家（只在解析到 RitsuLib 时生效）。</summary>
    public static void RefreshCombatUiForPlayer(NCombatUi combatUi, Player player)
    {
        if (combatUi == null || player == null)
        {
            return;
        }

        if (!Resolve())
        {
            return;
        }

        try
        {
            // 归属统一按「前台玩家」：与 SecondaryResourceCombatUiOwnerPatch 的前缀保持同一事实来源，
            // 避免「官方刷新用 A、我们显式 Bind 用 B」互相打架（取不到前台才退回调用方给的玩家）。
            Player target = LocalMultiControlRuntime.TryGetForegroundPlayer() ?? player;

            // ① 官方刷新入口（挂载回调：数值/布局/样式/显隐策略）
            _updateCombatUi!.Invoke(null, new object[] { combatUi, target });

            // ② 显式重绑每个计数器（复位「有过就常显」的粘滞标记；只 Bind 同一个人是幂等的）
            StringBuilder detail = new();
            int count = 0;
            foreach (Godot.Node descendant in combatUi.FindChildren("*", recursive: true, owned: false))
            {
                if (_counterType == null || descendant.GetType() != _counterType)
                {
                    continue;
                }

                count++;
                string before = DescribeCounter(descendant);
                _bind?.Invoke(descendant, new object[] { target, true });
                if (count <= 4)
                {
                    detail.Append(detail.Length > 0 ? "; " : string.Empty)
                        .Append(before)
                        .Append(" → ")
                        .Append(DescribeCounter(descendant));
                }
            }

            if (_lastLoggedPlayer.TryGetValue(combatUi.GetInstanceId(), out ulong logged) && logged == target.NetId)
            {
                return;
            }

            _lastLoggedPlayer[combatUi.GetInstanceId()] = target.NetId;
            LocalMultiControlLogger.Info(
                $"第三方次级资源战斗UI已同步: player={target.NetId}, counters={count}, 详情=[{detail}]");
        }
        catch (Exception exception)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                LocalMultiControlLogger.Warn(
                    $"第三方次级资源战斗UI同步失败（已忽略，不影响战斗）: player={player.NetId}, error={exception.Message}");
            }
        }
    }

    /// <summary>诊断：计数器的绑定玩家 / 可见性 / 粘滞标记。</summary>
    private static string DescribeCounter(Godot.Node counter)
    {
        string bound = "(null)";
        string material = "?";
        try
        {
            if (_boundPlayerField?.GetValue(counter) is Player boundPlayer)
            {
                bound = boundPlayer.NetId.ToString();
            }

            if (_hasBeenMaterialField?.GetValue(counter) is bool hasMaterial)
            {
                material = hasMaterial.ToString();
            }
        }
        catch
        {
            // 诊断信息取不到就算了
        }

        bool visible = counter is Godot.CanvasItem canvasItem && canvasItem.Visible;
        return $"bound={bound}, material={material}, visible={visible}";
    }

    /// <summary>解析 RitsuLib 的公共刷新/绑定接口；找不到（未安装 RitsuLib）返回 false 并记住结果。</summary>
    private static bool Resolve()
    {
        if (_resolved)
        {
            return _updateCombatUi != null;
        }

        _resolved = true;
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? string.Empty;
                if (name.IndexOf("RitsuLib", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Type? runtimeType = assembly.GetType(RuntimeTypeName, throwOnError: false);
                MethodInfo? method = runtimeType?.GetMethod(
                    UpdateMethodName,
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Godot.Node), typeof(Player) },
                    modifiers: null);
                if (method == null)
                {
                    continue;
                }

                _updateCombatUi = method;
                _counterType = assembly.GetType(CounterTypeName, throwOnError: false);
                _bind = _counterType?.GetMethod(
                    BindMethodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: new[] { typeof(Player), typeof(bool) },
                    modifiers: null);
                _boundPlayerField = _counterType == null ? null : ReflectionSafe.Field(_counterType, "_boundPlayer");
                _hasBeenMaterialField = _counterType == null ? null : ReflectionSafe.Field(_counterType, "_hasBeenMaterial");

                // RitsuLib 加载晚于本 mod 的 PatchAll 时，在这里补挂归属校正前缀（r102）。
                LocalMultiControl.Scripts.Patch.SecondaryResourceCombatUiOwnerPatch.TryApplyLate();
                LocalMultiControlLogger.Info(
                    $"已接入第三方次级资源战斗UI刷新入口: {assembly.GetName().Name}, "
                    + $"bind={(_bind != null ? "ok" : "missing")}");
                return true;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"解析第三方次级资源刷新入口失败（已忽略）: {exception.Message}");
        }

        return false;
    }

    /// <summary>跨程序集取私有字段（拿不到返回 null，不抛异常）。</summary>
    private static class ReflectionSafe
    {
        public static FieldInfo? Field(Type type, string fieldName)
        {
            try
            {
                return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch
            {
                return null;
            }
        }
    }
}
