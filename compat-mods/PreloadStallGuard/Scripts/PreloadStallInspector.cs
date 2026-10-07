using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Assets;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// 对原版 <see cref="AssetLoadingSession"/> 私有状态的只读探针 + 唯一的写操作（强制放行）。
///
/// 为什么必须反射：会话的进度（四个队列）与完成源（<c>_completionSource</c>）全是 private，
/// 原版只暴露 <see cref="AssetLoadingSession.IsCompleted"/> 与 <see cref="AssetLoadingSession.Task"/>。
/// 我们既不替换也不拷贝它的逻辑，只用「队列计数变了没有」判停滞、用 <c>TrySetResult</c> 放行等待者。
/// 字段名与 sts2src 反编译件一致（`AssetLoadingSession.cs:14-55`），游戏更新后若改名会退化成
/// 「探针读不到 → 计数恒为 0 → 恒定判定停滞」，因此启动/首次跟踪时会打日志说明探针是否可用。
/// </summary>
internal static class PreloadStallInspector
{
    private static readonly string[] QueueFieldNames = { "_toLoad", "_loading", "_finalizing", "_vfxScenes" };

    private static readonly FieldInfo? CompletionSourceField =
        typeof(AssetLoadingSession).GetField("_completionSource", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? NameField =
        typeof(AssetLoadingSession).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo?[] QueueFields = ResolveQueueFields();

    private static FieldInfo?[] ResolveQueueFields()
    {
        var fields = new FieldInfo?[QueueFieldNames.Length];
        for (int i = 0; i < QueueFieldNames.Length; i++)
        {
            fields[i] = typeof(AssetLoadingSession).GetField(QueueFieldNames[i], BindingFlags.Instance | BindingFlags.NonPublic);
        }

        return fields;
    }

    /// <summary>探针是否完整（字段名全部命中）。任一缺失说明游戏改了内部结构，需要适配。</summary>
    internal static bool ProbeAvailable => CompletionSourceField != null && NameField != null && QueueFields[0] != null;

    /// <summary>
    /// 进度签名：四个队列计数按 16 位分段落位。用签名而不是「计数之和」是因为资源在
    /// <c>_toLoad → _loading → _finalizing</c> 之间搬运时**总和不变**，用和会把正常推进误判成停滞。
    /// </summary>
    internal static long ProgressSignature(AssetLoadingSession session)
    {
        long signature = 0L;
        for (int i = 0; i < QueueFields.Length; i++)
        {
            long count = Math.Max(0, QueueCount(QueueFields[i], session));
            signature |= count << (16 * i);
        }

        return signature;
    }

    /// <summary>给日志用的一行描述（会话名 + 四个队列计数）。</summary>
    internal static string Describe(AssetLoadingSession session)
    {
        string name = "unknown";
        try
        {
            name = NameField?.GetValue(session) as string ?? "unknown";
        }
        catch
        {
            // 读不到名字不影响判定
        }

        return $"name={name}, toLoad={QueueCount(QueueFields[0], session)}, loading={QueueCount(QueueFields[1], session)}, "
            + $"finalizing={QueueCount(QueueFields[2], session)}, vfx={QueueCount(QueueFields[3], session)}";
    }

    /// <summary>
    /// 强制放行：把会话的完成源置为已完成，等它的 <c>await session.Task</c> 得以返回。
    /// 不假装资源已加载 —— 后续若真的取用，会走原版「未缓存则同步加载」路径（<c>AssetCache.Get</c> 的 WARN 分支）。
    /// </summary>
    internal static bool TryForceComplete(AssetLoadingSession session)
    {
        try
        {
            object? source = CompletionSourceField?.GetValue(session);
            return source is TaskCompletionSource<bool> completion && completion.TrySetResult(true);
        }
        catch
        {
            return false;
        }
    }

    private static int QueueCount(FieldInfo? field, AssetLoadingSession session)
    {
        if (field == null)
        {
            return 0;
        }

        try
        {
            object? value = field.GetValue(session);
            return value is ICollection collection ? collection.Count : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 抓当前调用栈里「有意义的」前几帧，用于回答「谁创建了 / 谁在等这个会话」。
    /// 过滤掉 Harmony 与 .NET 基础设施帧，只留业务调用方（第三方 mod 会以自己的命名空间出现）。
    /// 不做符号解析（<c>false</c> = 不抓文件/行号），避免拖慢热路径。
    /// </summary>
    internal static string DescribeCurrentStack(int maxFrames = 6)
    {
        try
        {
            var trace = new StackTrace(false);
            List<string> frames = new List<string>();
            for (int i = 0; i < trace.FrameCount && frames.Count < maxFrames; i++)
            {
                MethodBase? method = trace.GetFrame(i)?.GetMethod();
                string? declaring = method?.DeclaringType?.FullName;
                if (method == null || string.IsNullOrEmpty(declaring))
                {
                    continue;
                }

                if (declaring.StartsWith("PreloadStallGuard", StringComparison.Ordinal)
                    || declaring.StartsWith("HarmonyLib", StringComparison.Ordinal)
                    || declaring.StartsWith("System.", StringComparison.Ordinal)
                    || declaring.StartsWith("Godot.", StringComparison.Ordinal))
                {
                    continue;
                }

                frames.Add($"{declaring}.{method.Name}");
            }

            return frames.Count == 0 ? "unknown" : string.Join(" <- ", frames);
        }
        catch
        {
            return "unavailable";
        }
    }
}
