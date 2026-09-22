using RimWorld;
using Verse;
using Verse.AI;

namespace DMS
{
    public class CompProperties_CanBeDormant_NPCChance : CompProperties_CanBeDormant
    {
        /// <summary>
        /// 地圖生成期、以非玩家陣營生成時進入休眠的機率。1 = 全部休眠，0 = 全部醒著。
        /// Chance to start dormant when spawned for a non-player faction during map generation. 1 = all asleep, 0 = all awake.
        /// </summary>
        public float npcDormantChance = 0.65f;

        public CompProperties_CanBeDormant_NPCChance()
        {
            compClass = typeof(CompCanBeDormant_NPCChance);
        }
    }

    /// <summary>
    /// 原版 <see cref="CompCanBeDormant"/> 的變體：不用 startsDormant 一刀切，而是只在「地圖生成期、
    /// 掛在非玩家陣營下」生成時才擲骰決定要不要休眠。玩家自己蓋的、敵人在戰鬥中就地部署的、
    /// 開發模式直接放的，一律醒著。這樣同一個 ThingDef（例如 DMS_Turret_Sentry）不用另外定義封存變體。
    ///
    /// A <see cref="CompCanBeDormant"/> that rolls for dormancy only when the thing spawns for a non-player
    /// faction while its map is being generated. Player-built, enemy-deployed-in-combat and dev-spawned
    /// copies all stay awake, so one ThingDef serves both roles without a mothballed variant.
    ///
    /// 陣營若是在生成之後才補上（例如 <see cref="DMS_GenStep_UndergroundStructure"/> 先無陣營生成再指派），
    /// 由呼叫端在 SetFaction 之後再呼叫 <see cref="TryRollNpcDormancy"/>。
    /// If the faction is assigned after spawning, the caller invokes <see cref="TryRollNpcDormancy"/> afterwards.
    /// </summary>
    public class CompCanBeDormant_NPCChance : CompCanBeDormant
    {
        private bool rolled;

        public new CompProperties_CanBeDormant_NPCChance Props => (CompProperties_CanBeDormant_NPCChance)props;

        /// <summary>
        /// 玩家陣營的砲塔永遠視為醒著：玩家自己蓋的本來就不擲骰，而佔領（Claim）來的休眠砲塔一換到玩家陣營
        /// 就立刻可用，不必等挨打或敵人靠近才醒。
        /// Player-faction turrets always count as awake: player-built ones never roll, and a dormant turret the
        /// player claims becomes usable the moment its faction changes instead of waiting for damage or a hostile.
        /// </summary>
        public override bool Awake => parent.Faction == Faction.OfPlayer || base.Awake;

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(250))
            {
                EnsurePlayerAwakeState();
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            EnsurePlayerAwakeState();
        }

        /// <summary>
        /// 把玩家陣營砲塔的底層狀態同步成「已醒」，讓之後就算再換回 NPC 陣營也不會突然變回休眠。
        /// 不能走 <see cref="CompCanBeDormant.WakeUp"/>：它先檢查 <see cref="Awake"/>，而覆寫後對玩家永遠為 true。
        /// Syncs the underlying state of a player-faction turret to "awake", so a later faction change back to an NPC
        /// does not drop it into dormancy. Cannot go through <see cref="CompCanBeDormant.WakeUp"/>: it checks
        /// <see cref="Awake"/> first, which the override makes always true for the player.
        /// </summary>
        private void EnsurePlayerAwakeState()
        {
            if (parent.Faction != Faction.OfPlayer || base.Awake) return;

            wokeUpTick = GenTicks.TicksGame;
            wakeUpOnTick = int.MinValue;
            if (parent.Spawned && parent is IAttackTarget t)
            {
                parent.Map.attackTargetsCache.UpdateTarget(t);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // 讀檔重生時不擲骰：醒／睡狀態（wokeUpTick、wakeUpOnTick）與 rolled 都由存檔還原，
            // 重新擲骰會把已醒的砲塔打回休眠、或讓已休眠的醒來。
            // Never roll when respawning from a save: the awake/asleep state (wokeUpTick, wakeUpOnTick) and
            // rolled are restored by the scribe; rolling again would flip turrets the player already fought past.
            if (!respawningAfterLoad)
            {
                TryRollNpcDormancy();
            }
        }

        /// <summary>
        /// 只在地圖生成期、且父物件屬於非玩家陣營時擲一次骰。條件不成立時不記錄已擲，讓之後補上陣營的呼叫端還能再試。
        /// 存讀檔期間（Scribe 進行中）一律不擲，確保休眠狀態只由存檔決定。
        /// Rolls once, only during map generation and only for a non-player faction. When the preconditions fail
        /// the roll is not consumed, so a caller that assigns the faction later can still trigger it.
        /// Never rolls while the scribe is active, so dormancy after a load comes solely from the save.
        /// </summary>
        public void TryRollNpcDormancy()
        {
            if (rolled || !parent.Spawned) return;
            if (Scribe.mode != LoadSaveMode.Inactive) return;
            if (MapGenerator.mapBeingGenerated != parent.Map) return;

            Faction faction = parent.Faction;
            if (faction == null || faction == Faction.OfPlayer) return;

            rolled = true;
            if (Rand.Chance(Props.npcDormantChance))
            {
                ToSleep();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref rolled, "npcDormancyRolled", defaultValue: false);
        }
    }
}
