using System.Collections.Generic;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Models.Relics;

/// <summary>
/// 【瓦库形态】——瓦库托管新模式的专用遗物（与旧的"永久低语耳环"LocalWakuuStarterRelic 互相独立）。
/// 效果：+1 能量（同原版低语耳环）+ 接管所有回合自动出牌。
/// 仅当 LocalWakuuAutopilotConfig.UseVakuuForm 为 true 时才会被发放；
/// 关闭开关的玩家继续走原低语耳环路径，行为与旧版完全一致。
/// </summary>
internal sealed class LocalWakuuFormRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Event;

    // 一期复用低语耳环图标，后续可换专属图标。
    protected override string IconBaseName => "whispering_earring";

    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        new[] { HoverTipFactory.ForEnergy(this) };

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[] { new EnergyVar(1) };

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        if (player != Owner)
        {
            return amount;
        }

        return amount + DynamicVars.Energy.BaseValue;
    }

    public override Task AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext choiceContext, Player player)
    {
        return LocalWakuuRelicRuntime.ExecuteBeforePlayPhaseStartAsync(this, choiceContext, player);
    }
}
