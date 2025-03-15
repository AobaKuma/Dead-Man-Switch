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
    public class QuestPart_ChangeGoodwillForDMS : QuestPart
    {
        public string inSignal;

        public Faction faction;

        public int goodwillChange;

        public HistoryEventDef historyEvent;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (signal.tag == inSignal)
            {
                DoWork();
            }
        }

        private void DoWork()
        {
            Slate slate = QuestGen.slate;
            //设为敌对
            if (!faction.HostileTo(Faction.OfPlayer))
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillChange);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref goodwillChange, "goodwillChange", 0);
            Scribe_Defs.Look(ref historyEvent, "historyEvent");
        }
    }
}