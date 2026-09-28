using Fortified;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    /// <summary>
    /// 強制恐慌的共用把關：玩家的小人（殖民者、動物、囚犯、奴隸）不能被送進會跑出地圖的恐慌
    /// （原版 PanicFlee 一類），否則就永久失去了；改用 FFF 的暫時恐慌 FFF_FleeInPlace。
    /// 次聲波脈衝與恐慌光環都走這裡，敵人則維持原本的狀態。
    ///
    /// Shared guard for forced panic: the player's pawns (colonists, animals, prisoners, slaves) must never get a
    /// map-leaving panic (vanilla PanicFlee and kin), or they're gone for good; they get FFF's temporary
    /// FFF_FleeInPlace instead. Subsonic pulses and the panic aura both go through here; other pawns keep the
    /// requested state.
    /// </summary>
    public static class PanicSafetyUtility
    {
        public static MentalStateDef SafeStateFor(Pawn pawn, MentalStateDef state)
        {
            if (state == null) return null;
            bool playerOwned = pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer;
            if (playerOwned && typeof(MentalState_PanicFlee).IsAssignableFrom(state.stateClass))
            {
                return FFF_DefOf.FFF_FleeInPlace;
            }
            return state;
        }
    }
}
