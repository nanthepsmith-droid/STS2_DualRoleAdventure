using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace YuWanCardLocalCoopFix;

/// <summary>
/// 猪猪 mod（YuWanCard）遗物【队友帮付】在**本地多控**下的兼容补丁。
///
/// <para><b>现象</b>（用户原话）：「【队友帮付】遗物可以在商店让其它玩家付钱，但是我实际测试是不行的」——
/// 点按钮、选完队友之后什么都没发生（不扣钱、不买到东西）。</para>
///
/// <para><b>根因</b>（反编译 <c>YuWanCard.Content.dll</c> + 主 mod 回环服务对照）：这条链从头到尾都走
/// 原版联机的**定向**网络消息：</para>
/// <list type="number">
/// <item>选队友弹层先 <c>QueryAllTeammatesGold()</c> 向每位队友发 <c>TeammatePayGoldQueryMessage</c>
///   等 2 秒回包（本地席位不会回，只是超时后回退本机金币，所以弹层还能看）；</item>
/// <item>选定后 <c>SendRequest</c> 发 <c>TeammatePayRequestMessage</c> 给付款者 —— **这一步就断了**：
///   主 mod 的 <c>LocalLoopbackHostGameService.SendMessage&lt;T&gt;(message, playerId)</c> 是空实现
///   （只打一条「本地回环定向发消息」日志，不派发）⇒ 付款者永远收不到请求、不会弹确认框；</item>
/// <item>既然没有确认，也就没有 <c>TeammatePayResponseMessage</c> 回到发起者 ⇒
///   <c>ProcessPurchase</c> 永远不执行 ⇒ 购买不结算。</item>
/// </list>
///
/// <para>另外两处即使消息通了也不成立：<c>OnPayRequestReceived</c> 用 <c>LocalContext.GetMe()</c> 判
/// 「我是不是付款者」，而本地多控下 GetMe 只返回**当前前台**那一位；<c>DeductGoldLocally</c> 同样扣
/// GetMe 的钱 ⇒ 会扣错人。</para>
///
/// <para><b>做法</b>：只在本 mod 的回环会话里，接管第三方按钮的点击流程（前缀拦掉
/// <c>TeammatePayShopPatch.OnTeammatePayButtonPressed</c>）：</para>
/// <list type="number">
/// <item>直接读本机其他席位的真实金币，复用猪猪自己的「选择队友」弹层（不重画 UI）；</item>
/// <item>选定后**在本机扣那位席位的钱**（<c>PlayerCmd.LoseGold</c>，损失类型与猪猪一致 = Lost）；</item>
/// <item>再按猪猪原来的方式结算货物：<c>entry.OnTryPurchaseUnsynchronized(inventory, ignoreCost: true)</c>
///   （猪猪也是这么调的 —— 钱已经在付款者那边扣过了）；</item>
/// <item>购买失败则把金币退回给付款者，并记 WARN。</item>
/// </list>
/// 全程**不发任何网络消息**。真实联机（非回环 NetService）一律 `return true` 交回原版。
/// </summary>
internal static class TeammatePayLocalCompat
{
    /// <summary>等待玩家在弹层上做选择的兜底超时（超时只解除重入锁，不再结算）。</summary>
    private const int SelectionTimeoutMs = 180_000;

    private const string TargetMethodName = "OnTeammatePayButtonPressed";

    private static Type? _popupType;
    private static MethodInfo? _popupCreateMethod;
    private static MethodInfo? _popupWaitMethod;

    /// <summary>防重入：一次只处理一笔帮付（弹层是单例的 <c>NModalContainer</c>）。</summary>
    private static bool _running;

    internal static void Install(
        Harmony harmony,
        Type shopPatchType,
        Type popupType,
        List<string> patched,
        List<string> missing)
    {
        _popupType = popupType;
        _popupCreateMethod = popupType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(MerchantEntry), typeof(int), typeof(Dictionary<ulong, int>) },
            modifiers: null);
        _popupWaitMethod = popupType.GetMethod("WaitForSelection", BindingFlags.Public | BindingFlags.Instance);

        if (_popupCreateMethod == null || _popupWaitMethod == null)
        {
            missing.Add($"{popupType.Name}.Create/WaitForSelection");
            Log.Warn($"{Entry.LogPrefix} 「选择队友」弹层的方法签名不符（猪猪可能已更新），"
                + "【队友帮付】仍不可用: "
                + $"Create={(_popupCreateMethod != null ? "在" : "缺")}, "
                + $"WaitForSelection={(_popupWaitMethod != null ? "在" : "缺")}");
            // 弹层不可用时不挂补丁 —— 拦掉原流程只会更糟（点按钮彻底没反应）。
            return;
        }

        MethodInfo? target = null;
        try
        {
            List<MethodInfo> candidates = shopPatchType
                .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == TargetMethodName)
                .ToList();

            // 期望 6 个参数（slot, entry, cost, index, entryType, inventory）；形状变了就退化为唯一候选。
            target = candidates.FirstOrDefault(method => method.GetParameters().Length == 6)
                ?? (candidates.Count == 1 ? candidates[0] : null);

            if (target == null)
            {
                missing.Add($"{shopPatchType.Name}.{TargetMethodName}");
                Log.Warn($"{Entry.LogPrefix} 目标方法未找到（猪猪可能已更新），【队友帮付】保持原样: "
                    + $"{shopPatchType.Name}.{TargetMethodName}，候选={candidates.Count}");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(TeammatePayLocalCompat), nameof(OnPressedPrefix))));
            patched.Add($"{shopPatchType.Name}.{TargetMethodName}");
        }
        catch (Exception exception)
        {
            missing.Add($"{shopPatchType.Name}.{TargetMethodName}");
            Log.Warn($"{Entry.LogPrefix} 挂补丁失败: {shopPatchType.Name}.{TargetMethodName}: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 拦下猪猪的点击流程：仅在本地多控回环会话里改走本地结算；其它情况原样交回。
    /// </summary>
    private static bool OnPressedPrefix(
        MerchantEntry entry,
        int cost,
        MerchantInventory? inventory,
        ref Task __result)
    {
        if (!IsLocalLoopbackSession())
        {
            return true;
        }

        __result = RunLocalAsync(entry, cost, inventory);
        return false;
    }

    private static async Task RunLocalAsync(MerchantEntry entry, int cost, MerchantInventory? inventory)
    {
        if (_running)
        {
            Log.Info($"{Entry.LogPrefix} 队友帮付: 上一笔还没结束，忽略这次点击。");
            return;
        }

        _running = true;
        try
        {
            // RunManager.State 是 private（游戏只留了 DebugOnlyGetState 这个公开口子，主 mod 同口径）。
            RunState? state = RunManager.Instance?.DebugOnlyGetState();
            Player? requester = state == null ? null : LocalContext.GetMe(state);
            if (state == null || requester == null)
            {
                Log.Warn($"{Entry.LogPrefix} 队友帮付: 拿不到本局状态/发起者，放弃。");
                return;
            }

            List<Player> candidates = state.Players
                .Where(player => player.NetId != requester.NetId)
                .ToList();
            if (candidates.Count == 0)
            {
                Log.Warn($"{Entry.LogPrefix} 队友帮付: 本局没有其他席位可代付。");
                return;
            }

            Dictionary<ulong, int> golds = candidates.ToDictionary(player => player.NetId, player => player.Gold);
            // ⚠ 不要对 NetId 做 (int) 截断：Steam id 截断后是 `462261599` 这种陌生数字，
            // 实机判读时会被误当成"候选列表里是另一个席位"（2026-10-24 首次判读就差点踩中）。
            Log.Info($"{Entry.LogPrefix} 队友帮付（本地接管）: requester={requester.NetId}, cost={cost}, "
                + $"候选=[{string.Join(", ", candidates.Select(p => $"{p.NetId}:{p.Gold}G"))}]");

            Player? payer = await SelectPayerAsync(entry, cost, golds);
            if (payer == null)
            {
                Log.Info($"{Entry.LogPrefix} 队友帮付: 玩家取消/超时，未结算。");
                return;
            }

            if (payer.Gold < cost)
            {
                Log.Warn($"{Entry.LogPrefix} 队友帮付: 付款者金币不足，放弃。"
                    + $" payer={payer.NetId}, gold={payer.Gold}, cost={cost}");
                return;
            }

            await PlayerCmd.LoseGold(cost, payer, GoldLossType.Lost);
            Log.Info($"{Entry.LogPrefix} 队友帮付已扣款: payer={payer.NetId}, cost={cost}, "
                + $"剩余={(payer.Gold)}G");

            bool purchased;
            try
            {
                // 与猪猪原实现一致：钱已经在付款者那边扣掉，这里以 ignoreCost 结算货物。
                // 0.113：`MerchantEntry.OnTryPurchaseWrapper` 改名 `OnTryPurchaseUnsynchronized`（逐行等价）
        purchased = await entry.OnTryPurchaseUnsynchronized(inventory, ignoreCost: true);
            }
            catch (Exception exception)
            {
                Log.Error($"{Entry.LogPrefix} 队友帮付: 购买时抛异常，金币将退回: {exception}");
                await RefundAsync(payer, cost);
                return;
            }

            if (purchased)
            {
                Log.Info($"{Entry.LogPrefix} 队友帮付完成: payer={payer.NetId}, cost={cost}, "
                    + $"entry={entry.GetType().Name}, purchased=True");
            }
            else
            {
                Log.Warn($"{Entry.LogPrefix} 队友帮付: 货物结算失败（可能牌堆满/已售出），金币退回。"
                    + $" payer={payer.NetId}, cost={cost}, entry={entry.GetType().Name}");
                await RefundAsync(payer, cost);
            }
        }
        catch (Exception exception)
        {
            Log.Error($"{Entry.LogPrefix} 队友帮付（本地接管）失败: {exception}");
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>
    /// 复用猪猪自己的「选择队友」弹层：直接传入本机真实金币（不走它的网络查询），
    /// 等玩家点一位队友（或取消）。
    /// </summary>
    private static async Task<Player?> SelectPayerAsync(
        MerchantEntry entry,
        int cost,
        Dictionary<ulong, int> golds)
    {
        if (_popupType == null || _popupCreateMethod == null || _popupWaitMethod == null)
        {
            return null;
        }

        NModalContainer? modal = NModalContainer.Instance;
        if (modal == null)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 找不到 NModalContainer，放弃（稍后再点一次）。");
            return null;
        }

        if (modal.OpenModal != null)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 已有其它弹层打开，本次点击先忽略。");
            return null;
        }

        object? popup;
        try
        {
            popup = _popupCreateMethod.Invoke(null, new object?[] { entry, cost, golds });
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 创建「选择队友」弹层失败: {exception.GetType().Name}: {exception.Message}");
            return null;
        }

        if (popup is not Node node)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 弹层实例不是 Godot 节点，放弃。");
            return null;
        }

        Task waitTask;
        try
        {
            modal.Add(node, showBackstop: true);
            waitTask = (Task)_popupWaitMethod.Invoke(popup, null)!;
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 弹出「选择队友」失败: {exception.GetType().Name}: {exception.Message}");
            return null;
        }

        Task finished = await Task.WhenAny(waitTask, Task.Delay(SelectionTimeoutMs));
        if (finished != waitTask)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 等待选择超时（{SelectionTimeoutMs / 1000}s），本次不结算。");
            return null;
        }

        try
        {
            return waitTask.GetType().GetProperty("Result")?.GetValue(waitTask) as Player;
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 队友帮付: 读取选择结果失败: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>购买失败时把金币退回付款者（避免"扣了钱没拿到货"）。</summary>
    private static async Task RefundAsync(Player payer, int cost)
    {
        try
        {
            await PlayerCmd.GainGold(cost, payer);
            Log.Info($"{Entry.LogPrefix} 队友帮付已退款: payer={payer.NetId}, cost={cost}, 现有={payer.Gold}G");
        }
        catch (Exception exception)
        {
            Log.Error($"{Entry.LogPrefix} 队友帮付退款失败（请手动核对金币）: payer={payer.NetId}, cost={cost}, {exception}");
        }
    }

    /// <summary>
    /// 是否是本 mod（DualRoleAdventure / LocalMultiControl）的本地多控回环会话。
    /// 按类型名判定 —— 本补丁不引用主 mod 程序集（它没装就等于不是回环，交回原版）。
    /// </summary>
    private static bool IsLocalLoopbackSession()
    {
        INetGameService? netService = RunManager.Instance?.NetService;
        if (netService == null || !netService.IsConnected)
        {
            return false;
        }

        return string.Equals(netService.GetType().Name, "LocalLoopbackHostGameService", StringComparison.Ordinal);
    }
}
