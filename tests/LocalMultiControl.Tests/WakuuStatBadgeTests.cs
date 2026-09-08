using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 自有统计角标/悬停弹窗数据聚合（r69）纯逻辑测试：卡牌总抓取率 + 分幕首/重 + 整体胜率；
/// 事件选项选择率分幕；文本格式化；按 isMulti 过滤。
/// </summary>
[TestFixture]
public class WakuuStatBadgeTests
{
    private static PersonalStore MakeStore()
    {
        return new PersonalStore();
    }

    private static PersonalRunRecord Run(string runKey, bool isMulti, string result)
    {
        return new PersonalRunRecord { runKey = runKey, isMulti = isMulti, result = result, ts = 0 };
    }

    private static PersonalCardOfferRecord Card(
        string runKey, bool isMulti, int act, string card, bool isRepeat, bool picked)
    {
        return new PersonalCardOfferRecord
        {
            runKey = runKey, isMulti = isMulti, character = "IRONCLAD", act = act,
            card = card, isRepeat = isRepeat, picked = picked, ts = 0,
        };
    }

    private static PersonalEventOptionRecord Evt(
        string runKey, bool isMulti, int act, string eventId, string optionKey, bool chosen)
    {
        return new PersonalEventOptionRecord
        {
            runKey = runKey, isMulti = isMulti, character = "IRONCLAD", act = act,
            eventId = eventId, optionKey = optionKey, optionText = optionKey, chosen = chosen, ts = 0,
        };
    }

    [Test]
    public void 卡牌_多人模式下总抓取率与分幕首次重复()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("rA", true, "win"));
        store.runs.Add(Run("rB", false, "win"));
        store.runs.Add(Run("rC", false, "loss"));
        store.runs.Add(Run("rD", true, "loss"));
        store.runs.Add(Run("rE", true, "loss"));
        // 多人 run 的 CLASH 记录（5 offer / 2 pick）
        store.cardOffers.Add(Card("rA", true, 1, "CLASH", false, true));   // act1 首抓 → 拿
        store.cardOffers.Add(Card("rA", true, 1, "CLASH", true, false));   // act1 重复 → 没拿
        store.cardOffers.Add(Card("rA", true, 2, "CLASH", false, false));  // act2 首抓 → 没拿
        store.cardOffers.Add(Card("rD", true, 1, "CLASH", false, true));   // act1 首抓 → 拿
        store.cardOffers.Add(Card("rE", true, 3, "CLASH", false, false));  // act3 首抓 → 没拿（skipped run）
        // 单人 run 的记录（不应混入多人切片）
        store.cardOffers.Add(Card("rB", false, 1, "CLASH", false, false));
        store.cardOffers.Add(Card("rB", false, 1, "CLASH", true, true));
        store.cardOffers.Add(Card("rC", false, 1, "CLASH", false, false));

        CardStatBadge badge = WakuuStatBadgeQuery.BuildCard(store, "clash", isMulti: true);
        Assert.Multiple(() =>
        {
            // 总：5 offer / 2 pick
            Assert.That(badge.HasData, Is.True);
            Assert.That(badge.Total.Offered, Is.EqualTo(5));
            Assert.That(badge.Total.Picked, Is.EqualTo(2));
            Assert.That(badge.Total.PercentText, Is.EqualTo("40%"));
            // 胜负：held = rA(run有pick行)+rD = 2（rA win、rD loss → 1 胜）；skipped = rE = 1 loss
            Assert.That(badge.Held.Runs, Is.EqualTo(2));
            Assert.That(badge.Held.Wins, Is.EqualTo(1));
            Assert.That(badge.Skipped.Runs, Is.EqualTo(1));
            Assert.That(badge.Skipped.Wins, Is.EqualTo(0));
            // 分幕：act1 有数据（首 2/2、重 1/0）；act2 有（首 1/0）；act3 有（首 1/0）
            Assert.That(badge.ByAct.Count, Is.EqualTo(3));
            CardActStat act1 = badge.ByAct[0];
            Assert.That(act1.Act, Is.EqualTo(1));
            Assert.That(act1.First.Picked, Is.EqualTo(2));
            Assert.That(act1.First.Offered, Is.EqualTo(2));
            Assert.That(act1.Repeat.Offered, Is.EqualTo(1));
            Assert.That(act1.Repeat.Picked, Is.EqualTo(0));
        });

        // 弹窗正文应包含分层与胜负行（分幕行 act3 也列出）
        string tip = WakuuStatBadgeFormat.FormatCardTipBody(badge, "（多人）");
        Assert.Multiple(() =>
        {
            Assert.That(tip, Does.Contain("我的统计（多人）"));
            Assert.That(tip, Does.Contain("总抓取率 2/5 40%"));
            Assert.That(tip, Does.Contain("第1幕"));
            Assert.That(tip, Does.Contain("首抓 2/2 100%"));
            Assert.That(tip, Does.Contain("重复 0/1 0%"));
            Assert.That(tip, Does.Contain("第3幕"));
            Assert.That(tip, Does.Contain("拿了胜率 1/2 50%"));
            Assert.That(tip, Does.Contain("没拿 0/1 0%"));
        });
    }

    [Test]
    public void 卡牌_单人切片不混入多人记录且无数据HasData为假()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("rA", true, "win"));
        store.cardOffers.Add(Card("rA", true, 1, "STRIKE", false, true));

        // 单人切片下无记录 → 无数据
        CardStatBadge single = WakuuStatBadgeQuery.BuildCard(store, "STRIKE", isMulti: false);
        Assert.That(single.HasData, Is.False);
        Assert.That(single.Total.PercentText, Is.EqualTo("-"));
        // 多人切片有记录
        CardStatBadge multi = WakuuStatBadgeQuery.BuildCard(store, "STRIKE", isMulti: true);
        Assert.That(multi.HasData, Is.True);
    }

    [Test]
    public void 事件_按选项Key聚合总选择率与分幕且计入未选中页()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("rX", true, "win"));
        store.runs.Add(Run("rY", true, "loss"));
        // rX：act1 页选 A；act2 页 A 存在但没选
        store.eventChoices.Add(Evt("rX", true, 1, "EVT_TREE", "OPT_A", true));
        store.eventChoices.Add(Evt("rX", true, 2, "EVT_TREE", "OPT_A", false));
        // rY：act1 页 A 存在没选；act3 页 A 存在没选
        store.eventChoices.Add(Evt("rY", true, 1, "EVT_TREE", "OPT_A", false));
        store.eventChoices.Add(Evt("rY", true, 3, "EVT_TREE", "OPT_A", false));
        // 别的选项不应混入
        store.eventChoices.Add(Evt("rX", true, 1, "EVT_TREE", "OPT_B", true));

        EventStatBadge badge = WakuuStatBadgeQuery.BuildEvent(store, "EVT_TREE", "OPT_A", isMulti: true);
        Assert.Multiple(() =>
        {
            Assert.That(badge.HasData, Is.True);
            Assert.That(badge.Total.Offered, Is.EqualTo(4));
            Assert.That(badge.Total.Picked, Is.EqualTo(1));
            Assert.That(badge.Total.PercentText, Is.EqualTo("25%"));
            // 胜负：held=rX(win) 1 胜；skipped=rY(loss) 0 胜
            Assert.That(badge.Held.Runs, Is.EqualTo(1));
            Assert.That(badge.Held.Wins, Is.EqualTo(1));
            Assert.That(badge.Skipped.Runs, Is.EqualTo(1));
            Assert.That(badge.Skipped.Wins, Is.EqualTo(0));
            // 分幕 act1/2/3
            Assert.That(badge.ByAct.Select(a => a.Act), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(badge.ByAct[0].Chosen.Picked, Is.EqualTo(1)); // act1 选 1 次
        });

        string tip = WakuuStatBadgeFormat.FormatEventTipBody(badge, "");
        Assert.Multiple(() =>
        {
            Assert.That(tip, Does.Contain("选择率 1/4 25%"));
            Assert.That(tip, Does.Contain("第2幕 0/1 0%"));
            Assert.That(tip, Does.Contain("选了胜率 1/1 100%"));
            Assert.That(tip, Does.Contain("没选 0/1 0%"));
        });
    }

    [Test]
    public void 社区兜底正文标注来源并给出抓取率与胜率()
    {
        // 个人无记录 + 开启社区兜底时使用：面板需标注「来源：社区·皮皮军师」
        string body = WakuuStatBadgeFormat.FormatCommunityCardBody("（本地双控）", 0.314, 0.223, 0.198, 12480);
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("（本地双控）"));
            Assert.That(body, Does.Contain("来源：社区·皮皮军师"));
            Assert.That(body, Does.Contain("抓取率 31%"));
            Assert.That(body, Does.Contain("样本 12480"));
            Assert.That(body, Does.Contain("拿了胜率 22%"));
            Assert.That(body, Does.Contain("没拿 20%"));
            Assert.That(WakuuStatBadgeFormat.RateToPercentText(0.314), Is.EqualTo("31%"));
        });
    }

    [Test]
    public void 数据来源档位默认仅个人且融合按伪计数加权()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuStatBadgeSource.Normalize(null), Is.EqualTo(WakuuStatBadgeSource.PersonalOnly));
            Assert.That(WakuuStatBadgeSource.Normalize("weird"), Is.EqualTo(WakuuStatBadgeSource.PersonalOnly));
            Assert.That(WakuuStatBadgeSource.Normalize(WakuuStatBadgeSource.Blended), Is.EqualTo(WakuuStatBadgeSource.Blended));
        });

        // 个人 2/5(40%) + 社区 20%，伪计数强度 5 → (2 + 5*0.2) / (5 + 5) = 30%
        Assert.That(WakuuStatBadgeQuery.BlendPickRate(2, 5, 0.2, 12480), Is.EqualTo(0.3).Within(1e-9));
        // 社区无数据 → 退化为纯个人抓取率
        Assert.That(WakuuStatBadgeQuery.BlendPickRate(2, 5, null, 0), Is.EqualTo(0.4).Within(1e-9));
        // 个人无样本但有社区 → 直接用社区
        Assert.That(WakuuStatBadgeQuery.BlendPickRate(0, 0, 0.31, 100), Is.EqualTo(0.31).Within(1e-9));
    }

    [Test]
    public void 角标位置默认左下且四档解算正确()
    {
        // 默认左下：皮皮军师（SkadaHelper）把社区统计标签画在卡右侧，右下会与之重叠
        StatBadgePlacement bottomLeft = WakuuStatBadgeLayout.Resolve(
            WakuuStatBadgeCorner.Default, 64f, 20f, 8f, 5f);
        Assert.Multiple(() =>
        {
            Assert.That(WakuuStatBadgeCorner.Normalize(null), Is.EqualTo(WakuuStatBadgeCorner.BottomLeft));
            Assert.That(WakuuStatBadgeCorner.Normalize("weird"), Is.EqualTo(WakuuStatBadgeCorner.BottomLeft));
            Assert.That(bottomLeft.AnchorX, Is.EqualTo(0f));
            Assert.That(bottomLeft.AnchorY, Is.EqualTo(1f));
            Assert.That(bottomLeft.OffsetX, Is.EqualTo(8f));
            Assert.That(bottomLeft.OffsetY, Is.EqualTo(-25f));
        });

        StatBadgePlacement bottomRight = WakuuStatBadgeLayout.Resolve(
            WakuuStatBadgeCorner.BottomRight, 64f, 20f, 8f, 5f);
        Assert.Multiple(() =>
        {
            Assert.That(bottomRight.AnchorX, Is.EqualTo(1f));
            Assert.That(bottomRight.OffsetX, Is.EqualTo(-72f));
            Assert.That(bottomRight.OffsetY, Is.EqualTo(-25f));
        });

        StatBadgePlacement topRight = WakuuStatBadgeLayout.Resolve(
            WakuuStatBadgeCorner.TopRight, 64f, 20f, 8f, 5f);
        Assert.Multiple(() =>
        {
            Assert.That(topRight.AnchorY, Is.EqualTo(0f));
            Assert.That(topRight.OffsetY, Is.EqualTo(5f));
        });
    }
}
