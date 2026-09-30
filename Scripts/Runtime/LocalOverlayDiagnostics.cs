using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「奖励弹层被遮挡 / 读档转场黑幕」这类**画面可见性**问题的检查与诊断
/// （R4 第三刀：从 `LocalMultiControlRuntime` 抽出的独立职责）。
///
/// 为什么单独立一个单元：这一族的症状都是"逻辑在跑、画面不对"——
/// ① 奖励界面弹出时被打开的地图界面遮住（`NOverlayStack` 会把弹层 `Visible=false`）；
/// ② 读档重放期间 `NTransition` 全屏黑幕还没 `FadeIn`，此时若同步等待玩家操作就永久黑屏；
/// ③ 一时看不出是谁被遮了，需要把控件可见性链 / 场景里的转场节点打出来。
/// 它们与"席位身份""切人"无关，只跟 Godot 节点树打交道，所以独立成一处，Runtime 只保留调用编排。
///
/// 全部方法**只读 + 只打日志**（唯一副作用是"奖励弹出前顺手关掉还开着的地图界面"），
/// 且都自带 try/catch ⇒ 诊断失败不会影响主流程（与原实现逐字一致）。
/// </summary>
internal static class LocalOverlayDiagnostics
{
    /// <summary>
    /// 确保奖励界面弹出时没有被地图/角色面板遮挡。
    /// NOverlayStack.Push 在 StackIsCovered（NMapScreen 打开或 NCapstoneContainer 占用）时
    /// 会立即调用 screen.AfterOverlayHidden() 把弹层 Visible=false——读档重放路径会先恢复
    /// 地图/界面状态，导致战后奖励界面"黑屏"（弹层在栈上但不可见，仅模组自有按钮可见）。
    /// </summary>
    internal static void EnsureOverlayNotCoveredForRewards(string source)
    {
        try
        {
            NMapScreen? map = NMapScreen.Instance;
            bool mapOpen = map != null && map.IsOpen;
            bool capstoneInUse = NCapstoneContainer.Instance?.InUse ?? false;
            LocalMultiControlLogger.Info(
                $"奖励遮挡检查({source}): mapOpen={mapOpen}, capstoneInUse={capstoneInUse}");
            if (mapOpen && map != null)
            {
                map.Close(animateOut: false);
                LocalMultiControlLogger.Info($"奖励弹出前关闭处于打开状态的地图界面: source={source}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"奖励遮挡检查异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 当前是否处于读档重放的转场黑幕遮盖期。
    /// 原版读档链路：StartRun 先 FadeOut 变黑，随后 LoadRun 内部进入 PreFinishedRoom
    /// 并重放战后奖励，全部完成后才 FadeIn 亮屏。若此期间任何代码同步等待玩家
    /// 操作（如我们的合并奖励界面 await），FadeIn 将永远无法执行，表现为永久黑屏。
    /// 原版自身对此的处理是 reward.Offer() fire-and-forget 不等待。
    /// </summary>
    internal static bool IsLoadReplayTransitionCovering()
    {
        try
        {
            NTransition? transition = NGame.Instance?.Transition;
            return transition != null && transition.Visible && transition.InTransition;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 打印某个控件从自身到场景根的可见性链（Visible/透明度/位置/尺寸），
    /// 用于定位"逻辑存在但渲染不可见"的黑屏类问题。
    /// </summary>
    internal static void DumpControlVisibilityChain(Control? control, string source)
    {
        try
        {
            if (control == null)
            {
                LocalMultiControlLogger.Info($"可见性诊断({source}): 控件为 null");
                return;
            }

            List<string> chain = new();
            Node node = control;
            int depth = 0;
            while (node != null && depth < 24)
            {
                string entry = node switch
                {
                    Control c => $"{node.GetType().Name}[{node.Name}] Visible={c.Visible} ModulateA={c.Modulate.A:F2} Scale={c.Scale} Pos={c.Position} Size={c.Size}",
                    CanvasLayer l => $"{node.GetType().Name}[{node.Name}] Layer={l.Layer} Visible={l.Visible}",
                    _ => $"{node.GetType().Name}[{node.Name}]"
                };
                chain.Add(entry);
                node = node.GetParent();
                depth++;
            }

            LocalMultiControlLogger.Info($"可见性诊断({source}): {string.Join(" <- ", chain)}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"可见性诊断异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 扫描整棵场景树里的转场黑幕（NTransition）与加载遮罩（NLoadingOverlay）节点，
    /// 输出其可见状态——用于诊断"逻辑正常但画面全黑"的读档问题：
    /// 若 FadeOut 后配对的 FadeIn 未执行，NTransition 这个全屏 ColorRect 会永久盖住画面。
    /// </summary>
    internal static void DumpTransitionOverlayState(string source)
    {
        try
        {
            Node? game = NGame.Instance;
            if (game == null)
            {
                LocalMultiControlLogger.Info($"转场扫描({source}): NGame 不存在");
                return;
            }

            List<string> found = new();
            CollectTransitionNodes(game, found, source);
            if (found.Count == 0)
            {
                LocalMultiControlLogger.Info($"转场扫描({source}): 场景中无 NTransition/NLoadingOverlay 节点");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"转场扫描异常(已忽略): {exception.Message}");
        }
    }

    private static void CollectTransitionNodes(Node node, List<string> found, string source)
    {
        string typeName = node.GetType().Name;
        if (typeName == "NTransition" && node is ColorRect rect)
        {
            float simpleAlpha = -1f;
            float gradientAlpha = -1f;
            foreach (Node child in node.GetChildren())
            {
                if (child is Control cc && cc.Name == "SimpleTransition")
                {
                    simpleAlpha = cc.Modulate.A;
                }
                else if (child is Control gc && gc.Name == "GradientTransition")
                {
                    gradientAlpha = gc.Modulate.A;
                }
            }

            found.Add("hit");
            LocalMultiControlLogger.Info(
                $"转场扫描({source}): NTransition Visible={rect.Visible} ModulateA={rect.Modulate.A:F2} "
                + $"SimpleA={simpleAlpha:F2} GradientA={gradientAlpha:F2} Size={rect.Size} InTree={node.IsInsideTree()}");
        }
        else if (typeName == "NLoadingOverlay" && node is Control loading)
        {
            found.Add("hit");
            LocalMultiControlLogger.Info(
                $"转场扫描({source}): NLoadingOverlay Visible={loading.Visible} ModulateA={loading.Modulate.A:F2} Size={loading.Size}");
        }

        foreach (Node child in node.GetChildren())
        {
            CollectTransitionNodes(child, found, source);
        }
    }
}
