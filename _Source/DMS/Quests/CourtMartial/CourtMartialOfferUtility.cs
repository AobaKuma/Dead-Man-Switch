using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>軍事法庭的審判種類（生成時決定，存在 QuestPart_CourtTruce 上）。The kind of trial, fixed at generation.</summary>
    public enum CourtTrialKind
    {
        Ordinary,
        /// <summary>隱匿級永久敵對：官階最高者服刑一年，結案解除追殺令。Occulted kill order: the senior officer serves a year.</summary>
        Occult,
        /// <summary>受庇護的脫序者：只審脫序者，結案結束脫序者追殺。The sheltered deviant: only they stand trial.</summary>
        Deviant,
    }

    /// <summary>
    /// 決定此刻應有哪一種審判，並整理待接受的傳票：撤銷種類不符者，必要時補發。
    /// 脫序者受庇護時一律先審脫序者；結案後才輪到隱匿級審判（D6-B，分開審判）。
    ///
    /// Decides which trial is due right now and tidies pending summonses: withdraws those of the wrong kind and offers a
    /// replacement when needed. While the deviant is sheltered they are always tried first; the occult trial comes after
    /// that case closes (D6-B, separate trials).
    /// </summary>
    public static class CourtMartialOfferUtility
    {
        public static CourtTrialKind ExpectedKind
        {
            get
            {
                if (DeviantHuntUtility.HarboredDeviant != null) return CourtTrialKind.Deviant;
                return GameComponent_OccultechSanction.CompSafe?.PermanentHostile == true ? CourtTrialKind.Occult : CourtTrialKind.Ordinary;
            }
        }

        public static CourtTrialKind KindOf(QuestPart_CourtTruce truce)
        {
            if (truce.deviantTrial) return CourtTrialKind.Deviant;
            return truce.occultechTrial ? CourtTrialKind.Occult : CourtTrialKind.Ordinary;
        }

        /// <summary>
        /// 登記一次整理，由 GameComponent_DeviantLegacy 在下一個 250 tick 檢查時執行。不能當場做：從軍事法庭的訊號處理中
        /// 呼叫時，那場審判仍是 Ongoing（CourtMartialOngoing 還有 60 tick 快取），EnsureCourtMartialOffered 會直接返回。
        /// Queues a refresh for GameComponent_DeviantLegacy's next 250-tick check. Not done on the spot: called from a
        /// court-martial's signal handling, that trial is still Ongoing (and CourtMartialOngoing is cached for 60 ticks),
        /// so EnsureCourtMartialOffered would just return.
        /// </summary>
        public static void RequestRefresh(bool ensureOffer)
        {
            GameComponent_DeviantLegacy legacy = GameComponent_DeviantLegacy.CompSafe;
            if (legacy != null)
            {
                legacy.RequestCourtRefresh(ensureOffer);
            }
            else
            {
                Refresh(ensureOffer);
            }
        }

        /// <summary>
        /// 撤銷種類不符的待接受傳票；有撤銷、或 ensureOffer 時，若此刻應有的是隱匿級或脫序者審判就補發一份。
        /// 一般審判不主動補發，照舊由說書人的任務池決定。
        /// Withdraws pending summonses of the wrong kind; after a withdrawal, or with ensureOffer, offers one when an occult
        /// or deviant trial is due. Ordinary trials are not offered here and stay up to the storyteller's quest pool.
        /// </summary>
        public static void Refresh(bool ensureOffer)
        {
            if (Current.Game == null || Find.QuestManager == null) return;
            OccultechSanctionUtility.InvalidateCourtCache();
            CourtTrialKind expected = ExpectedKind;
            bool withdrawn = false;
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            // End 會把任務移入歷史；先複製再逐一結束。End moves quests to history; copy first.
            foreach (Quest quest in quests.ToList())
            {
                if (quest.root != DMS_DefOf.DMS_CourtMartial || quest.State != QuestState.NotYetAccepted) continue;
                QuestPart_CourtTruce truce = quest.PartsListForReading.OfType<QuestPart_CourtTruce>().FirstOrDefault();
                if (truce == null || KindOf(truce) == expected) continue;
                quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false);
                withdrawn = true;
            }
            if (expected != CourtTrialKind.Ordinary && (withdrawn || ensureOffer))
            {
                OccultechSanctionUtility.EnsureCourtMartialOffered();
            }
        }
    }
}
