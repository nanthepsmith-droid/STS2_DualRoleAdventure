using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// 常驻轮询节点：挂在 <c>SceneTree.Root</c> 下，每 <see cref="PreloadStallPolicy.PollIntervalMs"/>
/// 毫秒驱动一次 <see cref="PreloadStallRegistry.Poll"/>。
///
/// 为什么要自建节点：原版驱动预加载的 <c>NAssetLoader._Process</c> 只会处理
/// <c>_currentSession</c>，而本次要救的故障恰恰是「它没跑 / 会话卡在 <c>_currentSession</c>」——
/// 挂在同一条链路上就救不了。自建节点只做「看时间 + 放行」，不碰资源加载。
///
/// 挂载点用 <c>Root</c> 而不是某个房间节点：战斗房销毁重建时它必须活着。
/// <c>CallDeferred("add_child")</c> 是因为登记点可能在非主线程（<c>LoadInTheBackground</c>）。
/// </summary>
public partial class PreloadStallWatchdog : Node
{
    private const string NodeName = "PreloadStallGuardWatchdog";

    /// <summary>挂载失败（或刚挂还不在树里）时的重试节流。</summary>
    private const long AttachRetryIntervalMs = 1000L;

    private static PreloadStallWatchdog? _instance;
    private static long _lastAttachAttemptMs;

    private long _lastPollMs;
    private bool _processReported;

    /// <summary>是否已成功挂载并**真的在树里**（`CallDeferred` 成功不代表已入树）。</summary>
    internal static bool IsAttached => _instance != null && GodotObject.IsInstanceValid(_instance) && _instance.IsInsideTree();

    /// <summary>懒挂载（每 <see cref="AttachRetryIntervalMs"/> 最多尝试一次，失败会自愈重试）。</summary>
    internal static void EnsureAttached()
    {
        try
        {
            if (IsAttached)
            {
                return;
            }

            long now = PreloadStallRegistry.NowMs;
            if (now - _lastAttachAttemptMs < AttachRetryIntervalMs)
            {
                return;
            }

            _lastAttachAttemptMs = now;

            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            {
                return;
            }

            PreloadStallWatchdog? existing = tree.Root.GetNodeOrNull<PreloadStallWatchdog>(NodeName);
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var watchdog = new PreloadStallWatchdog
            {
                Name = NodeName,
                // 场景暂停（例如过场时）也要继续看门，否则正好在需要它的时候停摆。
                ProcessMode = ProcessModeEnum.Always
            };

            tree.Root.CallDeferred("add_child", watchdog);
            _instance = watchdog;
            Log.Info($"[PreloadStallGuard] 轮询节点已挂载: {NodeName}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[PreloadStallGuard] 轮询节点挂载失败（下次预加载时会重试）: {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (!_processReported)
        {
            // 这条日志是「轮询真的在跑」的唯一硬证据：没有它 = CallDeferred 没生效，兜底不会工作。
            _processReported = true;
            Log.Info($"[PreloadStallGuard] 轮询节点已进入 _Process（在跟踪={PreloadStallRegistry.PendingCount}）");
        }

        long now = PreloadStallRegistry.NowMs;
        if (now - _lastPollMs < PreloadStallPolicy.PollIntervalMs)
        {
            return;
        }

        _lastPollMs = now;
        try
        {
            PreloadStallRegistry.Poll(now);
        }
        catch (Exception ex)
        {
            // 兜底补丁自己绝不能把游戏弄崩：出问题只记日志。
            Log.Warn($"[PreloadStallGuard] 轮询异常: {ex.Message}");
        }
    }
}
