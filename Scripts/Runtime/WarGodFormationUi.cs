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
    /// 「拉开真人」只在原版支持的人数范围内做（<= 4）。人多到原版自己都排不下时，
    /// 硬按 4 人时代的 232px 下限去铺满屏幕反而更难看（r320，见 <see cref="SpreadRealPlayers"/>）。
    /// </summary>
    private const int MaxSpreadingPlayerCount = 4;

    /// <summary>上次"因人太多而跳过"的人数（只用于日志去重，避免每回合刷屏）。</summary>
    private static int _lastSkippedSpreadCount;


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
        // r366：位置摆好之后再调**绘制层级**（顺序与位置无关，但放在收尾里能顺带覆盖"回合开始重排"那次）。
        ApplyDrawOrder(state, warGods, tag);
    }

    /// <summary>
    /// r366：把战神节点在 `%AllyContainer` 里的**子节点顺序**调成"召唤物那一层"（纯表现）。
    ///
    /// 原版加节点是 `_allyContainer.AddChildSafely(node)` ⇒ 后召唤的直接画在**所有已存在的立绘上面**
    /// （用户 2026-11-17：「战神都是在本体图层上面的，挡到了」）。这里只重排、不动坐标：
    /// 非生物节点 → 战神（**后召唤的在前 = 更下层**）→ 其它生物（真人/宠物/敌人，画在最上面）。
    /// 判据与顺序见 <see cref="WarGodLayerPolicy"/>；失败只 WARN（层级是纯表现）。
    /// </summary>
    internal static void ApplyDrawOrder(
        CombatState? state,
        IReadOnlyList<(Player WarGod, ulong SummonerNetId)> warGods,
        string tag)
    {
        try
        {
            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null || warGods == null || warGods.Count == 0)
            {
                return;
            }

            // 登记顺序 = 召唤顺序（见 `_warGods` 的追加语义），所以这里天然是 oldest-first。
            List<NCreature> warGodNodes = new();
            foreach ((Player warGod, _) in warGods)
            {
                if (warGod?.Creature == null)
                {
                    continue;
                }

                NCreature? node = room.GetCreatureNode(warGod.Creature);
                if (node != null && GodotObject.IsInstanceValid(node))
                {
                    warGodNodes.Add(node);
                }
            }

            if (warGodNodes.Count == 0)
            {
                return;
            }

            Node? parent = warGodNodes[0].GetParent();
            if (parent == null)
            {
                return;
            }

            // 只处理与第一只同父的（正常情况下全部在 `%AllyContainer`；异常就跳过该只）。
            warGodNodes = warGodNodes.Where((node) => node.GetParent() == parent).ToList();
            if (warGodNodes.Count == 0)
            {
                return;
            }

            List<Node> children = parent.GetChildren().ToList();
            HashSet<Node> warGodSet = new(warGodNodes);
            List<int> nonCreature = new();
            List<int> warGodIndices = new();
            List<int> otherCreatures = new();
            for (int i = 0; i < children.Count; i++)
            {
                Node child = children[i];
                if (child is not NCreature)
                {
                    nonCreature.Add(i);
                }
                else if (warGodSet.Contains(child))
                {
                    warGodIndices.Add(i);
                }
                else
                {
                    otherCreatures.Add(i);
                }
            }

            List<int> desired = WarGodLayerPolicy.BuildDrawOrder(nonCreature, warGodIndices, otherCreatures);
            if (desired.Count != children.Count)
            {
                // 防御：理论上不可能（三组是 children 的划分），真出现就什么都不动。
                LocalMultiControlLogger.Warn(
                    $"{tag} 战神绘制层级跳过（下标划分对不上）: children={children.Count}, desired={desired.Count}");
                return;
            }

            int moved = 0;
            for (int i = 0; i < desired.Count; i++)
            {
                Node node = children[desired[i]];
                if (!GodotObject.IsInstanceValid(node))
                {
                    continue;
                }

                if (node.GetIndex() == i)
                {
                    continue;
                }

                parent.MoveChild(node, i);
                moved++;
            }

            if (moved > 0)
            {
                List<ulong> order = new();
                for (int i = warGods.Count - 1; i >= 0; i--)
                {
                    order.Add(warGods[i].WarGod?.NetId ?? 0UL);
                }

                LocalMultiControlLogger.Info(
                    $"{tag} 战神绘制层级已调整（召唤物排到本体之下，后召唤的更靠下层）: 战神数={warGodNodes.Count}, "
                    + $"绘制顺序=[{string.Join(" → ", order)}], 调整次数={moved}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 战神绘制层级调整失败（已忽略，不影响召唤）: error={exception.Message}");
        }
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

            // r320：**超过原版支持范围（4 人）时一律不动**。
            //
            // 232px 这个下限是为「2~4 人 + 战神」定的（r303 用户报「用了【无尽战神】后两个玩家突然离得很近」）。
            // 12 人实测（2026-10-24）：原版进战斗时把 12 个人排在 x=124…764 的一团（紧凑、观感可接受），
            // 而我们按 232 去"拉开"⇒ 一次性铺到 120…1800（整个屏幕宽）⇒ 用户观感「进战斗时没问题，
            // 然后**突然**站得很开」；而且这次位移同时要挪 12 个立绘 + 各自宠物，是一记可感的卡顿。
            // 人多到原版自己都排不下时，**保持原版/第三方排出来的样子**比我们硬铺更好。
            if (nodes.Count > MaxSpreadingPlayerCount)
            {
                if (_lastSkippedSpreadCount != nodes.Count)
                {
                    _lastSkippedSpreadCount = nodes.Count;
                    LocalMultiControlLogger.Info(
                        $"{tag} 真人站位不干预（人数 {nodes.Count} > {MaxSpreadingPlayerCount}，"
                        + "保持原版/第三方的紧凑排布；232px 下限是为 2~4 人定的）");
                }

                return;
            }

            _lastSkippedSpreadCount = 0;

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
