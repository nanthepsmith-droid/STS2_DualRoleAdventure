#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 局内「选药水」选择器（炼化用；实现复用通用弹层 <see cref="LocalWakuuChoiceOverlay"/>）。
///
/// 语义（2026-10-06 用户要求「毕竟瓦库都死了，我不是想拿几瓶就拿几瓶」）：
/// **多选**——瓦库的药水全靠出来，真人想拿几瓶就拿几瓶（上限 = 自己药水栏的**空位数**，
/// 与"战斗奖励拿药水"的口径一致：没空位就拿不了），确认后**没拿的那些也一并移除**。
///
/// 契约：返回选中的药水列表（**可以是空列表** = 确认了"一瓶都不拿"）；null = 取消 / 不可用；绝不挂死。
/// </summary>
internal static class LocalWakuuPotionPicker
{
    /// <summary>
    /// 弹出多选弹层并等待真人确认。<paramref name="maxSelect"/> = 最多能拿几瓶（= 药水栏空位数）。
    /// </summary>
    internal static async Task<IReadOnlyList<PotionModel>?> PickManyAsync(
        string title,
        IReadOnlyList<PotionModel> potions,
        int maxSelect)
    {
        if (potions == null || potions.Count == 0 || maxSelect <= 0)
        {
            return null;
        }

        List<string> labels = potions.Select(DescribePotion).ToList();
        LocalMultiControlLogger.Info(
            $"选药水选择器已打开: title={title}, candidates={potions.Count}, 可拿上限={maxSelect}");

        IReadOnlyList<int>? indexes = await LocalWakuuChoiceOverlay.PickIndexesAsync(
            title, labels, multiSelect: true, maxSelect: maxSelect);

        if (indexes == null)
        {
            LocalMultiControlLogger.Info($"选药水选择器已关闭: title={title}, chosen=取消");
            return null;
        }

        List<PotionModel> chosen = indexes
            .Where(index => index >= 0 && index < potions.Count)
            .Select(index => potions[index])
            .ToList();
        LocalMultiControlLogger.Info(
            $"选药水选择器已关闭: title={title}, chosen=[{string.Join(", ", chosen.Select(potion => potion.Id.Entry))}]");
        return chosen;
    }

    /// <summary>候选标签 = 药水名；名字取不到时退回模型 id（绝不让渲染异常炸掉弹层）。</summary>
    private static string DescribePotion(PotionModel potion)
    {
        try
        {
            string? text = potion.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text!;
            }
        }
        catch (Exception)
        {
            // 第三方药水本地化缺失时走 id 兜底。
        }

        return potion.Id.Entry;
    }
}
