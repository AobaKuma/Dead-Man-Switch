using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using Verse;
using Verse.Grammar;

namespace DMS
{
	/// <summary>
	/// 石碑庫：集齊密鑰後解出的最終站點。先鋒殘餘守著一座埋藏的石碑；
	/// 肅清、佔領、拆卸後把石碑帶回據點即成功，留在站點上（隨地圖消失）或被摧毀即失敗，主線隨後會再指出另一座。
	///
	/// Stele vault: the final site resolved from the glyphs. Legacy remnants guard a buried stele; clearing,
	/// claiming, uninstalling and bringing it home succeeds. Leaving it behind (it vanishes with the map) or
	/// destroying it fails, and the main line points to another one later.
	/// </summary>
	public class QuestNode_Root_SteleVault : QuestNode_Root_SteleSite
	{
		public ThingDef steleDef;

		protected override void RunInt()
		{
			Slate slate = QuestGen.slate;
			Quest quest = QuestGen.quest;
			bool retry = slate.Get("retry", false);
			QuestGen.AddQuestDescriptionConstants(new Dictionary<string, string> { { "retry", retry ? "True" : "False" } });
			QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String("steleMass", steleDef.GetStatValueAbstract(StatDefOf.Mass).ToString("0")) });
			Site site = GenerateSite();
			if (site == null)
			{
				return;
			}
			string mapGenerated = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated");
			string mapRemoved = QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved");

			QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
			choice.rewards.Add(new Reward_DefinedThingDef(steleDef));
			quest.RewardChoice().choices.Add(choice);

			quest.AddPart(new QuestPart_SteleVault
			{
				inSignalEnable = slate.Get<string>("inSignal"),
				inSignalMapGenerated = mapGenerated,
				inSignalMapRemoved = mapRemoved,
				site = site,
				steleDef = steleDef
			});
			quest.Letter(LetterDefOf.NeutralEvent, mapGenerated, text: "[letterTextMapGenerated]", label: "[letterLabelMapGenerated]");
		}

		protected override bool TestRunInt(Slate slate)
		{
			return steleDef != null && base.TestRunInt(slate);
		}
	}

	public class QuestPart_SteleVault : QuestPartActivable
	{
		public string inSignalMapGenerated;

		public string inSignalMapRemoved;

		public MapParent site;

		public ThingDef steleDef;

		private Thing stele;

		private bool resolved;

		protected override void ProcessQuestSignal(Signal signal)
		{
			base.ProcessQuestSignal(signal);
			if (resolved)
			{
				return;
			}
			if (signal.tag == inSignalMapGenerated)
			{
				RecordStele(allowFallback: true);
			}
			else if (signal.tag == inSignalMapRemoved)
			{
				// 地圖移除時留在上面的物品會被標成 Destroyed（微縮箱裡的石碑也一樣）；還活著就是被帶走了。
				// Things left on a removed map read as Destroyed (a stele in a minified crate too); alive means carried off.
				if (stele != null && !stele.Destroyed)
				{
					Succeed();
				}
				else
				{
					Fail();
				}
			}
		}

		private void RecordStele(bool allowFallback)
		{
			Map map = site?.Map;
			if (map == null || steleDef == null)
			{
				return;
			}
			stele = map.listerThings.ThingsOfDef(steleDef).FirstOrFallback();
			if (stele == null && allowFallback)
			{
				Log.Warning("[DMS] Stele vault generated without a stele; spawning one near the map centre.");
				stele = SteleQuestUtility.SpawnFallback(steleDef, map, map.ParentFaction);
			}
		}

		public override void QuestPartTick()
		{
			base.QuestPartTick();
			if (resolved || !SteleQuestUtility.IsCheckTick(quest))
			{
				return;
			}
			if (stele == null)
			{
				// 讀檔後參照遺失時，站點地圖還在就重新找一次。Re-find on the site map if the reference was lost on load.
				if (site?.Map != null)
				{
					RecordStele(allowFallback: false);
				}
				return;
			}
			if (stele.Destroyed)
			{
				// 地圖還在時被摧毀（地圖移除另由訊號處理）。Destroyed while the map still exists; map removal is handled by the signal.
				if (site?.Map != null)
				{
					Fail();
				}
				return;
			}
			if (SteleQuestUtility.IsHeldByPlayer(stele))
			{
				Succeed();
			}
		}

		private void Succeed()
		{
			resolved = true;
			Find.LetterStack.ReceiveLetter("DMS_Stele_SteleSecuredLabel".Translate(), "DMS_Stele_SteleSecuredText".Translate(),
				LetterDefOf.PositiveEvent, stele.SpawnedOrAnyParentSpawned ? new LookTargets(stele.SpawnedParentOrMe) : null, null, quest);
			SteleQuestUtility.MainPart(quest)?.Notify_VaultSecured();
			Complete();
			quest.End(QuestEndOutcome.Success, sendLetter: false);
		}

		private void Fail()
		{
			resolved = true;
			Find.LetterStack.ReceiveLetter("DMS_Stele_SteleLostLabel".Translate(), "DMS_Stele_SteleLostText".Translate(),
				LetterDefOf.NegativeEvent, null, null, quest);
			SteleQuestUtility.MainPart(quest)?.Notify_VaultLost();
			Complete();
			quest.End(QuestEndOutcome.Fail, sendLetter: false);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref inSignalMapGenerated, "inSignalMapGenerated");
			Scribe_Values.Look(ref inSignalMapRemoved, "inSignalMapRemoved");
			Scribe_References.Look(ref site, "site");
			Scribe_Defs.Look(ref steleDef, "steleDef");
			Scribe_References.Look(ref stele, "stele");
			Scribe_Values.Look(ref resolved, "resolved", defaultValue: false);
		}

		public override void AssignDebugData()
		{
			base.AssignDebugData();
			inSignalMapGenerated = "DebugSignal" + Rand.Int;
			inSignalMapRemoved = "DebugSignal" + Rand.Int;
		}
	}
}
