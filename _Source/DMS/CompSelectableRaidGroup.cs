using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    public class RaidGroupOption
    {
        public string labelKey;
        public RaidWaveDef raidWave;
        public QuestScriptDef quest;
        // Optional. All listed projects must be finished; omitted/empty means always unlocked.
        public List<ResearchProjectDef> researchPrerequisites;

        public string ResearchLockReason
        {
            get
            {
                var missing = new List<string>();
                if (researchPrerequisites != null)
                    foreach (var research in researchPrerequisites)
                        if (!research.IsFinished) missing.Add(research.LabelCap);
                return missing.Count == 0 ? null : (string)"DMS_RaidGroup_ResearchRequired".Translate(string.Join(", ", missing));
            }
        }
    }

    public class CompProperties_SelectableRaidGroup : CompProperties_UseEffect
    {
        public List<RaidGroupOption> groups = new List<RaidGroupOption>();
        public CompProperties_SelectableRaidGroup() { compClass = typeof(CompSelectableRaidGroup); }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (groups.Count == 0) yield return "Raid group beacon requires at least one group.";
            foreach (var group in groups)
                if (group.raidWave == null || group.quest == null || group.labelKey.NullOrEmpty())
                    yield return "Raid group beacon option requires labelKey, raidWave and quest.";
        }
    }

    public class CompSelectableRaidGroup : CompUseEffect
    {
        private int selected;
        private bool activated;
        private CompProperties_SelectableRaidGroup Props => (CompProperties_SelectableRaidGroup)props;
        private RaidGroupOption Selected => Props.groups[selected >= 0 && selected < Props.groups.Count ? selected : 0];

        // Helpers have private per-building properties; never mutate the shared ThingDef comps.
        private CompUseEffect_GiveTimedSiteQuest QuestEffect()
        {
            var effect = new CompUseEffect_GiveTimedSiteQuest { parent = parent };
            effect.Initialize(new CompProperties_UseEffect_GiveTimedSiteQuest {
                quest = Selected.quest, baseTimeoutDays = 16, minTimeoutDays = 4, maxTimeoutDays = 30,
                onlyOneActive = true, alreadyActiveKey = "DMS_DecoySite_AlreadyActive",
                cannotFindSiteKey = "DMS_DecoySite_CannotFindSite"
            });
            return effect;
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (activated || !parent.Spawned) return false;
            string researchLock = Selected.ResearchLockReason;
            if (researchLock != null) return researchLock;
            var report = Selected.raidWave.Worker.CanResolve();
            return report.Accepted ? QuestEffect().CanBeUsedBy(p) : report;
        }

        public override TaggedString ConfirmMessage(Pawn p)
        {
            int wave = Current.Game.GetComponent<GameComponent_RaidWave>()?.NumTimesCalledRaidWave(Selected.raidWave) ?? 0;
            return "DMS_RaidGroup_Confirm".Translate(Selected.labelKey.Translate(), Selected.raidWave.GetWaveDescription(wave));
        }

        public override void DoEffect(Pawn usedBy)
        {
            if (!CanBeUsedBy(usedBy).Accepted) return;
            // Verify site availability before committing the raid or consuming the beacon.
            var slate = new RimWorld.QuestGen.Slate();
            slate.Set("points", StorytellerUtility.DefaultSiteThreatPointsNow());
            slate.Set("map", parent.Map);
            slate.Set("asker", usedBy);
            slate.Set("timeoutTicks", FFF_TimedSiteUtility.QuestTimeoutTicks(16, 4, 30));
            if (!Selected.quest.CanRun(slate, Find.World))
            {
                Messages.Message("DMS_DecoySite_CannotFindSite".Translate(), parent, MessageTypeDefOf.RejectInput);
                return;
            }
            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(Selected.quest, slate);
            if (!quest.hidden && quest.root.sendAvailableLetter) QuestUtility.SendLetterQuestAvailable(quest);
            Selected.raidWave.Worker.Resolve(parent.Map);
            activated = true;
            DefDatabase<EffecterDef>.GetNamed("BurnoutMechlinkBoosterUsed").Spawn(parent.Position, parent.Map).Cleanup();
            parent.Kill();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (parent.Faction != Faction.OfPlayer || activated) yield break;
            yield return new Command_Action {
                defaultLabel = "DMS_RaidGroup_Select".Translate(Selected.labelKey.Translate()),
                defaultDesc = "DMS_RaidGroup_SelectDesc".Translate(),
                icon = parent.def.uiIcon,
                action = () => {
                    var options = new List<FloatMenuOption>();
                    for (int i = 0; i < Props.groups.Count; i++) {
                        int index = i;
                        RaidGroupOption group = Props.groups[i];
                        string researchLock = group.ResearchLockReason;
                        options.Add(researchLock == null
                            ? new FloatMenuOption(group.labelKey.Translate(), () => selected = index)
                            : new FloatMenuOption(group.labelKey.Translate() + " (" + researchLock + ")", null));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            string text = "DMS_RaidGroup_Select".Translate(Selected.labelKey.Translate());
            string researchLock = Selected.ResearchLockReason;
            return researchLock == null ? text : text + "\n" + researchLock;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref selected, "dmsSelectedRaidGroup", 0);
            Scribe_Values.Look(ref activated, "dmsRaidGroupActivated", false);
        }
    }

}
