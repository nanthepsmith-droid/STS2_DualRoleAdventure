using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 手牌变换期间 <c>LocalContext.NetId</c> 处理策略（r109 / r185）纯逻辑测试。
/// 覆盖「前台角色变换要钉 NetId」「后台角色变换要让开 NetId」，
/// 重点是 r109 的核心场景：自动出牌期间 NetId 已被 RunWatchdogAsync 钉在瓦库身上，
/// 此时必须显式让开，否则原版会到前台手牌找原卡节点并抛
/// <c>Couldn't get hand node for original card ...</c> → 出牌中断。
/// r185（BUG-25）补第三维度「原牌节点是否存在」：回合结束在手里触发的变换
/// （实机：AncientsAwakened「唯我」）在前台刚切、手牌 UI 未建时，
/// 即便 owner=前台且 NetId=owner 也会抛异常并炸穿回合循环（整场战斗软锁）——
/// 节点缺失时唯一安全的动作是让 IsMine=false 跳过整段视觉。
/// </summary>
[TestFixture]
public class CardTransformNetIdPolicyTests
{
    [Test]
    public void 非本地牌主_一律不动()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CardTransformNetIdPolicy.Decide(false, true, true, handNodeExists: true), Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(CardTransformNetIdPolicy.Decide(false, false, true, handNodeExists: true), Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(CardTransformNetIdPolicy.Decide(false, false, false, handNodeExists: true), Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(CardTransformNetIdPolicy.Decide(false, true, true, handNodeExists: false), Is.EqualTo(CardTransformNetIdAction.None));
        });
    }

    [Test]
    public void 前台角色变换_节点存在_NetId未对齐时钉到牌主人()
    {
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, isOwnerForeground: true, currentNetIdIsOwner: false, handNodeExists: true),
            Is.EqualTo(CardTransformNetIdAction.PinToOwner));
    }

    [Test]
    public void 前台角色变换_节点存在_NetId已对齐时不动()
    {
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, true, true, handNodeExists: true),
            Is.EqualTo(CardTransformNetIdAction.None));
    }

    [Test]
    public void 后台角色变换_节点存在_NetId已是牌主人时必须让开()
    {
        // r109 核心场景：自动出牌期间看门狗把 NetId 钉在瓦库身上 → 必须显式让开，
        // 「什么都不做」（r59 旧行为）会照样抛异常。
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, isOwnerForeground: false, currentNetIdIsOwner: true, handNodeExists: true),
            Is.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner));
    }

    [Test]
    public void 后台角色变换_节点存在_NetId本来就不是牌主人时不动()
    {
        // 原版 IsMine=false 自会跳过视觉，无需干预（保持 r59 既有行为）。
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, false, false, handNodeExists: true),
            Is.EqualTo(CardTransformNetIdAction.None));
    }

    [Test]
    public void 节点缺失_NetId是主人时必须让开_否则不动()
    {
        // BUG-25（r185）：原牌节点不存在 ⇒ 原版视觉分支必然抛异常。
        // 关键新增用例 = owner=前台 且 NetId=owner（r184「唯我」实机组合）：
        // 旧判定返回 None ⇒ 照抛 ⇒ 回合循环死亡 ⇒ 整场战斗软锁。
        // NetId 本来就不是主人时 IsMine 已为 false，视觉自会跳过 ⇒ 不动。
        Assert.Multiple(() =>
        {
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, isOwnerForeground: true, currentNetIdIsOwner: true, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, isOwnerForeground: true, currentNetIdIsOwner: false, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, isOwnerForeground: false, currentNetIdIsOwner: true, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, isOwnerForeground: false, currentNetIdIsOwner: false, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.None));
        });
    }

    [Test]
    public void 节点缺失_NetId本来就不是主人时不动()
    {
        // IsMine 已经是 false，原版自会跳过视觉，无需干预。
        Assert.Multiple(() =>
        {
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, true, false, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, false, false, handNodeExists: false),
                Is.EqualTo(CardTransformNetIdAction.None));
        });
    }

    [Test]
    public void 节点未探_维持r109既有行为()
    {
        // 探针没跑（不在战斗/没有变换牌）⇒ 与 r109 三参数版逐字等价。
        Assert.Multiple(() =>
        {
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, isOwnerForeground: true, currentNetIdIsOwner: false, handNodeExists: null),
                Is.EqualTo(CardTransformNetIdAction.PinToOwner));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, true, true, handNodeExists: null),
                Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, false, true, handNodeExists: null),
                Is.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner));
            Assert.That(
                CardTransformNetIdPolicy.Decide(true, false, false, handNodeExists: null),
                Is.EqualTo(CardTransformNetIdAction.None));
        });
    }

    [Test]
    public void 真值表_逐格期望成立()
    {
        foreach (bool isOwnerLocal in new[] { true, false })
        {
            foreach (bool isOwnerForeground in new[] { true, false })
            {
                foreach (bool netIdIsOwner in new[] { true, false })
                {
                    foreach (bool? handNodeExists in new bool?[] { true, false, null })
                    {
                        CardTransformNetIdAction action = CardTransformNetIdPolicy.Decide(
                            isOwnerLocal, isOwnerForeground, netIdIsOwner, handNodeExists);
                        string context = $"fg={isOwnerForeground}, netIdIsOwner={netIdIsOwner}, node={handNodeExists?.ToString() ?? "null"}";

                        // 期望动作 = 实现语义的逐格展开（写死，防回归漂移）：
                        // 非本地牌主 → None；
                        // 节点缺失 → 视觉必抛，让 IsMine=false（NetId 已是主人时显式让开，否则不动）；
                        // 节点存在/未探 → 前台要对齐（未对齐就钉），后台要让开（已对齐就显式让）。
                        CardTransformNetIdAction expected;
                        if (!isOwnerLocal)
                        {
                            expected = CardTransformNetIdAction.None;
                        }
                        else if (handNodeExists == false)
                        {
                            expected = netIdIsOwner
                                ? CardTransformNetIdAction.ShiftAwayFromOwner
                                : CardTransformNetIdAction.None;
                        }
                        else if (isOwnerForeground)
                        {
                            expected = netIdIsOwner
                                ? CardTransformNetIdAction.None
                                : CardTransformNetIdAction.PinToOwner;
                        }
                        else
                        {
                            expected = netIdIsOwner
                                ? CardTransformNetIdAction.ShiftAwayFromOwner
                                : CardTransformNetIdAction.None;
                        }

                        Assert.That(action, Is.EqualTo(expected), $"真值表漂移: {context}");
                    }
                }
            }
        }
    }
}
