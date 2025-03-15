using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace DMS
{

    public class QuestNode_ChangeRelation : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> factionStorAs;
        public SlateRef<string> inSignal;
        public int chageNum;

        protected override bool TestRunInt(Slate slate)
        {
            if (slate.Get<Faction>(factionStorAs.GetValue(slate)) == null)
            {
                return false;
            }
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_ChangeGoodwillForDMS questPart_ChangeGoodwillForDMS = new QuestPart_ChangeGoodwillForDMS();
            questPart_ChangeGoodwillForDMS.inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal");
            questPart_ChangeGoodwillForDMS.faction = slate.Get<Faction>(factionStorAs.GetValue(slate));
            questPart_ChangeGoodwillForDMS.goodwillChange = chageNum;
            QuestGen.quest.AddPart(questPart_ChangeGoodwillForDMS);
        }
    }
}