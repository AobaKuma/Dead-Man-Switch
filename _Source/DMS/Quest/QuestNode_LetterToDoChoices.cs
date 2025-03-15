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
using Verse.Noise;
using static Mono.Security.X509.X520;

namespace DMS
{

    public class QuestNode_LetterToDoChoices : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignal;
        public SlateRef<bool> isSendedLetter;
        public string title;
        public string letterText;
        public int letterDelayTicks;

        protected override bool TestRunInt(Slate slate)
        {
            if (!slate.Exists("map"))
            {
                return false;
            }
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_ToGetLetter questPart_ToGetLetter = new QuestPart_ToGetLetter();
            questPart_ToGetLetter.map = slate.Get<Map>("map");
            questPart_ToGetLetter.signalAccept = (QuestGenUtility.HardcodedSignalWithQuestID("LetterAccepted"));
            questPart_ToGetLetter.signalReject = (QuestGenUtility.HardcodedSignalWithQuestID("LetterRejected"));
            questPart_ToGetLetter.inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal");
            questPart_ToGetLetter.letterDelayTicks = letterDelayTicks;
            QuestGen.quest.AddPart(questPart_ToGetLetter); 
        }
    }
}