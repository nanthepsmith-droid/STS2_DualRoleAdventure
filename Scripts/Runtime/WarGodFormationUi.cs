using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战神立绘站位的**应用层**（r301）：把 <see cref="WarGodFormationPolicy"/> 算出的落点写到节点上。
///
/// 触发点：每次召唤成功（`FinishSpawnAsync`）⇒ **重排该绑主名下全部战神**（幂等：按登记顺序重算）。
/// 只动**战神自己的节点**（`GlobalPosition` + 解除原版"第二排置灰"），不碰真人与第三方单位。
/// 任何异常只 WARN —— 站位是纯表现，绝不能影响召唤本身。
/// </summary>
internal static class WarGodFormationUi
{
    /// <summary>
    /// **一次性收尾**（r303）：先把**真人**之间的间距拉开到下限（原版在人数变多时会把两人挤到一起），
    /// 再按绑主把战神归位。调用点 = 每次召唤批次结束之后 + 真人回合开始（幂等；只在需要时改）。
    /// </summary>
    internal static void Settle(
        CombatState? state,
        IReadOnlyList<(Player WarGod, ulong SummonerNetId)> warGods,
        string tag,
        bool spreadPlayers = true)
    {
        if (spreadPlayers)
        {
            SpreadRealPlayers(state, tag);
        }

        RelayoutBySummoner(state, warGods, tag);
    }

    /// <summary>
    /// 把**真人**立绘的横向间距拉到 <see cref="WarGodFormationPolicy.DefaultMinPlayerSpacing"/> 以上
    /// （间距本来就够 ⇒ 一个都不动）。只改 X，不动 Y，也不碰战神与敌人。
    /// </summary>
    private static void SpreadRealPlayers(CombatState? state, string tag)
    {
        try
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null || state == null)
            {
                return;
            }

            List<Player> owners = new();
            List<NCreature> nodes = new();
            List<float> centers = new();
            foreach (Player player in state.Players)
            {
                if (player?.Creature == null || LocalSelfCoopContext.IsTemporarySeat(player.NetId))
                {
                    continue;
                }

                NCreature? node = room.GetCreatureNode(player.Creature);
                if (node == null || !GodotObject.IsInstanceValid(node))
                {
                    continue;
                }

                owners.Add(player);
                nodes.Add(node);
                centers.Add(node.GlobalPosition.X);
            }

            if (nodes.Count < 2)
            {
                return;
            }

            Vector2 screen = room.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
            List<float> targets = WarGodFormationPolicy.SpreadEvenly(
                centers,
                WarGodFormationPolicy.DefaultMinPlayerSpacing,
                WarGodFormationPolicy.ScreenMarginX,
                screen.X - WarGodFormationPolicy.ScreenMarginX);

            List<string> moved = new();
            for (int i = 0; i < nodes.Count && i < targets.Count; i++)
            {
                float deltaX = targets[i] - centers[i];
                if (Math.Abs(deltaX) < 0.5f)
                {
                    continue;
                }

                // r307：**连着它的宠物一起挪** —— 原版布局是把宠物摆在主人脚边的，
                // 只挪主人会让宠物"脱钩"留在原地（用户 2026-10-22 的顾虑）。
                MoveWithPets(room, owners[i], nodes[i], new Vector2(deltaX, 0f));
                moved.Add($"{centers[i]:0}→{targets[i]:0}");
            }

            if (moved.Count > 0)
            {
                LocalMultiControlLogger.Info(
                    $"{tag} 真人站位已拉开（原版人多时会挤到一起）: 人数={nodes.Count}, 位移=[{string.Join(" | ", moved)}]");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 真人站位归位失败（已忽略）: error={exception.Message}");
        }
    }

    /// <summary>
    /// 按绑主分组重排战神立绘。传进来的必须是**同一批**登记（顺序 = 召唤顺序）。
    /// </summary>
    /// <param name="state">当前战斗态（用来按 NetId 反查绑主的 Creature）。</param>
    /// <param name="warGods">(战神 Player, 绑主 NetId) —— 绑主为 0 的 POC 哑战神不参与（保持原位置）。</param>
    internal static void RelayoutBySummoner(
        CombatState? state,
        IReadOnlyList<(Player WarGod, ulong SummonerNetId)> warGods,
        string tag)
    {
        try
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null || state == null || warGods == null || warGods.Count == 0)
            {
                return;
            }

            Vector2 screen = room.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;

            // 按绑主分组（保持登记顺序）
            Dictionary<ulong, List<Player>> groups = new();
            List<ulong> order = new();
            foreach ((Player warGod, ulong summonerNetId) in warGods)
            {
                if (summonerNetId == 0UL || warGod?.Creature == null)
                {
                    continue;
                }

                if (!groups.TryGetValue(summonerNetId, out List<Player>? list))
                {
                    list = new List<Player>();
                    groups[summonerNetId] = list;
                    order.Add(summonerNetId);
                }

                list.Add(warGod);
            }

            foreach (ulong summonerNetId in order)
            {
                List<Player> group = groups[summonerNetId];

                // 绑主的立绘节点：拿不到就跳过（宁可不摆，也别摆到 0,0）
                NCreature? summonerNode = FindSummonerNode(room, state, summonerNetId);
                if (summonerNode == null)
                {
                    LocalMultiControlLogger.Warn(
                        $"{tag} 战神站位跳过（找不到绑主立绘节点）: 绑主={summonerNetId}, 战神数={group.Count}");
                    continue;
                }

                Vector2 anchor = summonerNode.GlobalPosition;
                List<FormationPoint> points = WarGodFormationPolicy.Build(
                    group.Count, anchor.X, anchor.Y, screen.X, screen.Y);

                List<string> placed = new();
                for (int i = 0; i < group.Count && i < points.Count; i++)
                {
                    NCreature? node = room.GetCreatureNode(group[i].Creature);
                    if (node == null || !GodotObject.IsInstanceValid(node))
                    {
                        continue;
                    }

                    // r307：按**位移**移动并顺带挪它的宠物（战神自己也可能带宠物，例如亡灵契约师的奥斯提）。
                    Vector2 delta = new(points[i].X - node.GlobalPosition.X, points[i].Y - node.GlobalPosition.Y);
                    MoveWithPets(room, group[i], node, delta);

                    // 原版对"第二排"的玩家会 `Visuals.Modulate` 置灰；战神不是后排小怪，
                    // 恢复原色（否则玩家会以为它"没在场"）。
                    try
                    {
                        node.Visuals.Modulate = new Color(1f, 1f, 1f, 1f);
                    }
                    catch (Exception)
                    {
                        // 视觉对象缺失时忽略：位置已经摆好了。
                    }

                    placed.Add($"{group[i].NetId}@{points[i].X:0},{points[i].Y:0}");
                }

                LocalMultiControlLogger.Info(
                    $"{tag} 战神站位已按绑主归位: 绑主={summonerNetId}, 数量={group.Count}, "
                    + $"锚点={anchor.X:0},{anchor.Y:0}, 位置=[{string.Join(" | ", placed)}]");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 战神站位重排失败（已忽略，不影响召唤）: error={exception.Message}");
        }
    }

    /// <summary>
    /// 移动一个立绘节点，并把它名下的**宠物**一起平移同样的位移（r307）。
    ///
    /// 为什么：原版 `PositionPlayersAndPets` 是**把宠物摆在主人脚边**的（`pets` 循环按主人宽度分摊），
    /// 我们只挪主人 ⇒ 宠物留在旧坐标、"脱钩"（用户 2026-10-22 明确指出这个风险）。
    /// 宠物节点拿不到就跳过（不影响主人）。
    /// </summary>
    private static void MoveWithPets(NCombatRoom room, Player owner, NCreature node, Vector2 delta)
    {
        node.GlobalPosition += delta;

        IReadOnlyList<Creature>? pets = owner.PlayerCombatState?.Pets;
        if (pets == null || pets.Count == 0)
        {
            return;
        }

        foreach (Creature pet in pets)
        {
            if (pet == null)
            {
                continue;
            }

            NCreature? petNode = room.GetCreatureNode(pet);
            if (petNode != null && GodotObject.IsInstanceValid(petNode))
            {
                petNode.GlobalPosition += delta;
            }
        }
    }

    /// <summary>按绑主 NetId 找它的立绘节点（绑主可能是真人，也可能是别的在册席位）。</summary>
    private static NCreature? FindSummonerNode(NCombatRoom room, CombatState state, ulong summonerNetId)
    {
        try
        {
            Creature? summonerCreature = state.Players
                .FirstOrDefault((player) => player.NetId == summonerNetId)?.Creature;

            if (summonerCreature != null)
            {
                NCreature? node = room.GetCreatureNode(summonerCreature);
                if (node != null && GodotObject.IsInstanceValid(node))
                {
                    return node;
                }
            }
        }
        catch (Exception)
        {
            // 查不到就当没有节点
        }

        return null;
    }
}
