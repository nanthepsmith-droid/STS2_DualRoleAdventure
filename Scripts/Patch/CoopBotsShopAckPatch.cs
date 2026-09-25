using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 第三方兼容（R1，硬阻塞）：**本地回环混合局进商店必现的 ACK 死锁**旁路补丁。
///
/// 病灶（`CoopBots.BotShopDriver.Tick`，分析 §5 R1）：host 为被接管席位下单时
/// <code>
/// if (instance.NetService is NetHostGameService host) expected = host.ConnectedPeers…;
/// else expected = state.Players.Where(p =&gt; !BotRegistry.IsBot(p.NetId) &amp;&amp; p.NetId != instance.NetService.NetId);
/// </code>
/// 本 mod 的 <see cref="LocalLoopbackHostGameService"/> 实现的是接口 <c>INetHostGameService</c> 而**不是**
/// 抽象类 <c>NetHostGameService</c> ⇒ 走 else 分支 ⇒ 在 2 席混合局里 `expected` 就是「另一个本地席位」；
/// 而回环的 <c>SendMessage</c> 只在本地打印日志、不产生远端 ACK ⇒
/// `expected.Any(id =&gt; !acknowledgments.ContainsKey(id))` 恒真 ⇒
/// 每 10s 重发同一单、`pending` 永不清 ⇒ `Finished()` 永假 ⇒ 商店「继续」永远按不下去。
///
/// 修法（分析 §5 方案 A）：在 <c>Tick</c> 上挂 **Postfix**，当且仅当
/// <c>instance.NetService</c> 是本 mod 的回环服务时，把 `expected` 清空 ——
/// 语义 = 「回环下没有远端 peer 需要确认」。下一次 Tick 读到的 `expected` 为空 ⇒ 直接进入结果校验
/// （`acknowledgments` 在回环下同样为空，校验自然通过）。
///
/// 边界（为什么这样就够）：
/// <list type="bullet">
/// <item>只在**我们的**回环服务上生效：真实联机（<c>NetHostGameService</c>）与单机
/// （单席时 else 分支本来就产出空名单）都不受影响；</item>
/// <item>不触碰 CB 任何逻辑，只清它自己那一处「等谁确认」的名单；</item>
/// <item>第三方类型/字段缺失（未装 Co-op Bots、版本漂移改名）时 <c>Prepare()</c> 返回 false，
/// 整个类被跳过，**不影响 PatchAll**（与 Koishi / RitsuLib 补丁同款保护）。</item>
/// </list>
/// </summary>
[HarmonyPatch]
internal static class CoopBotsShopAckPatch
{
    private const string TargetSignature = "CoopBots.BotShopDriver:Tick";
    private const string ExpectedFieldName = "expected";

    /// <summary>旁路命中的日志条数上限（商店里每笔交易都会命中，避免刷屏）。</summary>
    private const int MaxHitLogs = 5;

    private static readonly Harmony _lateHarmony = new Harmony("sts2.dualroleadventure.late.coopbots");

    private static MethodBase? _target;
    private static FieldInfo? _expectedField;
    private static MethodInfo? _expectedClear;
    private static PropertyInfo? _expectedCount;
    private static bool _applied;
    private static int _hitLogCount;

    private static bool Prepare()
    {
        _target = AccessTools.Method(TargetSignature);
        if (_target == null)
        {
            LocalMultiControlLogger.Info(
                "[CoopBots商店ACK] 未找到 CoopBots.BotShopDriver.Tick（未装 Co-op Bots 或加载顺序较晚），暂缓挂载。");
            return false;
        }

        if (!ResolveExpectedField())
        {
            return false;
        }

        _applied = true;
        LocalMultiControlLogger.Info("[CoopBots商店ACK] 已挂载本地回环商店 ACK 旁路后置补丁。");
        return true;
    }

    private static MethodBase? TargetMethod()
    {
        return _target;
    }

    /// <summary>
    /// Co-op Bots 程序集晚于本 mod 的 PatchAll 时（Prepare 找不到目标）在运行期补挂。
    /// 由 <see cref="CoopBotsAdapter.EnsureReady"/> 在探测成功后调用。
    /// </summary>
    internal static void TryApplyLate()
    {
        if (_applied)
        {
            return;
        }

        MethodBase? target = AccessTools.Method(TargetSignature);
        if (target == null)
        {
            return;
        }

        if (!ResolveExpectedField())
        {
            return;
        }

        try
        {
            MethodInfo? postfix = AccessTools.Method(typeof(CoopBotsShopAckPatch), nameof(Postfix));
            if (postfix == null)
            {
                return;
            }

            _lateHarmony.Patch(target, postfix: new HarmonyMethod(postfix));
            _applied = true;
            LocalMultiControlLogger.Info("[CoopBots商店ACK] 已延迟挂载本地回环商店 ACK 旁路后置补丁（Co-op Bots 晚于本 mod 加载）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[CoopBots商店ACK] 延迟挂载失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>
    /// 参数用位序 <c>__0</c>（= 原方法第一个参数 <c>RunManager instance</c>）而不是参数名，避免上游改名导致补丁静默失效。
    /// </summary>
    private static void Postfix(RunManager __0)
    {
        try
        {
            // 只在我们自己的回环服务上生效：真实联机 / 单机一律不干预。
            if (__0 == null || __0.NetService is not LocalLoopbackHostGameService)
            {
                return;
            }

            object? expected = _expectedField?.GetValue(null);
            if (expected == null)
            {
                return;
            }

            int count = _expectedCount?.GetValue(expected) is int value ? value : 0;
            if (count <= 0)
            {
                return;
            }

            _expectedClear?.Invoke(expected, null);
            if (_hitLogCount < MaxHitLogs)
            {
                _hitLogCount++;
                LocalMultiControlLogger.Info(
                    $"[CoopBots商店ACK] 本地回环下清空商店等待确认名单: count={count}, "
                    + $"seats=[{string.Join(",", DescribeSeats(expected))}]（回环无远端 peer，无需 ACK）");
            }
        }
        catch (Exception exception)
        {
            // 每帧都跑的补丁：异常一律吞掉（否则会以每帧一条的频率刷屏），且**绝不能**影响商店流程。
            if (_hitLogCount == 0)
            {
                _hitLogCount = MaxHitLogs;
                LocalMultiControlLogger.Warn($"[CoopBots商店ACK] 旁路执行异常（已忽略，此后不再记录）: {exception.Message}");
            }
        }
    }

    /// <summary>定位 `expected` 静态字段及其 Count / Clear 成员（字段改名 = 版本漂移，直接放弃挂载）。</summary>
    private static bool ResolveExpectedField()
    {
        _expectedField = AccessTools.Field("CoopBots.BotShopDriver:" + ExpectedFieldName);
        if (_expectedField == null)
        {
            LocalMultiControlLogger.Warn(
                $"[CoopBots商店ACK] CoopBots.BotShopDriver 里没有 `{ExpectedFieldName}` 字段（疑似版本漂移），"
                + "商店 ACK 旁路未挂载；本地回环混合局的商店可能卡住。");
            return false;
        }

        Type fieldType = _expectedField.FieldType;
        _expectedClear = AccessTools.Method(fieldType, "Clear", Type.EmptyTypes);
        _expectedCount = AccessTools.Property(fieldType, "Count");
        if (_expectedClear == null || _expectedCount == null)
        {
            LocalMultiControlLogger.Warn(
                $"[CoopBots商店ACK] `{ExpectedFieldName}`（{fieldType.Name}）缺少 Clear/Count 成员，"
                + "商店 ACK 旁路未挂载。");
            return false;
        }

        return true;
    }

    /// <summary>读出被等待确认的席位（日志用；类型未知，逐个按 object 迭代）。</summary>
    private static List<string> DescribeSeats(object expected)
    {
        List<string> seats = new List<string>();
        if (expected is not IEnumerable enumerable)
        {
            return seats;
        }

        foreach (object? item in enumerable)
        {
            seats.Add(item?.ToString() ?? "?");
        }

        return seats;
    }
}
