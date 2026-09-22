using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 封存科技制裁機制的唯一判定與執行入口。
    ///
    /// 規則（依 <see cref="OccultechTier"/>）：
    /// - 限制級 Restricted：每完成一項 −80 艦隊好感度。殖民地有准尉（seniority 200）以上的
    ///   殖民者時視為持有臨時解禁令，完全免除。
    /// - 封存級 Sealed：每完成一項立即與艦隊敵對並馬上受到一波襲擊。持有任何未放棄的封存級
    ///   技術期間，與艦隊的關係最高只能回到中立；必須透過通訊台主動申報放棄才能重回盟友。
    ///   殖民地有准將（seniority 1000）以上的殖民者時免除。
    /// - 隱匿級 Occulted：無豁免。任一項完成即與艦隊永久敵對、遭到週期性追殺，並與隨機三個
    ///   艦隊友好派系敵對（該三者不鎖好感，可自行修復）。永久敵對只能透過軍事法庭
    ///   （把官階最高者送去服刑一年）解除，解除後回到中立。
    ///
    /// 防禦性設計：找不到艦隊派系、Def 缺少擴充、尚未進入遊戲等情況一律靜默略過，
    /// 制裁失效只是少扣一次好感度，誤判卻可能讓存檔卡在無法挽回的敵對狀態。
    /// </summary>
    public static class OccultechSanctionUtility
    {
        /// <summary>
        /// 關係守衛抑制旗標。制裁機制自己調整關係時（軍事法庭、放棄技術、解除永久敵對）
        /// 必須繞過 <see cref="Patch_Faction_CanChangeGoodwillFor"/> 的封鎖，否則會被自己擋下。
        /// </summary>
        public static bool GuardSuppressed;

        /// <summary>盟友門檻是 goodwill ≥ 75（見 FactionRelation.CheckKindThresholds），此處卡在 74。</summary>
        public const int AllyGoodwillCeiling = 74;

        private static List<ModExtension_OccultechSanction> allSanctions;

        // ─────────────────────────────── 查詢 ───────────────────────────────

        /// <summary>殖民艦隊派系；尚未生成或已被移除時回傳 null。</summary>
        public static Faction Fleet
        {
            get
            {
                if (Current.Game == null || Find.FactionManager == null)
                {
                    return null;
                }
                return Find.FactionManager.FirstFactionOfDef(DMS_DefOf.DMS_Army);
            }
        }

        /// <summary>所有掛了制裁擴充的知識類別設定（Def 生命週期內固定，可快取）。</summary>
        public static List<ModExtension_OccultechSanction> AllSanctions
        {
            get
            {
                if (allSanctions != null)
                {
                    return allSanctions;
                }
                allSanctions = new List<ModExtension_OccultechSanction>();
                foreach (KnowledgeCategoryDef cat in DefDatabase<KnowledgeCategoryDef>.AllDefsListForReading)
                {
                    ModExtension_OccultechSanction ext = cat.GetModExtension<ModExtension_OccultechSanction>();
                    if (ext != null)
                    {
                        allSanctions.Add(ext);
                    }
                }
                return allSanctions;
            }
        }

        public static ModExtension_OccultechSanction SanctionOfTier(OccultechTier tier)
        {
            List<ModExtension_OccultechSanction> list = AllSanctions;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].tier == tier)
                {
                    return list[i];
                }
            }
            return null;
        }

        /// <summary>隱匿級設定（追殺排程用）。</summary>
        public static ModExtension_OccultechSanction OccultedSanction => SanctionOfTier(OccultechTier.Occulted);

        /// <summary>取得此研究專案適用的制裁規則；非封存科技專案回傳 null。</summary>
        public static ModExtension_OccultechSanction SanctionFor(ResearchProjectDef proj)
        {
            return proj?.knowledgeCategory?.GetModExtension<ModExtension_OccultechSanction>();
        }

        /// <summary>
        /// 殖民地（含商隊與運輸途中）持有的最高艦隊官階 seniority；沒有任何持銜者回傳 -1。
        /// 一併回傳該名殖民者供信件顯示。
        /// </summary>
        public static int HighestFleetSeniority(out Pawn holder)
        {
            holder = null;
            int best = -1;
            Faction fleet = Fleet;
            if (fleet == null || !ModsConfig.RoyaltyActive)
            {
                return best;
            }
            List<Pawn> colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                RoyalTitleDef title = p.royalty?.GetCurrentTitle(fleet);
                if (title != null && title.seniority > best)
                {
                    best = title.seniority;
                    holder = p;
                }
            }
            return best;
        }

        /// <summary>此等級的豁免條件是否成立。</summary>
        public static bool IsExempt(ModExtension_OccultechSanction ext, out Pawn holder, out RoyalTitleDef heldTitle)
        {
            holder = null;
            heldTitle = null;
            if (ext == null || ext.exemptSeniority < 0)
            {
                return false;
            }
            int seniority = HighestFleetSeniority(out holder);
            if (seniority < ext.exemptSeniority)
            {
                holder = null;
                return false;
            }
            heldTitle = holder?.royalty?.GetCurrentTitle(Fleet);
            return true;
        }

        /// <summary>豁免門檻對應的官階（取 seniority 達標的最低階），純粹供文案顯示。</summary>
        public static RoyalTitleDef RequiredTitleFor(ModExtension_OccultechSanction ext)
        {
            Faction fleet = Fleet;
            if (ext == null || ext.exemptSeniority < 0 || fleet == null || !ModsConfig.RoyaltyActive)
            {
                return null;
            }
            List<RoyalTitleDef> titles = fleet.def.RoyalTitlesAllInSeniorityOrderForReading;
            if (titles == null)
            {
                return null;
            }
            for (int i = 0; i < titles.Count; i++)
            {
                if (titles[i].seniority >= ext.exemptSeniority)
                {
                    return titles[i];
                }
            }
            return null;
        }

        /// <summary>目前是否仍持有任何未放棄的封存級（可申報放棄的）已完成研究。</summary>
        public static bool HoldsRenounceableTech => HeldRenounceableProjects().Any();

        /// <summary>所有已完成、且所屬等級標記為可放棄的封存科技專案。</summary>
        public static IEnumerable<ResearchProjectDef> HeldRenounceableProjects()
        {
            if (Current.Game == null || Find.ResearchManager == null)
            {
                yield break;
            }
            List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ResearchProjectDef proj = all[i];
                ModExtension_OccultechSanction ext = SanctionFor(proj);
                if (ext == null || !ext.renounceable)
                {
                    continue;
                }
                bool finished;
                try
                {
                    finished = proj.IsFinished;
                }
                catch
                {
                    continue;
                }
                if (finished)
                {
                    yield return proj;
                }
            }
        }

        // IsAllyLocked 會被 FactionRelation.CheckKindThresholds 的 postfix 間接呼叫，
        // 而後者在外交結算時可能一輪跑很多次；掃描整份研究 Def 清單太貴，故快取一小段時間。
        private const int AllyLockCacheTicks = 60;
        private static int allyLockCacheTick = -1;
        private static bool allyLockCached;

        /// <summary>與艦隊的關係是否被鎖在「最高中立」（持有未放棄的封存級技術）。</summary>
        public static bool IsAllyLocked
        {
            get
            {
                GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
                if (comp == null || comp.PermanentHostile)
                {
                    return false;
                }
                int now = Find.TickManager?.TicksGame ?? 0;
                if (allyLockCacheTick >= 0 && now - allyLockCacheTick < AllyLockCacheTicks)
                {
                    return allyLockCached;
                }
                allyLockCacheTick = now;
                allyLockCached = HoldsRenounceableTech;
                return allyLockCached;
            }
        }

        /// <summary>研究完成／放棄後呼叫，讓 <see cref="IsAllyLocked"/> 立刻重新計算。</summary>
        public static void InvalidateAllyLockCache()
        {
            allyLockCacheTick = -1;
        }

        private static int courtCacheTick = -1;
        private static bool courtCached;

        /// <summary>
        /// 目前是否有進行中的軍事法庭任務。永久敵對的強制校正在審判期間必須停手：
        /// 休戰本來就是任務流程的一部分，否則接受任務的下一秒就會被自己打回敵對。
        ///
        /// 用「有沒有進行中的任務」而非存檔旗標：任務失敗、被刪、讀舊檔等情況都會自動歸位，
        /// 不會有旗標卡住導致永久敵對被悄悄解除的風險。
        /// </summary>
        public static bool CourtMartialOngoing
        {
            get
            {
                if (Current.Game == null)
                {
                    return false;
                }
                int now = Find.TickManager?.TicksGame ?? 0;
                if (courtCacheTick >= 0 && now - courtCacheTick < AllyLockCacheTicks)
                {
                    return courtCached;
                }
                courtCacheTick = now;
                courtCached = CourtMartialOngoingUncached;
                return courtCached;
            }
        }

        /// <summary>
        /// 不走快取的版本。兜底校正（<see cref="EnforceFleetRelation"/>）必須用這個：
        /// 玩家剛按下接受、休戰才生效的那幾十 tick 內，快取可能還停在「沒有審判」，
        /// 校正就會把剛談好的休戰一巴掌打回敵對。
        /// </summary>
        private static bool CourtMartialOngoingUncached
        {
            get
            {
                List<Quest> quests = Find.QuestManager?.QuestsListForReading;
                if (quests == null)
                {
                    return false;
                }
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].State == QuestState.Ongoing && quests[i].root == DMS_DefOf.DMS_CourtMartial)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // ─────────────────────────────── 制裁執行 ───────────────────────────────

        /// <summary>
        /// 研究完成時的總入口（由 <see cref="Patch_ResearchManager_FinishProject"/> 呼叫）。
        /// 每個專案終生只結算一次，紀錄存在 <see cref="GameComponent_OccultechSanction"/>。
        /// </summary>
        public static void Notify_ProjectFinished(ResearchProjectDef proj)
        {
            ModExtension_OccultechSanction ext = SanctionFor(proj);
            if (ext == null)
            {
                return;
            }
            if (Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            if (comp == null || !comp.MarkSanctioned(proj))
            {
                return;
            }
            InvalidateAllyLockCache();

            try
            {
                ApplySanction(proj, ext, comp);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Occultech sanction failed for {proj.defName}: {ex}");
            }
        }

        private static void ApplySanction(ResearchProjectDef proj, ModExtension_OccultechSanction ext,
            GameComponent_OccultechSanction comp)
        {
            Faction fleet = Fleet;
            if (fleet == null)
            {
                return;
            }

            // 豁免：臨時解禁令。隱匿級的 exemptSeniority 為負，永遠走不到這裡。
            if (IsExempt(ext, out Pawn holder, out RoyalTitleDef heldTitle))
            {
                Find.LetterStack.ReceiveLetter(
                    "DMS_Occultech_ExemptLabel".Translate(proj.LabelCap),
                    "DMS_Occultech_ExemptText".Translate(
                        proj.LabelCap,
                        holder?.LabelShortCap ?? "?",
                        heldTitle?.GetLabelCapFor(holder) ?? "?",
                        fleet.Name),
                    LetterDefOf.NeutralEvent, holder, fleet);
                return;
            }

            switch (ext.tier)
            {
                case OccultechTier.Restricted:
                    ApplyRestricted(proj, ext, fleet);
                    break;
                case OccultechTier.Sealed:
                    ApplySealed(proj, ext, fleet);
                    break;
                case OccultechTier.Occulted:
                    ApplyOcculted(proj, ext, fleet, comp);
                    break;
            }
        }

        /// <summary>限制級：單純扣好感度。</summary>
        private static void ApplyRestricted(ResearchProjectDef proj, ModExtension_OccultechSanction ext, Faction fleet)
        {
            int before = fleet.GoodwillWith(Faction.OfPlayer);
            if (ext.goodwillPenalty != 0)
            {
                WithGuardSuppressed(() => fleet.TryAffectGoodwillWith(
                    Faction.OfPlayer, ext.goodwillPenalty, canSendMessage: false, canSendHostilityLetter: true,
                    DMS_DefOf.DMS_OccultechResearched));
            }
            int after = fleet.GoodwillWith(Faction.OfPlayer);

            Find.LetterStack.ReceiveLetter(
                "DMS_Occultech_RestrictedLabel".Translate(proj.LabelCap),
                "DMS_Occultech_RestrictedText".Translate(
                    proj.LabelCap, fleet.Name, (-ext.goodwillPenalty).ToString(),
                    before.ToString(), after.ToString(),
                    RequiredTitleFor(ext)?.LabelCap ?? "?"),
                LetterDefOf.NegativeEvent, null, fleet);
        }

        /// <summary>封存級：立即敵對 ＋ 一波襲擊。之後靠 IsAllyLocked 卡住盟友關係。</summary>
        private static void ApplySealed(ResearchProjectDef proj, ModExtension_OccultechSanction ext, Faction fleet)
        {
            if (ext.hostileOnComplete)
            {
                SetFleetHostile(fleet);
            }

            bool raided = false;
            if (ext.raidOnComplete)
            {
                raided = TryFireFleetRaid(ext.raidPointsFactor, ext.minRaidPoints);
            }

            string body = "DMS_Occultech_SealedText".Translate(
                proj.LabelCap, fleet.Name, RequiredTitleFor(ext)?.LabelCap ?? "?").ToString();
            if (raided)
            {
                body += "\n\n" + "DMS_Occultech_SealedRaidLine".Translate(fleet.Name).ToString();
            }

            Find.LetterStack.ReceiveLetter(
                "DMS_Occultech_SealedLabel".Translate(proj.LabelCap),
                body,
                LetterDefOf.ThreatBig, null, fleet);
        }

        /// <summary>隱匿級：永久敵對 ＋ 追殺 ＋ 連坐艦隊友好派系。</summary>
        private static void ApplyOcculted(ResearchProjectDef proj, ModExtension_OccultechSanction ext,
            Faction fleet, GameComponent_OccultechSanction comp)
        {
            SetFleetHostile(fleet);
            comp.BeginPermanentHostility(ext.huntIntervalDays);

            List<Faction> collateral = PickFleetAllies(fleet, ext.hostileAllyCount);
            foreach (Faction f in collateral)
            {
                comp.AddCollateralFaction(f);
                // 連坐派系「不鎖好感」：只是打到敵對線以下，玩家之後可以自行修復。
                int delta = -100 - f.GoodwillWith(Faction.OfPlayer);
                if (delta != 0)
                {
                    f.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false,
                        canSendHostilityLetter: false, DMS_DefOf.DMS_OccultechResearched);
                }
                if (f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                {
                    f.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false, null, null);
                }
            }

            if (ext.raidOnComplete)
            {
                TryFireFleetRaid(ext.raidPointsFactor, ext.minRaidPoints);
            }

            string names = collateral.Count > 0
                ? collateral.Select(f => f.Name).ToCommaList(useAnd: true)
                : "DMS_Occultech_OccultedNoAllies".Translate().ToString();

            Find.LetterStack.ReceiveLetter(
                "DMS_Occultech_OccultedLabel".Translate(proj.LabelCap),
                "DMS_Occultech_OccultedText".Translate(proj.LabelCap, fleet.Name, names),
                LetterDefOf.ThreatBig, null, fleet);
        }

        /// <summary>
        /// 挑出艦隊的友好派系作為連坐對象。優先取與艦隊實際為盟友者，不足時從
        /// 其他非隱藏、目前尚未與玩家敵對的派系補滿。
        /// </summary>
        private static List<Faction> PickFleetAllies(Faction fleet, int count)
        {
            List<Faction> result = new List<Faction>();
            if (count <= 0)
            {
                return result;
            }

            bool Eligible(Faction f) =>
                f != null && f != fleet && !f.IsPlayer && !f.Hidden && !f.defeated
                && f.HasGoodwill && !f.def.permanentEnemy
                && f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile
                && !result.Contains(f);

            List<Faction> allies = Find.FactionManager.AllFactionsListForReading
                .Where(f => Eligible(f) && fleet.RelationKindWith(f) == FactionRelationKind.Ally)
                .InRandomOrder().ToList();
            foreach (Faction f in allies)
            {
                if (result.Count >= count) break;
                result.Add(f);
            }

            if (result.Count < count)
            {
                List<Faction> rest = Find.FactionManager.AllFactionsListForReading
                    .Where(Eligible).InRandomOrder().ToList();
                foreach (Faction f in rest)
                {
                    if (result.Count >= count) break;
                    result.Add(f);
                }
            }
            return result;
        }

        // ─────────────────────────────── 關係控制 ───────────────────────────────

        /// <summary>把艦隊打到敵對（繞過自己的守衛）。</summary>
        public static void SetFleetHostile(Faction fleet)
        {
            if (fleet == null)
            {
                return;
            }
            WithGuardSuppressed(() =>
            {
                int delta = -100 - fleet.GoodwillWith(Faction.OfPlayer);
                if (delta != 0)
                {
                    fleet.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false,
                        canSendHostilityLetter: false, DMS_DefOf.DMS_OccultechResearched);
                }
                if (fleet.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                {
                    fleet.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false, null, null);
                }
            });
        }

        /// <summary>把艦隊設回指定好感度／關係（供軍事法庭與放棄流程使用）。</summary>
        public static void SetFleetRelation(int goodwill, FactionRelationKind kind)
        {
            Faction fleet = Fleet;
            if (fleet == null)
            {
                return;
            }
            WithGuardSuppressed(() =>
            {
                int delta = goodwill - fleet.GoodwillWith(Faction.OfPlayer);
                if (delta != 0)
                {
                    fleet.TryAffectGoodwillWith(Faction.OfPlayer, delta, canSendMessage: false,
                        canSendHostilityLetter: false);
                }
                if (fleet.RelationKindWith(Faction.OfPlayer) != kind)
                {
                    fleet.SetRelationDirect(Faction.OfPlayer, kind, false, null, null);
                }
            });
        }

        /// <summary>
        /// 兜底校正（每 2000 ticks 由 GameComponent 呼叫）：Harmony patch 是即時攔截，
        /// 這裡處理漏網之魚（例如其他模組直接改 FactionRelation 欄位）。
        /// </summary>
        public static void EnforceFleetRelation()
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            Faction fleet = Fleet;
            if (comp == null || fleet == null || !fleet.HasGoodwill)
            {
                return;
            }

            // 審判期間的休戰是任務流程的一部分，不校正。
            if (comp.PermanentHostile && !CourtMartialOngoingUncached)
            {
                if (fleet.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                {
                    SetFleetHostile(fleet);
                }
                return;
            }

            if (IsAllyLocked && fleet.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
            {
                SetFleetRelation(AllyGoodwillCeiling, FactionRelationKind.Neutral);
                Messages.Message("DMS_Occultech_AllyBlockedMessage".Translate(fleet.Name),
                    MessageTypeDefOf.NegativeEvent, historical: false);
            }
        }

        /// <summary>解除永久敵對並回到中立（軍事法庭服刑期滿）。</summary>
        public static void EndPermanentHostility(bool sendLetter = true)
        {
            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            Faction fleet = Fleet;
            if (comp == null || !comp.PermanentHostile)
            {
                return;
            }
            comp.EndPermanentHostility();
            SetFleetRelation(0, FactionRelationKind.Neutral);

            if (sendLetter && fleet != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "DMS_Occultech_PardonLabel".Translate(),
                    "DMS_Occultech_PardonText".Translate(fleet.Name),
                    LetterDefOf.PositiveEvent, null, fleet);
            }
        }

        // ─────────────────────────────── 放棄技術 ───────────────────────────────

        /// <summary>
        /// 申報放棄所有已研究的封存級技術：進度歸零、解鎖物重新上鎖、制裁紀錄清除。
        /// 回傳實際被放棄的專案；沒有可放棄的目標時回傳空清單。
        ///
        /// 刻意不取消「已探明」狀態：玩家已經知道這項技術存在，抹除認知反而更奇怪；
        /// 重新研究時會再次觸發一次完整的封存級制裁。
        /// </summary>
        public static List<ResearchProjectDef> RenounceAll()
        {
            List<ResearchProjectDef> renounced = HeldRenounceableProjects().ToList();
            if (renounced.Count == 0)
            {
                return renounced;
            }

            GameComponent_OccultechSanction comp = GameComponent_OccultechSanction.CompSafe;
            foreach (ResearchProjectDef proj in renounced)
            {
                ClearProjectProgress(proj);
                comp?.ClearSanctioned(proj);
            }

            // 解鎖物（建築、配方、服裝）的可用性是即時查詢 researchPrerequisites 的，
            // 進度歸零後自然重新上鎖，不需要額外處理。
            //
            // 限制：原生的 researchMods 只有 Apply 沒有 Unapply，被放棄的專案若掛了
            // researchMods，其效果要到重新載入存檔才會消失。目前四個封存級專案都沒有用到
            // researchMods，之後若新增請避開，或自行提供還原邏輯。
            Find.ResearchManager.ReapplyAllMods();
            InvalidateAllyLockCache();

            Faction fleet = Fleet;
            if (fleet != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "DMS_Occultech_RenouncedLabel".Translate(),
                    "DMS_Occultech_RenouncedText".Translate(
                        fleet.Name, renounced.Select(p => p.LabelCap.ToString()).ToLineList("- ")),
                    LetterDefOf.NeutralEvent, null, fleet);
            }
            return renounced;
        }

        /// <summary>
        /// 把單一專案的進度歸零。知識型專案的進度依 Anomaly 是否啟用分別存在
        /// ResearchManager.anomalyKnowledge 或 FFF 的 GameComponent_KnowledgeStore，兩邊都要清。
        /// </summary>
        private static void ClearProjectProgress(ResearchProjectDef proj)
        {
            ResearchManager manager = Find.ResearchManager;
            if (manager == null || proj == null)
            {
                return;
            }

            if (manager.IsCurrentProject(proj))
            {
                manager.StopProject(proj);
            }

            // 一般研究（baseCost）與知識型研究都可能寫進 progress 字典，一併清掉。
            manager.progress?.Remove(proj);
            manager.anomalyKnowledge?.Remove(proj);

            Fortified.GameComponent_KnowledgeStore store = Fortified.GameComponent_KnowledgeStore.CompSafe;
            store?.SetStored(proj, 0f);
        }

        // ─────────────────────────────── 襲擊 ───────────────────────────────

        /// <summary>
        /// 對玩家的主要據點降下一波艦隊襲擊。沒有可用地圖時靜默失敗（回傳 false）。
        /// </summary>
        public static bool TryFireFleetRaid(float pointsFactor, float minPoints)
        {
            Faction fleet = Fleet;
            if (fleet == null)
            {
                return false;
            }
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return false;
            }
            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                parms.faction = fleet;
                parms.forced = true;
                parms.points = Mathf.Max(minPoints, parms.points * Mathf.Max(0.01f, pointsFactor));
                return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            catch (Exception ex)
            {
                Log.Error($"[DMS] Occultech fleet raid failed: {ex}");
                return false;
            }
        }

        // ─────────────────────────────── 軍事法庭 ───────────────────────────────

        /// <summary>
        /// 永久敵對期間主動提供軍事法庭任務。
        ///
        /// 軍事法庭是隱匿級永久敵對唯一的出路，不能只靠說書人隨機抽中，
        /// 玩家可能要等上好幾個季度才等到那張門票。因此追殺排程每觸發一次，就順手確認
        /// 玩家手上有一份可接的傳票；已經有進行中或待接受的任務時不重複發。
        ///
        /// 產生失敗（沒有持有艦隊官階的殖民者、沒有可用地圖）時靜默略過，下次再試。
        /// </summary>
        public static void EnsureCourtMartialOffered()
        {
            if (Current.Game == null || CourtMartialOngoing)
            {
                return;
            }
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests != null)
            {
                for (int i = 0; i < quests.Count; i++)
                {
                    // 已經躺在任務清單裡等待接受的傳票也算數。
                    if (quests[i].root == DMS_DefOf.DMS_CourtMartial
                        && (quests[i].State == QuestState.NotYetAccepted || quests[i].State == QuestState.Ongoing))
                    {
                        return;
                    }
                }
            }

            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return;
            }
            try
            {
                QuestUtility.GenerateQuestAndMakeAvailable(
                    DMS_DefOf.DMS_CourtMartial, StorytellerUtility.DefaultThreatPointsNow(map));
            }
            catch (Exception ex)
            {
                Log.Warning($"[DMS] Could not offer a court-martial for the occultech kill order: {ex}");
            }
        }

        // ─────────────────────────────── 工具 ───────────────────────────────

        /// <summary>在抑制關係守衛的情況下執行一段關係變更。</summary>
        public static void WithGuardSuppressed(Action action)
        {
            bool prev = GuardSuppressed;
            GuardSuppressed = true;
            try
            {
                action();
            }
            finally
            {
                GuardSuppressed = prev;
            }
        }

    }
}
