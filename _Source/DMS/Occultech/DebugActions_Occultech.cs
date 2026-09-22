using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存科技制裁的除錯工具。制裁只會在研究完成的那一刻結算一次，
    /// 沒有這些入口就只能靠反覆開新檔＋灌研究進度來測試。
    /// </summary>
    public static class DebugActions_Occultech
    {
        [DebugAction("DMS", "Occultech: 重放制裁 / Replay sanction",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReplaySanction()
        {
            List<DebugMenuOption> options = new List<DebugMenuOption>();
            foreach (ResearchProjectDef proj in DefDatabase<ResearchProjectDef>.AllDefsListForReading
                         .Where(p => OccultechSanctionUtility.SanctionFor(p) != null)
                         .OrderBy(p => p.knowledgeCategory.defName)
                         .ThenBy(p => p.defName))
            {
                ResearchProjectDef local = proj;
                options.Add(new DebugMenuOption(
                    $"[{local.knowledgeCategory.label}] {local.LabelCap}",
                    DebugMenuOptionMode.Action,
                    delegate
                    {
                        // 先清掉紀錄，否則已結算過的專案會被跳過。
                        GameComponent_OccultechSanction.CompSafe?.ClearSanctioned(local);
                        OccultechSanctionUtility.Notify_ProjectFinished(local);
                    }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction("DMS", "Occultech: 解除永久敵對 / End kill order",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void EndKillOrder()
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null || !comp.PermanentHostile)
            {
                Messages.Message("[DMS] No occultech kill order is active.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            OccultechSanctionUtility.EndPermanentHostility();
        }

        [DebugAction("DMS", "Occultech: 放棄封存級技術 / Renounce sealed tech",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Renounce()
        {
            List<ResearchProjectDef> done = OccultechSanctionUtility.RenounceAll();
            Messages.Message(
                done.Count > 0
                    ? $"[DMS] Renounced {done.Count} sealed project(s)."
                    : "[DMS] Nothing to renounce.",
                MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction("DMS", "Occultech: 狀態 / Dump status",
            actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DumpStatus()
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            Faction fleet = OccultechSanctionUtility.Fleet;
            if (comp == null)
            {
                return;
            }
            int seniority = OccultechSanctionUtility.HighestFleetSeniority(out Pawn holder);
            Log.Message(
                "[DMS] Occultech sanction status\n"
                + $"  fleet: {fleet?.Name ?? "<none>"} goodwill={fleet?.GoodwillWith(Faction.OfPlayer)} "
                + $"relation={fleet?.RelationKindWith(Faction.OfPlayer)}\n"
                + $"  permanentHostile={comp.PermanentHostile} courtMartialOngoing={OccultechSanctionUtility.CourtMartialOngoing}\n"
                + $"  allyLocked={OccultechSanctionUtility.IsAllyLocked}\n"
                + $"  highest fleet rank: {holder?.LabelShort ?? "<none>"} (seniority {seniority})\n"
                + $"  held renounceable: {OccultechSanctionUtility.HeldRenounceableProjects().Select(p => p.defName).ToCommaList(useAnd: false)}\n"
                + $"  collateral factions: {comp.CollateralFactions.Select(f => f.Name).ToCommaList(useAnd: false)}");
        }
    }
}
