using System;
using System.Collections.Generic;
using Fortified;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMS
{
    /// <summary>
    /// 情境音樂的來源讀取。全部是唯讀輪詢，由 <see cref="MapComponent_DMSMusicState"/> 在 tick 流程內呼叫：
    /// 地圖情境、玩家單位、FFF 警戒與封鎖、守墓人、站點增援倒數與 FFF 快速反應部隊。
    ///
    /// The situational-music source readers. All read-only polling, called by <see cref="MapComponent_DMSMusicState"/>
    /// inside the tick flow: map context, player units, FFF alert and lockdown, gravekeepers, the site's reinforcement
    /// countdown and FFF quick-reaction waves.
    /// </summary>
    public static class DMSMusicEventBridge
    {
        private const int ArrivalHashInterval = 2500;

        /// <param name="tracking">
        /// 地圖仍在危機或餘波窗口中（<see cref="DMSMusicGameState.Tracking"/>）；沒有情境的地圖也要完整讀取。
        /// The map is still in a crisis or aftermath window (<see cref="DMSMusicGameState.Tracking"/>); read everything
        /// even without a context.
        /// </param>
        /// <param name="encounterSong">啟動中 Boss 遭遇指定的曲目，沒有則為 null。The active boss encounter's song, or null.</param>
        public static DMSMusicInputs ReadInputs(MapComponent_DMSMusicState comp, bool tracking, out SongDef encounterSong)
        {
            Map map = comp.map;
            DMSMusicInputs inputs = DMSMusicInputs.Empty;
            inputs.context = DMSMusicContextUtility.ContextFor(map);

            // 警戒值每張地圖都讀：地圖之後才變成相關時，不會把既有水位當成「剛上升」。
            // Read the alert level on every map, so a map that only later becomes relevant does not see its standing
            // level as a fresh rise.
            MapComponent_AlertCounter alert = map.GetComponent<MapComponent_AlertCounter>();
            inputs.alertLevel = alert?.AlertLevel ?? 0f;
            encounterSong = null;
            ReadBossEncounters(map, ref inputs, ref encounterSong);
            ReadHuntdowns(comp, ref inputs);

            if (inputs.context == DMSMusicContext.None && !inputs.hasEncounter && !tracking)
            {
                return inputs;
            }

            inputs.playerPresent = AnyPlayerUnit(map);
            inputs.airspaceBlocked = alert?.IsAirspaceBlocked ?? false;
            inputs.lockdown = LockdownPhase(map);
            ReadLords(map, ref inputs);
            ReadReinforcements(map, ref inputs);
            return inputs;
        }

        /// <summary>
        /// 開火 hook 的過濾：玩家與敵對單位之間的攻擊，目標是未倒地的 pawn 或砲塔。友軍誤傷、雙方都不是玩家、
        /// 射擊普通物件或處決倒地者都不算。
        /// The firing-hook filter: an attack between the player and a hostile, aimed at a standing pawn or a turret.
        /// Friendly fire, fights without the player, shooting ordinary objects or finishing off the downed do not count.
        /// </summary>
        public static bool IsHostileExchange(Thing caster, Thing target)
        {
            if (caster == null || target == null || caster == target)
            {
                return false;
            }
            Faction player = Faction.OfPlayer;
            if ((caster.Faction == player) == (target.Faction == player))
            {
                return false;
            }
            if (!caster.HostileTo(target))
            {
                return false;
            }
            if (target is Pawn pawn)
            {
                return !pawn.Dead && !pawn.Downed;
            }
            return target is Building_Turret;
        }

        private static bool AnyPlayerUnit(Map map)
        {
            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].IsColonist || pawns[i].IsColonyMech)
                {
                    return true;
                }
            }
            return false;
        }

        private static DMSLockdownPhase LockdownPhase(Map map)
        {
            MapComponent_FacilityLockdown lockdown = MapComponent_FacilityLockdown.For(map);
            if (lockdown == null)
            {
                return DMSLockdownPhase.Idle;
            }
            switch (lockdown.State)
            {
                case FacilityLockdownState.Countdown:
                    return DMSLockdownPhase.Countdown;
                case FacilityLockdownState.Locked:
                    return DMSLockdownPhase.Locked;
                case FacilityLockdownState.Sealed:
                    return DMSLockdownPhase.Sealed;
                case FacilityLockdownState.Disarmed:
                    return DMSLockdownPhase.Disarmed;
                case FacilityLockdownState.Lost:
                    return DMSLockdownPhase.Lost;
                default:
                    return DMSLockdownPhase.Idle;
            }
        }

        /// <summary>
        /// 守墓人：盤查中的分隊為警戒；搜索中的分隊為警戒（窗口由遊戲端計算）；交戰或搜索中分隊最後看見入侵者的 tick
        /// 用來判斷接敵。交戰模式本身不算交戰：高警戒值會讓分隊長時間停在交戰模式卻沒有接敵。
        /// 友方 DMS 部隊（<c>DMS_Army</c> 的 <see cref="LordJob_AssistColony"/>）有成員在交戰窗口內出手時，加上選曲用的參戰標記。
        ///
        /// Gravekeepers: a challenging squad is an alert; a sweeping squad is an alert (the window is computed
        /// game-side); the last sighting by an engaging or sweeping squad decides contact. Engage mode alone is not
        /// combat: a high alert level keeps squads in it long after the last contact. Friendly DMS forces
        /// (<c>DMS_Army</c> under <see cref="LordJob_AssistColony"/>) whose members attacked inside the combat window add
        /// the song-selection tag.
        /// </summary>
        private static void ReadLords(Map map, ref DMSMusicInputs inputs)
        {
            int now = Find.TickManager.TicksGame;
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord.ownedPawns.Count == 0 || lord.faction == null)
                {
                    continue;
                }
                if (lord.LordJob is LordJob_AssistColony)
                {
                    if (lord.faction.def == DMS_DefOf.DMS_Army && !lord.faction.HostileTo(Faction.OfPlayer) && AnyAttackedRecently(lord, now))
                    {
                        inputs.songTags |= DMSMusicSongTag.AlliedEngaged;
                    }
                    continue;
                }
                if (!(lord.LordJob is LordJob_GravekeeperPatrol) || !(lord.CurLordToil is LordToil_GravekeeperPatrol toil)
                    || !lord.faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                switch (toil.Mode)
                {
                    case GravekeeperMode.Challenge:
                        inputs.gravekeeperChallenge = true;
                        break;
                    case GravekeeperMode.Search:
                        inputs.gravekeeperSearch = true;
                        NoteSighting(toil, ref inputs);
                        break;
                    case GravekeeperMode.Engage:
                        NoteSighting(toil, ref inputs);
                        break;
                }
            }
        }

        private static bool AnyAttackedRecently(Lord lord, int now)
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn_MindState mind = lord.ownedPawns[i].mindState;
                if (mind != null && now - mind.lastAttackTargetTick <= DMSMusicTuning.CombatWindowTicks)
                {
                    return true;
                }
            }
            return false;
        }

        private static void NoteSighting(LordToil_GravekeeperPatrol toil, ref DMSMusicInputs inputs)
        {
            if (toil.data is LordToilData_GravekeeperPatrol data && data.lastSeenTick > inputs.gravekeeperSightTick)
            {
                inputs.gravekeeperSightTick = data.lastSeenTick;
            }
        }

        /// <summary>
        /// 地表站點：原版 <see cref="TimedDetectionRaids"/>（FFF 限時站點啟動的倒數，地圖存在期間反覆發動）。
        /// 任何地圖：進行中任務裡以這張地圖為目標的 FFF <see cref="QuestPart_RaidWaveArrives"/>（警報呼叫的快速反應部隊與信標）。
        /// 取最近的一波。
        /// Surface sites: vanilla <see cref="TimedDetectionRaids"/> (the countdown FFF timed sites start, repeating while
        /// the map exists). Any map: FFF <see cref="QuestPart_RaidWaveArrives"/> in ongoing quests aimed at this map (alarm
        /// quick-reaction forces and beacons). The nearest wave wins.
        /// </summary>
        private static void ReadReinforcements(Map map, ref DMSMusicInputs inputs)
        {
            int nearest = -1;

            TimedDetectionRaids raids = map.Parent?.GetComponent<TimedDetectionRaids>();
            if (raids != null)
            {
                inputs.raidsSentCount = raids.RaidsSentCount;
                if (raids.DetectionCountdownStarted)
                {
                    nearest = raids.TicksLeftToSendRaids;
                }
            }

            int now = Find.TickManager.TicksGame;
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest.State != QuestState.Ongoing)
                {
                    continue;
                }
                List<QuestPart> parts = quest.PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (!(parts[j] is QuestPart_RaidWaveArrives wave) || wave.State != QuestPartState.Enabled
                        || wave.mapParent == null || wave.mapParent != map.Parent)
                    {
                        continue;
                    }
                    int left = DMSMusicTiming.TicksUntilArrival(now, wave.EnableTick, wave.minDelay, wave.maxDelay,
                        wave.mapParent.HashOffsetTicks() - now, ArrivalHashInterval, CanSummonNow(wave, map));
                    if (nearest < 0 || left < nearest)
                    {
                        nearest = left;
                    }
                }
            }

            inputs.reinforcementTicksLeft = nearest;
        }

        private static bool CanSummonNow(QuestPart_RaidWaveArrives wave, Map map)
        {
            return wave.raidWaveDef?.Worker != null && wave.raidWaveDef.Worker.ShouldSummonNow(map).Accepted;
        }

        /// <summary>
        /// Boss 遭遇：進行中的 DMS Boss 任務（根節點為 <see cref="QuestNode_Root_BossgroupFactionExposed"/>）裡的
        /// <see cref="QuestPart_BossgroupArrivesWithMusic"/>。
        /// <list type="bullet">
        /// <item>抵達前：以召喚地圖為目標，依原版規則推算預計抵達時間（接近時為警戒原因）。</item>
        /// <item>抵達後：以 Boss 實際所在地圖為準（離開站點時會改投玩家據點）。還在空投艙內為「即將抵達」；
        /// 生成在地圖上、敵對、未倒地、未休眠才算啟動；暫時暈眩（EMP）仍算啟動。</item>
        /// <item>死亡、離開地圖、轉為非敵對都不再計入；Boss 或護衛仍在地圖上時，這張地圖仍有遭遇。</item>
        /// </list>
        /// Boss encounters: <see cref="QuestPart_BossgroupArrivesWithMusic"/> in ongoing DMS boss quests (rooted at
        /// <see cref="QuestNode_Root_BossgroupFactionExposed"/>). Before arrival the summoning map is the target and the
        /// arrival is estimated by vanilla's rules (an alert reason when close). After arrival the bosses' actual map
        /// counts (leaving the site retargets the drop to a player home): still in a drop pod is "arriving"; spawned,
        /// hostile, standing and awake is active, and a temporary stun (EMP) stays active. Death, leaving the map or
        /// turning non-hostile stop counting; while a boss or escort is still on the map, the map keeps the encounter.
        /// </summary>
        private static void ReadBossEncounters(Map map, ref DMSMusicInputs inputs, ref SongDef encounterSong)
        {
            int now = Find.TickManager.TicksGame;
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest.State != QuestState.Ongoing || !(quest.root?.root is QuestNode_Root_BossgroupFactionExposed))
                {
                    continue;
                }
                List<QuestPart> parts = quest.PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j] is QuestPart_BossgroupArrivesWithMusic arrival)
                    {
                        int activeBefore = inputs.activeBosses;
                        ReadBossEncounter(arrival, map, now, ref inputs);
                        if (inputs.activeBosses > activeBefore && encounterSong == null)
                        {
                            encounterSong = arrival.song;
                        }
                    }
                }
            }
        }

        private static void ReadBossEncounter(QuestPart_BossgroupArrivesWithMusic arrival, Map map, int now, ref DMSMusicInputs inputs)
        {
            if (arrival.State == QuestPartState.Enabled)
            {
                if (arrival.mapParent == null || arrival.mapParent.Map != map)
                {
                    return;
                }
                inputs.hasEncounter = true;
                bool canSummon = arrival.bossgroupDef?.Worker != null && arrival.bossgroupDef.Worker.ShouldSummonNow(map).Accepted;
                int left = DMSMusicTiming.TicksUntilArrival(now, arrival.EnableTick, arrival.minDelay, arrival.maxDelay,
                    arrival.mapParent.HashOffsetTicks() - now, ArrivalHashInterval, canSummon);
                KeepNearest(ref inputs.bossTicksUntilArrival, left);
                return;
            }
            if (!arrival.Arrived)
            {
                return;
            }

            arrival.EnsureMusicPawns();
            List<Pawn> bosses = arrival.musicBosses;
            for (int i = 0; i < bosses.Count; i++)
            {
                Pawn boss = bosses[i];
                if (!OnMapAndHostile(boss, map))
                {
                    continue;
                }
                inputs.hasEncounter = true;
                if (!boss.Spawned)
                {
                    KeepNearest(ref inputs.bossTicksUntilArrival, 0);
                }
                else if (!boss.Downed && (boss.TryGetComp<CompCanBeDormant>()?.Awake ?? true))
                {
                    inputs.activeBosses++;
                }
            }
            List<Pawn> escorts = arrival.musicEscorts;
            for (int i = 0; i < escorts.Count && !inputs.hasEncounter; i++)
            {
                if (OnMapAndHostile(escorts[i], map))
                {
                    inputs.hasEncounter = true;
                }
            }
        }

        /// <summary>
        /// 登記的封存科技追殺襲擊仍在地圖上：這張地圖有遭遇（交火會被記錄）；其中有重型追殺時再加上選曲用的追殺標記。
        /// 永久敵對或研究狀態本身不會觸發；只有 DMS 自己發動、已登記的那幾支襲擊算數。
        /// A registered Occultech huntdown raid is still on the map: the map has an encounter (fire gets recorded), plus the
        /// huntdown song tag when one of them is a heavy hunt. Permanent hostility or research state alone never
        /// triggers it; only the raids DMS fired and registered count.
        /// </summary>
        private static void ReadHuntdowns(MapComponent_DMSMusicState comp, ref DMSMusicInputs inputs)
        {
            IReadOnlyList<Lord> lords = comp.HuntdownLords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (lord != null && lord.ownedPawns.Count > 0 && lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer))
                {
                    inputs.hasEncounter = true;
                    if (comp.IsHeavyHuntdown(lord))
                    {
                        inputs.songTags |= DMSMusicSongTag.Huntdown;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 這支追殺是否出動了足夠的鬣蜥級重型機兵（見 <see cref="DMSMusicTuning.HuntdownSongHeavyPower"/>）。
        /// Whether this hunt fielded enough Iguana-class heavies (see <see cref="DMSMusicTuning.HuntdownSongHeavyPower"/>).
        /// </summary>
        public static bool IsHeavyHuntdown(Lord lord)
        {
            int heavies = 0;
            List<Pawn> pawns = lord?.ownedPawns;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    if ((pawns[i]?.kindDef?.combatPower ?? 0f) >= DMSMusicTuning.HuntdownSongHeavyPower
                        && ++heavies >= DMSMusicTuning.HuntdownSongMinHeavies)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 執行一次襲擊發動，並把這次新出現、屬於 <paramref name="faction"/> 的 lord 登記為封存科技追殺。
        /// 原版襲擊在發動當下就建立 lord（空投或步行進場都一樣），所以前後比對即可，不需要 Harmony。
        /// Runs one raid firing and registers the lords of <paramref name="faction"/> that appeared because of it as
        /// Occultech huntdowns. Vanilla raids create their lord at firing time (drop pods or walking in alike), so a
        /// before/after comparison is enough without Harmony.
        /// </summary>
        public static bool FireAndRegisterHuntdown(Map map, Faction faction, Func<bool> fire)
        {
            if (map == null || faction == null)
            {
                return fire();
            }
            HashSet<Lord> before = new HashSet<Lord>(map.lordManager.lords);
            bool fired = fire();
            if (fired)
            {
                MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.For(map);
                foreach (Lord lord in map.lordManager.lords)
                {
                    if (lord.faction == faction && !before.Contains(lord))
                    {
                        comp?.RegisterHuntdown(lord);
                    }
                }
            }
            return fired;
        }

        private static bool OnMapAndHostile(Pawn pawn, Map map)
        {
            return pawn != null && !pawn.Dead && pawn.MapHeld == map && pawn.HostileTo(Faction.OfPlayer);
        }

        private static void KeepNearest(ref int ticksLeft, int candidate)
        {
            if (ticksLeft < 0 || candidate < ticksLeft)
            {
                ticksLeft = candidate;
            }
        }
    }

    /// <summary>
    /// 交戰事件 hook：每一發攻擊（含未命中、連發的每一發、砲塔與近戰）出手前，若是 DMS 相關地圖上的敵我交火，
    /// 就記錄該地圖的最後交戰 tick。第一個判斷只看單一地圖快取，非 DMS 地圖的開銷是一次參考比較加一個布林值。
    ///
    /// The combat-event hook: before each attack (misses, every burst shot, turrets and melee included), a hostile
    /// exchange on a DMS-relevant map records that map's last combat tick. The first check uses a one-map cache, so on
    /// any other map the cost is a reference compare and a bool.
    /// </summary>
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot_DMSMusic
    {
        public static void Prefix(Verb __instance)
        {
            Thing caster = __instance.caster;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            MapComponent_DMSMusicState comp = MapComponent_DMSMusicState.CachedFor(map);
            if (comp == null || !comp.RecordsCombat)
            {
                return;
            }
            if (DMSMusicEventBridge.IsHostileExchange(caster, __instance.CurrentTarget.Thing))
            {
                comp.NoteCombat();
            }
        }
    }

    /// <summary>
    /// 外部強制播放（Anomaly、FFF <c>CompUseEffect_PlaySong</c>、其他模組）開始時通知客戶端：DMS 讓位到這首播完，
    /// 也在私有旗標讀不到時用曲目比對判斷。
    /// Tells the client when an outside forced song starts (Anomaly, FFF <c>CompUseEffect_PlaySong</c>, other mods): DMS
    /// stands aside until it ends, and the recorded song is the fallback when the private flag cannot be read.
    /// </summary>
    [HarmonyPatch(typeof(MusicManagerPlay), nameof(MusicManagerPlay.ForcePlaySong))]
    public static class Patch_MusicManagerPlay_ForcePlaySong_DMSMusic
    {
        public static void Postfix(SongDef song)
        {
            GameComponent_DMSMusic.Instance?.Notify_ForcedSong(song);
        }
    }
}
