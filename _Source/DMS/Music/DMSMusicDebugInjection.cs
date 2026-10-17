using System;

namespace DMS
{
    /// <summary>
    /// 開發者注入的測試事件（不存檔、不依賴 RimWorld）。只疊加在音樂的輸入上，不碰真正的警戒、封鎖、任務、Boss 或守軍，
    /// 用來不必真的召喚 Boss 或觸發封鎖就能走過驗收矩陣。一次性的事件（開火、警戒上升）直接寫進遊戲端紀錄，不在這裡。
    ///
    /// Developer-injected test events (not saved, no RimWorld dependency). They only layer onto the music inputs and
    /// never touch the real alert, lockdown, quests, bosses or garrison, so the acceptance matrix can be walked without
    /// summoning bosses or tripping lockdowns. One-off events (fire, alert rises) go straight into the game-side record,
    /// not here.
    /// </summary>
    public class DMSMusicDebugInjection
    {
        public DMSMusicContext? context;
        public DMSLockdownPhase? lockdown;
        public int bosses;
        public bool challenge;
        public bool sweep;
        public int sightTick = DMSMusicTuning.Never;
        public int raidsSent;
        /// <summary>注入的增援預計抵達 tick；到了就算抵達一波。Injected reinforcement arrival tick; reaching it counts as one wave arriving.</summary>
        public int reinforcementArrivalTick = -1;
        /// <summary>注入的 Boss 預計抵達 tick；到了就變成一架啟動中的 Boss。Injected boss arrival tick; reaching it becomes one active boss.</summary>
        public int bossArrivalTick = -1;
        public DMSMusicSongTag tags;

        public void Apply(ref DMSMusicInputs inputs, int now)
        {
            if (reinforcementArrivalTick >= 0 && now >= reinforcementArrivalTick)
            {
                reinforcementArrivalTick = -1;
                raidsSent++;
            }
            if (bossArrivalTick >= 0 && now >= bossArrivalTick)
            {
                bossArrivalTick = -1;
                bosses++;
            }

            if (context.HasValue)
            {
                inputs.context = context.Value;
            }
            if (lockdown.HasValue)
            {
                inputs.lockdown = lockdown.Value;
            }
            inputs.activeBosses += bosses;
            inputs.gravekeeperChallenge |= challenge;
            inputs.gravekeeperSearch |= sweep;
            inputs.gravekeeperSightTick = Math.Max(inputs.gravekeeperSightTick, sightTick);
            inputs.raidsSentCount += raidsSent;
            if (reinforcementArrivalTick >= 0)
            {
                inputs.reinforcementTicksLeft = Nearest(inputs.reinforcementTicksLeft, reinforcementArrivalTick - now);
            }
            if (bossArrivalTick >= 0)
            {
                inputs.bossTicksUntilArrival = Nearest(inputs.bossTicksUntilArrival, bossArrivalTick - now);
            }
            if (bosses > 0 || bossArrivalTick >= 0)
            {
                inputs.hasEncounter = true;
            }
            inputs.songTags |= tags;
        }

        private static int Nearest(int current, int candidate)
        {
            return current < 0 || candidate < current ? candidate : current;
        }
    }
}
