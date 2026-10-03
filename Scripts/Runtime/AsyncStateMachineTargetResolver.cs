using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 解析「异步方法本体所在的状态机 <c>MoveNext</c>」的反射工具（r202）。
///
/// **为什么需要它**：C# 的 async 方法本体不在 kickoff 方法里，而在编译器生成的状态机类型
/// （如 <c>CardCmd+&lt;Transform&gt;d__13</c>）的 <c>MoveNext</c> 中（异常栈里就能看到
/// `CardCmd+&lt;Transform&gt;d__13.MoveNext_Patch1`）。想拦/兜底这段异步流程，必须挂到 **MoveNext** 上。
///
/// ⚠ **坑（r201 实测踩到，标记 r202 修）**：Roslyn 生成的状态机 <c>MoveNext</c> 是
/// <c>private</c>（元数据实见 `MoveNext attrs=Private, Final, Virtual, HideBySig, VtableLayoutMask`，
/// 类型本身是 `NestedPrivate, Sealed`）—— 用 <c>GetMethod("MoveNext", BindingFlags.Public)</c> 会**返回 null**，
/// 于是"静默降级"成只挂 kickoff（= 只在进入时让开一次，挡不住异步窗口里的写入）。
/// 所以一律用 <c>Public | NonPublic | DeclaredOnly</c>，并额外兼容 Roslyn 可能的显式接口实现名
/// （`System.Runtime.CompilerServices.IAsyncStateMachine.MoveNext`）。
///
/// 纯反射、零游戏类型依赖 ⇒ 可用测试工程里的一个 dummy async 方法直接单测（见
/// <c>AsyncStateMachineTargetResolverTests</c>），游戏更新导致解析失效时**离线**就能发现。
/// </summary>
internal static class AsyncStateMachineTargetResolver
{
    private const BindingFlags AllDeclared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// 取异步方法 <paramref name="kickoff"/> 的状态机 <c>MoveNext</c>；解析不到时返回 null。
    /// </summary>
    public static MethodInfo? FindMoveNext(MethodInfo? kickoff)
    {
        if (kickoff == null)
        {
            return null;
        }

        Type? stateMachine = kickoff.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        if (stateMachine == null)
        {
            // 属性拿不到（被裁剪/异常形态）时按名字扫嵌套类型兜底：状态机名形如 <Transform>d__13。
            stateMachine = kickoff.DeclaringType?
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.Name.Contains(kickoff.Name, StringComparison.Ordinal)
                    && FindMoveNextOn(candidate) != null);
        }

        return FindMoveNextOn(stateMachine);
    }

    /// <summary>在给定类型上找 <c>MoveNext</c>（含 private / 显式接口实现形态）。</summary>
    public static MethodInfo? FindMoveNextOn(Type? stateMachine)
    {
        return stateMachine?.GetMethods(AllDeclared).FirstOrDefault(method =>
            method.Name.Equals("MoveNext", StringComparison.Ordinal)
            || method.Name.EndsWith(".MoveNext", StringComparison.Ordinal));
    }
}
