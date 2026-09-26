using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 效果事实层的动作类型（总规范 v2 §4/§6「Effect Extraction」）。
///
/// 口径：这一层**只回答"这张卡执行了什么动作"**（例如"对敌人施加名为 Poison 的 10 层状态"），
/// **不回答"Poison 值多少钱"** —— 那是 <see cref="WakuuGenericBehavior"/>（效果解释层，v2 §7）
/// 与后续评分层的事。两层的分离是"未知效果不等于没有效果"（v2 §5）能落地的前提。
/// </summary>
internal enum WakuuEffectKind
{
    /// <summary>直接伤害（读得出数值，通常来自 Damage 变量）。</summary>
    DirectDamage,

    /// <summary>直接格挡。</summary>
    DirectBlock,

    /// <summary>抽牌。</summary>
    CardDraw,

    /// <summary>获得能量。</summary>
    EnergyGain,

    /// <summary>治疗。</summary>
    Heal,

    /// <summary>施加状态（PowerVar 形式的层数，含 mod 自定义状态）。</summary>
    ApplyStatus,

    /// <summary>修改自身/他人的能力数值（力量、敏捷等）。</summary>
    ModifyPower,

    /// <summary>获得资源（锻造、辉星、召唤等非能量资源）。</summary>
    GainResource,

    /// <summary>切换形态/姿态类状态。</summary>
    ChangeState,

    /// <summary>失去生命（自伤 / 代价）。</summary>
    HpLoss,

    /// <summary>读不出动作（计算型伤害、纯脚本效果、未知 mod 卡）。</summary>
    Unknown,
}

/// <summary>效果作用目标（与出牌指向类型同口径，但独立定义，避免依赖游戏模型）。</summary>
internal enum WakuuEffectTarget
{
    /// <summary>无目标 / 不适用。</summary>
    None,

    /// <summary>自己 / 本方。</summary>
    Self,

    /// <summary>单个敌人。</summary>
    Enemy,

    /// <summary>全体敌人。</summary>
    AllEnemies,

    /// <summary>单个队友。</summary>
    Ally,

    /// <summary>全体队友。</summary>
    AllAllies,

    /// <summary>随机目标。</summary>
    Random,
}

/// <summary>
/// 通用行为（总规范 v2 §8/§9）——**跨 mod 泛化的关键**。
///
/// 决策层只依赖行为，不依赖具体名字：`Poison` / `Bleed` / `Burn` 只要实际行为相似，
/// 都归入 <see cref="DamageOverTime"/>，瓦库就"大致会用"这个 mod 角色（v2 §11）。
/// </summary>
internal enum WakuuGenericBehavior
{
    /// <summary>未知（v2 §8「Unknown Semantic」）——**必须配合正值的未知潜能，绝不等价于"没效果"**。</summary>
    Unknown,

    /// <summary>立刻造成伤害。</summary>
    DirectDamage,

    /// <summary>立刻获得格挡。</summary>
    DirectBlock,

    /// <summary>抽牌 / 过滤。</summary>
    CardDraw,

    /// <summary>产出资源（能量、锻造、辉星等）。</summary>
    ResourceGeneration,

    /// <summary>消耗资源（代价型）。</summary>
    ResourceConsumption,

    /// <summary>持续伤害（毒等）。</summary>
    DamageOverTime,

    /// <summary>攻击伤害放大（力量、易伤等改变输出的一侧）。</summary>
    DamageMultiplier,

    /// <summary>受到的伤害缩放（格挡/减伤/防御增幅）。</summary>
    DefenseMultiplier,

    /// <summary>治疗 / 回复。</summary>
    Healing,

    /// <summary>正面增益。</summary>
    Buff,

    /// <summary>负面减益（虚弱、脆弱等）。</summary>
    Debuff,

    /// <summary>长线成长（每回合累加的能力）。</summary>
    Scaling,

    /// <summary>形态/姿态切换。</summary>
    StateSwitch,

    /// <summary>条件触发（需要前置条件才生效，孤儿组件判定的输入）。</summary>
    ConditionalTrigger,

    /// <summary>延迟生效（下回合/回合结束结算）。</summary>
    DelayedEffect,

    /// <summary>标记类（引爆/消费型标记）。</summary>
    Mark,

    /// <summary>牌堆操作（弃牌/消耗/检索/排序）。</summary>
    DeckManipulation,

    /// <summary>代价/风险（自伤、失去资源、不可逆改动）。</summary>
    Risk,
}

/// <summary>
/// 知识来源（总规范 v2 §74 的优先级链）：
/// CurrentObserved &gt; CurrentParsed &gt; StaticKnown &gt; PersistentObserved &gt; Community &gt; DefaultHeuristic。
/// </summary>
internal enum WakuuEffectSource
{
    /// <summary>兜底启发式（什么都不知道时的保守推断）。</summary>
    DefaultHeuristic,

    /// <summary>静态语义表（<see cref="WakuuStaticEffectTable"/>）。</summary>
    Static,

    /// <summary>从游戏结构化数据解析出来（动态变量、关键词等）。</summary>
    Parsed,

    /// <summary>运行期观测（M6，暂未落地）。</summary>
    Observed,

    /// <summary>社区统计（SkadaHelper 等，本层不用）。</summary>
    Community,

    /// <summary>个人统计 / 真人学习先验（M4）。</summary>
    Personal,
}

/// <summary>
/// 一条效果事实（v2 §6 的 `{type, target, id, amount}` 结构）。
///
/// 例：`1 费 · 对敌人施加 10 层 Poison` ⇒
/// <c>Kind=ApplyStatus, Target=Enemy, Id="PoisonPower", Amount=10, Behavior=DamageOverTime, Source=Static</c>；
/// 换成不认识的 mod 状态 ⇒ <c>Behavior=Unknown, Confidence=低</c>，但**事实本身保留**（v2 §7）。
/// </summary>
internal readonly struct WakuuEffectFeature
{
    public WakuuEffectFeature(
        WakuuEffectKind kind,
        WakuuEffectTarget target,
        string id,
        int amount,
        WakuuGenericBehavior behavior,
        WakuuEffectSource source,
        float confidence)
    {
        Kind = kind;
        Target = target;
        Id = id ?? string.Empty;
        Amount = amount;
        Behavior = behavior;
        Source = source;
        Confidence = WakuuConfidence.Clamp(confidence);
    }

    public WakuuEffectKind Kind { get; }

    public WakuuEffectTarget Target { get; }

    /// <summary>状态 / 资源 / 动作的标识（未知 mod 状态时是它的原始 id，绝不丢）。</summary>
    public string Id { get; }

    /// <summary>数量（层数 / 点数）；0 表示"有该效果但数量读不出"。</summary>
    public int Amount { get; }

    /// <summary>通用行为（v2 §9）；未知时为 <see cref="WakuuGenericBehavior.Unknown"/>。</summary>
    public WakuuGenericBehavior Behavior { get; }

    public WakuuEffectSource Source { get; }

    /// <summary>本条效果语义的置信度（0~1）。</summary>
    public float Confidence { get; }

    /// <summary>语义是否已知（v2 §8「Known Semantic」）。</summary>
    public bool IsKnown => Behavior != WakuuGenericBehavior.Unknown;

    /// <summary>日志用短描述（中文，便于 grep 锚点）。</summary>
    public override string ToString()
    {
        string id = string.IsNullOrEmpty(Id) ? "-" : Id;
        return $"{Kind}({id}×{Amount}, {Behavior}, {Source}, 置信={Confidence:0.00})";
    }
}
