using System;
using System.Collections.Generic;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 回放（run history）面板的席位名登记表（2026-11-20 收工 ④）。
///
/// <para>做法：打开某一局的回放时把「该局玩家 id 列表 + 本机平台 id + 该回放屏节点」登记进来；
/// <see cref="LocalSeatLabels"/> 在会话解析不到时来这里兜底 ⇒ 界面上不再是一串数字。</para>
///
/// <para><b>为什么带 owner 节点</b>：回放屏关掉（节点释放 / 不可见）后登记必须失效，
/// 否则之后真联机局里若某个队友 id 恰好等于「本机 id + k」，会被误显示成 <c>Player k+1</c>。
/// 判据 = 节点仍有效且可见（本地多控的页面守卫老坑：<c>Pop</c> 只隐藏、不移出树，只看
/// <c>IsInsideTree</c> 会把已关掉的屏当"还在"）。</para>
/// </summary>
internal static class RunHistorySeatLabels
{
    private static Node? _owner;
    private static ulong[] _playerIds = Array.Empty<ulong>();
    private static ulong _localPlayerId;
    private static string _runSignature = string.Empty;

    /// <summary>登记「当前正在看的那一局回放」的席位表。</summary>
    internal static void Register(Node owner, IReadOnlyList<ulong>? playerIds, ulong localPlayerId, string source)
    {
        try
        {
            ulong[] snapshot = playerIds == null ? Array.Empty<ulong>() : new List<ulong>(playerIds).ToArray();
            string signature = localPlayerId + ":" + string.Join(",", snapshot);
            _owner = owner;
            _playerIds = snapshot;
            _localPlayerId = localPlayerId;

            // 同一局的多个玩家图标会各调一次，去重只打一条锚点。
            if (signature == _runSignature)
            {
                return;
            }

            _runSignature = signature;
            LocalMultiControlLogger.Info(
                $"回放席位表已登记: source={source}, localPlatformId={localPlayerId}, "
                + $"players=[{string.Join(",", snapshot)}], 可解析={snapshot.Length >= 2}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"回放席位表登记失败（回放里的名字保持原版）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>该 id 是不是当前回放里的本地连续席位；是则给 <c>Player N</c>，否则 null（调用方放行原版）。</summary>
    internal static string? TryResolve(ulong playerId)
    {
        try
        {
            if (_playerIds.Length == 0 || !IsOwnerAlive())
            {
                return null;
            }

            int? slotIndex = RunHistorySeatLabelPolicy.ResolveSlotIndex(_playerIds, _localPlayerId, playerId);
            if (slotIndex == null)
            {
                return null;
            }

            // 回放里分不出「这局当时是瓦库还是真人」（存档没有这个标记）⇒ 统一按真人的 Player N 显示，
            // 号位口径与局内 Tab / 小键盘切人一致（= 席位序 + 1）。
            string? label = SeatDisplayNamePolicy.ResolveFromSlotIndex(slotIndex.Value, SeatDisplayKind.Human);
            if (label != null)
            {
                LocalMultiControlLogger.Info(
                    $"玩家显示名解析: 回放席位 seat={slotIndex.Value + 1}, player={playerId}, label={label}");
            }

            return label;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"回放席位名解析失败（放行原版名字）: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>清空登记（回放屏关闭 / 退到主菜单时用）。</summary>
    internal static void Clear(string reason)
    {
        if (_playerIds.Length == 0)
        {
            return;
        }

        _playerIds = Array.Empty<ulong>();
        _owner = null;
        _runSignature = string.Empty;
        LocalMultiControlLogger.Info($"回放席位表已清空: reason={reason}");
    }

    private static bool IsOwnerAlive()
    {
        if (_owner == null || !GodotObject.IsInstanceValid(_owner))
        {
            return false;
        }

        if (!_owner.IsInsideTree())
        {
            return false;
        }

        return _owner is not CanvasItem canvas || canvas.Visible;
    }
}
