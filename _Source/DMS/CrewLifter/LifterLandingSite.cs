using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 升降艙降落在空白地塊時生成的臨時地圖世界物件。
    /// 行為同原版商隊營地 (Camp),差別只有地圖移除判定:
    /// 原版只有殖民者 (或有機械師監管的機械體) 會阻止臨時地圖被移除,
    /// 本 mod 的機械體不需要機械師,單獨降落時地圖會在下一個 tick 被移除、連人帶艙消失;
    /// 這裡讓玩家陣營的機械體同樣能撐住地圖。
    /// </summary>
    public class LifterLandingSite : Camp
    {
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            if (HasMap && LifterCrewUtility.AnyPlayerMechOnMap(Map))
            {
                alsoRemoveWorldObject = false;
                return false;
            }
            return base.ShouldRemoveMapNow(out alsoRemoveWorldObject);
        }
    }

    public static class LifterCrewUtility
    {
        /// <summary>玩家陣營的機械體 (不要求 Biotech 的 IsColonyMech / 監管者)。</summary>
        public static bool IsPlayerMech(Pawn p)
        {
            return p != null && !p.Dead && p.Faction == Faction.OfPlayer && p.RaceProps.IsMechanoid;
        }

        /// <summary>可以撐起臨時地圖的乘員:殖民者或玩家機械體。</summary>
        public static bool IsCrew(Pawn p)
        {
            return p != null && !p.Dead && (p.IsColonist || IsPlayerMech(p));
        }

        /// <summary>仿 TransportersArrivalActionUtility.AnyNonDownedColonist,但機械體也算數。</summary>
        public static bool AnyNonDownedCrew(IEnumerable<IThingHolder> pods)
        {
            foreach (IThingHolder pod in pods)
            {
                ThingOwner held = pod.GetDirectlyHeldThings();
                if (held == null) continue;
                for (int i = 0; i < held.Count; i++)
                {
                    if (held[i] is Pawn p && !p.Downed && IsCrew(p)) return true;
                }
            }
            return false;
        }

        /// <summary>地圖上 (含 skyfaller / 升降艙容器內) 是否有玩家機械體。</summary>
        public static bool AnyPlayerMechOnMap(Map map)
        {
            List<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (IsPlayerMech(pawns[i])) return true;
            }
            return false;
        }
    }
}
