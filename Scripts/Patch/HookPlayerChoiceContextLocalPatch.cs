using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// HookPlayerChoiceContext 在构造时用 LocalContext.NetId 作为 _localPlayerId。
/// 单人双角色模式下，若后台角色（如 player2）触发选牌/效果，而当前 LocalContext.NetId 指向另一角色，
/// 则 _gameAction.OwnerId != _localPlayerId，选择动作永远不会被本地入队执行（HookPlayerChoiceContext.cs:194/206），
/// 表现为回合开始/结束的效果卡住或被跳过。这里在构造后强制把 _localPlayerId 归属到该选择实际所属的角色。
/// </summary>
[HarmonyPatch(typeof(HookPlayerChoiceContext))]
internal static class HookPlayerChoiceContextLocalPatch
{
    private static void ForceLocalOwnerIfNeeded(HookPlayerChoiceContext context)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        Player? owner = context.Owner;
        if (owner == null || !LocalSelfCoopContext.LocalPlayerIds.Contains(owner.NetId))
        {
            return;
        }

        FieldInfo field = AccessTools.Field(typeof(HookPlayerChoiceContext), "_localPlayerId");
        if (field == null)
        {
            return;
        }

        ulong currentLocalPlayerId = (ulong)(field.GetValue(context) ?? 0UL);
        if (currentLocalPlayerId == owner.NetId)
        {
            return;
        }

        field.SetValue(context, owner.NetId);
        LocalMultiControlLogger.Warn(
            $"本地多控：hook 选择上下文 _localPlayerId 已强制归属到所选角色 {currentLocalPlayerId} -> {owner.NetId}，确保选择动作可本地入队执行。");
    }

    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(Player), typeof(ulong), typeof(GameActionType) })]
    [HarmonyPostfix]
    private static void PlayerCtorPostfix(HookPlayerChoiceContext __instance)
    {
        ForceLocalOwnerIfNeeded(__instance);
    }

    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(AbstractModel), typeof(ulong), typeof(ICombatState), typeof(GameActionType) })]
    [HarmonyPostfix]
    private static void ModelCombatCtorPostfix(HookPlayerChoiceContext __instance)
    {
        ForceLocalOwnerIfNeeded(__instance);
    }

    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(AbstractModel), typeof(Player), typeof(ulong), typeof(GameActionType) })]
    [HarmonyPostfix]
    private static void ModelPlayerCtorPostfix(HookPlayerChoiceContext __instance)
    {
        ForceLocalOwnerIfNeeded(__instance);
    }
}