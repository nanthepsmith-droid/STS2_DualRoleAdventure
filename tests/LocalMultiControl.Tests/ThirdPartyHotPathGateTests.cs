using ThirdPartyHotPathFix.Scripts.Gates;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// `compat-mods/ThirdPartyHotPathFix` 里三个纯逻辑件的单测（源码以链接方式编进本工程）。
///
/// 这些闸门每秒被调用上万次，一旦缓存失效键写错，后果是**悄悄改变第三方玩法**
/// （该跳的没跳只是白慢；不该跳的跳了 = SealAll / 泛抗性 / 能力压制失效），
/// 所以失效语义必须锁死在测试里。
/// </summary>
[TestFixture]
public sealed class HookFilterPerfGateTests
{
    [Test]
    public void CombatSnapshot_第一次判定必须重算()
    {
        var cache = new CombatSnapshotCache();
        object state = new object();

        Assert.That(cache.TryGet(state, 0, 12, 3, out _, out _, out _), Is.False);
    }

    [Test]
    public void CombatSnapshot_同一局面命中且三个结论一起返回()
    {
        var cache = new CombatSnapshotCache();
        object state = new object();
        cache.Commit(state, version: 7, allies: 12, enemies: 3, koishi: true, saya: false, ironCavalry: true);

        Assert.That(cache.TryGet(state, 7, 12, 3, out bool koishi, out bool saya, out bool iron), Is.True);
        Assert.That(koishi, Is.True);
        Assert.That(saya, Is.False);
        Assert.That(iron, Is.True);
    }

    [Test]
    public void CombatSnapshot_能力版本变化必须重算()
    {
        var cache = new CombatSnapshotCache();
        object state = new object();
        cache.Commit(state, version: 7, allies: 12, enemies: 3, koishi: false, saya: false, ironCavalry: false);

        Assert.That(
            cache.TryGet(state, 8, 12, 3, out _, out _, out _),
            Is.False,
            "新挂 / 移除能力后必须立刻看见（否则会在机制在场时错误跳过过滤链）");
    }

    [Test]
    public void CombatSnapshot_换战斗或生物数变化必须重算()
    {
        var cache = new CombatSnapshotCache();
        object first = new object();
        object second = new object();
        cache.Commit(first, version: 7, allies: 12, enemies: 3, koishi: false, saya: false, ironCavalry: false);

        Assert.That(cache.TryGet(second, 7, 12, 3, out _, out _, out _), Is.False, "换一场战斗");
        Assert.That(cache.TryGet(first, 7, 11, 3, out _, out _, out _), Is.False, "我方少了一个席位");
        Assert.That(cache.TryGet(first, 7, 12, 4, out _, out _, out _), Is.False, "敌人分裂 / 召唤");
    }

    [Test]
    public void CombatSnapshot_Reset后必须重算()
    {
        var cache = new CombatSnapshotCache();
        object state = new object();
        cache.Commit(state, version: 7, allies: 12, enemies: 3, koishi: false, saya: false, ironCavalry: false);

        cache.Reset();

        Assert.That(cache.TryGet(state, 7, 12, 3, out _, out _, out _), Is.False);
    }

    [Test]
    public void YuiRelic_遗物总数不变时命中缓存()
    {
        var cache = new YuiRelicCache();
        object run = new object();

        Assert.That(cache.TryGet(run, relicCount: 140, out _), Is.False, "没算过就该 miss");

        cache.Commit(run, relicCount: 140, armed: false);
        Assert.That(cache.TryGet(run, 140, out bool armed), Is.True);
        Assert.That(armed, Is.False);
    }

    [Test]
    public void YuiRelic_拿到或失去遗物必须重算()
    {
        var cache = new YuiRelicCache();
        object run = new object();
        cache.Commit(run, relicCount: 140, armed: false);

        Assert.That(cache.TryGet(run, 141, out _), Is.False, "刚捡到遗物 ⇒ 可能带来复读机");
    }

    [Test]
    public void YuiRelic_换存档或Reset必须重算()
    {
        var cache = new YuiRelicCache();
        object first = new object();
        object second = new object();
        cache.Commit(first, relicCount: 140, armed: true);

        Assert.That(cache.TryGet(second, 140, out _), Is.False, "换一局");
        cache.Reset();
        Assert.That(cache.TryGet(first, 140, out _), Is.False);
    }

    [Test]
    public void Verdict_只在结论翻转时允许打日志()
    {
        var counter = new VerdictCounter(minLogIntervalMs: 0);

        Assert.That(counter.Record(skip: true), Is.True, "首次判定");
        Assert.That(counter.Record(skip: true), Is.False, "结论没变必须闭嘴");
        Assert.That(counter.Record(skip: false), Is.True, "翻成激活");
        Assert.That(counter.Record(skip: false), Is.False);
        Assert.That(counter.Record(skip: true), Is.True, "翻回跳过");

        Assert.That(counter.Skipped, Is.EqualTo(3));
        Assert.That(counter.Armed, Is.EqualTo(2));
        Assert.That(counter.Total, Is.EqualTo(5));
        Assert.That(counter.Format(), Is.EqualTo("跳过=3 激活=2"));
    }

    [Test]
    public void Verdict_翻转太快时吞掉日志但继续计数()
    {
        // 「汉中」判据看手牌，一回合里反复抽/弃会让结论来回翻 ⇒ 必须有最小间隔
        var counter = new VerdictCounter(minLogIntervalMs: 3600_000);

        Assert.That(counter.Record(skip: true), Is.True, "首次判定（初始化锚点）必须打");
        Assert.That(counter.Record(skip: false), Is.False, "1 小时内不再打，但计数照记");

        Assert.That(counter.Skipped, Is.EqualTo(1));
        Assert.That(counter.Armed, Is.EqualTo(1));
    }
}
