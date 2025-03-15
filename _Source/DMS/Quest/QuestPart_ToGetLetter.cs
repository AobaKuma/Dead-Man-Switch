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
using static Mono.Security.X509.X520;

namespace DMS
{
    public class QuestPart_ToGetLetter : QuestPart
    {
        public string inSignal;
        public string title;
        public string letterText;
        public string signalAccept;
        public string signalReject;
        public int letterDelayTicks;
        public Map map;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (signal.tag == inSignal)
            {
                DoWork();
            }
        }
        private void DoWork()
        {
            SendLetter(quest);
        }
        public void SendLetter(Quest quest)
        {
            bool isSendedLetter = false;
            QuestGen.slate.TryGet("isSendedLetter", out isSendedLetter);
            if (!isSendedLetter)
            {
                QuestGen.slate.Set("isSendedLetter", true);
                
                TaggedString title = "殖民舰队的信";
                TaggedString letterText = "殖民舰队的信";
                Choiceletter_Branch choiceLetter_AcceptJoiner = (Choiceletter_Branch)LetterMaker.MakeLetter(title, letterText, QuestDefOf.QuestBranch);
                choiceLetter_AcceptJoiner.map = map;
                choiceLetter_AcceptJoiner.slate = QuestGen.slate;
                choiceLetter_AcceptJoiner.quest = quest;
                choiceLetter_AcceptJoiner.signalAccept = signalAccept;
                choiceLetter_AcceptJoiner.signalReject = signalReject;
                choiceLetter_AcceptJoiner.StartTimeout(18000);
                Find.LetterStack.ReceiveLetter(choiceLetter_AcceptJoiner, null, letterDelayTicks);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref letterText, "letterText");
            Scribe_Values.Look(ref signalAccept, "signalAccept");
            Scribe_Values.Look(ref signalReject, "signalReject");
        }
    }
}