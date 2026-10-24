using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 研究完成 → 觸發封存科技制裁，以及研究解鎖的任務線（<see cref="ModExtension_ResearchQuest"/>）。
    /// 掛 postfix 而非 prefix：讓原生把進度寫滿、解鎖物生效、完成信件發完之後才結算，
    /// 這樣制裁信件會排在研究完成信件後面，敘事順序正確。
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class Patch_ResearchManager_FinishProject
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return;
            }
            try
            {
                OccultechSanctionUtility.Notify_ProjectFinished(proj);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Occultech sanction hook failed for {proj.defName}: {ex}");
            }
            // 研究解鎖的任務線（例：量子位元破譯 → 石碑）。排在制裁之後，信件順序為研究完成 → 制裁 → 任務。
            try
            {
                ResearchQuestUtility.Notify_ProjectFinished(proj);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Research quest hook failed for {proj.defName}: {ex}");
            }
            // 脫序者交付、等前置完成的封存檔案。排在最後：這項研究的信件與制裁先發完，解譯信件才接在後面。
            // Sealed files the deviant handed over that were waiting on prerequisites. Last, so their decode letters follow
            // this project's own letters and sanction.
            try
            {
                DeviantTechUtility.Notify_ProjectFinished(proj);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Deviant pending-grant hook failed for {proj.defName}: {ex}");
            }
            // 追殺間隔隨研究收緊（微電子基礎完成前 28~32 天，完成後 5~7 天）。只換 def 上的值：
            // 已經在倒數的那一波維持原樣，下一波排程時才用新間隔。
            // Hunt intervals tighten with research (28~32 days before MicroelectronicsBasics, 5~7 after). Only the def
            // value changes: a wave already counting down keeps its time, and the next one scheduled uses the new range.
            try
            {
                HuntIntervalUtility.RefreshAll();
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Hunt interval hook failed for {proj.defName}: {ex}");
            }
        }
    }

    /// <summary>
    /// 封鎖對殖民艦隊的正向好感度變動。
    ///
    /// - 永久敵對（隱匿級）：一律擋下，只能靠軍事法庭解除。
    /// - 庇護被追殺的脫序者（脫序者接納任務）：同樣擋下，直到脫序者不在或受審結案。
    /// - 持有未放棄的封存級技術：好感度已達盟友門檻下緣時擋下，讓關係停在中立。
    ///
    /// 制裁機制自己改關係時會先設 <see cref="OccultechSanctionUtility.GuardSuppressed"/>，不受此限。
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
    public static class Patch_Faction_CanChangeGoodwillFor
    {
        [HarmonyPostfix]
        public static void Postfix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (!__result || goodwillChange <= 0 || OccultechSanctionUtility.GuardSuppressed)
            {
                return;
            }
            if (!OccultechRelationGuard.IsPlayerFleetPair(__instance, other, out Faction fleet))
            {
                return;
            }

            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null)
            {
                return;
            }
            // 隱匿級永久敵對，或正庇護著被追殺的脫序者。Occulted kill order, or a hunted deviant being sheltered.
            if (OccultechSanctionUtility.HostilityLocked)
            {
                __result = false;
                return;
            }
            if (OccultechSanctionUtility.IsAllyLocked
                && fleet.GoodwillWith(Faction.OfPlayer) >= OccultechSanctionUtility.AllyGoodwillCeiling)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// 關係等級的即時校正。原生 <see cref="FactionRelation.CheckKindThresholds"/> 依好感度門檻
    /// 自動升降關係；這裡在它跑完之後把違反封存科技制裁的結果拉回來。
    ///
    /// <see cref="Patch_Faction_CanChangeGoodwillFor"/> 已經擋掉大部分來源，本 patch 處理
    /// 「一次大額好感度直接跨過門檻」以及其他模組繞過 CanChangeGoodwillFor 的情況。
    /// </summary>
    [HarmonyPatch(typeof(FactionRelation), nameof(FactionRelation.CheckKindThresholds))]
    public static class Patch_FactionRelation_CheckKindThresholds
    {
        [HarmonyPostfix]
        public static void Postfix(FactionRelation __instance, Faction faction)
        {
            if (OccultechSanctionUtility.GuardSuppressed || __instance == null)
            {
                return;
            }
            if (!OccultechRelationGuard.IsPlayerFleetPair(faction, __instance.other, out _))
            {
                return;
            }

            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null)
            {
                return;
            }
            if (OccultechSanctionUtility.HostilityLocked)
            {
                __instance.kind = FactionRelationKind.Hostile;
                return;
            }
            if (__instance.kind == FactionRelationKind.Ally && OccultechSanctionUtility.IsAllyLocked)
            {
                __instance.kind = FactionRelationKind.Neutral;
            }
        }
    }

    /// <summary>
    /// 通訊台對話：向艦隊申報放棄已研究的封存級技術。
    /// 這是「回到盟友關係」的唯一途徑，也是玩家能主動操作制裁狀態的唯一介面。
    /// </summary>
    [HarmonyPatch(typeof(FactionDialogMaker), nameof(FactionDialogMaker.FactionDialogFor))]
    public static class Patch_FactionDialogMaker_FactionDialogFor
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn negotiator, Faction faction, ref DiaNode __result)
        {
            if (__result == null || faction == null || faction.def != DMS_DefOf.DMS_Army)
            {
                return;
            }
            try
            {
                DiaNode root = __result;
                List<ResearchProjectDef> held = OccultechSanctionUtility.HeldRenounceableProjects().ToList();
                if (held.Count == 0)
                {
                    return;
                }

                DiaOption opt = new DiaOption("DMS_Occultech_RenounceOption".Translate());

                GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
                if (comp != null && comp.PermanentHostile)
                {
                    // 永久敵對期間艦隊根本不受理申報：出路只有軍事法庭。
                    opt.Disable("DMS_Occultech_RenounceBlockedPermanent".Translate());
                }
                else if (DeviantHuntUtility.HarboredDeviant != null)
                {
                    // 正庇護著被追殺的脫序者時同樣不受理。Nor while a hunted deviant is being sheltered.
                    opt.Disable("DMS_Occultech_RenounceBlockedDeviant".Translate());
                }
                else
                {
                    string list = held.Select(p => p.LabelCap.ToString()).ToLineList("- ");
                    DiaNode confirm = new DiaNode("DMS_Occultech_RenounceConfirm".Translate(faction.Name, list));

                    DiaOption accept = new DiaOption("DMS_Occultech_RenounceAccept".Translate())
                    {
                        resolveTree = true,
                        action = delegate
                        {
                            List<ResearchProjectDef> done = OccultechSanctionUtility.RenounceAll();
                            if (done.Count > 0)
                            {
                                Messages.Message(
                                    "DMS_Occultech_RenouncedMessage".Translate(done.Count),
                                    MessageTypeDefOf.NeutralEvent, historical: false);
                            }
                        },
                    };
                    confirm.options.Add(accept);
                    confirm.options.Add(new DiaOption("GoBack".Translate()) { linkLateBind = () => root });
                    opt.link = confirm;
                }

                // 插在最後一個選項（原生的「斷線」）之前，讓「斷線」維持在列表底部。
                int insertAt = Math.Max(0, root.options.Count - 1);
                root.options.Insert(insertAt, opt);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Occultech renounce dialog failed: {ex}");
            }
        }
    }

    /// <summary>關係守衛共用的配對判定。放在獨立類別避免每個 patch 各寫一份。</summary>
    internal static class OccultechRelationGuard
    {
        /// <summary>
        /// 這兩個派系是否恰好是「玩家 ↔ 殖民艦隊」。臨時派系不算:Hegemony 的使節任務會用艦隊的 def 建一個
        /// temporary 代理派系,它不是艦隊本身,不該被制裁鎖成敵對。
        /// Whether these are exactly player ↔ fleet. Temporary factions don't count: Hegemony envoy missions create a
        /// temporary proxy with the fleet's def, which is not the fleet and must not be locked hostile by the sanction.
        /// </summary>
        public static bool IsPlayerFleetPair(Faction a, Faction b, out Faction fleet)
        {
            fleet = null;
            if (a == null || b == null || a == b || a.temporary || b.temporary)
            {
                return false;
            }
            FactionDef army = DMS_DefOf.DMS_Army;
            if (a.IsPlayer && b.def == army)
            {
                fleet = b;
                return true;
            }
            if (b.IsPlayer && a.def == army)
            {
                fleet = a;
                return true;
            }
            return false;
        }
    }
}
