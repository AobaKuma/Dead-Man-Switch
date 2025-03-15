using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace DMS
{
    public class QuestPart_DMS_GiveRoyalFavor : QuestPart
    {
        public Pawn giveTo;

        public string inSignal;

        public int amount;

        public Faction faction;

        public override IEnumerable<Faction> InvolvedFactions
        {
            get
            {
                foreach (Faction involvedFaction in base.InvolvedFactions)
                {
                    yield return involvedFaction;
                }

                if (faction != null)
                {
                    yield return faction;
                }
            }
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignal)
            {
                giveTo = QuestGen.slate.Get<Pawn>("Accepter");
                Pawn arg = giveTo;
                if (arg == null)
                {
                    signal.args.TryGetArg("CHOSEN", out arg);
                }

                if (arg != null && arg.royalty != null)
                {
                    arg.royalty.GainFavor(faction, amount);
                }
            }
        }

        public override void AssignDebugData()
        {
            base.AssignDebugData();
            giveTo = PawnsFinder.AllMaps_FreeColonists.RandomElement();
            inSignal = "DebugSignal" + Rand.Int;
            faction = Find.FactionManager.RandomEnemyFaction();
            amount = 10;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref giveTo, "giveTo");
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref amount, "amount", 0);
        }

        public override void ReplacePawnReferences(Pawn replace, Pawn with)
        {
            if (giveTo == replace)
            {
                giveTo = with;
            }
        }
    }
}