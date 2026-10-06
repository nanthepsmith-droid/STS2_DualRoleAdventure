using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>选玩家选择器的一个候选（NetId + 展示标签）。</summary>
internal readonly struct LocalPlayerPickerEntry
{
    internal LocalPlayerPickerEntry(ulong netId, string label)
    {
        NetId = netId;
        Label = label;
    }

    internal ulong NetId { get; }

    internal string Label { get; }
}

/// <summary>
/// 局内「选玩家」选择器（瓦库四功能共用基建，2026-10-05 用户拍板新建）。
///
/// 背景：四个功能（炼化 / 净化 / 我们联合 / 地狱战神）都需要「先选一个瓦库」这一步，
/// 而 mod 全局**没有**任何局内选玩家的现成 UI（核验报告 §6.1）。核验原本建议"按瓦库各生成一条
/// 休息处选项"以回避造 UI，但用户拍板要一个**统一的选择器**覆盖四个功能，故本类提供该基建。
///
/// 实现：弹层本体已抽成通用 <see cref="LocalWakuuChoiceOverlay"/>（返回选中下标），
/// 本类只负责把下标映射回 <c>NetId</c> 与打日志。
///
/// 契约：
/// <list type="bullet">
/// <item>返回选中的 NetId；取消 / 无人可选 / 节点被外部移除（如换房间清场）一律返回 <c>null</c>；</item>
/// <item><b>绝不挂死</b>：弹层 <c>_ExitTree</c> 会补一次结果，调用方的 await 一定会回来；</item>
/// <item>调用方必须 <c>await</c>，并在 <c>null</c> 时把"这次没选"当正常取消（返回 false 让选项不被消费）。</item>
/// </list>
/// </summary>
internal static class LocalWakuuPlayerPicker
{
    /// <summary>
    /// 弹出选择器并等待真人选择。返回 null = 取消 / 不可用。
    /// </summary>
    internal static async Task<ulong?> PickAsync(string title, IReadOnlyList<LocalPlayerPickerEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        LocalMultiControlLogger.Info($"选玩家选择器已打开: title={title}, candidates={entries.Count}");
        int? index = await LocalWakuuChoiceOverlay.PickIndexAsync(
            title,
            entries.Select(entry => entry.Label).ToList());

        ulong? chosen = index.HasValue && index.Value >= 0 && index.Value < entries.Count
            ? entries[index.Value].NetId
            : null;
        LocalMultiControlLogger.Info(
            $"选玩家选择器已关闭: title={title}, chosen={(chosen.HasValue ? chosen.Value.ToString() : "null")}");
        return chosen;
    }
}
