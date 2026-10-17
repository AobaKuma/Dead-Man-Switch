using System;
using System.Collections.Generic;

namespace DMS.MusicTests
{
    /// <summary>
    /// 情境音樂狀態規則的合成測試（見 Plans/DMS_Music_Transitions_實作計畫.md 10.1）。
    /// Synthetic tests for the situational-music state rules.
    /// </summary>
    internal static class Program
    {
        private const int Never = DMSMusicTuning.Never;

        private static int checks;
        private static int failures;
        private static string currentTest;

        private static int Main()
        {
            Run(nameof(PriorityOrder), PriorityOrder);
            Run(nameof(YieldAndRelevance), YieldAndRelevance);
            Run(nameof(AlertRiseWindow), AlertRiseWindow);
            Run(nameof(FirstEvaluationOnlySetsBaselines), FirstEvaluationOnlySetsBaselines);
            Run(nameof(LockdownPhases), LockdownPhases);
            Run(nameof(GravekeeperWindows), GravekeeperWindows);
            Run(nameof(ApproachWarnings), ApproachWarnings);
            Run(nameof(CeasefireHold), CeasefireHold);
            Run(nameof(AlertHoldNeedsAllReasonsGone), AlertHoldNeedsAllReasonsGone);
            Run(nameof(BossDeathGoesStraightToAftermath), BossDeathGoesStraightToAftermath);
            Run(nameof(EscortsStillFightingAfterBossDeath), EscortsStillFightingAfterBossDeath);
            Run(nameof(DisarmedDuringCountdownWhileFighting), DisarmedDuringCountdownWhileFighting);
            Run(nameof(AftermathNeedsAWatchedCrisis), AftermathNeedsAWatchedCrisis);
            Run(nameof(AftermathWindowAndSupersede), AftermathWindowAndSupersede);
            Run(nameof(SameInputSameResult), SameInputSameResult);
            Run(nameof(ArrivalEstimate), ArrivalEstimate);
            Run(nameof(ColonyEncounterOwnsItsCrisis), ColonyEncounterOwnsItsCrisis);
            Run(nameof(SequenceEndsEarly), SequenceEndsEarly);
            Run(nameof(SongChoiceOrder), SongChoiceOrder);
            Run(nameof(DebugInjectionAndClear), DebugInjectionAndClear);

            Console.WriteLine($"{checks - failures}/{checks} checks passed.");
            return failures == 0 ? 0 : 1;
        }

        // ── 選擇器 / Selector ─────────────────────────────────────────────────

        private static void PriorityOrder()
        {
            DMSMusicFacts facts = new DMSMusicFacts
            {
                context = DMSMusicContext.Facility,
                playerPresent = true,
                lockdown = DMSLockdownPhase.Countdown,
                activeBosses = 2,
                reasons = DMSMusicReason.CombatShots | DMSMusicReason.AlertRise,
                aftermathCrisisId = 3,
            };
            DMSMusicClientMapState client = new DMSMusicClientMapState { lastObservedCrisisId = 3 };

            Equal(DMSMusicState.LockdownCountdown, Select(facts, client, 0f), "countdown beats everything");
            facts.lockdown = DMSLockdownPhase.Locked;
            Equal(DMSMusicState.BossCombat, Select(facts, client, 0f), "boss beats combat and trap");
            facts.activeBosses = 0;
            Equal(DMSMusicState.InstallationCombat, Select(facts, client, 0f), "combat beats trap");
            facts.reasons = DMSMusicReason.AlertRise;
            Equal(DMSMusicState.FacilityTrapped, Select(facts, client, 0f), "trap beats alert");
            facts.lockdown = DMSLockdownPhase.Disarmed;
            Equal(DMSMusicState.SecurityAlert, Select(facts, client, 0f), "alert beats aftermath");
            facts.reasons = DMSMusicReason.None;
            Equal(DMSMusicState.Aftermath, Select(facts, client, 0f), "aftermath beats exploring");
            client.MarkAftermathPlayed(3);
            Equal(DMSMusicState.FacilityExplore, Select(facts, client, 0f), "facility explore");
            facts.context = DMSMusicContext.Installation;
            Equal(DMSMusicState.InstallationExplore, Select(facts, client, 0f), "installation explore");
        }

        private static void YieldAndRelevance()
        {
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicFacts facts = new DMSMusicFacts
            {
                context = DMSMusicContext.Installation,
                playerPresent = true,
                reasons = DMSMusicReason.CombatShots,
            };
            Equal(DMSMusicState.None, DMSMusicSelector.Select(facts, client, 0f, DMSMusicYield.WorldView), "world view yields");
            Equal(DMSMusicState.None, DMSMusicSelector.Select(facts, client, 0f, DMSMusicYield.ForcedSong), "forced song yields");
            Equal(DMSMusicState.None, DMSMusicSelector.Select(facts, client, 0f, DMSMusicYield.Disabled), "disabled yields");

            facts.playerPresent = false;
            Equal(DMSMusicState.None, Select(facts, client, 0f), "no player units: vanilla");

            facts.playerPresent = true;
            facts.context = DMSMusicContext.None;
            Equal(DMSMusicState.None, Select(facts, client, 0f), "a colony without a DMS encounter stays vanilla even in combat");

            facts.hasEncounter = true;
            Equal(DMSMusicState.InstallationCombat, Select(facts, client, 0f), "a colony with a DMS encounter gets combat");
            facts.reasons = DMSMusicReason.None;
            Equal(DMSMusicState.None, Select(facts, new DMSMusicClientMapState(), 0f), "a calm colony never explores");
        }

        // ── 遊戲端窗口 / Game-side windows ─────────────────────────────────────

        private static void AlertRiseWindow()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            game.Evaluate(inputs, 0);

            inputs.alertLevel = 20f;
            Check(game.Evaluate(inputs, 60).reasons.HasFlag(DMSMusicReason.AlertRise), "a rise is an alert reason");
            Check(game.Evaluate(inputs, 60 + 2500).reasons.HasFlag(DMSMusicReason.AlertRise), "still inside the window");
            Check(!game.Evaluate(inputs, 60 + 2501).reasons.HasFlag(DMSMusicReason.AlertRise), "the window closes even though the level stays");

            inputs.alertLevel = 19.5f;
            Check(!game.Evaluate(inputs, 30000).reasons.HasFlag(DMSMusicReason.AlertRise), "decay is not a rise");
            inputs.alertLevel = 39.5f;
            Check(game.Evaluate(inputs, 30060).reasons.HasFlag(DMSMusicReason.AlertRise), "a new rise after decay counts");
        }

        private static void FirstEvaluationOnlySetsBaselines()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            inputs.alertLevel = 80f;
            inputs.gravekeeperSearch = true;
            inputs.raidsSentCount = 3;

            DMSMusicFacts facts = game.Evaluate(inputs, 100000);
            Equal(DMSMusicReason.None, facts.reasons, "an old save's alert level, sweep and raid count are not new");
            Check(!facts.crisisActive && facts.aftermathCrisisId == 0, "no crisis and no aftermath from defaults");

            facts = game.Evaluate(inputs, 100060);
            Equal(DMSMusicReason.None, facts.reasons, "still nothing on the next refresh");
        }

        private static void LockdownPhases()
        {
            foreach (DMSLockdownPhase phase in Enum.GetValues(typeof(DMSLockdownPhase)))
            {
                DMSMusicFacts facts = new DMSMusicFacts { context = DMSMusicContext.Facility, playerPresent = true, lockdown = phase };
                bool countdown = phase == DMSLockdownPhase.Countdown;
                bool trapped = phase == DMSLockdownPhase.Locked || phase == DMSLockdownPhase.Sealed;
                Equal(countdown, facts.LockdownCountingDown, $"{phase}: countdown");
                Equal(trapped, facts.Trapped, $"{phase}: trapped");

                DMSMusicState expected = countdown ? DMSMusicState.LockdownCountdown
                    : trapped ? DMSMusicState.FacilityTrapped
                    : DMSMusicState.FacilityExplore;
                Equal(expected, Select(facts, new DMSMusicClientMapState(), 0f), $"{phase}: state");

                facts.playerPresent = false;
                Check(!facts.LockdownCountingDown && !facts.Trapped, $"{phase}: nothing once the player is out");
            }

            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            game.Evaluate(inputs, 0);
            inputs.lockdown = DMSLockdownPhase.Sealed;
            Check(game.Evaluate(inputs, 60).crisisActive, "sealed is still a crisis");
            inputs.lockdown = DMSLockdownPhase.Lost;
            DMSMusicFacts lost = game.Evaluate(inputs, 120);
            Check(!lost.crisisActive && lost.aftermathCrisisId == 1, "lost ends the crisis");
        }

        private static void GravekeeperWindows()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            game.Evaluate(inputs, 0);

            inputs.gravekeeperSightTick = 1000;
            Check(game.Evaluate(inputs, 1600).reasons.HasFlag(DMSMusicReason.GravekeeperContact), "contact 600 ticks ago");
            Check(!game.Evaluate(inputs, 1601).reasons.HasFlag(DMSMusicReason.GravekeeperContact), "contact 601 ticks ago is over");

            inputs.gravekeeperSightTick = Never;
            inputs.gravekeeperChallenge = true;
            DMSMusicFacts challenge = game.Evaluate(inputs, 2000);
            Check(challenge.reasons.HasFlag(DMSMusicReason.GravekeeperChallenge) && !challenge.HasCombatReason, "a challenge is an alert, not combat");

            inputs.gravekeeperChallenge = false;
            inputs.gravekeeperSearch = true;
            Check(game.Evaluate(inputs, 3000).reasons.HasFlag(DMSMusicReason.GravekeeperSearch), "a fresh sweep is an alert");
            Check(game.Evaluate(inputs, 5500).reasons.HasFlag(DMSMusicReason.GravekeeperSearch), "still inside the sweep window");
            Check(!game.Evaluate(inputs, 5501).reasons.HasFlag(DMSMusicReason.GravekeeperSearch), "a permanent sweep stops holding the alert");
        }

        private static void ApproachWarnings()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Installation);
            game.Evaluate(inputs, 0);

            inputs.reinforcementTicksLeft = 1801;
            Check(!game.Evaluate(inputs, 60).reasons.HasFlag(DMSMusicReason.ReinforcementsApproaching), "1801 ticks out is too early");
            inputs.reinforcementTicksLeft = 1800;
            Check(game.Evaluate(inputs, 120).reasons.HasFlag(DMSMusicReason.ReinforcementsApproaching), "1800 ticks out warns");
            inputs.reinforcementTicksLeft = 0;
            Check(game.Evaluate(inputs, 180).reasons.HasFlag(DMSMusicReason.ReinforcementsApproaching), "arriving now still warns");

            inputs.reinforcementTicksLeft = -1;
            inputs.raidsSentCount = 1;
            Check(game.Evaluate(inputs, 240).reasons.HasFlag(DMSMusicReason.ReinforcementsArrived), "a sent raid is an arrival");
            Check(!game.Evaluate(inputs, 240 + 2501).reasons.HasFlag(DMSMusicReason.ReinforcementsArrived), "the arrival window closes");

            inputs.bossTicksUntilArrival = 1800;
            DMSMusicFacts boss = game.Evaluate(inputs, 5000);
            Check(boss.reasons.HasFlag(DMSMusicReason.BossApproaching), "an approaching boss warns");
            Equal(DMSMusicState.SecurityAlert, Select(boss, new DMSMusicClientMapState(), 0f), "an approaching boss is an alert, not boss combat");
        }

        // ── 客戶端緩衝 / Client holds ──────────────────────────────────────────

        private static void CeasefireHold()
        {
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicFacts fighting = new DMSMusicFacts { context = DMSMusicContext.Installation, playerPresent = true, reasons = DMSMusicReason.CombatShots };
            DMSMusicFacts calm = new DMSMusicFacts { context = DMSMusicContext.Installation, playerPresent = true };

            Equal(DMSMusicState.InstallationCombat, Observe(fighting, client, 0f), "fighting");
            Equal(DMSMusicState.InstallationCombat, Observe(calm, client, 5f), "held after the last fire");
            Equal(DMSMusicState.InstallationCombat, Observe(calm, client, 11.99f), "held just under 12 s");
            Equal(DMSMusicState.InstallationExplore, Observe(calm, client, 12f), "released at 12 s");

            Equal(DMSMusicState.InstallationCombat, Observe(fighting, client, 20f), "fighting again");
            Equal(DMSMusicState.InstallationCombat, Observe(fighting, client, 30f), "fire keeps extending the hold");
            Equal(DMSMusicState.InstallationCombat, Observe(calm, client, 41f), "measured from the last fire");
            Equal(DMSMusicState.InstallationExplore, Observe(calm, client, 42f), "released 12 s after the last fire");
        }

        private static void AlertHoldNeedsAllReasonsGone()
        {
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicFacts facts = new DMSMusicFacts
            {
                context = DMSMusicContext.Facility,
                playerPresent = true,
                reasons = DMSMusicReason.AlertRise | DMSMusicReason.ReinforcementsApproaching,
            };
            Equal(DMSMusicState.SecurityAlert, Observe(facts, client, 0f), "two alert reasons");
            facts.reasons = DMSMusicReason.ReinforcementsApproaching;
            Equal(DMSMusicState.SecurityAlert, Observe(facts, client, 100f), "one reason left keeps the alert");
            facts.reasons = DMSMusicReason.None;
            Equal(DMSMusicState.SecurityAlert, Observe(facts, client, 114.9f), "the hold starts when the last reason goes");
            Equal(DMSMusicState.FacilityExplore, Observe(facts, client, 115f), "released 15 s later");
        }

        // ── 危機與餘波 / Crises and aftermath ──────────────────────────────────

        private static void BossDeathGoesStraightToAftermath()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Installation);
            game.Evaluate(inputs, 0);

            inputs.activeBosses = 1;
            game.NoteCombat(50);
            DMSMusicFacts facts = game.Evaluate(inputs, 60);
            Check(facts.crisisActive && facts.crisisId == 1, "the boss fight is crisis 1");
            Equal(DMSMusicState.BossCombat, Observe(facts, client, 1f), "boss combat");

            game.NoteCombat(110);
            inputs.activeBosses = 0;
            facts = game.Evaluate(inputs, 120);
            Check(!facts.HasCombatReason, "the killing shots leave the combat window");
            Check(!facts.crisisActive && facts.aftermathCrisisId == 1, "crisis 1 ended");
            Equal(DMSMusicState.Aftermath, Observe(facts, client, 2f), "boss death goes straight to the aftermath");

            client.MarkAftermathPlayed(1);
            Equal(DMSMusicState.InstallationExplore, Observe(game.Evaluate(inputs, 180), client, 3f), "the aftermath plays once");
        }

        private static void EscortsStillFightingAfterBossDeath()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Installation);
            game.Evaluate(inputs, 0);

            inputs.activeBosses = 2;
            game.NoteCombat(50);
            Observe(game.Evaluate(inputs, 60), client, 1f);

            inputs.activeBosses = 1;
            Equal(DMSMusicState.BossCombat, Observe(game.Evaluate(inputs, 120), client, 2f), "one boss down, the other still counts");

            inputs.activeBosses = 0;
            Observe(game.Evaluate(inputs, 180), client, 3f);
            game.NoteCombat(200);
            DMSMusicFacts facts = game.Evaluate(inputs, 240);
            Check(facts.crisisActive && facts.crisisId == 2, "escorts firing open crisis 2");
            Equal(DMSMusicState.InstallationCombat, Observe(facts, client, 4f), "escorts still fighting: regular combat");

            facts = game.Evaluate(inputs, 900);
            Check(!facts.crisisActive && facts.aftermathCrisisId == 2, "crisis 2 ends after the combat window");
            Equal(DMSMusicState.InstallationCombat, Observe(facts, client, 10f), "the ceasefire hold still runs");
            Equal(DMSMusicState.Aftermath, Observe(facts, client, 16f), "aftermath for the watched crisis once the hold ends");
        }

        private static void DisarmedDuringCountdownWhileFighting()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            game.Evaluate(inputs, 0);

            inputs.lockdown = DMSLockdownPhase.Countdown;
            game.NoteCombat(100);
            Equal(DMSMusicState.LockdownCountdown, Observe(game.Evaluate(inputs, 120), client, 1f), "countdown");

            inputs.lockdown = DMSLockdownPhase.Disarmed;
            game.NoteCombat(170);
            DMSMusicFacts facts = game.Evaluate(inputs, 180);
            Check(facts.crisisActive && facts.aftermathCrisisId == 0, "disarming mid-fight does not end the crisis");
            Equal(DMSMusicState.InstallationCombat, Observe(facts, client, 2f), "back to combat, no aftermath");
        }

        private static void AftermathNeedsAWatchedCrisis()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Installation);
            Observe(game.Evaluate(inputs, 0), client, 0f);

            // 玩家在別張地圖時發生並結束的危機。A crisis that happens and ends while the player looks elsewhere.
            game.NoteCombat(100);
            game.Evaluate(inputs, 120);
            DMSMusicFacts facts = game.Evaluate(inputs, 900);
            Check(facts.aftermathCrisisId == 1, "the game side reports the ended crisis");
            Equal(DMSMusicState.InstallationExplore, Observe(facts, client, 30f), "no aftermath for a crisis nobody watched");

            // 讀檔後的客戶端紀錄是空的。After a load the client record is empty.
            game.NoteCombat(1000);
            Observe(game.Evaluate(inputs, 1020), client, 40f);
            facts = game.Evaluate(inputs, 1800);
            Equal(DMSMusicState.InstallationExplore, Observe(facts, new DMSMusicClientMapState(), 100f), "no aftermath replay after a load");
        }

        private static void AftermathWindowAndSupersede()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Installation);
            game.Evaluate(inputs, 0);

            game.NoteCombat(100);
            Observe(game.Evaluate(inputs, 120), client, 1f);
            DMSMusicFacts facts = game.Evaluate(inputs, 720);
            Check(facts.aftermathCrisisId == 1, "crisis 1 ended at 720");
            Check(game.Evaluate(inputs, 720 + 5000).aftermathCrisisId == 1, "aftermath window still open at 5000");
            Check(game.Evaluate(inputs, 720 + 5001).aftermathCrisisId == 0, "aftermath window closed at 5001");

            game.NoteCombat(7000);
            Observe(game.Evaluate(inputs, 7020), client, 100f);
            facts = game.Evaluate(inputs, 7620);
            Check(facts.aftermathCrisisId == 2, "the newer crisis supersedes");
            Equal(DMSMusicState.Aftermath, Observe(facts, client, 120f), "aftermath for crisis 2");
        }

        private static void SameInputSameResult()
        {
            DMSMusicGameState a = new DMSMusicGameState();
            DMSMusicGameState b = new DMSMusicGameState();
            DMSMusicInputs inputs = Site(DMSMusicContext.Facility);
            inputs.alertLevel = 40f;
            inputs.gravekeeperSightTick = 300;
            a.Evaluate(inputs, 0);
            b.Evaluate(inputs, 0);
            inputs.alertLevel = 60f;
            DMSMusicFacts first = a.Evaluate(inputs, 360);
            DMSMusicFacts again = a.Evaluate(inputs, 360);
            DMSMusicFacts other = b.Evaluate(inputs, 360);
            Equal(Describe(first), Describe(again), "re-evaluating the same tick changes nothing");
            Equal(Describe(first), Describe(other), "two maps with the same history agree");

            DMSMusicClientMapState client = new DMSMusicClientMapState();
            client.Observe(first, 10f);
            client.Observe(first, 10f);
            DMSMusicState state = DMSMusicSelector.Select(first, client, 10f, DMSMusicYield.None);
            for (int i = 0; i < 100; i++)
            {
                if (DMSMusicSelector.Select(first, client, 10f, DMSMusicYield.None) != state)
                {
                    Check(false, "asking repeatedly changed the state");
                    return;
                }
            }
            Check(true, "asking repeatedly never changes the state");
            Equal(DMSMusicState.InstallationCombat, state, "contact 60 ticks ago is combat");
        }

        // ── 殖民地上的 Boss 遭遇 / A boss encounter at a colony ───────────────

        private static void ColonyEncounterOwnsItsCrisis()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicInputs inputs = DMSMusicInputs.Empty;
            inputs.playerPresent = true;
            Equal(DMSMusicState.None, Observe(game.Evaluate(inputs, 0), client, 0f), "a plain colony is vanilla");
            Check(!game.Tracking(0), "nothing to track yet");

            inputs.hasEncounter = true;
            inputs.bossTicksUntilArrival = 0;
            Equal(DMSMusicState.SecurityAlert, Observe(game.Evaluate(inputs, 60), client, 1f), "boss still in its drop pod: alert");

            inputs.bossTicksUntilArrival = -1;
            inputs.activeBosses = 1;
            game.NoteCombat(100);
            Equal(DMSMusicState.BossCombat, Observe(game.Evaluate(inputs, 120), client, 2f), "boss landed and fighting");

            // Boss 死亡，任務結束（遭遇消失），護衛還在開火。The boss dies, its quest ends (no encounter), escorts keep firing.
            inputs.activeBosses = 0;
            inputs.hasEncounter = false;
            Observe(game.Evaluate(inputs, 180), client, 3f);
            Check(game.Tracking(180), "the ended crisis keeps the map tracked through its aftermath window");
            game.NoteCombat(200);
            DMSMusicFacts facts = game.Evaluate(inputs, 240);
            Check(facts.Relevant, "the escorts' fight stays with DMS after the quest ends");
            Equal(DMSMusicState.InstallationCombat, Observe(facts, client, 4f), "escorts still fighting: regular combat");

            facts = game.Evaluate(inputs, 900);
            Equal(DMSMusicState.InstallationCombat, Observe(facts, client, 10f), "ceasefire hold");
            Equal(DMSMusicState.Aftermath, Observe(facts, client, 16f), "then the aftermath");
            client.MarkAftermathPlayed(facts.aftermathCrisisId);
            Equal(DMSMusicState.None, Observe(game.Evaluate(inputs, 960), client, 40f), "a colony never explores afterwards");

            facts = game.Evaluate(inputs, 900 + DMSMusicTuning.AftermathWindowTicks + 1);
            Check(!facts.Relevant && !game.Tracking(900 + DMSMusicTuning.AftermathWindowTicks + 1), "the colony goes back to vanilla once the window closes");
        }

        // ── 序列與選曲 / Sequences and song choice ───────────────────────────

        private static void SequenceEndsEarly()
        {
            Check(!DMSMusicSequenceRules.EndsEarly(DMSMusicState.BossCombat, DMSMusicState.BossCombat, false), "same state on another map keeps playing");
            Check(!DMSMusicSequenceRules.EndsEarly(DMSMusicState.FacilityExplore, DMSMusicState.InstallationExplore, true), "a downgrade on the same map follows vanilla's fade");
            Check(DMSMusicSequenceRules.EndsEarly(DMSMusicState.FacilityExplore, DMSMusicState.InstallationExplore, false), "underground to surface ends at once");
            Check(DMSMusicSequenceRules.EndsEarly(DMSMusicState.InstallationCombat, DMSMusicState.None, true), "yielding (world view, forced song) ends at once");
            Check(DMSMusicSequenceRules.EndsEarly(DMSMusicState.InstallationCombat, DMSMusicState.None, false), "leaving for a vanilla map ends at once");
        }

        private static void SongChoiceOrder()
        {
            var variants = new List<KeyValuePair<DMSMusicSongTag, string>>
            {
                new KeyValuePair<DMSMusicSongTag, string>(DMSMusicSongTag.Huntdown, "huntdown"),
                new KeyValuePair<DMSMusicSongTag, string>(DMSMusicSongTag.MainBattle, "mainBattle"),
                new KeyValuePair<DMSMusicSongTag, string>(DMSMusicSongTag.AlliedEngaged, "allied"),
            };
            string[] facility = { "gunsUp" };
            string[] surface = { "garrisons", "second" };
            Predicate<string> all = s => true;

            Equal("huntdown", Pick(DMSMusicSongTag.Huntdown | DMSMusicSongTag.AlliedEngaged, variants, null, facility, surface, false, 0, all), "huntdown beats allied");
            Equal("mainBattle", Pick(DMSMusicSongTag.MainBattle | DMSMusicSongTag.AlliedEngaged, variants, null, facility, surface, false, 0, all), "main battle beats allied");
            Equal("allied", Pick(DMSMusicSongTag.AlliedEngaged, variants, null, facility, surface, true, 0, all), "allied beats the context pool");
            Equal("bossVariant", Pick(DMSMusicSongTag.None, variants, "bossVariant", facility, surface, false, 0, all), "the encounter's own song beats the pool");
            Equal("gunsUp", Pick(DMSMusicSongTag.None, variants, null, facility, surface, true, 5, all), "underground uses the facility pool");
            Equal("garrisons", Pick(DMSMusicSongTag.None, variants, null, facility, surface, false, 0, all), "surface uses the general pool");
            Equal("second", Pick(DMSMusicSongTag.None, variants, null, facility, surface, false, 1, all), "the pool rotates");
            Equal("garrisons", Pick(DMSMusicSongTag.None, variants, null, facility, surface, false, 2, all), "the rotation wraps");

            Predicate<string> missing = s => s != "huntdown" && s != "gunsUp" && s != "garrisons";
            Equal("second", Pick(DMSMusicSongTag.Huntdown, variants, null, facility, surface, true, 0, missing), "missing files fall through to the next playable song");
            Equal(null, Pick(DMSMusicSongTag.None, variants, null, facility, surface, true, 0, s => false), "nothing playable gives null");
        }

        private static string Pick(DMSMusicSongTag tags, List<KeyValuePair<DMSMusicSongTag, string>> variants, string encounter,
            string[] facility, string[] pool, bool inFacility, int rotation, Predicate<string> playable)
        {
            return DMSMusicSongChoice.Pick(tags, variants, encounter, facility, pool, inFacility, rotation, playable);
        }

        // ── 開發者注入 / Developer injection ─────────────────────────────────

        private static void DebugInjectionAndClear()
        {
            DMSMusicGameState game = new DMSMusicGameState();
            DMSMusicClientMapState client = new DMSMusicClientMapState();
            DMSMusicDebugInjection injection = new DMSMusicDebugInjection();
            DMSMusicInputs colony = DMSMusicInputs.Empty;
            colony.playerPresent = true;

            Equal(DMSMusicState.None, Observe(Evaluate(game, colony, injection, 0), client, 0f), "an empty injection changes nothing");

            injection.context = DMSMusicContext.Facility;
            Equal(DMSMusicState.FacilityExplore, Observe(Evaluate(game, colony, injection, 60), client, 1f), "forced context");

            injection.bossArrivalTick = 60 + 1800;
            DMSMusicFacts facts = Evaluate(game, colony, injection, 120);
            Check(facts.reasons.HasFlag(DMSMusicReason.BossApproaching) && facts.hasEncounter, "an injected boss approach warns");
            Equal(DMSMusicState.SecurityAlert, Observe(facts, client, 2f), "approaching boss: alert");
            facts = Evaluate(game, colony, injection, 1860);
            Check(facts.activeBosses == 1 && injection.bossArrivalTick < 0, "reaching the arrival tick turns it into one active boss");
            Equal(DMSMusicState.BossCombat, Observe(facts, client, 30f), "injected boss fight");

            injection.lockdown = DMSLockdownPhase.Countdown;
            Equal(DMSMusicState.LockdownCountdown, Observe(Evaluate(game, colony, injection, 1920), client, 31f), "injected countdown beats the boss");

            injection.reinforcementArrivalTick = 1980;
            Evaluate(game, colony, injection, 1980);
            facts = Evaluate(game, colony, injection, 2040);
            Check(facts.reasons.HasFlag(DMSMusicReason.ReinforcementsArrived), "an injected wave arrives");

            // 清除：拿掉注入、清掉窗口與緩衝。Clear: drop the injection, the windows and the holds.
            game.NoteCombat(2040);
            game.NoteAlertRise(2040);
            game.ClearWindows();
            client.ClearHolds();
            facts = Evaluate(game, colony, null, 2100);
            Check((facts.reasons & (DMSMusicReason.CombatShots | DMSMusicReason.AlertRise | DMSMusicReason.ReinforcementsArrived)) == 0,
                "clearing ends every event window");
            Check(!facts.HasCombatReason && !client.CombatHeld(facts, 32f) && !client.AlertHeld(facts, 32f), "and every hold");
        }

        private static DMSMusicFacts Evaluate(DMSMusicGameState game, DMSMusicInputs inputs, DMSMusicDebugInjection injection, int now)
        {
            injection?.Apply(ref inputs, now);
            return game.Evaluate(inputs, now);
        }

        // ── 抵達時間 / Arrival estimate ──────────────────────────────────────

        private static void ArrivalEstimate()
        {
            Equal(4000, DMSMusicTiming.TicksUntilArrival(1000, 1000, 2500, 60000, 0, 2500, true), "first hash boundary after the minimum delay");
            Equal(1500, DMSMusicTiming.TicksUntilArrival(6000, 1000, 2500, 60000, 0, 2500, true), "past the minimum delay: the next boundary from now");
            Equal(0, DMSMusicTiming.TicksUntilArrival(7500, 1000, 2500, 60000, 0, 2500, true), "on a boundary it arrives now");
            Equal(55000, DMSMusicTiming.TicksUntilArrival(6000, 1000, 2500, 60000, 0, 2500, false), "blocked: only the forced arrival is certain");
            Equal(3950, DMSMusicTiming.TicksUntilArrival(1000, 1000, 3900, 3950, 0, 2500, true), "the forced arrival can come before the boundary");
            Equal(3300, DMSMusicTiming.TicksUntilArrival(1000, 1000, 2500, 60000, 700, 2500, true), "the world object's hash offset shifts the boundary");
            Equal(0, DMSMusicTiming.TicksUntilArrival(70000, 1000, 2500, 60000, 0, 2500, false), "never negative past the forced arrival");

            // 與原版一樣以 int 溢位計算 hash 邊界。Hash boundaries wrap like vanilla's int maths.
            int[] offsets = { int.MaxValue - 100, int.MinValue + 7, -1, 1234567, -987654 };
            foreach (int offset in offsets)
            {
                for (int from = 3500; from < 3500 + 2500 * 3; from += 377)
                {
                    int arrival = from + DMSMusicTiming.TicksUntilArrival(from, 1000, 2500, 900000, offset, 2500, true);
                    bool onBoundary = unchecked(arrival + offset) % 2500 == 0;
                    bool soonest = arrival >= from && arrival - from < 2500;
                    if (!onBoundary || !soonest)
                    {
                        Check(false, $"offset {offset}, from {from}: arrival {arrival} is not the next boundary");
                        return;
                    }
                }
            }
            Check(true, "boundaries match vanilla's wrapped modulo for extreme hash offsets");
        }

        // ── 工具 / Helpers ────────────────────────────────────────────────────

        private static DMSMusicInputs Site(DMSMusicContext context)
        {
            DMSMusicInputs inputs = DMSMusicInputs.Empty;
            inputs.context = context;
            inputs.playerPresent = true;
            return inputs;
        }

        private static DMSMusicState Select(in DMSMusicFacts facts, DMSMusicClientMapState client, float now)
        {
            return DMSMusicSelector.Select(facts, client, now, DMSMusicYield.None);
        }

        private static DMSMusicState Observe(in DMSMusicFacts facts, DMSMusicClientMapState client, float now)
        {
            client.Observe(facts, now);
            return Select(facts, client, now);
        }

        private static string Describe(in DMSMusicFacts f)
        {
            return $"{f.context}|{f.playerPresent}|{f.hasEncounter}|{f.lockdown}|{f.activeBosses}|{f.reasons}|{f.songTags}|"
                + $"{f.crisisActive}|{f.crisisId}|{f.aftermathCrisisId}|{f.alertLevel}|{f.reinforcementTicksLeft}|{f.bossTicksUntilArrival}";
        }

        private static void Run(string name, Action test)
        {
            currentTest = name;
            try
            {
                test();
            }
            catch (Exception e)
            {
                checks++;
                failures++;
                Console.WriteLine($"FAIL {name}: threw {e}");
            }
        }

        private static void Check(bool condition, string what)
        {
            checks++;
            if (!condition)
            {
                failures++;
                Console.WriteLine($"FAIL {currentTest}: {what}");
            }
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            Check(Equals(expected, actual), $"{what} (expected {expected}, got {actual})");
        }
    }
}
