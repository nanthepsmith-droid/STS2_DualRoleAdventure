using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 在**海克斯符文候选卡**上画「选取率角标」（v1.7.0，用户 2026-10-21 点名）。
///
/// <para><b>为什么</b>：瓦库按抓率自动作答，但真人看不到每张卡的历史选取率 ⇒ 无法判断它为什么这么挑。
/// 这里把 <see cref="RunePickStats"/> 的账本画成每个候选左上角的小角标
/// （`33% (0/1)`；没记录 = `—`），口径与主 mod 的统计角标一致（只显示我们自己的本地数据）。</para>
///
/// <para><b>挂在哪儿</b>：候选按钮 = 界面私有 <c>_holders</c>（复用 <see cref="WakuuRuneAutoAnswer"/> 的读取），
/// 角标是按钮的子 <see cref="Label"/>（`MouseFilter=Ignore` 不挡点击）。界面 reroll 时会
/// <c>RebuildCards()</c> 重建按钮 ⇒ 同时挂它的 postfix（反射找不到就只靠 0.5s 轮询兜底）。</para>
///
/// <para>自选池屏（锻造选择：一屏一大堆符文）**不画**（不参与统计，也没有"3 选 1"语义）。
/// 开关：<c>HRLF_RUNE_BADGE=0</c>。任何失败只 WARN，绝不影响界面本身。</para>
/// </summary>
internal static class RunePickRateBadge
{
    private const string BadgeName = "LMC_RunePickRateBadge";

    /// <summary>轮询兜底间隔（秒）：reroll 重建、界面晚半拍建好都能补上。</summary>
    private const float RefreshIntervalSeconds = 0.5f;

    /// <summary>最多轮询多少次（约 2 分钟；屏关掉就自己停）。</summary>
    private const int MaxRefreshAttempts = 240;

    internal static readonly bool Enabled =
        !string.Equals(System.Environment.GetEnvironmentVariable("HRLF_RUNE_BADGE"), "0", StringComparison.Ordinal);

    private static bool _warned;
    private static int _loggedAttachCount;

    private static readonly Color ColorHigh = new(0.55f, 1.0f, 0.55f);
    private static readonly Color ColorMid = new(1.0f, 0.85f, 0.45f);
    private static readonly Color ColorLow = new(1.0f, 0.55f, 0.55f);
    private static readonly Color ColorUnknown = new(0.78f, 0.78f, 0.78f);

    /// <summary>挂 <c>RebuildCards</c> 的 postfix（reroll 后立刻补角标）；找不到只记 missing，靠轮询。</summary>
    internal static void Install(Harmony harmony, Type screenType, List<string> patched, List<string> missing)
    {
        if (!Enabled)
        {
            Log.Info($"{Entry.LogPrefix} 符文选取率角标已关闭（HRLF_RUNE_BADGE=0）。");
            return;
        }

        try
        {
            MethodInfo? rebuild = null;
            foreach (MethodInfo candidate in screenType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (candidate.Name == "RebuildCards" && candidate.GetParameters().Length == 0)
                {
                    rebuild = candidate;
                    break;
                }
            }

            if (rebuild == null)
            {
                missing.Add($"{screenType.Name}.RebuildCards(选取率角标即时刷新)");
                Log.Info($"{Entry.LogPrefix} 没找到 {screenType.Name}.RebuildCards ⇒ 选取率角标只靠 0.5s 轮询刷新"
                    + "（reroll 后可能短暂缺失）。");
                return;
            }

            harmony.Patch(
                rebuild,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(RunePickRateBadge), nameof(RebuildCardsPostfix))));
            patched.Add($"{screenType.Name}.RebuildCards");
        }
        catch (Exception exception)
        {
            missing.Add($"{screenType.Name}.RebuildCards(选取率角标即时刷新)");
            WarnOnce($"挂选取率角标的 RebuildCards 刷新失败（退回轮询）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void RebuildCardsPostfix(object __instance)
    {
        if (__instance is Node node)
        {
            Refresh(node);
        }
    }

    /// <summary>界面开始等作答时调用：先画一次，再起一个轮询兜底（屏关掉自动停）。</summary>
    internal static void Attach(object? screen)
    {
        if (!Enabled || screen is not Node node || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        try
        {
            Refresh(node);
            ScheduleRefresh(node, attempt: 1);
        }
        catch (Exception exception)
        {
            WarnOnce($"挂符文选取率角标失败: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void ScheduleRefresh(Node node, int attempt)
    {
        if (attempt > MaxRefreshAttempts || !WakuuRuneAutoAnswer.IsScreenOpenPublic(node))
        {
            return;
        }

        SceneTree? tree = node.GetTree();
        if (tree == null)
        {
            return;
        }

        tree.CreateTimer(RefreshIntervalSeconds).Timeout += () =>
        {
            try
            {
                Refresh(node);
            }
            catch (Exception exception)
            {
                WarnOnce($"刷新符文选取率角标失败: {exception.GetType().Name}: {exception.Message}");
                return;
            }

            ScheduleRefresh(node, attempt + 1);
        };
    }

    /// <summary>给当前候选逐个补/更新角标（幂等：已有 Label 就地改文本与颜色）。</summary>
    private static void Refresh(Node screen)
    {
        if (!Enabled || !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
        {
            return;
        }

        if (!WakuuRuneAutoAnswer.TryReadCandidatesPublic(
                screen, out List<Button> buttons, out List<RelicModel> options, out bool selfPickMode))
        {
            return;
        }

        if (selfPickMode)
        {
            return; // 自选池屏（锻造）：一屏一大堆，不参与统计也不画
        }

        int count = Math.Min(buttons.Count, options.Count);
        for (int i = 0; i < count; i++)
        {
            Button button = buttons[i];
            if (!GodotObject.IsInstanceValid(button))
            {
                continue;
            }

            string entry = EntryOf(options[i]);
            RunePickStat? stat = RunePickStats.Lookup(entry);
            int offered = Math.Max(0, stat?.Offered ?? 0);
            int picked = Math.Clamp(stat?.Picked ?? 0, 0, offered);

            Label? label = FindBadge(button);
            if (label == null)
            {
                label = CreateBadge(button);
            }

            label.Text = RuneBadgePolicy.Format(offered, picked);
            label.AddThemeColorOverride("font_color", ColorFor(RuneBadgePolicy.Classify(offered, picked)));
        }

        if (_loggedAttachCount < 1 && count > 0)
        {
            _loggedAttachCount++;
            Log.Info($"{Entry.LogPrefix} 符文选取率角标已挂到候选卡（左上角，`HRLF_RUNE_BADGE=0` 可关），"
                + $"候选={count}, screen={screen.GetType().Name}");
        }
    }

    private static Label CreateBadge(Button button)
    {
        Label label = new()
        {
            Name = BadgeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(6f, 4f),
        };

        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        button.AddChild(label);
        return label;
    }

    private static Label? FindBadge(Button button)
    {
        foreach (Node child in button.GetChildren())
        {
            if (child is Label label && label.Name == BadgeName && GodotObject.IsInstanceValid(label))
            {
                return label;
            }
        }

        return null;
    }

    private static Color ColorFor(RuneBadgePolicy.Tier tier)
    {
        return tier switch
        {
            RuneBadgePolicy.Tier.High => ColorHigh,
            RuneBadgePolicy.Tier.Mid => ColorMid,
            RuneBadgePolicy.Tier.Low => ColorLow,
            _ => ColorUnknown,
        };
    }

    private static string EntryOf(RelicModel? relic)
    {
        if (relic == null)
        {
            return string.Empty;
        }

        try
        {
            return relic.Id.Entry ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} {message}");
    }
}
