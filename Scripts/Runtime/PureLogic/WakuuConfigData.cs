using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管配置的磁盘数据模型（纯数据，无 IO 无游戏依赖）。
/// 字段名即 json 字段名（camelCase），默认值 = 原版低语耳环行为。
/// </summary>
internal sealed class WakuuConfigData
{
    public bool useVakuuForm { get; set; }

    public bool playAllCards { get; set; } = true;

    public bool backgroundMode { get; set; } = true;

    public bool suppressVanillaEarring { get; set; } = true;

    public bool autoClaimCards { get; set; } = true;

    public bool autoClaimGoldRelics { get; set; } = true;

    /// <summary>药水奖励自动领取（满栏按稀有度换药/先喝鲜血），默认开。</summary>
    public bool autoClaimPotions { get; set; } = true;

    public bool autoChooseEvents { get; set; } = true;

    public bool autoRestChoice { get; set; } = true;

    /// <summary>战斗中自动用药水：默认关（拍板：保守版写死规则，先观察）。</summary>
    public bool autoUsePotions { get; set; }

    public bool neowAutoChoose { get; set; }

    /// <summary>
    /// 社区统计辅助：读 SkadaHelper（皮皮军师）的社区大数据为卡牌奖励与事件选项加权（可行性分析 §8.2）。
    /// 默认关（关 = 纯最左 / 事件沿用 eventChoiceMode）；开启后若未安装 SkadaHelper 或查表无数据，
    /// 一律静默回退到与关闭时完全一致的行为。
    /// </summary>
    public bool skadaAssist { get; set; }

    /// <summary>
    /// 智能选牌优先级（可行性分析 §9.1/9.2）：开启后事件里的牌库删除/变化选牌按优先级表选取
    /// （删除优先 诅咒→状态→任务→打击→防御；变化优先变掉打击/防御并硬排除坏牌）。
    /// 默认关（关 = 纯 cardPickMode 策略，与既有行为一致）。
    /// </summary>
    public bool smartPick { get; set; }

    /// <summary>
    /// 附魔智能选牌：开启后附魔选牌按「原版附魔一览表」用户填写的规则表挑牌
    /// （每种附魔各有优先级，如腐化挑伤害最高的攻击牌、注能挑能抽 3 张以上的技能牌）。
    /// 默认开（用户已按表填写，关 = 回到纯 cardPickMode 策略）。
    /// 关闭或该附魔填了"维持现状"时行为与既有完全一致。
    /// </summary>
    public bool smartEnchant { get; set; } = true;

    public string eventChoiceMode { get; set; } = WakuuChoiceModes.First;

    public string cardPickMode { get; set; } = WakuuChoiceModes.Last;

    /// <summary>战斗中决策大脑：heuristic=启发式（默认，即现有出牌逻辑）/ auto=自动探测可用求解器。</summary>
    public string wakuuBrain { get; set; } = WakuuBrainModes.Heuristic;
}

/// <summary>瓦库大脑模式取值常量（单一来源）。</summary>
internal static class WakuuBrainModes
{
    public const string Heuristic = "heuristic";

    public const string Auto = "auto";
}

/// <summary>
/// 配置的 JSON 解析/序列化纯函数（无 IO）。解析失败抛 JsonException，由调用方决定兜底策略。
/// </summary>
internal static class WakuuConfigJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>解析配置 json；输入 null 返回 null（沿用当前生效值的语义由调用方处理）。</summary>
    public static WakuuConfigData? Parse(string json)
    {
        return JsonSerializer.Deserialize<WakuuConfigData>(json, JsonOptions);
    }

    /// <summary>序列化为缩进 json 文本。</summary>
    public static string Serialize(WakuuConfigData data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }
}
