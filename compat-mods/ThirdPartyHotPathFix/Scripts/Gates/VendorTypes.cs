using System;

namespace ThirdPartyHotPathFix.Scripts.Gates;

/// <summary>
/// 四家第三方 mod 的「关键类型」句柄，由 <c>Entry</c> 在打补丁时注入
/// （compat mod 刻意**不引用**第三方 dll：他们换实现 / 没装时本 mod 必须能干净地什么都不做）。
///
/// 这里只登记两类：
/// 1. **激活判据用的能力 / 卡类型**（判断"这家的机制这局到底在不在场"）；
/// 2. **YUICore 的两个公共接口**（判断"有没有遗物要接收触发通知 / 复读"）。
/// </summary>
internal static class VendorTypes
{
    /// <summary>Koishi：<c>SealAllPower</c>（在场时遗物 hook 会被吞掉）。</summary>
    internal static Type? SealAllPower;

    /// <summary>figure_Saya：<c>GeneralizedResistancePower</c>（在场时可见减益 hook 会被吞掉）。</summary>
    internal static Type? GeneralizedResistancePower;

    /// <summary>newsanguo：手牌里的 <c>AntiMagicField</c>（汉中）。</summary>
    internal static Type? AntiMagicFieldCard;

    /// <summary>newsanguo：敌人身上的 <c>IronCavalryPower</c>（铁骑）。</summary>
    internal static Type? IronCavalryPower;

    /// <summary>YUICore：<c>IAfterRelicTriggeredHook</c>（遗物触发后通知）。</summary>
    internal static Type? AfterRelicTriggeredHook;

    /// <summary>YUICore：<c>IRelicTriggerRepeater</c>（遗物触发复读）。</summary>
    internal static Type? RelicTriggerRepeater;
}
