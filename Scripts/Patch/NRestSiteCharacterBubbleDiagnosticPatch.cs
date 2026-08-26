using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 火堆选择气泡诊断（临时）：记录 ShowSelectedRestSiteOption / SetSelectingRestSiteOption
/// 的每次调用与确认节点实际状态，定位"多张升级时锻造图标不出现"的表现层断点。
/// </summary>
[HarmonyPatch]
internal static class NRestSiteCharacterBubbleDiagnosticPatch
{
    private static readonly FieldInfo? _confirmationField = AccessTools.Field(
        typeof(NRestSiteCharacter), "_selectedOptionConfirmation");

    [HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.ShowSelectedRestSiteOption))]
    [HarmonyPostfix]
    private static void ShowSelectedPostfix(NRestSiteCharacter __instance, RestSiteOption option)
    {
        try
        {
            object? confirmation = _confirmationField?.GetValue(__instance);
            string state = DescribeNode(confirmation as Control);
            LocalMultiControlLogger.Info(
                $"[气泡诊断] ShowSelected: owner={__instance.Player.NetId}, option={option.OptionId}, 确认节点={state}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[气泡诊断] ShowSelected 检查失败: {exception.Message}");
        }
    }

    [HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.SetSelectingRestSiteOption))]
    [HarmonyPostfix]
    private static void SetSelectingPostfix(NRestSiteCharacter __instance, RestSiteOption? option)
    {
        try
        {
            string optionText = option?.OptionId ?? "null";
            LocalMultiControlLogger.Info(
                $"[气泡诊断] SetSelecting: owner={__instance.Player.NetId}, option={optionText}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[气泡诊断] SetSelecting 检查失败: {exception.Message}");
        }
    }

    private static string DescribeNode(Control? node)
    {
        if (node == null)
        {
            return "null";
        }

        return $"{node.GetType().Name}, inTree={node.IsInsideTree()}, visible={node.Visible}, "
               + $"pos={node.GlobalPosition}, size={node.Size}, parent={(node.GetParent()?.Name.ToString() ?? "无")}";
    }
}
