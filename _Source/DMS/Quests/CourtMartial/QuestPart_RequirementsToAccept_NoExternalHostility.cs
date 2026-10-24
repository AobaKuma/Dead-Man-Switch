using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 軍事法庭傳票的接受條件:外部模組(見 <see cref="FleetSettlementBridge"/>)把艦隊與玩家鎖在敵對時不能接受。
    /// 傳票可能早在鎖定之前就發出;接受後休戰會立刻被外部規則打回敵對,任務以「休戰破裂」失敗並怪到玩家頭上。
    /// Court-martial acceptance requirement: refused while an external mod (see <see cref="FleetSettlementBridge"/>)
    /// locks the fleet and the player into hostility. A summons may predate the lock; accepting it would have the
    /// external rules knock the truce straight back to hostile and fail the quest as a broken truce.
    /// </summary>
    public class QuestPart_RequirementsToAccept_NoExternalHostility : QuestPart_RequirementsToAccept
    {
        public Faction faction;

        public override AcceptanceReport CanAccept()
        {
            string reason = FleetSettlementBridge.HostilityLock(faction, Faction.OfPlayer);
            return reason == null ? AcceptanceReport.WasAccepted : new AcceptanceReport(reason);
        }

        public override bool ShowInRequirementBox => !CanAccept().Accepted;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
        }
    }
}
