using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DMS
{
	public class QuestPart_SteleSubQuestEnd : QuestPart
	{
		public string inSignal;

		public string signalSteleSubquestCompleted;

		public bool sendLetter;

		public bool playSound;

		public override void Notify_QuestSignalReceived(Signal signal)
		{
			base.Notify_QuestSignalReceived(signal);
			if (!(signal.tag != inSignal))
			{
				Log.Message("DMS-1");
				if (quest.parent != null)
				{
					quest.parent.Notify_SignalReceived(new Signal(signalSteleSubquestCompleted, true));
				}
				quest.End(QuestEndOutcome.Success, sendLetter, playSound);
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref inSignal, "inSignal");
			Scribe_Values.Look(ref signalSteleSubquestCompleted, "signalSteleSubquestCompleted");
			Scribe_Values.Look(ref sendLetter, "sendLetter", defaultValue: false);
			Scribe_Values.Look(ref playSound, "playSound", defaultValue: false);
		}

		public override void AssignDebugData()
		{
			base.AssignDebugData();
			inSignal = "DebugSignal" + Rand.Int;
		}
	}
}
