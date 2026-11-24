using System;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 极速模式（Blitz）商店自动购买的判定表（用户 2026-11-23 填表口径）。
///
/// 契约：① 诅咒上限 5（买完仍 ≤5）；② 卡牌奖励类只在牌组 &lt; 档位上限+5 时买；
/// ③ 「诅咒选择」只在次数不够用 + 还有足够多值得买的条目时才买；
/// ④ 「诅咒回复」按 40% / 20% 两档（商店后必是精英）；⑤ 其余条目按各自条件。
/// </summary>
[TestFixture]
public class BlitzShopBuyPolicyTests
{
    [Test]
    public void 极速商店条目枚举顺序与游戏一致()
    {
        // 运行层按 (int) 直接映射：上游增删 / 改名时这条会先炸，别等实机买错东西。
        BlitzShopEntryKind[] local = Enum.GetValues<BlitzShopEntryKind>();
        MegaCrit.Sts2.Core.Entities.Merchant.BlitzMerchantEntryType[] game =
            Enum.GetValues<MegaCrit.Sts2.Core.Entities.Merchant.BlitzMerchantEntryType>();

        Assert.That(local, Has.Length.EqualTo(game.Length), "条目数量必须一致");
        for (int i = 0; i < game.Length; i++)
        {
            Assert.That((int)local[i], Is.EqualTo((int)game[i]), $"{game[i]} 的枚举值");
            Assert.That(local[i].ToString(), Is.EqualTo(game[i].ToString()), "名字必须逐字一致，否则映射会静默错位");
        }
    }

    [Test]
    public void 诅咒档与稀有档标记()
    {
        Assert.That(BlitzShopBuyPolicy.IsCursed(BlitzShopEntryKind.Glam), Is.True);
        Assert.That(BlitzShopBuyPolicy.IsCursed(BlitzShopEntryKind.TwoRelics), Is.True);
        Assert.That(BlitzShopBuyPolicy.IsCursed(BlitzShopEntryKind.Relic), Is.False);
        Assert.That(BlitzShopBuyPolicy.IsCursed(BlitzShopEntryKind.UpgradedRareCard), Is.False);

        Assert.That(BlitzShopBuyPolicy.IsRare(BlitzShopEntryKind.PrismaticCardReward), Is.True);
        Assert.That(BlitzShopBuyPolicy.IsRare(BlitzShopEntryKind.RareCardReward), Is.True);
        Assert.That(BlitzShopBuyPolicy.IsRare(BlitzShopEntryKind.UpgradedRareCard), Is.True);
        Assert.That(BlitzShopBuyPolicy.IsRare(BlitzShopEntryKind.Relic), Is.False);
    }

    [Test]
    public void 卡牌升级服务_没有非基础牌可升级就不买()
    {
        Assert.That(Decide(BlitzShopEntryKind.CardUpgrade, Base()).Buy, Is.False);

        var ctx = Base();
        ctx.UpgradableNonBasicCount = 1;
        Assert.That(Decide(BlitzShopEntryKind.CardUpgrade, ctx).Buy, Is.True);
    }

    [Test]
    public void 卡牌奖励类_牌组到上限加五张就不买了()
    {
        var ctx = Base();
        ctx.DeckSize = 44;
        ctx.DeckLimit = 40;
        Assert.That(Decide(BlitzShopEntryKind.CardRewards, ctx).Buy, Is.True, "还没到上限+5");

        ctx.DeckSize = 45;
        Assert.That(Decide(BlitzShopEntryKind.CardRewards, ctx).Buy, Is.False);

        foreach (BlitzShopEntryKind kind in new[]
                 {
                     BlitzShopEntryKind.UpgradedCardReward,
                     BlitzShopEntryKind.ColorlessCardReward,
                     BlitzShopEntryKind.EnchantedCardReward,
                     BlitzShopEntryKind.PrismaticCardReward,
                     BlitzShopEntryKind.RareCardReward,
                     BlitzShopEntryKind.UpgradedRareCard,
                 })
        {
            Assert.That(Decide(kind, ctx).Buy, Is.False, $"{kind} 也应受牌组上限约束");
        }
    }

    [Test]
    public void 大卡组档_牌组上限是无穷大_不能整型溢出成全部不买()
    {
        // DeckLimit = int.MaxValue 时 "DeckLimit + 5" 会溢出成负数 ⇒ 卡牌奖励全被跳过（回归哨兵）。
        var ctx = Base();
        ctx.DeckLimit = int.MaxValue;
        ctx.DeckSize = 500;

        Assert.That(Decide(BlitzShopEntryKind.CardRewards, ctx).Buy, Is.True);
        Assert.That(Decide(BlitzShopEntryKind.UpgradedRareCard, ctx).Buy, Is.True);
        Assert.That(Decide(BlitzShopEntryKind.CardRewardOfEachRarity, ctx).Buy, Is.True);
    }

    [Test]
    public void 随机升级_非基础可升级牌少于两张就不买()
    {
        var ctx = Base();
        ctx.UpgradableNonBasicCount = 1;
        Assert.That(Decide(BlitzShopEntryKind.RandomUpgrades, ctx).Buy, Is.False);

        ctx.UpgradableNonBasicCount = 2;
        Assert.That(Decide(BlitzShopEntryKind.RandomUpgrades, ctx).Buy, Is.True);

        ctx.DeckSize = 40; // 档位上限（无 +5 余量）
        Assert.That(Decide(BlitzShopEntryKind.RandomUpgrades, ctx).Buy, Is.False, "牌组到上限就不再塞升级");
    }

    [Test]
    public void 变化服务_要有可变化的基础牌且未到上限()
    {
        var ctx = Base();
        Assert.That(Decide(BlitzShopEntryKind.CardTransform, ctx).Buy, Is.False);

        ctx.ChangeableBasicCount = 2;
        Assert.That(Decide(BlitzShopEntryKind.CardTransform, ctx).Buy, Is.True);

        ctx.DeckSize = 40;
        Assert.That(Decide(BlitzShopEntryKind.CardTransform, ctx).Buy, Is.False);
    }

    [Test]
    public void 删牌_有诅咒或牌组超上限才买()
    {
        var ctx = Base();
        ctx.DeckSize = 20;
        Assert.That(Decide(BlitzShopEntryKind.CardRemoval, ctx).Buy, Is.False, "没诅咒也没超上限");

        ctx.RemovableCurseCount = 1;
        ctx.RemovableJunkCount = 1;
        Assert.That(Decide(BlitzShopEntryKind.CardRemoval, ctx).Buy, Is.True, "有可删诅咒就删");

        ctx.RemovableCurseCount = 0;
        ctx.RemovableJunkCount = 3;
        ctx.DeckSize = 41;
        ctx.DeckLimit = 40;
        Assert.That(Decide(BlitzShopEntryKind.CardRemoval, ctx).Buy, Is.True, "牌组超上限也删");

        var noJunk = Base();
        noJunk.DeckSize = 45;
        noJunk.DeckLimit = 40;
        noJunk.RemovableJunkCount = 0;
        Assert.That(Decide(BlitzShopEntryKind.CardRemoval, noJunk).Buy, Is.False, "没有垃圾牌可删");
    }

    [Test]
    public void 删牌_只有不可删诅咒时不买_r401()
    {
        // 【进阶之灾】带【永恒】词条 ⇒ 删牌屏不显示 ⇒ 它单独在牌组里不该触发"买删牌"。
        var ctx = Base();
        ctx.DeckSize = 20;
        ctx.DeckLimit = 40;
        ctx.CurseCount = 1;           // 牌组里确实有 1 张诅咒（进阶之灾）
        ctx.RemovableCurseCount = 0;  // 但可删的为 0
        ctx.RemovableJunkCount = 1;   // 还有一张打击可删（所以第一条闸门不挡）
        Assert.That(Decide(BlitzShopEntryKind.CardRemoval, ctx).Buy, Is.False,
            "没可删诅咒、牌组也没超上限 ⇒ 不花钱删打击");
    }

    [Test]
    public void 药水_栏满且无可换才不买()
    {
        var ctx = Base();
        ctx.HasOpenPotionSlot = true;
        Assert.That(Decide(BlitzShopEntryKind.Potions, ctx).Buy, Is.True);

        ctx.HasOpenPotionSlot = false;
        ctx.HasBloodPotion = true;
        Assert.That(Decide(BlitzShopEntryKind.Potions, ctx).Buy, Is.True, "有鲜血药水可喝掉腾位");

        ctx.HasBloodPotion = false;
        ctx.LowestPotionRarity = 0;
        Assert.That(Decide(BlitzShopEntryKind.Potions, ctx).Buy, Is.True, "栏内最低是普通 ⇒ 值得换");

        ctx.LowestPotionRarity = BlitzShopBuyPolicy.PotionRarityRare;
        Assert.That(Decide(BlitzShopEntryKind.Potions, ctx).Buy, Is.False, "栏内最低已是稀有 ⇒ 不值得换");
    }

    [Test]
    public void 附魔服务_按该附魔的目标牌数判定_探测不到时用兜底()
    {
        var ctx = Base();
        ctx.NonBasicEnchantableCount = 5;
        ctx.EnchantTargetCount = 0;
        Assert.That(Decide(BlitzShopEntryKind.CardEnchant, ctx).Buy, Is.False, "该附魔没有目标牌");

        ctx.EnchantTargetCount = 1;
        Assert.That(Decide(BlitzShopEntryKind.CardEnchant, ctx).Buy, Is.True);

        ctx.EnchantTargetCount = -1; // 探测失败 ⇒ 用兜底
        Assert.That(Decide(BlitzShopEntryKind.CardEnchant, ctx).Buy, Is.True);
    }

    [Test]
    public void 遗物与升级稀有卡_无条件买_只受牌组上限约束()
    {
        Assert.That(Decide(BlitzShopEntryKind.Relic, Base()).Buy, Is.True);
        Assert.That(Decide(BlitzShopEntryKind.UpgradedRareCard, Base()).Buy, Is.True);

        var full = Base();
        full.DeckSize = 45;
        full.DeckLimit = 40;
        Assert.That(Decide(BlitzShopEntryKind.UpgradedRareCard, full).Buy, Is.False);
        Assert.That(Decide(BlitzShopEntryKind.Relic, full).Buy, Is.True, "遗物不占牌组");
    }

    [Test]
    public void 华彩_诅咒到上限或没有能力高费牌就不买()
    {
        var ctx = Base();
        ctx.GlamTargetCount = 2;
        Assert.That(Decide(BlitzShopEntryKind.Glam, ctx).Buy, Is.True);

        ctx.GlamTargetCount = 0;
        Assert.That(Decide(BlitzShopEntryKind.Glam, ctx).Buy, Is.False);

        ctx.GlamTargetCount = 2;
        ctx.CurseCount = BlitzShopBuyPolicy.CurseCap;
        Assert.That(Decide(BlitzShopEntryKind.Glam, ctx).Buy, Is.False, "诅咒到上限就不换了");
    }

    [Test]
    public void 诅咒选择_只在次数不够用且值得买时才买()
    {
        var ctx = Base();
        ctx.CurseCount = 0;

        ctx.RemainingChoices = 3;
        ctx.DesirableUnboughtCount = 5;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.False, "剩 3 次时白花 1 次");

        ctx.RemainingChoices = 2;
        ctx.DesirableUnboughtCount = 3;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.True, "剩 2 次且还有 3 个想买的");

        ctx.DesirableUnboughtCount = 2;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.False, "剩 2 次只要 3 个才划算");

        ctx.RemainingChoices = 1;
        ctx.DesirableUnboughtCount = 2;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.True, "剩 1 次且有 2 个想买的");

        ctx.DesirableUnboughtCount = 1;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.False);

        ctx.DesirableUnboughtCount = 4;
        ctx.CurseCount = BlitzShopBuyPolicy.CurseCap;
        Assert.That(Decide(BlitzShopEntryKind.TwoMoreChoices, ctx).Buy, Is.False, "诅咒到上限");
    }

    [Test]
    public void 诅咒卡牌奖励与诅咒升级稀有_受诅咒上限约束()
    {
        var ctx = Base();
        ctx.CurseCount = BlitzShopBuyPolicy.CurseCap - 1;
        Assert.That(Decide(BlitzShopEntryKind.CardRewardOfEachRarity, ctx).Buy, Is.True);
        Assert.That(Decide(BlitzShopEntryKind.UpgradedRareCardReward, ctx).Buy, Is.True);

        ctx.CurseCount = BlitzShopBuyPolicy.CurseCap;
        Assert.That(Decide(BlitzShopEntryKind.CardRewardOfEachRarity, ctx).Buy, Is.False);
        Assert.That(Decide(BlitzShopEntryKind.UpgradedRareCardReward, ctx).Buy, Is.False);
    }

    [Test]
    public void 诅咒遗物_一局最多买两次()
    {
        var ctx = Base();
        Assert.That(Decide(BlitzShopEntryKind.TwoRelics, ctx).Buy, Is.True);

        ctx.CursedRelicBuysThisRun = BlitzShopBuyPolicy.MaxCursedRelicBuys - 1;
        Assert.That(Decide(BlitzShopEntryKind.TwoRelics, ctx).Buy, Is.True);

        ctx.CursedRelicBuysThisRun = BlitzShopBuyPolicy.MaxCursedRelicBuys;
        Assert.That(Decide(BlitzShopEntryKind.TwoRelics, ctx).Buy, Is.False);
    }

    [Test]
    public void 诅咒回复_40百分点考虑_20百分点无视诅咒必买()
    {
        var healthy = Base();
        healthy.Hp = 90;
        healthy.MaxHp = 100;
        Assert.That(Decide(BlitzShopEntryKind.FullHealAndMaxHp, healthy).Buy, Is.False, "血多不买");

        var fullHp = Base();
        fullHp.Hp = 100;
        fullHp.MaxHp = 100;
        fullHp.CurseCount = 10;
        Assert.That(Decide(BlitzShopEntryKind.FullHealAndMaxHp, fullHp).Buy, Is.False, "满血回满无收益");

        var low = Base();
        low.Hp = 40;
        low.MaxHp = 100;
        Assert.That(Decide(BlitzShopEntryKind.FullHealAndMaxHp, low).Buy, Is.True, "HP=40% 正好在考虑档内");

        low.CurseCount = BlitzShopBuyPolicy.CurseCap;
        Assert.That(Decide(BlitzShopEntryKind.FullHealAndMaxHp, low).Buy, Is.False, "40% 档仍受诅咒上限约束");

        var critical = Base();
        critical.Hp = 19;
        critical.MaxHp = 100;
        critical.CurseCount = 99;
        Assert.That(Decide(BlitzShopEntryKind.FullHealAndMaxHp, critical).Buy, Is.True, "HP<20% 无视诅咒数");
    }

    [Test]
    public void 优先级_保命高于一切_药水最低()
    {
        var ctx = Base();
        ctx.Hp = 10;
        ctx.MaxHp = 100;
        int critical = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.FullHealAndMaxHp, ctx);
        int choice = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.TwoMoreChoices, ctx);
        int rewards = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.CardRewards, ctx);
        int relics = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.Relic, ctx);
        int removal = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.CardRemoval, ctx);
        int potions = BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.Potions, ctx);

        Assert.That(critical, Is.LessThan(choice));
        Assert.That(choice, Is.LessThan(rewards));
        Assert.That(rewards, Is.LessThan(relics));
        Assert.That(relics, Is.LessThan(removal));
        Assert.That(removal, Is.LessThan(potions));

        var healthy = Base();
        healthy.Hp = 90;
        healthy.MaxHp = 100;
        Assert.That(
            BlitzShopBuyPolicy.Priority(BlitzShopEntryKind.FullHealAndMaxHp, healthy),
            Is.GreaterThan(critical), "血多时回满不再排最前");
    }

    [Test]
    public void 每条不买的判定都带原因码()
    {
        var ctx = Base();
        foreach (BlitzShopEntryKind kind in Enum.GetValues<BlitzShopEntryKind>())
        {
            BlitzShopDecision decision = BlitzShopBuyPolicy.Evaluate(kind, ctx);
            Assert.That(decision.Reason, Is.Not.Null.And.Not.Empty, $"{kind} 必须给原因码（日志要求）");
        }
    }

    private static BlitzShopContext Base()
    {
        return new BlitzShopContext
        {
            RemainingChoices = 3,
            Hp = 70,
            MaxHp = 100,
            DeckSize = 20,
            DeckLimit = 40,
            CurseCount = 0,
            UpgradableNonBasicCount = 0,
            ChangeableBasicCount = 0,
            RemovableJunkCount = 4,
            NonBasicEnchantableCount = 0,
            GlamTargetCount = 0,
            HasOpenPotionSlot = true,
            HasBloodPotion = false,
            LowestPotionRarity = -1,
            DesirableUnboughtCount = 0,
            CursedRelicBuysThisRun = 0,
        };
    }

    private static BlitzShopDecision Decide(BlitzShopEntryKind kind, BlitzShopContext context)
    {
        return BlitzShopBuyPolicy.Evaluate(kind, context);
    }
}
