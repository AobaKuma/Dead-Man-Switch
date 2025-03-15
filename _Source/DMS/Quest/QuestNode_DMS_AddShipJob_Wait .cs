using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.Grammar;

namespace DMS
{
    public class QuestNode_DMSAddShipJob_Wait : QuestNode_AddShipJob
    {
        public SlateRef<int> ticks;

        public SlateRef<bool> leaveImmediatelyWhenSatisfied;

        public SlateRef<List<Thing>> sendAwayIfAllDespawned;

        protected override void AddJobVars(ShipJob shipJob, Slate slate)
        {
            if (shipJob is ShipJob_Wait shipJob_Wait)
            {
                shipJob_Wait.leaveImmediatelyWhenSatisfied = leaveImmediatelyWhenSatisfied.GetValue(slate);
                shipJob_Wait.sendAwayIfAllDespawned = sendAwayIfAllDespawned.GetValue(slate);
            }

            if (shipJob is ShipJob_WaitTime shipJob_WaitTime)
            {
                shipJob_WaitTime.duration = ticks.GetValue(slate);
            }
        }
        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;
            ShipJob shipJob = ShipJobMaker.MakeShipJob(jobDef.GetValue(slate) ?? DefaultShipJobDef);
            AddJobVars(shipJob, slate);
            QuestPart_AddShipJob part = new QuestPart_AddShipJob
            {
                inSignal = (QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal")),
                shipJob = shipJob,
                shipJobStartMode = (shipJobStartMode.GetValue(slate) ?? ShipJobStartMode.Queue),
                transportShip = transportShip.GetValue(slate)
            };

            //来一些步兵
            List<Pawn> list = new List<Pawn>();
            for (int j = 0; j < 6; j++)
            {
                Pawn item = quest.GeneratePawn(QuestKindDefOf.DMS_Escort, QuestGen.slate.Get<Faction>("enemyFaction"));
                list.Add(item);
            }
            quest.EnsureNotDowned(list);
            slate.Set("defenders", list);
            slate.Set("shuttleContents", list);
            transportShip.GetValue(slate).shipThing.TryGetComp<CompShuttle>().requiredPawns.AddRange(list);
            transportShip.GetValue(slate).TransporterComp.innerContainer.TryAddRangeOrTransfer(list, canMergeWithExistingStacks: true, destroyLeftover: true);

            //兵的行为
            QuestPart_DMS_EscortPawn questPart_EscortPawn = new QuestPart_DMS_EscortPawn();
            questPart_EscortPawn.inSignal = (QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal"));
            questPart_EscortPawn.escortee = QuestGen.slate.Get<Pawn>("joiner");
            Log.Warning(questPart_EscortPawn.escortee.Name.ToString());
            questPart_EscortPawn.pawns.AddRange(list);
            questPart_EscortPawn.mapOfPawn = QuestGen.slate.Get<Pawn>("joiner");
            questPart_EscortPawn.faction = QuestGen.slate.Get<Pawn>("joiner").Faction;
            questPart_EscortPawn.shuttle = transportShip.GetValue(slate).shipThing;
            questPart_EscortPawn.questTag = "rua";
            questPart_EscortPawn.leavingDangerMessage = "MessageBestowingDanger".Translate();
            quest.AddPart(questPart_EscortPawn);
            //飞船下人
            quest.AddShipJob(transportShip.GetValue(slate), ShipJobDefOf.Unload);

            QuestGen.quest.AddPart(part);
        }
    }
}