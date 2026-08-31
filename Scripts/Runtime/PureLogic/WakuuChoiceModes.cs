namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 策略选择模式的取值常量（单一来源）。
/// first=第一个（最上）/ last=最后一个（默认）/ random=随机 / rare=稀有度最高。
/// 供事件选择、战斗内效果选牌（含附魔选牌）、配置规范化与纯逻辑单元测试共用。
/// </summary>
internal static class WakuuChoiceModes
{
    public const string First = "first";
    public const string Last = "last";
    public const string Random = "random";
    public const string Rare = "rare";
}
