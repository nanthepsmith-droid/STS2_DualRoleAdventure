using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardReward), "OnSelect")]
internal static class CardRewardPatch
{
    private struct SenderState
    {
        internal bool IsPatched;
        internal ulong? PreviousContextNetId;
        internal ulong PreviousSenderId;
    }

    [HarmonyPrefix]
    private static void Prefix(CardReward __instance, ref SenderState __state)
    {
        __state = default;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService loopback)
        {
            return;
        }

        // r147：**第三方席位**（Co-op Bots 的合成 Bot）的卡牌奖励不归我们接管。
        // 这里把 LocalContext.NetId 改成奖励归属者，等于告诉游戏"这张奖励是本机玩家的"——
        // `CardReward.OnSelect` 里 `LocalContext.IsMe(player)` 为真就会去弹真正的
        // `NCardRewardSelectionScreen`。而 Co-op Bots 只为"被接管的真人席位"抑制这个弹屏
        // （`AutoPilot.IsAutopiloted`，对合成 Bot 恒 false），它给合成 Bot 设计的是**远端作答**
        // （`WaitForRemoteChoice` → `BotRemoteChoicePatch` 用大脑选牌、根本不弹屏）。
        // 实机（2026-09-25，marker r146）后果：Bot 的奖励屏与本 mod / 第三方的弹层推入互相踩踏，
        // `NOverlayStack.Push` 的 `add_child` 失败 → 名单里留下没有节点的「幽灵弹层」
        // → 共享背板变暗 + 吞掉全部输入（整局软锁，只能重开）。12 次失败里有 12 次都是 2 张卡牌奖励的第二张。
        // 本地席位的归属钉住（r134 修 BUG-13 的那条）保持不变。
        if (!LocalSeatSource.IsLocalSeat(__instance.Player.NetId))
        {
            LocalMultiControlLogger.Info(
                $"第三方席位卡牌奖励交回原版远端作答（本 mod 不切上下文）: player={__instance.Player.NetId}, "
                + $"驱动={(CoopBotsAdapter.Drives(__instance.Player.NetId) ? "Co-op Bots" : "未识别（未装/未就绪）")}");
            return;
        }

        __state.IsPatched = true;
        __state.PreviousContextNetId = LocalContext.NetId;
        __state.PreviousSenderId = loopback.NetId;

        LocalContext.NetId = __instance.Player.NetId;
        loopback.SetCurrentSenderId(__instance.Player.NetId);
        LocalMultiControlLogger.Info($"卡牌奖励切换到奖励归属角色: player={__instance.Player.NetId}");
    }

    [HarmonyPostfix]
    private static void Postfix(SenderState __state)
    {
        if (!__state.IsPatched)
        {
            return;
        }

        if (RunManager.Instance.NetService is LocalLoopbackHostGameService loopback && loopback.NetId != __state.PreviousSenderId)
        {
            loopback.SetCurrentSenderId(__state.PreviousSenderId);
        }

        LocalContext.NetId = __state.PreviousContextNetId;
    }
}
