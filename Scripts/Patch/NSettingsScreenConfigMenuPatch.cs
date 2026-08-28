using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 往游戏"设置 → 常规（GeneralSettings）"页注入一行【瓦库托管】入口按钮，
/// 点击把 LocalWakuuConfigSubmenu 推入原版子菜单栈（见 NSubmenuStackGetSubmenuTypePatch），
/// 改动写回 vakuu_autopilot.json。
/// 按钮行注入方式沿用社区通用做法（BaseLib InjectSettingsModConfigPatch 同款）：
/// 复制 SendFeedback 分隔线 + Modding 按钮行，改名换文案后接到 NButton.Released 上。
/// 注意：复制体不携带运行期信号连接（原版 Modding 按钮 → 打开 Mod 页的连接是
/// NSettingsScreen._Ready 在原实例上代码连接的），因此不会误开 Mod 页。
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
public static class NSettingsScreenConfigMenuPatch
{
    private const string RowName = "VakuuConfig";
    private const string ButtonName = "VakuuConfigButton";

    /// <summary>NSubmenu._stack 是 protected，反射缓存一次供点击时读取。</summary>
    private static readonly System.Reflection.FieldInfo? _stackField = AccessTools.Field(typeof(NSubmenu), "_stack");

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
        MarginContainer? feedbackRow = rows.GetNodeOrNull<MarginContainer>("SendFeedback");
        MarginContainer? moddingRow = rows.GetNodeOrNull<MarginContainer>("Modding");
        if (divider == null || feedbackRow == null || moddingRow == null)
        {
            LocalMultiControlLogger.Warn($"未找到参照行（SendFeedbackDivider={divider}, SendFeedback={feedbackRow}, Modding={moddingRow}），跳过瓦库托管按钮注入");
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
        // 插在 SendFeedback 行之后、Modding 行之前（与 BaseLib 一致），
        // 避免"瓦库托管"紧贴游戏自带的"发送反馈"，防止误导成给本 mod 反馈。
        feedbackRow.AddSibling(vakuuDivider);
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

        button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => OpenConfigSubmenu(settingsScreen)));

        // 参考 BaseLib 补齐焦点导航：SendFeedback → 瓦库按钮 → Modding → Credits，
        // 保证手柄/键盘在设置页里逐项上下移动时不会被新插入的行打断。
        SetupFocusNavigation(settingsScreen, rows, feedbackRow, moddingRow, button);

        LocalMultiControlLogger.Info("已在设置界面注入瓦库托管按钮");
    }

    private static void SetupFocusNavigation(
        NSettingsScreen settingsScreen,
        VBoxContainer rows,
        MarginContainer feedbackRow,
        MarginContainer moddingRow,
        NButton vakuuButton)
    {
        Control? feedbackButton = feedbackRow.GetNodeOrNull<Control>("FeedbackButton");
        Control? moddingButton = moddingRow.GetNodeOrNull<Control>("ModdingButton");
        Control? creditsButton = rows.GetNodeOrNull<Control>("Credits/CreditsButton");
        if (feedbackButton == null || moddingButton == null || creditsButton == null)
        {
            LocalMultiControlLogger.Warn($"设置焦点导航失败（FeedbackButton={feedbackButton}, ModdingButton={moddingButton}, CreditsButton={creditsButton}）");
            return;
        }

        creditsButton.FocusNeighborTop = creditsButton.GetPathTo(moddingButton);
        moddingButton.FocusNeighborBottom = moddingButton.GetPathTo(creditsButton);
        vakuuButton.FocusNeighborTop = vakuuButton.GetPathTo(feedbackButton);
        vakuuButton.FocusNeighborBottom = vakuuButton.GetPathTo(moddingButton);
        feedbackButton.FocusNeighborBottom = feedbackButton.GetPathTo(vakuuButton);
        moddingButton.FocusNeighborTop = moddingButton.GetPathTo(vakuuButton);
    }

    /// <summary>
    /// 把瓦库托管设置页推入当前设置界面所在的子菜单栈（BaseLib 的做法）：
    /// 主菜单与局内暂停菜单分别走 NMainMenuSubmenuStack / NRunSubmenuStack，
    /// 两个栈的 GetSubmenuType 已由 NSubmenuStackGetSubmenuTypePatch 注册本 mod 的子菜单类型。
    /// </summary>
    private static void OpenConfigSubmenu(NSettingsScreen settingsScreen)
    {
        try
        {
            if (_stackField?.GetValue(settingsScreen) is not NSubmenuStack stack)
            {
                LocalMultiControlLogger.Warn("打开瓦库托管设置页失败：设置界面不在任何子菜单栈中");
                return;
            }

            switch (stack)
            {
                case NMainMenuSubmenuStack mainMenuStack:
                    mainMenuStack.PushSubmenuType<LocalWakuuConfigSubmenu>();
                    break;
                case NRunSubmenuStack runStack:
                    runStack.PushSubmenuType<LocalWakuuConfigSubmenu>();
                    break;
                default:
                    LocalMultiControlLogger.Warn($"打开瓦库托管设置页失败：未知子菜单栈 {stack.GetType().Name}");
                    break;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"打开瓦库托管设置页异常: {exception.Message}");
        }
    }
}
