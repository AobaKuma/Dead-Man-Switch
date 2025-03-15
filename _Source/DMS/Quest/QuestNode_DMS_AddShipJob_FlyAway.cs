using Mono.Unix.Native;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Grammar;
using Verse.Noise;

namespace DMS
{
    public class QuestNode_DMS_AddShipJob_FlyAway : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> inSignal;

        public SlateRef<TransportShip> transportShip;

        public SlateRef<ShipJobDef> jobDef;

        public SlateRef<ShipJobStartMode?> shipJobStartMode;

        public SlateRef<MapParent> destination;

        public SlateRef<TransportShipDropMode?> unsatisfiedDropMode;

        public SlateRef<Faction> faction;

        public SlateRef<int> amount;

        protected ShipJobDef DefaultShipJobDef => ShipJobDefOf.FlyAway;

        protected override void RunInt()
        {
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
            QuestGen.quest.AddPart(part);

            //任务成功的part
            QuestPart_ToSendSignal signalPart = new QuestPart_ToSendSignal
            {
                inSignal = (QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? QuestGen.slate.Get<string>("inSignal")),
                outSignal = (QuestGenUtility.HardcodedSignalWithQuestID("Success")),
            };
            QuestGen.quest.AddPart(signalPart);
        }

        protected void PostProcessRewardChoice(QuestPart_Choice rewardChoice)
        {
            if (ModsConfig.IdeologyActive && Faction.OfPlayer.ideos.FluidIdeo != null)
            {
                for (int i = 0; i < rewardChoice.choices.Count; i++)
                {
                    rewardChoice.choices[i].rewards.Add(new Reward_DevelopmentPoints(QuestGen.quest));
                }
            }
        }

        protected void AddJobVars(ShipJob shipJob, Slate slate)
        {
            if (shipJob is ShipJob_FlyAway shipJob_FlyAway)
            {
                MapParent value = destination.GetValue(slate);
                if (value != null)
                {
                    shipJob_FlyAway.destinationTile = value.Tile;
                }

                shipJob_FlyAway.dropMode = unsatisfiedDropMode.GetValue(slate) ?? TransportShipDropMode.NonRequired;
            }
        }
        protected override bool TestRunInt(Slate slate)
        {
            if (jobDef.GetValue(slate) == null)
            {
                return DefaultShipJobDef != null;
            }

            return true;
        }
    }
}