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

        /// <summary>走廊的電纜（中線或牆下，見 conduitAlongWalls）。The corridor's conduit (centre line or under the walls; see conduitAlongWalls).</summary>
        public ThingDef conduitDef;

        /// <summary>
        /// 電纜沿走廊的牆鋪（壓在每段走廊矩形的整圈牆線下，路口與樞紐的牆線相連成同一個電網），不走中線。
        /// 搭配暗裝電纜（HiddenConduit）時地面上看不到。走廊上的設備離牆都不超過半個走廊寬，會自己接上。
        /// Run the conduit along the corridor walls (under the whole wall ring of every corridor rect; junction and
        /// hub wall lines join them into one grid) instead of down the centre. With HiddenConduit it doesn't show.
        /// Everything in the corridor is within half a corridor of a wall, so it connects on its own.
        /// </summary>
        public bool conduitAlongWalls;

        /// <summary>
        /// 整條走廊掛一座的壁掛配電盤（須為 wall attachment），是走廊上所有設施的電源。
        /// Wall-mounted substation (a wall attachment), one per corridor, powering everything on it.
        /// </summary>
        public ThingDef substationDef;

        /// <summary>
        /// 由走廊自己在側牆掛配電盤。版面掛了 ModExtension_VaultTunnels 時設 false，配電盤改放在檢修通道裡。
        /// Whether the corridor hangs its own substation on a side wall. Set false when the layout carries
        /// ModExtension_VaultTunnels; the substation then lives in a maintenance tunnel instead.
        /// </summary>
        public bool wallSubstation = true;

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

        /// <summary>哨站前後要不要放砲塔。關掉時砲塔只出現在走廊角落。Whether checkpoints get turrets fore and aft; off, turrets only go in corridor corners.</summary>
        public bool checkpointTurrets = true;

        /// <summary>
        /// 走廊角落（兩面都是實牆的內角，例如死巷兩角、樞紐每一臂的末端）各有這個機率放一座砲塔。0 = 不放。
        /// Chance each corridor corner (an inside corner walled on both sides, e.g. a dead end's corners or the end of
        /// each hub arm) gets a turret. 0 = none.
        /// </summary>
        public float cornerTurretChance;

        /// <summary>每段走廊矩形最多幾座角落砲塔。Most corner turrets per corridor rect.</summary>
        public int maxCornerTurretsPerRect = 1;

        /// <summary>哨站正中央的感測器。Sensor in the middle of the checkpoint.</summary>
        public ThingDef sensorDef;

        /// <summary>哨站旁邊、靠牆的警報反應設施（例如 FPV 巢）。Alert effector beside the checkpoint.</summary>
        public ThingDef nestDef;
        public float nestChance = 0.5f;

        /// <summary>
        /// 哨站前後、走廊中線上的毒氣釋放口；null = 不放。坐在中線電纜上，與哨戒砲共用電路。
        /// Gas vents on the centre line ahead of and behind each checkpoint; null = none. They sit on the strip
        /// conduit and share the sentries' circuit.
        /// </summary>
        public ThingDef gasVentDef;

        /// <summary>每座哨站的每一側放一個釋放口的機率。Chance per checkpoint side.</summary>
        public float gasVentChance = 0.5f;

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
