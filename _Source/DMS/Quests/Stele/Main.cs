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
	/// <summary>
	/// 石碑主線（由研究「量子位元破譯」派發，見 <see cref="ModExtension_ResearchQuest"/>）。
	/// 兩個階段：先陸續派發撞擊地點子任務，直到帶回 keysCount 枚密鑰；密鑰解讀後用掉，
	/// 派發石碑庫最終站點。把石碑帶回據點即主線成功。
	///
	/// Stele main line (issued by the qubit decryption research, see <see cref="ModExtension_ResearchQuest"/>).
	/// Two stages: impact-site subquests until keysCount glyphs are brought home; reading the glyphs spends them
	/// and issues the stele vault. Bringing the stele home completes the line.
	/// </summary>
	public class QuestNode_Root_Stele : QuestNode
	{
		public List<QuestScriptDef> subquestDefs = new List<QuestScriptDef>();

		public QuestScriptDef vaultQuestDef;

		public int keysCount = 4;

		public ThingDef steleDef;

		protected override bool TestRunInt(Slate slate)
		{
			return !Find.QuestManager.QuestsListForReading.Any((Quest q) => q.State == QuestState.Ongoing && q.root == DMS_DefOf.DMS_Stele);
		}

		protected override void RunInt()
		{
			Quest quest = QuestGen.quest;
			Slate slate = QuestGen.slate;
			slate.Set("keysCount", keysCount);
			QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String("keysCount", keysCount.ToString()) });
			QuestPart_SubquestGenerator_Stele generator = new QuestPart_SubquestGenerator_Stele
			{
				inSignalEnable = slate.Get<string>("inSignal"),
				signalSteleSubquestCompleted = QuestGenUtility.HardcodedSignalWithQuestID("steleSubquestCompleted"),
				signalListenMode = QuestPart.SignalListenMode.OngoingOnly,
				maxActiveSubquests = 3,
				keysCount = keysCount,
				vaultQuestDef = vaultQuestDef
			};
			generator.subquestDefs.AddRange(subquestDefs);
			quest.AddPart(generator);
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
		public enum Stage
		{
			CollectingGlyphs,
			Vault,
			Done
		}

		public string signalSteleSubquestCompleted;

		public int keysCount = 4;

		public QuestScriptDef vaultQuestDef;

		private Stage stage;

		private float lastSubquestTick;

		private bool givenFirstSubquest;

		private int vaultRetryTick = -1;

		private int vaultsGiven;

		private List<QuestScriptDef> tmpSubquestDefs = new List<QuestScriptDef>();

		private List<OccultechKey> keys = new List<OccultechKey>();

		// 第一個撞擊地點在 3~5 天後出現，之後每 6~12 天一個。
		// The first impact site surfaces after 3~5 days, then one every 6~12 days.
		private const int MTBFirstSubquestTicks = 60000;

		private const int MinTimeFirstSubquest = 180000;

		private const int MaxTimeFirstSubquest = 300000;

		private const int MTBSubquestsTicks = 180000;

		private const int MinTimeBetweenSubquests = 360000;

		private const int MaxTimeBetweenSubquests = 720000;

		// 石碑遺失後 3~5 天指出下一座石碑庫；找不到地塊時 6 小時後重試。
		// After a lost stele the next vault is pointed out in 3~5 days; retry in 6 hours if no tile is found.
		private static readonly IntRange VaultRetryDelayTicks = new IntRange(180000, 300000);

		private const int VaultGenerateFailRetryTicks = 15000;

		public Stage CurrentStage => stage;

		public int KeysCount => Mathf.Max(1, keysCount);

		public int CollectedKeysCount => keys.Count(SteleQuestUtility.IsHeldByPlayer);

		private int MTB => givenFirstSubquest ? MTBSubquestsTicks : MTBFirstSubquestTicks;

		private int MinTime => givenFirstSubquest ? MinTimeBetweenSubquests : MinTimeFirstSubquest;

		private int MaxTime => givenFirstSubquest ? MaxTimeBetweenSubquests : MaxTimeFirstSubquest;

		/// <summary>進行中或待接受的撞擊地點子任務數。Ongoing or pending glyph subquests.</summary>
		private int PendingGlyphSubquests => quest.GetSubquests().Count((Quest q) => (q.State == QuestState.Ongoing || q.State == QuestState.NotYetAccepted) && subquestDefs.Contains(q.root));

		protected override bool CanGenerateSubquest
		{
			get
			{
				if (stage != Stage.CollectingGlyphs)
				{
					return false;
				}
				if (!base.CanGenerateSubquest)
				{
					return false;
				}
				if ((float)Find.TickManager.TicksGame - lastSubquestTick < (float)MinTime)
				{
					return false;
				}
				// 已帶回的加上還在外面的，夠用就不再派發。Stop once glyphs in hand plus glyphs still out there are enough.
				return CollectedKeysCount + PendingGlyphSubquests < KeysCount;
			}
		}

		public override string ExpiryInfoPart
		{
			get
			{
				if (stage != Stage.CollectingGlyphs)
				{
					return null;
				}
				return "DMS_Stele_GlyphProgress".Translate(Mathf.Min(CollectedKeysCount, KeysCount), KeysCount);
			}
		}

		public bool TryAddKey(OccultechKey key)
		{
			if (stage != Stage.CollectingGlyphs || key == null)
			{
				return false;
			}
			if (!keys.Contains(key))
			{
				keys.Add(key);
			}
			return true;
		}

		public void RemoveKey(OccultechKey key)
		{
			keys.Remove(key);
		}

		private void PruneKeys()
		{
			keys.RemoveAll((OccultechKey k) => k == null || k.Destroyed);
		}

		/// <summary>撞擊地點子任務成功時呼叫。Called when an impact-site subquest succeeds.</summary>
		public void Notify_GlyphRecovered()
		{
			if (stage != Stage.CollectingGlyphs)
			{
				return;
			}
			Messages.Message("DMS_Stele_GlyphRecovered".Translate(Mathf.Min(CollectedKeysCount, KeysCount), KeysCount),
				MessageTypeDefOf.PositiveEvent);
		}

		/// <summary>石碑庫子任務成功時呼叫。Called when the vault subquest succeeds.</summary>
		public void Notify_VaultSecured()
		{
			if (stage == Stage.Done)
			{
				return;
			}
			stage = Stage.Done;
			vaultRetryTick = -1;
			if (State == QuestPartState.Enabled)
			{
				Complete();
			}
			quest.End(QuestEndOutcome.Success, sendLetter: false);
		}

		/// <summary>石碑庫子任務失敗時呼叫：過幾天指出下一座。Called when the vault fails: point out another in a few days.</summary>
		public void Notify_VaultLost()
		{
			if (stage != Stage.Vault)
			{
				return;
			}
			vaultRetryTick = Find.TickManager.TicksGame + VaultRetryDelayTicks.RandomInRange;
		}

		/// <summary>
		/// 密鑰集齊：解讀會讓密鑰的量子態塌縮，用掉剛好 keysCount 枚已帶回的密鑰，然後派發石碑庫。
		/// All glyphs in: reading collapses their quantum states, spending exactly keysCount glyphs in hand, then the vault is issued.
		/// </summary>
		private void BeginVaultStage()
		{
			int toConsume = KeysCount;
			for (int i = keys.Count - 1; i >= 0 && toConsume > 0; i--)
			{
				OccultechKey key = keys[i];
				if (!SteleQuestUtility.IsHeldByPlayer(key))
				{
					continue;
				}
				keys.RemoveAt(i);
				key.questPart = null;
				key.Destroy();
				toConsume--;
			}
			stage = Stage.Vault;
			TryGenerateVault();
		}

		private void TryGenerateVault()
		{
			vaultRetryTick = -1;
			if (vaultQuestDef == null)
			{
				Log.Error("[DMS] Stele main quest has no vaultQuestDef; the line cannot finish.");
				stage = Stage.Done;
				return;
			}
			Slate slate = InitSlate();
			slate.Set("retry", vaultsGiven > 0);
			if (!vaultQuestDef.CanRun(slate, Find.World))
			{
				vaultRetryTick = Find.TickManager.TicksGame + VaultGenerateFailRetryTicks;
				return;
			}
			Quest vault = QuestUtility.GenerateQuestAndMakeAvailable(vaultQuestDef, slate);
			vault.parent = quest;
			vaultsGiven++;
			if (!vault.hidden && vault.root.sendAvailableLetter)
			{
				QuestUtility.SendLetterQuestAvailable(vault);
			}
		}

		protected override QuestScriptDef GetNextSubquestDef()
		{
			tmpSubquestDefs.Clear();
			Slate slate = InitSlate();
			foreach (QuestScriptDef def in subquestDefs)
			{
				if (def.CanRun(slate, Find.World))
				{
					tmpSubquestDefs.Add(def);
				}
			}
			if (tmpSubquestDefs.TryRandomElement(out var result))
			{
				return result;
			}
			return null;
		}

		protected override bool TryGenerateSubquest()
		{
			bool generated = base.TryGenerateSubquest();
			if (generated)
			{
				lastSubquestTick = Find.TickManager.TicksGame;
				givenFirstSubquest = true;
				return generated;
			}
			Log.Warning("[DMS] Failed to generate stele glyph subquest, trying again in 6 hours");
			lastSubquestTick += 15000f;
			return generated;
		}

		public override void Notify_QuestSignalReceived(Signal signal)
		{
			base.Notify_QuestSignalReceived(signal);
			if (signal.tag == signalSteleSubquestCompleted)
			{
				PruneKeys();
			}
		}

		protected override Slate InitSlate()
		{
			float points = ((Find.AnyPlayerHomeMap == null) ? StorytellerUtility.DefaultThreatPointsNow(Find.World) : StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap));
			Slate slate = new Slate();
			slate.Set("points", points);
			slate.Set("signalSteleSubquestCompleted", signalSteleSubquestCompleted);
			return slate;
		}

		public override void QuestPartTick()
		{
			if (!SteleQuestUtility.IsCheckTick(quest))
			{
				return;
			}
			switch (stage)
			{
			case Stage.CollectingGlyphs:
				PruneKeys();
				if (CollectedKeysCount >= KeysCount)
				{
					BeginVaultStage();
					break;
				}
				if (subquestDefs.Count != 0 && !base.Paused && CanGenerateSubquest
					&& (Rand.MTBEventOccurs(MTB, 1f, SteleQuestUtility.CheckIntervalTicks) || (float)Find.TickManager.TicksGame > lastSubquestTick + (float)MaxTime))
				{
					TryGenerateSubquest();
				}
				break;
			case Stage.Vault:
				if (vaultRetryTick >= 0 && Find.TickManager.TicksGame >= vaultRetryTick && !base.Paused)
				{
					TryGenerateVault();
				}
				break;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref signalSteleSubquestCompleted, "signalSteleSubquestCompleted");
			Scribe_Values.Look(ref keysCount, "keysCount", 0);
			Scribe_Defs.Look(ref vaultQuestDef, "vaultQuestDef");
			Scribe_Values.Look(ref stage, "stage", Stage.CollectingGlyphs);
			Scribe_Values.Look(ref lastSubquestTick, "lastSubquestTick", 0f);
			Scribe_Values.Look(ref givenFirstSubquest, "givenFirstSubquest", defaultValue: false);
			Scribe_Values.Look(ref vaultRetryTick, "vaultRetryTick", -1);
			Scribe_Values.Look(ref vaultsGiven, "vaultsGiven", 0);
			Scribe_Collections.Look(ref keys, "keys", saveDestroyedThings: false, lookMode: LookMode.Reference);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				keys ??= new List<OccultechKey>();
				PruneKeys();
				// 舊存檔沒有這兩個欄位，從 Def 補上。Old saves lack these two fields; fill them from the Def.
				QuestNode_Root_Stele root = DMS_DefOf.DMS_Stele.root as QuestNode_Root_Stele;
				if (keysCount <= 0)
				{
					keysCount = root?.keysCount ?? 4;
				}
				vaultQuestDef ??= root?.vaultQuestDef;
			}
		}

		public override void DoDebugWindowContents(Rect innerRect, ref float curY)
		{
			if (base.State != QuestPartState.Enabled)
			{
				return;
			}
			Rect rect = new Rect(innerRect.x, curY, 500f, 25f);
			Widgets.Label(rect, $"Stage: {stage}, glyphs held: {CollectedKeysCount} / {KeysCount}, tracked: {keys.Count}");
			curY += rect.height + 4f;
			if (stage == Stage.CollectingGlyphs)
			{
				Rect rect2 = new Rect(innerRect.x, curY, 500f, 25f);
				if (Widgets.ButtonText(rect2, "Force next glyph subquest"))
				{
					lastSubquestTick = -999999;
				}
				curY += rect2.height + 4f;
				Rect rect3 = new Rect(innerRect.x, curY, 500f, 25f);
				if (Widgets.ButtonText(rect3, "Skip to vault stage (keeps glyphs)"))
				{
					stage = Stage.Vault;
					TryGenerateVault();
				}
				curY += rect3.height + 4f;
			}
			else if (stage == Stage.Vault)
			{
				Rect rect4 = new Rect(innerRect.x, curY, 500f, 25f);
				if (Widgets.ButtonText(rect4, "Issue another vault now"))
				{
					TryGenerateVault();
				}
				curY += rect4.height + 4f;
			}
			base.DoDebugWindowContents(innerRect, ref curY);
		}
	}
}
