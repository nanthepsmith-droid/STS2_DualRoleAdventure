using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 删牌优先级的「过渡牌」档（用户 2026-11-18 口径）：
/// 一层抓取率高、二/三层抓取率低的牌按**落差**先删（它们是过渡牌，进中后期删很正常）。
///
/// 判据全部落在纯数据上（offer/pick 计数），所以这里直接构造个人记录器的行来钉：
/// ① 只有"一层高 + 后两层低 + 落差够 + 样本够"才算过渡牌；
/// ② 任何一项不满足都返回 0（= 退化成原规则表，绝不凭空造信号）；
/// ③ 切片（角色 / 单人多人）与其它个人信号同一口径，未结束局不计。
/// </summary>
[TestFixture]
public class WakuuTransitionCardTierTests
{
    private static PersonalStore Store(params string[] finishedRuns)
    {
        PersonalStore store = new();
        foreach (string runKey in finishedRuns)
        {
            store.runs.Add(new PersonalRunRecord { runKey = runKey, isMulti = true, result = "win", ts = 0 });
        }

        return store;
    }

    private static void AddOffers(
        PersonalStore store, string runKey, string card, int act, int offered, int picked,
        string character = "IRONCLAD", bool isMulti = true)
    {
        for (int i = 0; i < offered; i++)
        {
            store.cardOffers.Add(new PersonalCardOfferRecord
            {
                runKey = runKey,
                isMulti = isMulti,
                character = character,
                act = act,
                card = card,
                isRepeat = false,
                picked = i < picked,
                ts = 0,
            });
        }
    }

    [Test]
    public void 一层高后两层低_按落差给分()
    {
        PersonalStore store = Store("r1", "r2", "r3");
        // 一层 3/4 = 75%，二三层 0/4 = 0% ⇒ 落差 0.75
        AddOffers(store, "r1", "POMMEL_STRIKE", 1, 2, 1);
        AddOffers(store, "r2", "POMMEL_STRIKE", 1, 2, 2);
        AddOffers(store, "r1", "POMMEL_STRIKE", 2, 2, 0);
        AddOffers(store, "r2", "POMMEL_STRIKE", 3, 2, 0);

        double score = WakuuTransitionCardTier.ScoreOf(store, "POMMEL_STRIKE", isMulti: true, character: "IRONCLAD");

        Assert.That(score, Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void 一层抓取率不够高_不算过渡牌()
    {
        PersonalStore store = Store("r1", "r2", "r3");
        AddOffers(store, "r1", "CLOTHESLINE", 1, 5, 2);  // 40%
        AddOffers(store, "r1", "CLOTHESLINE", 2, 3, 0);

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "CLOTHESLINE", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(0.0));
    }

    [Test]
    public void 后两层抓取率也不低_不算过渡牌()
    {
        PersonalStore store = Store("r1", "r2", "r3");
        AddOffers(store, "r1", "SHRUG_IT_OFF", 1, 4, 4);  // 100%
        AddOffers(store, "r1", "SHRUG_IT_OFF", 2, 4, 3);  // 75%（后两层仍然有人要）

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "SHRUG_IT_OFF", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(0.0));
    }

    [Test]
    public void 样本不足_不算过渡牌()
    {
        PersonalStore store = Store("r1", "r2");
        AddOffers(store, "r1", "TWIN_STRIKE", 1, 2, 2);  // 一层只出现 2 次（< 3）
        AddOffers(store, "r1", "TWIN_STRIKE", 2, 1, 0);  // 后两层只出现 1 次（< 2）

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "TWIN_STRIKE", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(0.0));
    }

    [Test]
    public void 落差刚好擦边_不算过渡牌()
    {
        PersonalStore store = Store("r1", "r2");
        AddOffers(store, "r1", "ANGER", 1, 4, 2);  // 50%（刚好到下限）
        AddOffers(store, "r1", "ANGER", 2, 4, 1);  // 25%… 但落差 0.25 ≥ 0.20 会成立

        // 换成 0.50 vs 0.35：落差 0.15 < 0.20 ⇒ 不成立
        PersonalStore edge = Store("r1", "r2");
        AddOffers(edge, "r1", "ANGER", 1, 4, 2);   // 50%
        AddOffers(edge, "r1", "ANGER", 2, 20, 7);  // 35%

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(edge, "ANGER", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(0.0),
            "50% 与 35% 只差 15 个百分点，达不到 20 的门槛");
        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "ANGER", isMulti: true, character: "IRONCLAD"),
            Is.GreaterThan(0.0),
            "对照组：落差 25 个百分点应当成立");
    }

    [Test]
    public void 切片口径_角色与单人多人分开()
    {
        PersonalStore store = Store("r1", "r2");
        AddOffers(store, "r1", "POMMEL_STRIKE", 1, 4, 4, character: "IRONCLAD", isMulti: true);
        AddOffers(store, "r1", "POMMEL_STRIKE", 2, 4, 0, character: "IRONCLAD", isMulti: true);
        // 同 id 但另一个角色 / 另一种模式的样本完全不该参与
        AddOffers(store, "r2", "POMMEL_STRIKE", 1, 4, 0, character: "SILENT", isMulti: true);
        AddOffers(store, "r2", "POMMEL_STRIKE", 2, 4, 4, character: "SILENT", isMulti: true);

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "POMMEL_STRIKE", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(1.0).Within(1e-9));
        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "POMMEL_STRIKE", isMulti: true, character: "SILENT"),
            Is.EqualTo(0.0), "SILENT 那批是反过来的（一层 0、后两层 100%），不是过渡牌");
        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "POMMEL_STRIKE", isMulti: false, character: "IRONCLAD"),
            Is.EqualTo(0.0), "单人局切片没有样本");
    }

    [Test]
    public void 未结束的局不计入()
    {
        PersonalStore store = new();
        store.runs.Add(new PersonalRunRecord { runKey = "ongoing", isMulti = true, result = "ongoing", ts = 0 });
        AddOffers(store, "ongoing", "POMMEL_STRIKE", 1, 4, 4);
        AddOffers(store, "ongoing", "POMMEL_STRIKE", 2, 4, 0);

        Assert.That(
            WakuuTransitionCardTier.ScoreOf(store, "POMMEL_STRIKE", isMulti: true, character: "IRONCLAD"),
            Is.EqualTo(0.0));
    }

    [Test]
    public void 空参数与未知卡_返回零()
    {
        PersonalStore store = Store("r1");
        Assert.That(WakuuTransitionCardTier.ScoreOf(null!, "X", null, null), Is.EqualTo(0.0));
        Assert.That(WakuuTransitionCardTier.ScoreOf(store, null, null, null), Is.EqualTo(0.0));
        Assert.That(WakuuTransitionCardTier.ScoreOf(store, string.Empty, null, null), Is.EqualTo(0.0));
        Assert.That(WakuuTransitionCardTier.ScoreOf(store, "NOT_IN_STORE", null, null), Is.EqualTo(0.0));
    }
}
