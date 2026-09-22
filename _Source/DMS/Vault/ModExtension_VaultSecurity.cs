using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在走廊 LayoutRoomDef 上，指定 <see cref="RoomContents_VaultMainHall"/> 要鋪的警戒設施。
    /// 每一項都可以留空，留空就不生成那一類設施。
    /// Attached to the corridor LayoutRoomDef; tells <see cref="RoomContents_VaultMainHall"/> which
    /// security fixtures to lay down. Every entry is optional; leave one null to skip that kind.
    /// </summary>
    public class ModExtension_VaultSecurity : DefModExtension
    {
        // ── 走廊本體 / Corridor shape ──────────────────────────────────

        /// <summary>
        /// 走廊向兩側擴張的格數，走廊總寬 = 2 * expansion + 1（含牆）。3 → 內部淨寬 5。
        /// Half-width of the corridor; total width is 2 * expansion + 1 including walls.
        /// </summary>
        public int corridorExpansion = 3;

        /// <summary>沿走廊中線鋪的地板，null 則不鋪。Centre strip terrain; null skips it.</summary>
        public TerrainDef stripTerrain;

        // ── 供電 / Power ────────────────────────────────────────────────

        /// <summary>中線與哨站底下的電纜。Conduit laid under the strip and checkpoints.</summary>
        public ThingDef conduitDef;

        /// <summary>
        /// 整條走廊掛一座的壁掛配電盤（須為 wall attachment），是走廊上所有設施的電源。
        /// Wall-mounted substation (a wall attachment), one per corridor, powering everything on it.
        /// </summary>
        public ThingDef substationDef;

        // ── 哨站 / Checkpoint ───────────────────────────────────────────

        /// <summary>哨站四角的掩體。Cover at the checkpoint corners.</summary>
        public ThingDef barricadeDef;
        public ThingDef barricadeStuff;

        /// <summary>哨站前後兩端的砲塔。Turrets at the two ends of the checkpoint.</summary>
        public ThingDef turretDef;

        /// <summary>
        /// 每座砲塔有這個機率換成報廢品（例如 DMS_Wreckage_SentryGun），讓哨站看起來年久失修、也讓火力不那麼整齊。
        /// Chance each turret is a wreck instead (e.g. DMS_Wreckage_SentryGun); the checkpoints read as
        /// neglected, and the firepower is less uniform.
        /// </summary>
        public ThingDef wreckedTurretDef;
        public float turretWreckChance = 0.35f;

        /// <summary>哨站正中央的感測器。Sensor in the middle of the checkpoint.</summary>
        public ThingDef sensorDef;

        /// <summary>哨站旁邊、靠牆的警報反應設施（例如 FPV 巢）。Alert effector beside the checkpoint.</summary>
        public ThingDef nestDef;
        public float nestChance = 0.5f;

        /// <summary>兩座哨站之間的目標間距。Target spacing between checkpoints.</summary>
        public int checkpointSpacing = 14;

        /// <summary>單段走廊最多幾座哨站。Cap per corridor segment.</summary>
        public int maxCheckpointsPerRect = 2;

        // ── 監視 / Surveillance ─────────────────────────────────────────

        /// <summary>
        /// 裝在走廊兩端牆上、順著走廊看的攝影機（須為 wall attachment）。
        /// Camera mounted on each end wall, looking down the corridor (a wall attachment).
        /// </summary>
        public ThingDef cameraDef;

        /// <summary>
        /// 砲塔等內建電池設備生成時的電量比例。
        /// Charge fraction for spawned turrets and other internal-battery devices.
        /// </summary>
        public float initialBatteryPct = 1f;
    }
}
