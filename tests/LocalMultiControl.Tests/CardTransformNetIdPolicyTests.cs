using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 手牌变换期间 <c>LocalContext.NetId</c> 处理策略（r109）纯逻辑测试。
/// 覆盖「前台角色变换要钉 NetId」「后台角色变换要让开 NetId」，
/// 重点是 r109 的核心场景：自动出牌期间 NetId 已被 RunWatchdogAsync 钉在瓦库身上，
/// 此时必须显式让开，否则原版会到前台手牌找原卡节点并抛
/// <c>Couldn't get hand node for original card ...</c> → 出牌中断。
/// </summary>
[TestFixture]
public class CardTransformNetIdPolicyTests
{
    [Test]
    public void 非本地牌主_一律不动()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CardTransformNetIdPolicy.Decide(false, true, true), Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(CardTransformNetIdPolicy.Decide(false, false, true), Is.EqualTo(CardTransformNetIdAction.None));
            Assert.That(CardTransformNetIdPolicy.Decide(false, false, false), Is.EqualTo(CardTransformNetIdAction.None));
        });
    }

    [Test]
    public void 前台角色变换_NetId未对齐时钉到牌主人()
    {
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, isOwnerForeground: true, currentNetIdIsOwner: false),
            Is.EqualTo(CardTransformNetIdAction.PinToOwner));
    }

    [Test]
    public void 前台角色变换_NetId已对齐时不动()
    {
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, true, true),
            Is.EqualTo(CardTransformNetIdAction.None));
    }

    [Test]
    public void 后台角色变换_NetId已是牌主人时必须让开()
    {
        // r109 核心场景：自动出牌期间看门狗把 NetId 钉在瓦库身上 → 必须显式让开，
        // 「什么都不做」（r59 旧行为）会照样抛异常。
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, isOwnerForeground: false, currentNetIdIsOwner: true),
            Is.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner));
    }

    [Test]
    public void 后台角色变换_NetId本来就不是牌主人时不动()
    {
        // 原版 IsMine=false 自会跳过视觉，无需干预（保持 r59 既有行为）。
        Assert.That(
            CardTransformNetIdPolicy.Decide(true, false, false),
            Is.EqualTo(CardTransformNetIdAction.None));
    }

    [Test]
    public void 真值表_不变式成立()
    {
        foreach (bool isOwnerLocal in new[] { true, false })
        {
            foreach (bool isOwnerForeground in new[] { true, false })
            {
                foreach (bool netIdIsOwner in new[] { true, false })
                {
                    CardTransformNetIdAction action =
                        CardTransformNetIdPolicy.Decide(isOwnerLocal, isOwnerForeground, netIdIsOwner);

                    if (!isOwnerLocal)
                    {
                        Assert.That(action, Is.EqualTo(CardTransformNetIdAction.None),
                            $"非本地牌主不应干预: fg={isOwnerForeground}, netIdIsOwner={netIdIsOwner}");
                        continue;
                    }

                    if (isOwnerForeground)
                    {
                        // 前台角色的变换必须让 IsMine=true → 绝不能"让开"
                        Assert.That(action, Is.Not.EqualTo(CardTransformNetIdAction.ShiftAwayFromOwner),
                            $"前台角色变换不能让开 NetId: netIdIsOwner={netIdIsOwner}");
                    }
                    else
                    {
                        // 后台角色的变换必须让 IsMine=false → 绝不能"钉到主人"
                        Assert.That(action, Is.Not.EqualTo(CardTransformNetIdAction.PinToOwner),
                            $"后台角色变换不能钉到牌主人: netIdIsOwner={netIdIsOwner}");
                    }
                }
            }
        }
    }
}
