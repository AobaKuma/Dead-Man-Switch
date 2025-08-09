using Verse;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Linq;

namespace DMS
{
    public class QuestNode_Root_BossgroupFactionExposed : QuestNode
    {
        //fuck tynan
        private static readonly IntRange MaxDelayTicksRange = new IntRange(60000, 180000);

        private static readonly IntRange MinDelayTicksRange = new IntRange(2500, 5000);
        private readonly FactionDef factionDef;
        [MustTranslate]
        private readonly string LetterLabelBossgroupSummoned, LetterLabelBossgroupArrived, LetterBossgroupSummoned, LetterBossgroupArrived, AlertBossgroupIncoming, AlertBossgroupIncomingDesc;
        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;
            Map map = slate.Get<Map>("map");
            ThingDef thingDef = slate.Get<ThingDef>("reward");
            BossgroupDef bossgroupDef = slate.Get<BossgroupDef>("bossgroup");
            int timesSummoned = slate.Get("wave", 0);
            Faction faction = FactionUtility.DefaultFactionFrom(factionDef);
            if (faction == null)
            {
                List<FactionRelation> list = new List<FactionRelation>();
                foreach (Faction item in Find.FactionManager.AllFactionsListForReading)
                {
                    list.Add(new FactionRelation
                    {
                        other = item,
                        kind = FactionRelationKind.Hostile
                    });
                }
                faction = FactionGenerator.NewGeneratedFactionWithRelations(new FactionGeneratorParms(factionDef, default, true), list);
                faction.temporary = true;
                Find.FactionManager.Add(faction);
            }
            List<Pawn> list2 = new List<Pawn>();
            List<Pawn> list3 = new List<Pawn>();
            int waveIndex = bossgroupDef.GetWaveIndex(timesSummoned);
            BossGroupWave wave = bossgroupDef.GetWave(waveIndex);
            PawnGenerationRequest request = new PawnGenerationRequest(bossgroupDef.boss.kindDef, faction, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true);
            for (int i = 0; i < wave.bossCount; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (!wave.bossApparel.NullOrEmpty())
                {
                    for (int j = 0; j < wave.bossApparel.Count; j++)
                    {
                        Apparel newApparel = (Apparel)ThingMaker.MakeThing(wave.bossApparel[j]);
                        pawn.apparel.Wear(newApparel, dropReplacedApparel: true, locked: true);
                    }
                }
                Find.WorldPawns.PassToWorld(pawn);
                list3.Add(pawn);
            }
            for (int k = 0; k < wave.escorts.Count; k++)
            {
                PawnGenerationRequest request2 = new PawnGenerationRequest(wave.escorts[k].kindDef, faction, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true);
                for (int l = 0; l < wave.escorts[k].count; l++)
                {
                    Pawn pawn2 = PawnGenerator.GeneratePawn(request2);
                    list2.Add(pawn2);
                    Find.WorldPawns.PassToWorld(pawn2);
                }
            }
            slate.Set("mapParent", map.Parent);
            slate.Set("escortees", list3.ToList());
            IntVec3 intVec = DropCellFinder.FindRaidDropCenterDistant(map);
            IEnumerable<Pawn> enumerable = list3.Concat(list2);
            foreach (Pawn item2 in enumerable)
            {
                map.attackTargetsCache.UpdateTarget(item2);
            }
            string text = QuestGen.GenerateNewSignal("BossgroupArrives");
            QuestPart_BossgroupArrivesWithMusic questPart_BossgroupArrives = new()
            {
                mapParent = map.Parent,
                bossgroupDef = bossgroupDef,
                minDelay = MinDelayTicksRange.RandomInRange,
                maxDelay = MaxDelayTicksRange.RandomInRange,
                inSignalEnable = QuestGen.slate.Get<string>("inSignal"),
                song = bossgroupDef.GetModExtension<ModExtension_BossSong>()?.GetSong(),
            };
            questPart_BossgroupArrives.outSignalsCompleted.Add(text);
            quest.AddPart(questPart_BossgroupArrives);
            Quest quest2 = quest;
            MapParent parent = map.Parent;
            string inSignal = text;
            quest2.DropPods(parent, enumerable, null, null, null, null, false, useTradeDropSpot: false, joinPlayer: false, makePrisoners: false, inSignal, null, QuestPart.SignalListenMode.OngoingOnly, intVec, destroyItemsOnCleanup: true, dropAllInSamePod: false, allowFogged: false, faction);

            var BossLabelCap = bossgroupDef.boss.kindDef.LabelCap.Named("BOSSLABEL");
            var BossLabel = bossgroupDef.boss.kindDef.label.Named("BOSSLABEL");
            var FactionColored = faction.NameColored.ToString().Named("FACTION");


            quest.Letter(LetterDefOf.NeutralEvent, null, null, label: LetterLabelBossgroupSummoned.Formatted(BossLabelCap),
                text: LetterBossgroupSummoned.Formatted(FactionColored), relatedFaction: faction);
            quest.Letter(LetterDefOf.Bossgroup,
                label: LetterLabelBossgroupArrived.Formatted(BossLabelCap),
                inSignal: text,
                chosenPawnSignal: null,
                text: LetterBossgroupArrived.Formatted(FactionColored, bossgroupDef.LeaderDescription.Named("LEADERDESC"), BossLabel, faction.def.pawnsPlural.Named("PAWNPURAL"), bossgroupDef.GetWaveDescription(waveIndex).Named("WAVEDESC"))
                , relatedFaction: faction, useColonistsOnMap: null, useColonistsFromCaravanArg: false, signalListenMode: QuestPart.SignalListenMode.OngoingOnly, lookTargets: enumerable);
            QuestPart_Bossgroup questPart_Bossgroup = new QuestPart_Bossgroup();
            questPart_Bossgroup.pawns.AddRange(enumerable);
            questPart_Bossgroup.faction = faction;
            questPart_Bossgroup.mapParent = map.Parent;
            questPart_Bossgroup.bosses.AddRange(list3);
            questPart_Bossgroup.stageLocation = intVec;
            questPart_Bossgroup.inSignal = text;
            quest.AddPart(questPart_Bossgroup);
            quest.Alert(AlertBossgroupIncoming.Formatted(BossLabelCap),
                AlertBossgroupIncomingDesc.Formatted(BossLabel), null, critical: true, getLookTargetsFromSignal: false, null, text);
            string inSignal4 = QuestGenUtility.HardcodedSignalWithQuestID("escortees.KilledLeavingsLeft");
            quest.ThingAnalyzed(thingDef, delegate
            {
                quest.Letter(LetterDefOf.PositiveEvent, null, null, null, null, useColonistsFromCaravanArg: false, QuestPart.SignalListenMode.OngoingOnly, null, filterDeadPawnsFromLookTargets: false, "[bossDefeatedLetterText]", null, "[bossDefeatedLetterLabel]");
            }, delegate
            {
                quest.Letter(LetterDefOf.PositiveEvent, null, null, null, null, useColonistsFromCaravanArg: false, QuestPart.SignalListenMode.OngoingOnly, null, filterDeadPawnsFromLookTargets: false, "[bossDefeatedStudyChipLetterText]", null, "[bossDefeatedLetterLabel]");
            }, inSignal4);
            quest.AnyPawnAlive(list3, null, delegate
            {
                QuestGen_End.End(quest, QuestEndOutcome.Unknown);
            }, QuestGenUtility.HardcodedSignalWithQuestID("escortees.Killed"));
            quest.End(QuestEndOutcome.Unknown, 0, null, QuestGenUtility.HardcodedSignalWithQuestID("mapParent.Destroyed"));
        }

        protected override bool TestRunInt(Slate slate)
        {
            if (slate.Exists("wave") && slate.Exists("bossgroup") && slate.Exists("map"))
            {
                return slate.Exists("reward");
            }
            return false;
        }
    }
}