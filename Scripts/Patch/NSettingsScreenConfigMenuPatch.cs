using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 往游戏"设置 → 常规（GeneralSettings）"页注入一行【瓦库托管】入口按钮，
/// 点击弹出自家开关面板（LocalWakuuConfigPanel），改动写回 vakuu_autopilot.json。
/// 注入方式照搬"古明地恋"mod 的 KoishiConfigMenuPatch：
/// 复制 SendFeedback 分隔线 + Modding 按钮行，改名换文案后接到 NButton.Released 上。
/// 注意：复制体不携带运行期信号连接（原版 Modding 按钮 → 打开 Mod 页的连接是
/// NSettingsScreen._Ready 在原实例上代码连接的），因此不会误开 Mod 页。
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
public static class NSettingsScreenConfigMenuPatch
{
    private const string RowName = "VakuuConfig";
    private const string ButtonName = "VakuuConfigButton";

    public static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            InjectConfigButton(__instance);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库托管设置按钮失败: {exception.Message}");
        }
    }

    private static void InjectConfigButton(NSettingsScreen settingsScreen)
    {
        // 节点路径与 KoishiConfigMenuPatch 一致：GeneralSettings 面板的内容容器
        Control? generalSettings = settingsScreen.GetNodeOrNull<Control>("ScrollContainer/Mask/Clipper/GeneralSettings");
        VBoxContainer? rows = generalSettings?.GetNodeOrNull<VBoxContainer>("VBoxContainer");
        if (rows == null)
        {
            LocalMultiControlLogger.Warn("未找到 GeneralSettings 容器，跳过瓦库托管按钮注入");
            return;
        }

        if (rows.GetNodeOrNull(RowName) != null)
        {
            return; // 已注入过（设置界面复用同一实例时防重复）
        }

        ColorRect? divider = rows.GetNodeOrNull<ColorRect>("SendFeedbackDivider");
        MarginContainer? moddingRow = rows.GetNodeOrNull<MarginContainer>("Modding");
        if (divider == null || moddingRow == null)
        {
            LocalMultiControlLogger.Warn($"未找到参照行（SendFeedbackDivider={divider}, Modding={moddingRow}），跳过瓦库托管按钮注入");
            return;
        }

        ColorRect vakuuDivider = (ColorRect)divider.Duplicate(15);
        vakuuDivider.Name = "VakuuConfigDivider";

        MarginContainer vakuuRow = (MarginContainer)moddingRow.Duplicate(15);
        vakuuRow.Name = RowName;
        vakuuRow.Visible = true; // Modding 行默认可能隐藏（无 mod 时），我们始终显示

        NButton? button = vakuuRow.GetNodeOrNull<NButton>("ModdingButton");
        if (button == null)
        {
            LocalMultiControlLogger.Warn("复制行内未找到 ModdingButton，跳过瓦库托管按钮注入");
            return;
        }

        button.Name = ButtonName;
        button.UniqueNameInOwner = true;
        divider.AddSibling(vakuuDivider);
        vakuuDivider.AddSibling(vakuuRow);
        button.Owner = settingsScreen; // 与 Koishi 做法一致，注册 %VakuuConfigButton 便于外部定位

        // 入树后复制体的 _Ready 会把两处 Label 重置回 MODDING 文案，必须在其后覆盖：
        // 行标题（MegaRichTextLabel）+ 按钮文字（Label）
        RichTextLabel? rowLabel = vakuuRow.GetNodeOrNull<RichTextLabel>("Label");
        if (rowLabel != null)
        {
            rowLabel.Text = "瓦库托管";
        }

        Label? buttonLabel = button.GetNodeOrNull<Label>("Label");
        if (buttonLabel != null)
        {
            buttonLabel.Text = "打开设置面板";
        }

        button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => OpenPanelSafe()));
        LocalMultiControlLogger.Info("已在设置界面注入瓦库托管按钮");
    }

    private static void OpenPanelSafe()
    {
        try
        {
            LocalWakuuConfigPanel.OpenModal();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"打开瓦库托管设置面板失败: {exception.Message}");
        }
    }
}
