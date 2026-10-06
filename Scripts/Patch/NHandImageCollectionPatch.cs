using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NHandImageCollection), "UpdateHandVisibility")]
internal static class NHandImageCollectionUpdateVisibilityPatch
{
    private static readonly object ProbeSync = new object();

    private static MethodInfo? _getStateForPlayer;

    private static FieldInfo? _netScreenTypeField;

    private static bool _probeResolved;

    private static bool _probeWarned;

    [HarmonyPrefix]
    private static bool Prefix(NHandImageCollection __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        PeerInputSynchronizer? synchronizer =
            AccessTools.Field(typeof(NHandImageCollection), "_synchronizer")?.GetValue(__instance) as PeerInputSynchronizer;
        List<NHandImage>? hands =
            AccessTools.Field(typeof(NHandImageCollection), "_hands")?.GetValue(__instance) as List<NHandImage>;
        if (synchronizer == null || hands == null)
        {
            return false;
        }

        // R3 B2：「本机当前屏幕属于谁」= 回环上下文（逐字等价于旧写法 `LocalContext.NetId ?? 0UL`）。
        bool hasLocalScreen = TryGetScreenType(synchronizer, LocalSeatSource.ContextSeatId() ?? 0UL, out NetScreenType localScreenType);
        foreach (NHandImage hand in hands)
        {
            NetScreenType handScreenType = default;
            bool hasHandScreen = TryGetScreenType(synchronizer, hand.Player.NetId, out handScreenType);
            if (!hasHandScreen)
            {
                // 正式版已移除 IsSinglePlayerOrFakeMultiplayer，这里直接回退到本地当前屏幕状态，
                // 避免宝箱 UI 初始化阶段因缺失远端输入状态而中断整套界面创建。
                if (!hasLocalScreen)
                {
                    hand.Visible = false;
                    continue;
                }

                handScreenType = localScreenType;
            }

            bool shouldShow = hasLocalScreen &&
                handScreenType == NetScreenType.SharedRelicPicking &&
                localScreenType == NetScreenType.SharedRelicPicking;
            if (!hand.Visible && shouldShow)
            {
                hand.AnimateIn();
            }

            hand.Visible = shouldShow;
        }

        bool cursorShown = !hasLocalScreen || localScreenType != NetScreenType.SharedRelicPicking;
        NGame.Instance?.CursorManager.SetCursorShown(cursorShown);
        return false;
    }

    /// <summary>
    /// **只读**探测该玩家的屏幕类型（r223 修）。
    ///
    /// 为什么不能直接用 <c>synchronizer.GetScreenType(playerId)</c>：它的实现是
    /// <c>GetOrCreateStateForPlayer(playerId).netScreenType</c> —— **不存在就会创建**，
    /// 创建时触发它自己的 <c>StateAdded</c> 事件 ⇒ `NHandImageCollection.OnInputStateAdded` ⇒
    /// `AddHand(playerId)`；而宝箱界面 <c>Initialize</c> 已经给每个玩家加过手势 ⇒ 游戏自己打
    /// `[ERROR] Tried to add hand for player … twice!`（实机：炼化过的死席位 …327 没有输入状态，
    /// 我们这次探测就把它"造"出来了）。
    ///
    /// 所以这里走**私有只读**路径 <c>GetStateForPlayer(ulong)</c>（不存在返回 null）：拿不到状态就
    /// 按"无屏幕类型"处理（调用方有既有的回退：用本机当前屏幕类型推断）——**绝不产生副作用**。
    /// 反射解析失败（游戏更新改名）时同样返回 false 并只打一条 WARN：退化为"不显示手势"（纯观感），
    /// 换来的是**不会**再触发那条游戏 ERROR。
    /// </summary>
    private static bool TryGetScreenType(PeerInputSynchronizer synchronizer, ulong playerId, out NetScreenType screenType)
    {
        screenType = default;
        if (!EnsureReadOnlyProbe())
        {
            return false;
        }

        try
        {
            object? state = _getStateForPlayer!.Invoke(synchronizer, new object[] { playerId });
            if (state == null)
            {
                return false;
            }

            if (_netScreenTypeField!.GetValue(state) is NetScreenType value)
            {
                screenType = value;
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            if (!_probeWarned)
            {
                _probeWarned = true;
                LocalMultiControlLogger.Warn($"宝箱手势层只读探测输入状态失败（已退化为不显示）: {exception.Message}");
            }

            return false;
        }
    }

    /// <summary>解析私有只读入口与屏幕类型字段（只做一次）。</summary>
    private static bool EnsureReadOnlyProbe()
    {
        if (_probeResolved)
        {
            return _getStateForPlayer != null && _netScreenTypeField != null;
        }

        lock (ProbeSync)
        {
            if (_probeResolved)
            {
                return _getStateForPlayer != null && _netScreenTypeField != null;
            }

            _getStateForPlayer = AccessTools.Method(
                typeof(PeerInputSynchronizer), "GetStateForPlayer", new[] { typeof(ulong) });
            // 状态类型直接从方法返回类型拿（`PeerInputState` 在哪个命名空间随版本漂移，别写死 typeof）。
            _netScreenTypeField = _getStateForPlayer == null
                ? null
                : AccessTools.Field(_getStateForPlayer.ReturnType, "netScreenType");
            _probeResolved = true;

            if (_getStateForPlayer == null || _netScreenTypeField == null)
            {
                LocalMultiControlLogger.Warn(
                    "宝箱手势层找不到只读输入状态入口（游戏更新？）: "
                    + $"GetStateForPlayer={_getStateForPlayer != null}, netScreenType={_netScreenTypeField != null}"
                    + " ⇒ 退化为不显示手势（避免用会创建状态的 GetScreenType 触发游戏 ERROR）。");
                return false;
            }

            return true;
        }
    }
}
