using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 火堆选择气泡诊断（临时）：记录 ShowSelectedRestSiteOption / SetSelectingRestSiteOption
/// 的每次调用与确认节点实际状态，定位"多张升级时锻造图标不出现"的表现层断点。
///
/// 2026-09-30 增强（BUG-27 实机反馈"气泡不见了"）：旧版只看**节点自身** `Visible`，
/// 而 `Visible=true` 但**祖先链把它藏了**（容器隐藏 / modulate 变 0）在日志里看不出来。
/// 现在每次选中后 +800ms 再做一次**存活自检**：逐个角色打「思考气泡 / 已选确认节点」的
/// `IsVisibleInTree` + 透明度，只在"自身可见但整链不可见"时才额外打可见性链（避免刷屏）。
/// </summary>
[HarmonyPatch]
internal static class NRestSiteCharacterBubbleDiagnosticPatch
{
    /// <summary>存活自检延迟：够走完房间的 AfterSelectingOptionAsync + 我们刷新休息区 UI。</summary>
    private const int SurvivalCheckDelayMs = 800;

    private static readonly FieldInfo? _confirmationField = AccessTools.Field(
        typeof(NRestSiteCharacter), "_selectedOptionConfirmation");

    private static readonly FieldInfo? _thoughtBubbleField = AccessTools.Field(
        typeof(NRestSiteCharacter), "_thoughtBubbleVfx");

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
            // 延迟自检：气泡"建了但看不见"多半发生在这一帧之后（房间刷新 / 选项清空 / 变灰）。
            TaskHelper.RunSafely(LogBubbleSurvivalAfterDelayAsync(__instance, option.OptionId));
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

    private static async Task LogBubbleSurvivalAfterDelayAsync(NRestSiteCharacter character, string optionId)
    {
        try
        {
            await Task.Delay(SurvivalCheckDelayMs);
            if (character == null || !GodotObject.IsInstanceValid(character))
            {
                LocalMultiControlLogger.Info(
                    $"[气泡诊断] 气泡存活自检: option={optionId}, 角色节点已释放（换房 / 退场属正常）");
                return;
            }

            ulong owner = character.Player?.NetId ?? 0UL;
            Godot.Node? confirmation = _confirmationField?.GetValue(character) as Godot.Node;
            Godot.Node? thought = _thoughtBubbleField?.GetValue(character) as Godot.Node;
            LocalMultiControlLogger.Info(
                $"[气泡诊断] 气泡存活自检(+{SurvivalCheckDelayMs}ms): owner={owner}, option={optionId}, "
                + $"确认节点={DescribeBubble(confirmation)}, 思考气泡={DescribeBubble(thought)}");
            DumpChainIfHidden(confirmation, $"确认节点-{owner}");
            DumpChainIfHidden(thought, $"思考气泡-{owner}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[气泡诊断] 气泡存活自检失败: {exception.Message}");
        }
    }

    /// <summary>「自身 visible 但整链不可见」才打可见性链 —— 这正是旧版日志的诊断盲区。</summary>
    private static void DumpChainIfHidden(Godot.Node? node, string source)
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (node is not CanvasItem canvasItem)
        {
            LocalMultiControlLogger.Info(
                $"[气泡诊断] 可见性诊断(休息区气泡-{source}): {node.GetType().Name} 不是 CanvasItem，无法判整链可见性");
            return;
        }

        if (canvasItem.IsVisibleInTree())
        {
            return;
        }

        if (node is Control control)
        {
            LocalOverlayDiagnostics.DumpControlVisibilityChain(control, $"休息区气泡-{source}");
            return;
        }

        LocalMultiControlLogger.Info(
            $"[气泡诊断] 可见性诊断(休息区气泡-{source}): {DescribeBubble(node)}（非 Control 节点，只看自身与父链名字）"
            + $", parent={(node.GetParent()?.Name.ToString() ?? "无")}");
    }

    private static string DescribeBubble(Godot.Node? node)
    {
        if (node == null)
        {
            return "null";
        }

        if (!GodotObject.IsInstanceValid(node))
        {
            return "已释放";
        }

        string basic = $"{node.GetType().Name}, inTree={node.IsInsideTree()}";
        if (node is Control control)
        {
            basic += $", visible={control.Visible}, visibleInTree={control.IsVisibleInTree()}, "
                     + $"a={control.Modulate.A:F2}, pos={control.GlobalPosition}, size={control.Size}, "
                     + $"parent={(control.GetParent()?.Name.ToString() ?? "无")}";
        }
        else if (node is CanvasItem item)
        {
            basic += $", visible={item.Visible}, visibleInTree={item.IsVisibleInTree()}, a={item.Modulate.A:F2}, "
                     + $"parent={(item.GetParent()?.Name.ToString() ?? "无")}";
        }

        return basic;
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
