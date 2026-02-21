using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace DMS
{
    public class QuestNode_TrackDoc : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignalEnable;
        [NoTranslate]
        public SlateRef<string> inSignalProcess;
        [NoTranslate]
        public SlateRef<string> outSignalComplete;

        public SlateRef<int> targetCount;

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TrackDoc questPart = new QuestPart_TrackDoc();
            questPart.inSignalEnable = (QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal"));
            questPart.inSignalProcess = QuestGenUtility.HardcodedSignalWithQuestID(inSignalProcess.GetValue(slate));
            questPart.outSignalComplete = QuestGenUtility.HardcodedSignalWithQuestID(outSignalComplete.GetValue(slate));
            questPart.targetCount = targetCount.GetValue(slate);
            QuestGen.quest.AddPart(questPart);
        }
    }

    public class QuestPart_TrackDoc : QuestPartActivable
    {
        public int targetCount;
        public int processedCount;
        public string inSignalProcess;
        public string outSignalComplete;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignalProcess && State == QuestPartState.Enabled)
            {
                processedCount++;
                if (processedCount >= targetCount)
                {
                    if (!outSignalComplete.NullOrEmpty())
                    {
                        Find.SignalManager.SendSignal(new Signal(outSignalComplete));
                    }
                    Complete();
                }
            }
        }

        public override string ExpiryInfoPart
        {
            get
            {
                if (State == QuestPartState.Enabled)
                {
                    return "DMS_ProcessedDocInBeacon".Translate() + ": " + processedCount + " / " + targetCount;
                }
                return null;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetCount, "targetCount", 0);
            Scribe_Values.Look(ref processedCount, "processedCount", 0);
            Scribe_Values.Look(ref inSignalProcess, "inSignalProcess");
            Scribe_Values.Look(ref outSignalComplete, "outSignalComplete");
        }
    }
}
