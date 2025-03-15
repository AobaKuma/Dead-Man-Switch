using RimWorld.QuestGen;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DMS
{
    public class QuestNode_DMSShuttle : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> storeAs;

        public SlateRef<IEnumerable<Pawn>> requiredPawns;

        public SlateRef<IEnumerable<ThingDefCount>> requiredItems;

        public SlateRef<int> requireColonistCount;

        public SlateRef<bool> acceptColonists;

        public SlateRef<bool?> acceptChildren;

        public SlateRef<bool> onlyAcceptColonists;

        public SlateRef<bool> onlyAcceptHealthy;

        public SlateRef<Faction> owningFaction;

        public SlateRef<bool> permitShuttle;

        public SlateRef<float> overrideMass;

        public SlateRef<float?> minAge;

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Thing thing = ThingMaker.MakeThing(QuestKindDefOf.DMS_Shuttle);
            if (owningFaction.GetValue(slate) != null)
            {
                thing.SetFaction(owningFaction.GetValue(slate));
            }

            CompShuttle compShuttle = thing.TryGetComp<CompShuttle>();
            if (requiredPawns.GetValue(slate) != null)
            {
                compShuttle.requiredPawns.AddRange(requiredPawns.GetValue(slate));
            }

            if (requiredItems.GetValue(slate) != null)
            {
                compShuttle.requiredItems.AddRange(requiredItems.GetValue(slate));
            }

            compShuttle.acceptColonists = acceptColonists.GetValue(slate);
            compShuttle.acceptChildren = acceptChildren.GetValue(slate) ?? true;
            compShuttle.onlyAcceptColonists = onlyAcceptColonists.GetValue(slate);
            compShuttle.onlyAcceptHealthy = onlyAcceptHealthy.GetValue(slate);
            compShuttle.requiredColonistCount = requireColonistCount.GetValue(slate);
            compShuttle.permitShuttle = permitShuttle.GetValue(slate);
            compShuttle.minAge = minAge.GetValue(slate) ?? 0f;
            if (overrideMass.TryGetValue(slate, out var value) && value > 0f)
            {
                compShuttle.Transporter.massCapacityOverride = value;
            }

            QuestGen.slate.Set(storeAs.GetValue(slate), thing);
        }
    }
}
