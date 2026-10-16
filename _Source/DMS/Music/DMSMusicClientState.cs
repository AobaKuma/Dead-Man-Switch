namespace DMS
{
    /// <summary>
    /// 一張地圖的客戶端音樂紀錄：聽感緩衝與餘波。時間是客戶端未暫停的實際秒數，不存檔，也從不寫回遊戲端。
    /// One map's client-side music record: the listening holds and the aftermath. Times are the client's unpaused real
    /// seconds; nothing here is saved or ever written back to the game side.
    ///
    /// 只有正在觀看的地圖會呼叫 <see cref="Observe"/>，所以「觀察過的危機」就是玩家看著它發生的危機；
    /// 讀檔後紀錄從零開始，因此讀檔前的危機不會補播餘波。
    /// Only the viewed map is observed, so an "observed crisis" is one the player watched; the record starts empty after a
    /// load, so crises from before the load never get an aftermath.
    /// </summary>
    public class DMSMusicClientMapState
    {
        public float combatHoldUntil = float.NegativeInfinity;
        public float alertHoldUntil = float.NegativeInfinity;
        public int lastActiveBosses;
        public int lastObservedCrisisId;
        public int lastPlayedAftermathId;

        /// <summary>
        /// 以最新事實更新緩衝與觀察紀錄。同一份事實、同一時間點重複呼叫不會改變結果。
        /// Updates the holds and the observation record from the latest facts. Calling it again with the same facts and
        /// time changes nothing.
        /// </summary>
        public void Observe(in DMSMusicFacts facts, float now)
        {
            if (facts.HasCombatReason)
            {
                combatHoldUntil = now + DMSMusicTuning.CeasefireSeconds;
            }
            else if (lastActiveBosses > 0 && facts.activeBosses == 0)
            {
                // Boss 遭遇結束、也沒有其他交火：停戰緩衝跟著結束，直接交給餘波（見 DMSMusicGameState）。
                // The boss encounter ended with no other fire: the ceasefire hold ends with it and the aftermath takes over.
                combatHoldUntil = float.NegativeInfinity;
            }
            if (facts.HasAlertReason)
            {
                alertHoldUntil = now + DMSMusicTuning.AlertHoldSeconds;
            }
            lastActiveBosses = facts.activeBosses;
            if (facts.crisisActive && facts.crisisId > lastObservedCrisisId)
            {
                lastObservedCrisisId = facts.crisisId;
            }
        }

        public bool CombatHeld(in DMSMusicFacts facts, float now)
        {
            return facts.HasCombatReason || now < combatHoldUntil;
        }

        public bool AlertHeld(in DMSMusicFacts facts, float now)
        {
            return facts.HasAlertReason || now < alertHoldUntil;
        }

        /// <summary>剛結束的危機是玩家看過的，而且還沒播過餘波。The crisis that just ended was watched and has had no aftermath yet.</summary>
        public bool AftermathPending(in DMSMusicFacts facts)
        {
            int id = facts.aftermathCrisisId;
            return id > 0 && id == lastObservedCrisisId && id > lastPlayedAftermathId;
        }

        /// <summary>開發者工具：結束停戰與警戒緩衝。Developer tools: end the ceasefire and alert holds.</summary>
        public void ClearHolds()
        {
            combatHoldUntil = float.NegativeInfinity;
            alertHoldUntil = float.NegativeInfinity;
        }

        public void MarkAftermathPlayed(int crisisId)
        {
            if (crisisId > lastPlayedAftermathId)
            {
                lastPlayedAftermathId = crisisId;
            }
        }
    }
}
