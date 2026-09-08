using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

// ============================================================
// 自有统计角标/悬停详情 UI（marker r72）。
// 只画「个人偏好记录器」的数据（与皮皮军师/SkadaHelper 社区统计 UI 完全分开、互不覆盖）。
// - 角标：奖励选牌卡/商店卡/事件选项按钮右下角一个小百分比（总抓取率/总选择率）。
// - 悬停详情：鼠标移到目标上时，在其**正下方**显示自绘统计面板
//   （卡：1/2/3 幕首抓/重复抓取率 + 拿了/没拿胜率；事件：分幕选择率 + 选了/没选胜率）。
//
// 两者都由常驻全屏 StatBadgeOverlay 驱动，不挂进游戏 hover tip / 不依赖父节点布局：
// 每帧读目标实际 GetGlobalRect() 摆位，悬停命中由「视口鼠标位置是否落在目标矩形内」判断。
// 视觉用 Godot 基础控件 + SystemFont 自绘；MouseFilter=Ignore 不挡操作；
// 文本用个人记录器存档，不查任何社区数据；一切异常 try/catch + WARN，不影响游戏本体。
// ============================================================
namespace LocalMultiControl.Scripts.Runtime;

internal static partial class LocalStatBadgeUi
{
    /// <summary>角标/面板名字。</summary>
    private const string BadgeName = "LMCStatBadge";

    /// <summary>角标与面板的字体（系统中文字体兜底；不存在时退回引擎默认）。</summary>
    private static Font? _font;

    private static Font GetFont()
    {
        if (_font != null)
        {
            return _font;
        }

        SystemFont systemFont = new()
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
        _font = systemFont;
        return _font;
    }

    /// <summary>开关总闸：设置页 statBadge 开启且记录器本身启用。</summary>
    public static bool Show => LocalWakuuAutopilotConfig.StatBadge && LocalPersonalRecorder.IsEnabled;

    /// <summary>当前 run 是否按「多人（本地双控）」口径统计。</summary>
    public static bool IsMultiNow => LocalSelfCoopContext.IsEnabled;

    /// <summary>统计范围标签（无角色区分，双人同屏共用）。</summary>
    public static string ScopeLabel => IsMultiNow ? "（本地双控）" : "（单机）";

    /// <summary>全屏 overlay（惰性挂到当前树根）。</summary>
    private static StatBadgeOverlay? _overlay;

    /// <summary>奖励选牌角标定位诊断计数（最多打 3 条，便于核对 holder/hitbox 矩形）。</summary>
    private static int _rewardDiagCount;

    private static StatBadgeOverlay GetOverlay(Godot.Control anchor)
    {
        if (_overlay == null || !GodotObject.IsInstanceValid(_overlay) || _overlay.GetTree() == null)
        {
            _overlay = new StatBadgeOverlay();
            if (anchor?.GetTree() != null)
            {
                anchor.GetTree().Root.AddChild(_overlay);
            }
        }

        return _overlay;
    }

    private static StatBadgeOverlay? OverlayIfExists =>
        _overlay != null && GodotObject.IsInstanceValid(_overlay) ? _overlay : null;

    private static void SetBadge(Godot.Control owner, string percentText, string detailBody)
    {
        if (owner == null || string.IsNullOrEmpty(percentText) || !Show)
        {
            return;
        }

        GetOverlay(owner).SetBadge(owner, percentText, detailBody);
    }

    private static void ClearBadge(Godot.Control owner)
    {
        OverlayIfExists?.ClearBadge(owner);
    }

    /// <summary>查社区（皮皮军师）数据；未装/查无数据/异常返回 null。</summary>
    private static WakuuCardSignal? TryQueryCommunityCard(string? characterId, string cardId)
    {
        if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(cardId))
        {
            return null;
        }

        try
        {
            return WakuuSkadaAdapter.TryGetCardSignal(characterId!, cardId);
        }
        catch (Exception e)
        {
            LocalMultiControlLogger.Warn($"统计角标（社区数据）查询失败: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 按「数据来源档位」决定一张卡的角标百分比与悬停正文，**始终只显示一个百分比**：
    /// - personalOnly：仅个人（无记录显示 0%）；
    /// - personalThenCommunity：有个人用个人，无个人才用社区补足；
    /// - blended：个人与社区按伪计数加权融合成一个抓取率（个人样本越多越主导）。
    /// </summary>
    private static void BuildCardBadgeContent(
        string cardId,
        CardStatBadge badge,
        string? characterId,
        out string percentText,
        out string body)
    {
        string source = LocalWakuuAutopilotConfig.StatBadgeSource;
        bool hasData = badge.HasData;

        WakuuCardSignal? signal = source == WakuuStatBadgeSource.PersonalOnly
            ? null
            : TryQueryCommunityCard(characterId, cardId);

        if (hasData && source == WakuuStatBadgeSource.Blended)
        {
            double blended = WakuuStatBadgeQuery.BlendPickRate(
                badge.Total.Picked,
                badge.Total.Offered,
                signal?.PickRate,
                signal?.OfferCount ?? 0);
            percentText = WakuuStatBadgeFormat.RateToPercentText(blended);
            body = signal == null
                ? WakuuStatBadgeFormat.FormatCardTipBody(badge, ScopeLabel)
                : WakuuStatBadgeFormat.FormatBlendedCardBody(
                    badge, ScopeLabel, signal.Value.PickRate, signal.Value.OfferCount, blended);
            return;
        }

        if (hasData)
        {
            percentText = badge.Total.PercentText;
            body = WakuuStatBadgeFormat.FormatCardTipBody(badge, ScopeLabel);
            return;
        }

        if (signal != null)
        {
            // 无个人记录 → 社区（皮皮军师）数据补足
            percentText = WakuuStatBadgeFormat.RateToPercentText(signal.Value.PickRate);
            body = WakuuStatBadgeFormat.FormatCommunityCardBody(
                ScopeLabel,
                signal.Value.PickRate,
                signal.Value.WinRateHeld,
                signal.Value.WinRateSkipped,
                signal.Value.OfferCount);
            return;
        }

        percentText = "0%";
        body = WakuuStatBadgeFormat.FormatEmptyTip(ScopeLabel, "该卡");
    }

    // ---------------- 奖励选牌卡（NCardRewardSelectionScreen.RefreshOptions postfix） ----------------

    /// <summary>
    /// 刷新奖励选牌屏的角标。不依赖具体节点路径——直接遍历整棵子树找 NCardHolder，
    /// 避免 UI/CardRow 之类路径在不同版本/不同选牌屏上不匹配导致静默无角标。
    /// </summary>
    public static void RefreshRewardScreen(NCardRewardSelectionScreen screen)
    {
        if (screen == null)
        {
            return;
        }

        try
        {
            List<NCardHolder> holders = new();
            foreach (Node node in screen.FindChildren("*", recursive: true, owned: false))
            {
                if (node is NCardHolder holder && holder.CardNode != null)
                {
                    holders.Add(holder);
                }
            }

            if (!Show)
            {
                foreach (NCardHolder holder in holders)
                {
                    ClearBadge(holder);
                }

                return;
            }

            PersonalStore store = LocalPersonalRecorder.Snapshot();
            int attached = 0;
            foreach (NCardHolder holder in holders)
            {
                string cardId = holder.CardNode?.Model?.Id?.Entry ?? string.Empty;
                if (string.IsNullOrEmpty(cardId))
                {
                    continue;
                }

                CardStatBadge badge = WakuuStatBadgeQuery.BuildCard(store, cardId, IsMultiNow);
                if (_rewardDiagCount < 3)
                {
                    _rewardDiagCount++;
                    Godot.Control diagAnchor = holder.Hitbox != null ? holder.Hitbox : holder;
                    LocalMultiControlLogger.Info(
                        $"统计角标诊断: cardId={cardId}, holderRect={holder.GetGlobalRect()}, "
                        + $"hitboxRect={diagAnchor.GetGlobalRect()}, viewport={holder.GetViewportRect().Size}");
                }

                // 数据来源三档（仅个人 / 个人+社区兜底 / 融合）统一决定角标与悬停正文
                BuildCardBadgeContent(
                    cardId,
                    badge,
                    holder.CardNode?.Model?.Owner?.Character?.Id?.Entry,
                    out string percentText,
                    out string body);
                SetBadge(holder, percentText, body);
                attached++;
            }

            LocalMultiControlLogger.Info(
                $"统计角标（奖励选牌）：卡牌holder={holders.Count}，已挂角标={attached}，无个人数据={holders.Count - attached}");
            if (holders.Count == 0)
            {
                LocalMultiControlLogger.Warn("统计角标（奖励选牌）：未在选牌屏子树内找到任何 NCardHolder（UI 结构可能变化）。");
            }
        }
        catch (Exception e)
        {
            LocalMultiControlLogger.Warn($"统计角标（奖励选牌）刷新失败: {e.Message}");
        }
    }

    // ---------------- 商店卡（NMerchantInventory.Initialize postfix） ----------------

    /// <summary>
    /// 刷新某张商店卡的角标。返回是否真的挂上了角标（便于调用方统计/诊断）。
    /// 商品条目（CreationResult）晚于 FillSlot 生成时会安排一次延迟重试。
    /// </summary>
    public static bool RefreshMerchantCard(NMerchantCard merchantCard)
    {
        if (merchantCard == null)
        {
            return false;
        }

        try
        {
            ClearBadge(merchantCard);
            if (!Show)
            {
                return false;
            }

            string cardId = merchantCard.Entry is MerchantCardEntry entry && entry.CreationResult?.Card != null
                ? entry.CreationResult.Card.Id.Entry
                : string.Empty;
            if (string.IsNullOrEmpty(cardId))
            {
                ScheduleMerchantRetry(merchantCard);
                return false;
            }

            CardStatBadge badge = WakuuStatBadgeQuery.BuildCard(LocalPersonalRecorder.Snapshot(), cardId, IsMultiNow);
            // 数据来源三档（仅个人 / 个人+社区兜底 / 融合）统一决定角标与悬停正文
            string? characterId = merchantCard.Entry is MerchantCardEntry cardEntry
                ? cardEntry.CreationResult?.Card?.Owner?.Character?.Id?.Entry
                : null;
            BuildCardBadgeContent(cardId, badge, characterId, out string percentText, out string body);
            SetBadge(merchantCard, percentText, body);
            return true;
        }
        catch (Exception e)
        {
            LocalMultiControlLogger.Warn($"统计角标（商店卡）刷新失败: {e.Message}");
            return false;
        }
    }

    private const string MetaMerchantRetry = "lmc_stat_merchant_retry";

    /// <summary>商品条目尚未生成时，0.35s 后再刷一次（同一卡槽只排一次）。</summary>
    private static void ScheduleMerchantRetry(NMerchantCard merchantCard)
    {
        try
        {
            if (merchantCard.GetTree() == null || merchantCard.HasMeta(MetaMerchantRetry))
            {
                return;
            }

            merchantCard.SetMeta(MetaMerchantRetry, true);
            merchantCard.GetTree().CreateTimer(0.35f).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(merchantCard))
                {
                    return;
                }

                merchantCard.RemoveMeta(MetaMerchantRetry);
                RefreshMerchantCard(merchantCard);
            };
        }
        catch (Exception e)
        {
            LocalMultiControlLogger.Warn($"统计角标（商店卡）延迟重试排程失败: {e.Message}");
        }
    }

    // ---------------- 事件选项按钮（NEventRoom.SetOptions postfix） ----------------
    // r80：挂载点从 RefreshEventState 改为 SetOptions（进入事件房间首页也经它渲染按钮，
    // 只挂 RefreshEventState 会让首页选项没有角标、仅切页后的末页才有）。

    public static void RefreshEventButtons(NEventOptionButton button)
    {
        if (button == null)
        {
            return;
        }

        try
        {
            ClearBadge(button);
            if (!Show)
            {
                return;
            }

            string eventId = button.Event?.Id?.Entry ?? string.Empty;
            string optionKey = button.Option?.TextKey ?? string.Empty;
            if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(optionKey))
            {
                return;
            }

            EventStatBadge badge = WakuuStatBadgeQuery.BuildEvent(
                LocalPersonalRecorder.Snapshot(), eventId, optionKey, IsMultiNow);
            bool hasData = badge.HasData;
            // 无个人记录也显示角标（0%），悬停时说明「样本 0」
            SetBadge(
                button,
                hasData ? badge.Total.PercentText : "0%",
                hasData
                    ? WakuuStatBadgeFormat.FormatEventTipBody(badge, ScopeLabel)
                    : WakuuStatBadgeFormat.FormatEmptyTip(ScopeLabel, "该选项"));
        }
        catch (Exception e)
        {
            LocalMultiControlLogger.Warn($"统计角标（事件选项）刷新失败: {e.Message}");
        }
    }

    /// <summary>把自绘统计详情面板钉到目标正下方（常驻 overlay 的子面板）。</summary>
    private static void PositionDetailPanel(RichTextLabel panel, Godot.Control owner, Rect2 viewport)
    {
        const float width = 340f;
        const float gap = 8f;
        const float side = 4f;

        float panelHeight = Mathf.Max(28f, panel.GetContentHeight() + 18f);
        panel.Size = new Vector2(width, panelHeight);
        panel.Position = new Vector2(
            Mathf.Clamp(owner.GetGlobalRect().GetCenter().X - width / 2f, side, viewport.Size.X - width - side),
            owner.GetGlobalRect().End.Y + gap);
        if (panel.Position.Y + panelHeight > viewport.Size.Y - side)
        {
            // 底部放不下 → 放到目标上方
            panel.Position = new Vector2(
                panel.Position.X,
                Mathf.Max(side, owner.GetGlobalRect().Position.Y - gap - panelHeight));
        }
    }

    /// <summary>
    /// 常驻全屏角标 overlay：登记目标每帧按其实际全局矩形钉右下角小百分比；
    /// 鼠标落在某目标上时在其正下方显示该目标的统计详情面板。
    /// </summary>
    private sealed partial class StatBadgeOverlay : Godot.Control
    {
        private sealed class BadgeEntry
        {
            public required Godot.Control Owner { get; init; }

            /// <summary>
            /// 取矩形用的参照控件：卡牌用卡面点击区 Hitbox（NCardHolder 自身的矩形可能是整行容器宽，
            /// 用它定位会让角标落到整行的最左/最右端甚至屏幕外）。非卡牌目标就是 owner 本身。
            /// </summary>
            public required Godot.Control AnchorTarget { get; init; }

            public required Label Badge { get; init; }

            public required string Body { get; init; }
        }

        private readonly List<BadgeEntry> _entries = new();

        private readonly RichTextLabel _detail;

        private Godot.Control? _detailOwner;

        private string? _detailText;

        // 角标视觉目标（像素）：盒宽/高与右下留白
        private const float BadgeWidth = 64f;
        private const float BadgeHeight = 20f;
        private const float GapRight = 8f;
        private const float GapBottom = 5f;

        public StatBadgeOverlay()
        {
            Name = BadgeName + "Overlay";
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(true);

            _detail = new RichTextLabel
            {
                Name = BadgeName + "Detail",
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            _detail.CustomMinimumSize = new Vector2(340f, 0f);
            _detail.AddThemeFontOverride("normal_font", GetFont());
            _detail.AddThemeFontSizeOverride("normal_font_size", 14);
            _detail.AddThemeColorOverride("default_color", new Color(1f, 0.97f, 0.9f));

            StyleBoxFlat background = new()
            {
                BgColor = new Color(0.04f, 0.04f, 0.06f, 0.95f),
                BorderColor = new Color(1f, 0.9f, 0.45f, 0.9f),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 10f,
                ContentMarginTop = 7f,
                ContentMarginRight = 10f,
                ContentMarginBottom = 7f,
            };
            _detail.AddThemeStyleboxOverride("normal", background);
            AddChild(_detail);
        }

        public void SetBadge(Godot.Control owner, string text, string body)
        {
            ClearBadge(owner);
            if (!GodotObject.IsInstanceValid(owner))
            {
                return;
            }

            Label label = new()
            {
                Name = BadgeName,
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontOverride("font", GetFont());
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.45f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f));
            label.AddThemeConstantOverride("outline_size", 4);

            AddChild(label);
            MoveChild(_detail, -1); // 详情面板保持绘制在最上层
            // 卡牌用 Hitbox（卡面）作定位参照；holder 自身矩形可能是整行容器宽
            Godot.Control anchorTarget = owner is NCardHolder cardHolder && cardHolder.Hitbox != null
                ? cardHolder.Hitbox
                : owner;
            _entries.Add(new BadgeEntry
            {
                Owner = owner,
                AnchorTarget = anchorTarget,
                Badge = label,
                Body = body ?? string.Empty,
            });
            owner.TreeExiting += () => ClearBadge(owner);
        }

        public void ClearBadge(Godot.Control owner)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner == owner)
                {
                    _entries[i].Badge.QueueFree();
                    _entries.RemoveAt(i);
                }
            }

            if (_detailOwner == owner)
            {
                _detailOwner = null;
                _detailText = null;
                _detail.Visible = false;
            }
        }

        public override void _Process(double delta)
        {
            // 1) 逐帧摆角标（位置档位可配置，默认左下以避开皮皮军师画在卡右侧的社区统计标签）
            StatBadgePlacement placement = WakuuStatBadgeLayout.Resolve(
                LocalWakuuAutopilotConfig.StatBadgeCorner, BadgeWidth, BadgeHeight, GapRight, GapBottom);
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                BadgeEntry entry = _entries[i];
                Godot.Control owner = entry.Owner;
                if (owner == null || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
                {
                    entry.Badge.QueueFree();
                    _entries.RemoveAt(i);
                    continue;
                }

                Godot.Control anchor = entry.AnchorTarget;
                if (anchor == null || !GodotObject.IsInstanceValid(anchor) || !anchor.IsInsideTree())
                {
                    anchor = owner;
                }

                Rect2 rect = anchor.GetGlobalRect();
                if (rect.Size.X < 1f || rect.Size.Y < 1f || !owner.Visible)
                {
                    entry.Badge.Visible = false;
                    continue;
                }

                entry.Badge.Visible = true;
                entry.Badge.Size = new Vector2(BadgeWidth, BadgeHeight);
                entry.Badge.Position = new Vector2(
                    rect.Position.X + placement.AnchorX * rect.Size.X + placement.OffsetX,
                    rect.Position.Y + placement.AnchorY * rect.Size.Y + placement.OffsetY);
            }

            // 2) 悬停检测 → 正下方面板
            Rect2 viewport = GetViewportRect();
            Vector2 mouse = GetViewport().GetMousePosition();

            BadgeEntry? hovered = null;
            for (int i = _entries.Count - 1; i >= 0 && hovered == null; i--)
            {
                BadgeEntry entry = _entries[i];
                Godot.Control owner = entry.Owner;
                if (owner == null || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() || !owner.Visible)
                {
                    continue;
                }

                Godot.Control hoverAnchor = entry.AnchorTarget;
                if (hoverAnchor == null || !GodotObject.IsInstanceValid(hoverAnchor) || !hoverAnchor.IsInsideTree())
                {
                    hoverAnchor = owner;
                }

                Rect2 rect = hoverAnchor.GetGlobalRect();
                if (rect.Size.X >= 1f && rect.Size.Y >= 1f && rect.HasPoint(mouse))
                {
                    hovered = entry;
                }
            }

            if (hovered == null || string.IsNullOrEmpty(hovered.Body))
            {
                _detail.Visible = false;
                _detailOwner = null;
                _detailText = null;
                return;
            }

            if (!ReferenceEquals(hovered.Owner, _detailOwner) || _detailText != hovered.Body)
            {
                _detail.Text = hovered.Body;
                _detailOwner = hovered.Owner;
                _detailText = hovered.Body;
            }

            _detail.Visible = true;
            PositionDetailPanel(_detail, hovered.AnchorTarget ?? hovered.Owner, viewport);
        }
    }
}
