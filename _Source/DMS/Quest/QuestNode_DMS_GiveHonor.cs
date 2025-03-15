using RimWorld.QuestGen;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DMS
{
    public class QuestNode_DMS_GiveHonor : QuestNode
    {
        public SlateRef<Pawn> giveTo;

        public SlateRef<bool> giveToAccepter;

        [NoTranslate]
        public SlateRef<string> inSignal;

        public SlateRef<string> factionStorAs;

        public SlateRef<Thing> factionOf;

        public int amount;

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
            QuestPart_DMS_GiveRoyalFavor questPart_GiveRoyalFavor = new QuestPart_DMS_GiveRoyalFavor();
            questPart_GiveRoyalFavor.faction = slate.Get<Faction>(factionStorAs.GetValue(slate));
            questPart_GiveRoyalFavor.amount = amount;
            questPart_GiveRoyalFavor.inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal");
            QuestGen.quest.AddPart(questPart_GiveRoyalFavor);
        }
    }
}