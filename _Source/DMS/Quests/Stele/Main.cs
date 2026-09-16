using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace DMS
{
	public class QuestNode_Root_Stele : QuestNode
	{
		public List<QuestScriptDef> subquestDefs = new List<QuestScriptDef>();

		public int keysCount = 4;

		public ThingDef steleDef;

		protected override bool TestRunInt(Slate slate)
		{
			if (Find.QuestManager.QuestsListForReading.Any((Quest q) => q.State == QuestState.Ongoing && q.root == DMS_DefOf.DMS_Stele))
			{
				return false;
			}
			return true;
		}

		protected override void RunInt()
		{
			Quest quest = QuestGen.quest;
			Slate slate = QuestGen.slate;
			slate.Set("keysCount", keysCount);
			string inSignalEnable = slate.Get<string>("inSignal");
			string inSignalKeysFound = QuestGenUtility.HardcodedSignalWithQuestID("allSteleQuestsFinished");
			string signalSteleSubquestCompleted = QuestGenUtility.HardcodedSignalWithQuestID("steleSubquestCompleted");
			QuestPart_SubquestGenerator_Stele questPart_SubquestGenerator = new QuestPart_SubquestGenerator_Stele
			{
				inSignalEnable = inSignalEnable,
				signalSteleSubquestCompleted = signalSteleSubquestCompleted,
				signalListenMode = QuestPart.SignalListenMode.OngoingOnly,
				maxActiveSubquests = 3,
				signalKeysFound = inSignalKeysFound
			};
			questPart_SubquestGenerator.subquestDefs.AddRange(subquestDefs);
			quest.AddPart(questPart_SubquestGenerator);
			QuestPart_Choice questPart_Choice = quest.RewardChoice();
			QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice
			{
				rewards = { new Reward_DefinedThingDef(steleDef) }
			};
			questPart_Choice.choices.Add(choice);
		}
	}

	public class QuestPart_SubquestGenerator_Stele : QuestPart_SubquestGenerator
	{
		public string signalKeysFound;

		public string signalSteleSubquestCompleted;

		private float lastSubquestTick;

		private bool givenFirstSubquest;

		private const int MTBSubquestsTicks = 300000;

		private const int MTBFirstSubquestTicks = 120000;

		private const int MinTimeBetweenSubquests = 900000;

		private const int MaxTimeBetweenSubquests = 1800000;

		private const int MinTimeFirstSubquest = 300000;

		private const int MaxTimeFirstSubquest = 480000;

		private List<QuestScriptDef> tmpSubquestDefs = new List<QuestScriptDef>();

		private List<OccultechKey> keys = new List<OccultechKey>();

		public bool AllKeysFound => keys.Count >= KeysCount;

		private int KeysCount => (DMS_DefOf.DMS_Stele.root as QuestNode_Root_Stele)?.keysCount ?? 4;

		private int MTB
		{
			get
			{
				if (!givenFirstSubquest)
				{
					return MTBFirstSubquestTicks;
				}
				return MTBSubquestsTicks;
			}
		}

		private int MinTime
		{
			get
			{
				if (!givenFirstSubquest)
				{
					return MinTimeFirstSubquest;
				}
				return MinTimeBetweenSubquests;
			}
		}

		private int MaxTime
		{
			get
			{
				if (!givenFirstSubquest)
				{
					return MaxTimeFirstSubquest;
				}
				return MaxTimeBetweenSubquests;
			}
		}

		protected override bool CanGenerateSubquest
		{
			get
			{
				if ((float)Find.TickManager.TicksGame - lastSubquestTick < (float)MinTime)
				{
					return false;
				}
				if (AllKeysFound)
				{
					return false;
				}
				return true;
			}
		}

		public bool TryAddKey(OccultechKey key)
		{
			if (AllKeysFound)
			{
				return false;
			}
			if (keys.Contains(key))
			{
				return true;
			}
			keys.Add(key);
			return true;
		}

		public void RemoveKey(OccultechKey key)
		{
			keys.Remove(key);
		}

		protected override QuestScriptDef GetNextSubquestDef()
		{
			tmpSubquestDefs.Clear();
			GetPossibleSubquests(tmpSubquestDefs);
			if (tmpSubquestDefs.TryRandomElement(out var result))
			{
				return result;
			}
			return null;
		}

		private void GetPossibleSubquests(List<QuestScriptDef> outList)
		{
			IEnumerable<Quest> subquests = quest.GetSubquests();
			foreach (QuestScriptDef def in subquestDefs)
			{
				if (def.CanRun(InitSlate(), Find.World))
				{
					outList.Add(def);
				}
			}
		}

		protected override bool TryGenerateSubquest()
		{
			bool num = base.TryGenerateSubquest();
			if (num)
			{
				lastSubquestTick = Find.TickManager.TicksGame;
				givenFirstSubquest = true;
				return num;
			}
			Log.Warning("Failed to generate gravcore subquest, trying again in 6 hours");
			lastSubquestTick += 15000f;
			return num;
		}

		public override void Notify_QuestSignalReceived(Signal signal)
		{
			base.Notify_QuestSignalReceived(signal);
			Log.Message(signal.ToString());
			if(signal.tag == signalSteleSubquestCompleted)// && AllKeysFound)
			{
				for (int i = keys.Count - 1; i >= 0; i--)
				{
					if (keys[i] == null || keys[i].Destroyed)
					{
						keys.RemoveAt(i);
					}
				}
				Log.Warning("signal recieved");
			}
		}

		protected override Slate InitSlate()
		{
			float var = ((Find.AnyPlayerHomeMap == null) ? StorytellerUtility.DefaultThreatPointsNow(Find.World) : StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap));
			Slate slate = new Slate();
			slate.Set("points", var);
			slate.Set("signalSteleSubquestCompleted", signalSteleSubquestCompleted);
			return slate;
		}

		public override void QuestPartTick()
		{
			if (subquestDefs.Count != 0 && !base.Paused && CanGenerateSubquest && (Rand.MTBEventOccurs(MTB, 1f, 1f) || (float)Find.TickManager.TicksGame > lastSubquestTick + (float)MaxTime))
			{
				TryGenerateSubquest();
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref signalSteleSubquestCompleted, "signalSteleSubquestCompleted");
			Scribe_Values.Look(ref signalKeysFound, "signalKeysFound");
			Scribe_Values.Look(ref lastSubquestTick, "lastSubquestTick", 0f);
			Scribe_Values.Look(ref givenFirstSubquest, "givenFirstSubquest", defaultValue: false);
			Scribe_Collections.Look(ref keys, "keys", saveDestroyedThings: false, lookMode: LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				for (int i = keys.Count - 1; i >= 0; i--)
				{
					if (keys[i] == null || keys[i].Destroyed)
					{
						keys.RemoveAt(i);
					}
				}
			}
		}

		public override void DoDebugWindowContents(Rect innerRect, ref float curY)
		{
			if (base.State == QuestPartState.Enabled)
			{
				Rect rect = new Rect(innerRect.x, curY, 500f, 25f);
				if (Widgets.ButtonText(rect, "Force subquest " + ToString()))
				{
					lastSubquestTick = -999999;
				}
				curY += rect.height + 4f;
			}
		}
	}
}
