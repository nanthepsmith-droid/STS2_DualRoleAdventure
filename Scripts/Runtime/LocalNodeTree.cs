using System;
using System.Collections.Generic;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 节点树遍历的共用设施（R2 去重复）。
///
/// 背景：深度优先枚举子树这段代码曾在 4 个补丁里各写一份（逐字相同）：
/// `RestSitePatch` / `NRemoteLobbyPlayerSwitchPatch` / `NMultiplayerPlayerStateSwitchPatch` /
/// `NDailyRunLocalSelfCoopPatch`。其中三处还挂在 `_Process` 上每帧跑
/// （战斗玩家状态条 / 大厅席位卡 / 每日页席位卡），于是「同一棵子树每帧全量扫描」被复制了三份。
/// 这里收成一处实现；需要跨帧复用**稳定节点引用**的调用点走 <see cref="LocalIdLabelCache"/>。
/// ⚠ 写后读（补建后立刻判「已存在吗」）与每帧布局**必须**调用这里的实时遍历 —— 不能套缓存。
///
/// 注意：本文件依赖 Godot（`Node`），因此**不进 `PureLogic`** —— 按 ADR 的分层规则，
/// `PureLogic` 只放零 Godot 依赖的纯函数。
/// </summary>
internal static class LocalNodeTree
{
    /// <summary>深度优先枚举 <paramref name="root"/> 的全部后代（不含自身）。</summary>
    internal static IEnumerable<Node> EnumerateDescendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node nested in EnumerateDescendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// 按玩家 id 文本在子树里找游戏原生「id 标签」（首个命中）；
    /// <paramref name="excludedNames"/> 里的节点跳过（大厅页要跳过我们自己注入的提示标签）。
    /// 这是**实时**遍历；需要跨帧复用的调用点请走 <see cref="LocalIdLabelCache"/>。
    /// </summary>
    internal static Label? FindIdLabel(Node root, string playerIdText, StringName[]? excludedNames = null)
    {
        foreach (Node node in EnumerateDescendants(root))
        {
            if (node is not Label label || IsExcluded(label, excludedNames))
            {
                continue;
            }

            if (LooksLikeIdLabel(label, playerIdText))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    /// 游戏原生「玩家 id 标签」的判定：节点名里带 `id`，或文本里含该玩家的 id。
    /// （两个调用点原本各自写了一遍同样的判断，R2 收到这里。）
    /// </summary>
    internal static bool LooksLikeIdLabel(Label label, string playerIdText)
    {
        string name = label.Name.ToString();
        string text = label.Text ?? string.Empty;
        bool nameLooksLikeId = name.Contains("id", StringComparison.OrdinalIgnoreCase);
        bool textContainsPlayerId = text.Contains(playerIdText, StringComparison.Ordinal);
        return nameLooksLikeId || textContainsPlayerId;
    }

    private static bool IsExcluded(Label label, StringName[]? excludedNames)
    {
        if (excludedNames == null)
        {
            return false;
        }

        foreach (StringName excluded in excludedNames)
        {
            if (label.Name == excluded)
            {
                return true;
            }
        }

        return false;
    }
}
