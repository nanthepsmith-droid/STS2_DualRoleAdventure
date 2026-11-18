using System;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.addons.mega_text;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗内**左侧玩家列表名牌**的落盘（2026-11-18 §排期 6，r376）。
///
/// <para><b>为什么必须直接写</b>（r375 实机判读）：名牌文字由原版 <c>_Ready</c> 里的
/// <c>SetTextAutoSize(PlatformUtil.GetPlayerNameRaw(…, Player.NetId))</c> 写死，而实机拿到的是
/// 「0 号行 = 我的 Steam 昵称（Ohlaohla）、其余行 = 玩家 id 原串」—— 说明**这条调用没走到我们的
/// <c>GetPlayerNameRaw</c> 前缀**（同一时刻、同一个 id，从本补丁里调 <see cref="LocalSeatLabels.TryResolve"/>
/// 却能拿到 <c>bot2</c>/<c>player3</c>）。大厅（`NRemoteLobbyPlayer`）那条 Raw 调用是生效的，
/// 唯独进局后的名牌不行 ⇒ 不再依赖"工具函数被拦"，改为**在名牌节点自身上把文字重新写一遍**。</para>
///
/// <para>做法：`_Ready` 之后取名牌节点 → 解析本席位称呼 → 命中就 `SetTextAutoSize(称呼)`
/// （用原版同一个 API，字号/居中口径不变）；解析不到（真联机队友 / 第三方席位）一个字都不动。</para>
///
/// <para>日志同时打「写之前 / 写之后」，一条就能看出"工具函数前缀到底有没有生效"：
/// `before=<原版给的>` 若是 Steam 昵称/id 原串、`after=botN` ⇒ 本条路径没走前缀。</para>
/// </summary>
[HarmonyPatch(typeof(NMultiplayerPlayerState), "_Ready")]
internal static class NMultiplayerPlayerStateNameplatePatch
{
    private static readonly FieldInfo? NameplateField =
        AccessTools.Field(typeof(NMultiplayerPlayerState), "_nameplateLabel");

    [HarmonyPostfix]
    private static void Postfix(NMultiplayerPlayerState __instance)
    {
        try
        {
            MegaLabel? nameplate = NameplateField?.GetValue(__instance) as MegaLabel;
            if (nameplate == null)
            {
                LocalMultiControlLogger.Warn("左侧玩家列表名牌：未找到 _nameplateLabel 字段，本次不接管");
                return;
            }

            ulong playerId = __instance.Player?.NetId ?? 0UL;
            string before = nameplate.Text;
            string? resolved = LocalSeatLabels.TryResolve(playerId);
            if (resolved == null)
            {
                LocalMultiControlLogger.Info(
                    $"列表名牌: index={__instance.GetIndex()}, player={playerId}, before={before}, "
                    + "after=<非本地席位，保持原版>");
                return;
            }

            nameplate.SetTextAutoSize(resolved);
            LocalMultiControlLogger.Info(
                $"列表名牌: index={__instance.GetIndex()}, player={playerId}, before={before}, after={resolved}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"左侧玩家列表名牌接管失败（保持原版文字）: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
