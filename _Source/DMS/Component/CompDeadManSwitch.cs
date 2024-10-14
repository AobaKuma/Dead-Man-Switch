using RimWorld;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using System.Collections.Generic;
using Verse.Noise;
using System.Linq;
using RimWorld.Planet;

namespace DMS
{
    //在失去機械師控制後只會自動關機
    public class CompDeadManSwitch : ThingComp
    {
        public CompProperties_DeadManSwitch Props => (CompProperties_DeadManSwitch)props;
        private int delayCheck;
        public override void CompTick()
        {
            if (delayCheck > 0)
            {
                delayCheck--;
            }
            if (delayCheck <= 0)
            {
                TryTriggerDMS();
                delayCheck = Props.minDelayUntilDMS;
            }
        }
        private void TryTriggerDMS()
        {
            Pawn mech = parent as Pawn;
            if (mech.IsWorldPawn() && mech.Faction != Faction.OfPlayer && mech.GetOverseer() != null)//媽的，又有機兵覺醒啦。
            {
                //得寫個生成讓他自己找回來
            }
            
            if (!mech.Faction.IsPlayer) return;
            CompOverseerSubject a = parent.GetComp<CompOverseerSubject>();

            var overseer = mech.GetOverseer()?.mechanitor;
            if (overseer != null) return;
            if (mech.Spawned && !overseer.CanOverseeSubject) //在無法連接時自己找其他人連接。
            {
                var li = mech.Map.mapPawns.FreeColonistsSpawned.Where(c => MechanitorUtility.IsMechanitor(c));
                if (li.Any())
                {
                    foreach (var c in li)
                    {
                        Log.Message(c.Name);
                        if (c.mechanitor.CanOverseeSubject(mech))
                        {
                            mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                            c.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                            Messages.Message("DMS_AutomatroidReconnected".Translate(), new LookTargets(parent), MessageTypeDefOf.PositiveEvent);
                            return;
                        }
                    }
                }
                Messages.Message("DMS_AutomatroidDisconnected".Translate(), new LookTargets(parent), MessageTypeDefOf.NeutralEvent);
                return;
            }
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            delayCheck = Props.minDelayUntilDMS;
        }
        public override string CompInspectStringExtra()
        {  
            if (!parent.Faction.IsPlayer || parent.GetComp<CompOverseerSubject>() == null) return null;
            if (parent.GetComp<CompOverseerSubject>().State != OverseerSubjectState.Overseen)
            {
                string str = "DMS_WillTerminateTheBetrayedUnit".Translate();
                return str;
            }
            return null;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref delayCheck, "delayCheck", 0);
        }
    }

    public class CompProperties_DeadManSwitch : CompProperties
    {
        public int minDelayUntilDMS = 3000;

        public CompProperties_DeadManSwitch()
        {
            compClass = typeof(CompDeadManSwitch);
        }
    }
}
