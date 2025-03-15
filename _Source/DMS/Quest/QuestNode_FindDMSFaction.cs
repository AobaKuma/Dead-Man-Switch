using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace DMS
{

    public class QuestNode_FindDMSFaction : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;
        protected override bool TestRunInt(Slate slate)
        {
            if (TryFindFaction(out var var, slate))
            {
                slate.Set(storeAs.GetValue(slate), var, true);
                return true;
            }

            return false;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            if (!QuestGen.slate.TryGet<Faction>(storeAs.GetValue(slate), out var var)  && TryFindFaction(out var, QuestGen.slate))
            {
                QuestGen.slate.Set(storeAs.GetValue(slate), var);
                if (!var.Hidden)
                {
                    QuestPart_InvolvedFactions questPart_InvolvedFactions = new QuestPart_InvolvedFactions();
                    questPart_InvolvedFactions.factions.Add(var);
                    QuestGen.quest.AddPart(questPart_InvolvedFactions);
                }
            }
        }

        private bool TryFindFaction(out Faction faction, Slate slate)
        {
            var isfoundFaction = (from x in Find.FactionManager.GetFactions(allowHidden: true)
                                  where x.def == DefDatabase<FactionDef>.GetNamed("DMS_Army")
                                  select x).TryRandomElement(out faction);
            if (isfoundFaction == false || faction.def != DefDatabase<FactionDef>.GetNamed("DMS_Army"))
            { 
                faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("DMS_Army")));
                return true;
            }
            return isfoundFaction;
        }
    }
}