using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class NCharacterSelectLocalCountButtonsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Process))]
internal static class NCharacterSelectLocalCountButtonsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
internal static class NCharacterSelectLocalCountButtonsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NCharacterSelectScreen __instance)
    {
        LocalCharacterSelectCountButtons.Remove(__instance);
    }
}

internal static class LocalCharacterSelectCountButtons
{
    private const string LtHintIconName = "LocalSelfCoopLtHintIcon";
    private const string MinusHintIconName = "LocalSelfCoopMinusHintIcon";
    private const string PlusHintIconName = "LocalSelfCoopPlusHintIcon";
    private const string PrevHintIconName = "LocalSelfCoopPrevHintIcon";
    private const string NextHintIconName = "LocalSelfCoopNextHintIcon";
    private const string PlusSignName = "LocalSelfCoopHintPlusSign";
    private static readonly Vector2 HintIconSize = new Vector2(24f, 24f);

    private static readonly LocalPlayerCountPanel Panel = new(new LocalPlayerCountPanelOptions(
        namePrefix: "LocalSelfCoop",
        sourcePrefix: "ui-button",
        successLogText: "通过实体按钮调整本地人数成功",
        resolvePosition: ResolvePanelPosition)
    {
        AfterPanelCreated = OnPanelCreated,
        AfterLayout = RefreshHintIcons,
        AfterAdjust = ResyncActiveScreen
    });

    public static void Sync(NCharacterSelectScreen screen)
    {
        // 官方联机也用 NCharacterSelectScreen：只有「本页正是我们入口打开的那一个」才挂面板（r159），
        // 避免残留会话让官方联机角色选择页冒出我们的人数/切换按钮。
        bool shouldShow = LocalSelfCoopContext.IsEnabled
            && ReferenceEquals(LocalSelfCoopContext.ActiveCharacterSelectScreen, screen);
        Panel.Sync(screen, shouldShow);
    }

    public static void Remove(NCharacterSelectScreen screen)
    {
        Panel.Remove(screen);
    }

    /// <summary>
    /// 注意：该坐标经过实机对齐，目的是避免与确认按钮重叠导致 + 按钮不可点击。
    /// 请不要随意改回靠右布局，如需改动先实测「+ 按钮在 2-&gt;3/4 人时可稳定点击」。
    /// 另外确认按钮还没就绪时本帧不布置（与原实现一致）。
    /// </summary>
    private static Vector2? ResolvePanelPosition(Vector2 viewportSize, Vector2 panelSize, Node screen)
    {
        if (screen is not NCharacterSelectScreen characterSelect)
        {
            return null;
        }

        NConfirmButton? embarkButton =
            AccessTools.Field(typeof(NCharacterSelectScreen), "_embarkButton")?.GetValue(characterSelect) as NConfirmButton;
        return embarkButton == null
            ? null
            : LocalPlayerCountPanelOptions.BottomRight(viewportSize, panelSize, screen);
    }

    private static void OnPanelCreated(Control panel)
    {
        EnsureHintIcon(panel, LtHintIconName);
        EnsureHintIcon(panel, MinusHintIconName);
        EnsureHintIcon(panel, PlusHintIconName);
        EnsureHintIcon(panel, PrevHintIconName);
        EnsureHintIcon(panel, NextHintIconName);
        EnsurePlusSign(panel);
        LocalMultiControlLogger.Info("角色选择页已创建本地人数 +/- 实体按钮。");
    }

    private static void ResyncActiveScreen()
    {
        NCharacterSelectScreen? activeScreen = LocalSelfCoopContext.ActiveCharacterSelectScreen;
        if (activeScreen != null && GodotObject.IsInstanceValid(activeScreen))
        {
            Sync(activeScreen);
        }
    }

    private static void EnsureHintIcon(Control panel, string nodeName)
    {
        if (panel.GetNodeOrNull<TextureRect>(nodeName) != null)
        {
            return;
        }

        TextureRect icon = new()
        {
            Name = nodeName,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddChild(icon);
    }

    private static void EnsurePlusSign(Control panel)
    {
        if (panel.GetNodeOrNull<Label>(PlusSignName) != null)
        {
            return;
        }

        Label plusSign = new()
        {
            Name = PlusSignName,
            Text = "+",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        plusSign.AddThemeFontSizeOverride("font_size", 16);
        plusSign.AddThemeColorOverride("font_color", new Color("f3efe6"));
        plusSign.AddThemeColorOverride("font_outline_color", new Color("111111"));
        plusSign.AddThemeConstantOverride("outline_size", 3);
        panel.AddChild(plusSign);
    }

    private static void RefreshHintIcons(Control panel, float secondColumnX, float secondRowY)
    {
        bool shouldShowHints = NControllerManager.Instance?.InputType == InputType.Controller;
        Texture2D? lt = NControllerManager.Instance?.GetHotkeyIcon(Controller.leftTrigger);
        Texture2D? left = NControllerManager.Instance?.GetHotkeyIcon(Controller.dPadLeft);
        Texture2D? right = NControllerManager.Instance?.GetHotkeyIcon(Controller.dPadRight);
        Texture2D? up = NControllerManager.Instance?.GetHotkeyIcon(Controller.dPadUp);
        Texture2D? down = NControllerManager.Instance?.GetHotkeyIcon(Controller.dPadDown);

        PlaceHint(panel, LtHintIconName, lt, new Vector2(secondColumnX * 0.5f - 10f, secondRowY - 44f));
        PlaceHint(panel, MinusHintIconName, left, new Vector2(8f, 4f));
        PlaceHint(panel, PlusHintIconName, right, new Vector2(secondColumnX + 8f, 4f));
        PlaceHint(panel, PrevHintIconName, up, new Vector2(8f, secondRowY + 4f));
        PlaceHint(panel, NextHintIconName, down, new Vector2(secondColumnX + 8f, secondRowY + 4f));

        if (panel.GetNodeOrNull<Label>(PlusSignName) is { } plusSign)
        {
            plusSign.Position = new Vector2(secondColumnX * 0.5f + 18f, secondRowY - 40f);
            plusSign.Visible = shouldShowHints && lt != null;
        }

        SetHintVisible(panel, LtHintIconName, shouldShowHints);
        SetHintVisible(panel, MinusHintIconName, shouldShowHints);
        SetHintVisible(panel, PlusHintIconName, shouldShowHints);
        SetHintVisible(panel, PrevHintIconName, shouldShowHints);
        SetHintVisible(panel, NextHintIconName, shouldShowHints);
    }

    private static void PlaceHint(Control panel, string nodeName, Texture2D? texture, Vector2 position)
    {
        if (panel.GetNodeOrNull<TextureRect>(nodeName) is not { } icon)
        {
            return;
        }

        icon.Texture = texture;
        icon.Size = HintIconSize;
        icon.CustomMinimumSize = HintIconSize;
        icon.Position = position;
        icon.Visible = texture != null;
    }

    private static void SetHintVisible(Control panel, string nodeName, bool visible)
    {
        if (panel.GetNodeOrNull<TextureRect>(nodeName) is { } icon)
        {
            icon.Visible = icon.Texture != null && visible;
        }
    }
}
