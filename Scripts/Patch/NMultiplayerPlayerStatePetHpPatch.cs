using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

// ============================================================
// 召唤物（宠物）血量显示（marker r86）。
// 挂在多人顶部的「玩家状态条」NMultiplayerPlayerState 上：持有召唤物的玩家
// （**不判职业**——Osty 可以由亡灵契约师为其他玩家召唤、其他角色也能合法持有 Necrobinder
// 卡牌召唤；The Queen 等走原版 AddPet 体系的 mod 召唤物同样在列）
// 在自身 HP 条右侧逐行显示其 PlayerCombatState.Pets 里每一只召唤物的迷你血条 + 名字 + 血量。
// 死亡（尸体保留、可复活阶段）显示 0-X 置灰。
//
// 视觉与交互：
// - 面板是 NMultiplayerPlayerState 的普通子 Control（非 TopLevel，随父条整体移动/隐藏），
//   MouseFilter=Ignore 不挡任何点击；锚定在 HP 条右缘外侧、与其顶端同高，逐行向下排
//   （切前台按钮在名字行高度，与血条行的垂直带错开）。
// - 每帧从 Pets 读值重算条宽/文本，天然覆盖「中途召唤 / 死亡 / 复活 / 战斗结束清空」。
// - 仅本地双控 + run 进行中 + 开关开时显示；门禁任一不满足即整体隐藏。
// 注意：补丁类必须类级 [HarmonyPatch]（PatchAll 只应用类上带标记的类型）。
// ============================================================
[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
internal static class NMultiplayerPlayerStatePetHpPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerPlayerState __instance)
    {
        LocalPlayerPetHpBadgeUi.Ensure(__instance);
    }
}

/// <summary>
/// 玩家状态条上的召唤物血条 UI：为每个 NMultiplayerPlayerState 挂一个常驻 tracker，
/// 由 tracker 逐帧把该玩家的 Pets 刷成一行行迷你血条。
/// </summary>
internal static partial class LocalPlayerPetHpBadgeUi
{
    private const string TrackerName = "LocalPetHpBadgeTracker";

    /// <summary>面板锚点：HP 条右缘再往右几像素。</summary>
    private const float AnchorRightOfHpBar = 6f;

    /// <summary>单行宽度/高度。</summary>
    private const float RowWidth = 150f;
    private const float RowHeight = 16f;

    /// <summary>行与行之间垂直间距。</summary>
    private const float RowSeparation = 2f;

    private const float FgInsetX = 2f;
    private const float FgInsetY = 2f;

    private static Font? _font;

    private static Font GetFont()
    {
        if (_font != null)
        {
            return _font;
        }

        _font = new SystemFont
        {
            FontNames = new[]
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "PingFang SC",
                "Noto Sans CJK SC",
                "Source Han Sans SC",
            },
        };
        return _font;
    }

    public static void Ensure(NMultiplayerPlayerState state)
    {
        if (state == null || state.GetNodeOrNull<Node>(TrackerName) != null)
        {
            return;
        }

        LocalPlayerPetHpBadgeTracker tracker = new()
        {
            Name = TrackerName,
        };
        tracker.Initialize(state);
        state.AddChild(tracker);
    }

    /// <summary>一只召唤物对应的一行控件（根 = Bg 容器，Fg 与文本是其子节点）。</summary>
    private sealed partial class PetHpRow
    {
        public required ColorRect Bg { get; init; }

        public required ColorRect Fg { get; init; }

        public required Label Label { get; init; }
    }

    /// <summary>
    /// 常驻每帧 tracker：读 state.Player.PlayerCombatState?.Pets，把血条面板钉在该玩家 HP 条右侧。
    /// 面板为该 state 的普通子节点（非 TopLevel），随父条整体移动/显隐。
    /// </summary>
    private sealed partial class LocalPlayerPetHpBadgeTracker : Node
    {
        private NMultiplayerPlayerState? _state;

        private Control? _panel;

        private readonly List<PetHpRow> _rows = new();

        private int _lastPetCount = -1;

        private bool _loggedLayoutDiag;

        public void Initialize(NMultiplayerPlayerState state)
        {
            _state = state;
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            if (_state == null || !GodotObject.IsInstanceValid(_state))
            {
                QueueFree();
                return;
            }

            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                LocalMultiControlLogger.Warn($"召唤物血条刷新失败: {e.Message}");
            }
        }

        private void Refresh()
        {
            bool enabled = LocalWakuuAutopilotConfig.PetHpBadge
                && LocalSelfCoopContext.IsEnabled
                && RunManager.Instance != null
                && RunManager.Instance.IsInProgress;

            Player? player = _state!.Player;
            PlayerCombatState? combat = player?.PlayerCombatState;
            IReadOnlyList<Creature>? pets = combat?.Pets;

            bool hasPets = enabled && pets != null && pets.Count > 0;
            if (!hasPets)
            {
                if (_panel != null)
                {
                    _panel.Visible = false;
                }

                _lastPetCount = -1;
                return;
            }

            EnsurePanel();
            ReconcileRows(pets!);

            NHealthBar? healthBar = ResolveHealthBar(_state!);
            Control? hpBarContainer = healthBar?.HpBarContainer;
            if (hpBarContainer == null || !GodotObject.IsInstanceValid(hpBarContainer))
            {
                // 拿不到血条容器时不摆位（保留已构建行，等下一帧）
                return;
            }

            Rect2 hpRect = hpBarContainer.GetGlobalRect();
            Vector2 localOffset = hpRect.Position - _state!.GlobalPosition;
            float totalHeight = _rows.Count * RowHeight
                + Math.Max(0, _rows.Count - 1) * RowSeparation;
            _panel!.Visible = true;
            // 锚在 HP 条右缘外侧、与其顶端同高；血条行向下排（切按钮在名字行，垂直带错开）。
            _panel.Position = new Vector2(localOffset.X + hpRect.Size.X + AnchorRightOfHpBar, localOffset.Y);
            _panel.Size = new Vector2(RowWidth, totalHeight);

            if (!_loggedLayoutDiag)
            {
                _loggedLayoutDiag = true;
                LocalMultiControlLogger.Info(
                    $"召唤物血条：player={player?.NetId}, pets={pets!.Count}, "
                    + $"stateRect={_state!.GetGlobalRect()}, hpRect={hpRect}, panel={_panel.Position}（仅首帧）");
            }

            for (int i = 0; i < _rows.Count && i < pets!.Count; i++)
            {
                PetHpRow row = _rows[i];
                UpdateRow(row, pets[i]);
            }
        }

        /// <summary>把一只召唤物刷到一行面板控件上（数值每帧更新，行控件复用避免频繁建删）。</summary>
        private static void UpdateRow(PetHpRow row, Creature pet)
        {
            int current = Math.Max(0, (int)pet.CurrentHp);
            int max = Math.Max(1, (int)pet.MaxHp);
            bool alive = pet.IsAlive;

            string name;
            try
            {
                name = pet.Name;
            }
            catch (Exception)
            {
                name = string.Empty;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "Pet";
            }

            row.Label.Text = $"{name} {current}/{max}";

            float ratio = alive ? Mathf.Clamp((float)current / max, 0f, 1f) : 0f;
            float innerWidth = Mathf.Max(0f, RowWidth - FgInsetX * 2f);
            float innerHeight = Mathf.Max(0f, RowHeight - FgInsetY * 2f);
            row.Fg.Size = new Vector2(innerWidth * ratio, innerHeight);
            row.Fg.Visible = alive && ratio > 0f;
            row.Fg.Color = alive ? new Color(0.85f, 0.22f, 0.2f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.9f);
            row.Label.AddThemeColorOverride(
                "font_color",
                alive ? new Color(1f, 0.96f, 0.88f) : new Color(0.72f, 0.72f, 0.72f));
        }

        private static NHealthBar? ResolveHealthBar(NMultiplayerPlayerState state)
        {
            try
            {
                return AccessTools.Field(typeof(NMultiplayerPlayerState), "_healthBar")
                    ?.GetValue(state) as NHealthBar;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void EnsurePanel()
        {
            if (_panel != null && GodotObject.IsInstanceValid(_panel))
            {
                return;
            }

            Control panel = new()
            {
                Name = "LocalPetHpBadgePanel",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _state!.AddChild(panel);
            _panel = panel;
            _rows.Clear();
            _lastPetCount = -1;
        }

        /// <summary>数量变化时重建全部行（宠物极少，重建成本可忽略）。</summary>
        private void ReconcileRows(IReadOnlyList<Creature> pets)
        {
            if (_rows.Count == pets.Count && _lastPetCount == pets.Count)
            {
                return;
            }

            foreach (PetHpRow row in _rows)
            {
                row.Bg.QueueFree();
            }

            _rows.Clear();
            for (int i = 0; i < pets.Count; i++)
            {
                ColorRect bg = new()
                {
                    Name = "Bg",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Color = new Color(0f, 0f, 0f, 0.62f),
                    Position = new Vector2(0f, i * (RowHeight + RowSeparation)),
                    Size = new Vector2(RowWidth, RowHeight),
                };

                ColorRect fg = new()
                {
                    Name = "Fg",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Position = new Vector2(FgInsetX, FgInsetY),
                    Size = new Vector2(RowWidth - FgInsetX * 2f, RowHeight - FgInsetY * 2f),
                };

                Label label = new()
                {
                    Name = "Label",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Size = new Vector2(RowWidth, RowHeight),
                };
                label.AddThemeFontOverride("font", GetFont());
                label.AddThemeFontSizeOverride("font_size", 13);
                label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f, 0.9f));
                label.AddThemeConstantOverride("outline_size", 4);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;

                bg.AddChild(fg);
                bg.AddChild(label);
                _panel!.AddChild(bg);

                _rows.Add(new PetHpRow { Bg = bg, Fg = fg, Label = label });
            }

            _lastPetCount = pets.Count;
        }
    }
}
