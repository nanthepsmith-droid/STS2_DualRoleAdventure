#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 局内「选遗物」选择器（炼化用；实现复用通用弹层 <see cref="LocalWakuuChoiceOverlay"/>）。
///
/// 背景：炼化要"从目标瓦库身上**自选若干件**遗物"（件数档位 1 / 3 / 5 / 不限，见
/// <see cref="WakuuRefinePolicy.ResolveRelicLimit"/>；不可选【永久低语耳环】/【瓦库形态】，
/// 由调用方过滤）。用户口径（2026-10-06）：只让拿 1 件则炼化收益太低，「完全不如让瓦库活着」，
/// 所以默认档是**不限**，同时保底给出 1 / 3 / 5 三档。
///
/// ⚠ 收编是**真拿走**（先 `RelicCmd.Remove` 摘掉目标身上那件、再授一件新的给玩家），
/// 移除校验在调用方（`RefineWakuuRestSiteOption.TransferRelicAsync`）—— 防"无限强力遗物"。
///
/// 契约：返回选中的遗物列表（**可以是空列表** = 确认了"一件都不拿"）；null = 取消 / 不可用；绝不挂死。
/// </summary>
internal static class LocalWakuuRelicPicker
{
    /// <summary>
    /// 弹出多选弹层并等待真人确认。<paramref name="maxSelect"/> = 最多能拿几件（按档位与候选数取小）。
    /// </summary>
    internal static async Task<IReadOnlyList<RelicModel>?> PickManyAsync(
        string title,
        IReadOnlyList<RelicModel> relics,
        int maxSelect)
    {
        if (relics == null || relics.Count == 0 || maxSelect <= 0)
        {
            return null;
        }

        List<string> labels = relics.Select(DescribeRelic).ToList();
        LocalMultiControlLogger.Info(
            $"选遗物选择器已打开: title={title}, candidates={relics.Count}, 可拿上限={maxSelect}");

        IReadOnlyList<int>? indexes = await LocalWakuuChoiceOverlay.PickIndexesAsync(
            title, labels, multiSelect: true, maxSelect: maxSelect);

        if (indexes == null)
        {
            LocalMultiControlLogger.Info($"选遗物选择器已关闭: title={title}, chosen=取消");
            return null;
        }

        List<RelicModel> chosen = indexes
            .Where(index => index >= 0 && index < relics.Count)
            .Select(index => relics[index])
            .ToList();
        LocalMultiControlLogger.Info(
            $"选遗物选择器已关闭: title={title}, chosen=[{string.Join(", ", chosen.Select(relic => relic.Id.Entry))}]");
        return chosen;
    }

    /// <summary>候选标签 = 遗物标题；标题取不到时退回模型 id（绝不让渲染异常炸掉弹层）。</summary>
    private static string DescribeRelic(RelicModel relic)
    {
        try
        {
            string? text = relic.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text!;
            }
        }
        catch (Exception)
        {
            // 标题本地化缺失（第三方遗物）时走 id 兜底。
        }

        return relic.Id.Entry;
    }
}
