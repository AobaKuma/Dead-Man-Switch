using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Noise;

namespace DMS
{
    [StaticConstructorOnStartup]
    public class RoyalTitlePermitWorker_TradeGroup : RoyalTitlePermitWorker
    {
        //使用後呼叫徵募中介。
        public override IEnumerable<FloatMenuOption> GetRoyalAidOptions(Map map, Pawn pawn, Faction faction)
        {
            if (faction.HostileTo(Faction.OfPlayer))
            {
                yield return new FloatMenuOption("CommandCallRoyalAidFactionHostile".Translate(faction.Named("FACTION")), null);
                yield break;
            }

            Action action = null;
            string description = def.LabelCap + ": ";
            if (FillAidOption(pawn, faction, ref description, out var free))
            {
                action = delegate
                {
                    DoEffect(pawn, faction, free);
                };
            }
            yield return new FloatMenuOption(description, action, faction.def.FactionIcon, faction.Color);
        }
        private void DoEffect(Pawn caller, Faction faction, bool free)
        {
            caller.MapHeld
        }
    }
}
