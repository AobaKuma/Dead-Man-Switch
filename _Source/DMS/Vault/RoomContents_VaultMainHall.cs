using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 地下設施的大廳長廊：中線鋪地板與電纜，整條走廊掛一座壁掛配電盤供電，
    /// 沿途設哨站（掩體 + 哨戒砲 + 震動感測器），走廊兩端牆上裝順著走廊看的攝影機。
    /// 所有設施都掛在 <see cref="VaultRoomUtility.DefenderFaction"/> 名下，砲塔生成即帶電。
    ///
    /// The vault's main hall: a floored, wired centre strip powered by a single wall substation,
    /// checkpoints (cover + sentries + seismic sensor) along the way, and a camera on each
    /// end wall watching down the corridor. Everything belongs to the defender faction and the
    /// turrets spawn charged, so the corridor is live the moment the player steps in.
    ///
    /// 各項設施由走廊 LayoutRoomDef 上的 <see cref="ModExtension_VaultSecurity"/> 指定。
    /// The fixtures come from <see cref="ModExtension_VaultSecurity"/> on the corridor room def.
    /// 繼承原版 RoomContents_Corridor 只為了沿用它的中線地板；地下沒有「外面」，所以不開外門。
    /// Derives from vanilla RoomContents_Corridor for the strip; no exterior doors underground.
    /// </summary>
    public class RoomContents_VaultMainHall : RoomContents_Corridor
    {
        /// <summary>哨站距走廊兩端至少幾格。Minimum gap between a checkpoint and the corridor end.</summary>
        private const int CheckpointEndMargin = 3;

        /// <summary>哨站間距的隨機抖動。Jitter applied to the checkpoint spacing.</summary>
        private static readonly IntRange SpacingJitter = new IntRange(-3, 3);

        private ModExtension_VaultSecurity ext;

        protected override IntRange ExteriorDoorCount => IntRange.Zero;

        protected override TerrainDef StripCorridorTerrain => ext?.stripTerrain;

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            ext = RoomDef?.GetModExtension<ModExtension_VaultSecurity>();
            Faction defenders = faction ?? VaultRoomUtility.DefenderFaction;

            // 先鋪設施再交給 base 放預製塊與雜物，這樣哨站不會被裝飾物擠掉。
            // Fixtures first, then base for prefabs and junk, so dressing never displaces a checkpoint.
            if (ext != null)
            {
                List<Segment> segments = new List<Segment>();
                foreach (CellRect rect in room.rects)
                {
                    Segment seg = new Segment(rect);
                    if (seg.Length >= 3) segments.Add(seg);
                }

                foreach (Segment seg in segments)
                {
                    SpawnStrip(map, room, seg);
                    SpawnCheckpoints(map, seg, defenders);
                    SpawnCameras(map, seg, defenders);
                    SpawnCornerTurrets(map, seg, defenders);
                }

                // 各段的電纜在路口相接，整條走廊共用一座配電盤就夠；長的那段優先。
                // 配電盤改放檢修通道時（wallSubstation = false）由 DMS_LayoutWorker_Vault 處理。
                // The strips join at the junctions, so one substation serves the whole corridor;
                // try the longest segment first. With wallSubstation off, DMS_LayoutWorker_Vault puts
                // it in a maintenance tunnel instead.
                if (ext.wallSubstation)
                {
                    segments.SortByDescending(seg => seg.Length);
                    foreach (Segment seg in segments)
                    {
                        if (TrySpawnSubstation(map, seg, defenders)) break;
                    }
                }
            }

            base.FillRoom(map, room, faction, threatPoints);
        }

        // ── 走廊座標 / Segment coordinates ──────────────────────────────────

        /// <summary>
        /// 一段直走廊的局部座標：along 沿走廊方向（0 = 起點內側第一格），across 垂直方向（0 = 中線）。
        /// Local coordinates for one straight corridor rect: along the axis (0 = first interior cell)
        /// and across it (0 = centre line).
        /// </summary>
        private readonly struct Segment
        {
            public readonly CellRect Rect;
            public readonly CellRect Interior;
            public readonly Rot4 Axis;
            public readonly Rot4 Side;
            public readonly IntVec3 Start;
            public readonly int Length;
            public readonly int HalfWidth;

            public Segment(CellRect rect)
            {
                Rect = rect;
                Interior = rect.ContractedBy(1);
                Axis = rect.Width > rect.Height ? Rot4.East : Rot4.North;
                Side = Axis.Rotated(RotationDirection.Clockwise);
                Start = Interior.GetCenterCellOnEdge(Axis.Opposite);
                Length = Axis.IsHorizontal ? Interior.Width : Interior.Height;
                HalfWidth = (Axis.IsHorizontal ? Interior.Height : Interior.Width) / 2;
            }

            public IntVec3 Cell(int along, int across) => Start + Axis.FacingCell * along + Side.FacingCell * across;

            /// <summary>走廊某側的牆格。The wall cell beside a given position.</summary>
            public IntVec3 WallCell(int along, int sideSign) => Cell(along, sideSign * (HalfWidth + 1));
        }

        // ── 電纜 / Power strip ───────────────────────────────────────────────

        /// <summary>
        /// 沿中線鋪電纜，並往兩端延伸到走廊房間的邊界為止：T 字的直段這樣才會穿過橫段的中線，
        /// 兩段的電網才接得起來。conduitAlongWalls 時改壓在整圈牆線下。
        /// Lays conduit along the centre line and keeps going past the rect until it leaves the
        /// corridor room, so a T-junction's stem crosses the bar's centre line and the grids join.
        /// With conduitAlongWalls it goes under the whole wall ring instead.
        /// </summary>
        private void SpawnStrip(Map map, LayoutRoom room, Segment seg)
        {
            if (ext.conduitDef == null) return;

            // 沿牆：整圈牆線（含路口那條線，之後的閘門會蓋在上面）。
            // Along the walls: the whole wall ring, junction lines included (the gates go on top of them later).
            if (ext.conduitAlongWalls)
            {
                foreach (IntVec3 cell in seg.Rect.EdgeCells) SpawnConduit(map, cell);
                return;
            }

            for (int along = 0; along < seg.Length; along++)
            {
                SpawnConduit(map, seg.Cell(along, 0));
            }

            for (int dir = -1; dir <= 1; dir += 2)
            {
                int along = dir < 0 ? -1 : seg.Length;
                for (int step = 0; step < 64; step++, along += dir)
                {
                    IntVec3 cell = seg.Cell(along, 0);
                    if (!cell.InBounds(map) || !room.Contains(cell, 1) || cell.GetEdifice(map) != null) break;
                    SpawnConduit(map, cell);
                }
            }
        }

        private void SpawnConduit(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || cell.GetTransmitter(map) != null) return;
            GenSpawn.Spawn(ext.conduitDef, cell, map);
        }

        // ── 哨站 / Checkpoints ───────────────────────────────────────────────

        private void SpawnCheckpoints(Map map, Segment seg, Faction faction)
        {
            if (ext.turretDef == null && ext.barricadeDef == null && ext.sensorDef == null) return;

            int last = seg.Length - 1 - CheckpointEndMargin;
            if (last < CheckpointEndMargin) return;

            int spacing = Mathf.Max(ext.checkpointSpacing, 4);
            int next = Rand.RangeInclusive(CheckpointEndMargin, Mathf.Min(last, CheckpointEndMargin + spacing));
            int spawned = 0;

            for (int along = CheckpointEndMargin; along <= last && spawned < ext.maxCheckpointsPerRect; along++)
            {
                if (along < next || !CanSpawnCheckpoint(map, seg, along)) continue;

                SpawnCheckpoint(map, seg, along, faction);
                spawned++;
                next = along + spacing + SpacingJitter.RandomInRange;
            }
        }

        /// <summary>
        /// 哨站要蓋在直段上：3x3 內沒有任何建築，兩側三格都是實牆（沒有門、不是路口）。
        /// A checkpoint needs a straight stretch: an empty 3x3 with solid, doorless wall on both sides.
        /// </summary>
        private static bool CanSpawnCheckpoint(Map map, Segment seg, int along)
        {
            for (int a = -1; a <= 1; a++)
            {
                for (int s = -1; s <= 1; s++)
                {
                    IntVec3 cell = seg.Cell(along + a, s);
                    if (!cell.InBounds(map) || !seg.Interior.Contains(cell) || cell.GetEdifice(map) != null) return false;
                }

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    IntVec3 wall = seg.WallCell(along + a, sign);
                    if (!wall.InBounds(map)) return false;

                    Building edifice = wall.GetEdifice(map);
                    if (edifice == null || edifice.def.IsDoor) return false;
                }
            }

            return true;
        }

        private void SpawnCheckpoint(Map map, Segment seg, int along, Faction faction)
        {
            // 四角掩體 / Cover at the corners
            if (ext.barricadeDef != null)
            {
                for (int a = -1; a <= 1; a += 2)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        VaultRoomUtility.SpawnSecurity(ext.barricadeDef, seg.Cell(along + a, s), map, Rot4.North,
                            faction, ext.initialBatteryPct, ext.barricadeStuff);
                    }
                }
            }

            // 前後砲塔，坐在中線電纜上；有機率是壞的。checkpointTurrets 關掉時砲塔改放走廊角落。
            // Turrets fore and aft on the strip conduit; some of them are wrecks. With checkpointTurrets off they go in
            // the corridor corners instead.
            if (ext.turretDef != null && ext.checkpointTurrets)
            {
                for (int a = -1; a <= 1; a += 2)
                {
                    SpawnTurret(map, seg.Cell(along + a, 0), faction);
                }
            }

            // 中央感測器 / Sensor in the middle
            if (ext.sensorDef != null)
            {
                VaultRoomUtility.SpawnSecurity(ext.sensorDef, seg.Cell(along, 0), map, Rot4.North,
                    faction, ext.initialBatteryPct);
            }

            // 靠牆的反應設施，放在哨站前後一格外，不擋住哨站兩側的通道。
            // Effector against the wall, one row past the checkpoint so the side lanes stay open.
            if (ext.nestDef != null && seg.HalfWidth >= 2 && Rand.Chance(ext.nestChance))
            {
                int a = along + (Rand.Bool ? 2 : -2);
                int s = (Rand.Bool ? 1 : -1) * seg.HalfWidth;
                IntVec3 cell = seg.Cell(a, s);
                if (seg.Interior.Contains(cell) && cell.GetEdifice(map) == null)
                {
                    VaultRoomUtility.SpawnSecurity(ext.nestDef, cell, map, Rot4.North, faction, ext.initialBatteryPct);
                }
            }

            // 毒氣釋放口：哨站前後 3~5 格的中線上，玩家推進到哨站時正好噴在接近路線上。
            // Gas vents on the centre line 3~5 cells before and after the checkpoint, right on the approach.
            if (ext.gasVentDef != null)
            {
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    if (!Rand.Chance(ext.gasVentChance)) continue;
                    IntVec3 cell = seg.Cell(along + dir * Rand.RangeInclusive(3, 5), 0);
                    if (!seg.Interior.Contains(cell) || cell.GetEdifice(map) != null) continue;
                    if (cell.GetFirstThing(map, ext.gasVentDef) != null) continue;
                    VaultRoomUtility.SpawnSecurity(ext.gasVentDef, cell, map, Rot4.North, faction, ext.initialBatteryPct);
                }
            }
        }

        /// <summary>放一座砲塔；有機率換成報廢品。Spawns a turret, or sometimes a wreck.</summary>
        private void SpawnTurret(Map map, IntVec3 cell, Faction faction)
        {
            if (ext.wreckedTurretDef != null && Rand.Chance(ext.turretWreckChance))
            {
                // 報廢品不掛防務陣營，玩家可以直接拆。Wrecks are factionless so the player can just clear them.
                GenSpawn.Spawn(ThingMaker.MakeThing(ext.wreckedTurretDef), cell, map, Rot4.North);
                return;
            }
            VaultRoomUtility.SpawnSecurity(ext.turretDef, cell, map, Rot4.North, faction, ext.initialBatteryPct);
        }

        // ── 角落砲塔 / Corner turrets ────────────────────────────────────────

        /// <summary>
        /// 走廊矩形的四個內角中，兩面都是實牆的（死巷、樞紐臂末端；路口那側還開著所以不算）才放，
        /// 每個各擲一次 cornerTurretChance，每段最多 maxCornerTurretsPerRect 座；門旁邊不放，免得堵住門。
        /// 角落離中線電纜不超過半個走廊寬，砲塔會自己接上電網。
        /// Of a corridor rect's four inside corners, only those walled on both sides count (dead ends, hub arm ends;
        /// a junction side is still open). Each rolls cornerTurretChance, up to maxCornerTurretsPerRect per rect, and
        /// none goes beside a door where it would block it. A corner is at most half a corridor from the strip, so the
        /// turret connects to the grid on its own.
        /// </summary>
        private void SpawnCornerTurrets(Map map, Segment seg, Faction faction)
        {
            if (ext.turretDef == null || ext.cornerTurretChance <= 0f || ext.maxCornerTurretsPerRect <= 0) return;

            CellRect interior = seg.Interior;
            IntVec3[] corners =
            {
                new IntVec3(interior.minX, 0, interior.minZ),
                new IntVec3(interior.minX, 0, interior.maxZ),
                new IntVec3(interior.maxX, 0, interior.minZ),
                new IntVec3(interior.maxX, 0, interior.maxZ),
            };

            int placed = 0;
            foreach (IntVec3 corner in corners.InRandomOrder())
            {
                if (placed >= ext.maxCornerTurretsPerRect) break;
                if (!corner.InBounds(map) || !IsWalledCorner(map, corner, interior)) continue;
                if (!VaultRoomUtility.IsClearFloor(map, corner)) continue;
                if (GenAdj.CardinalDirections.Any(d => (corner + d).InBounds(map) && (corner + d).GetEdifice(map) is Building_Door)) continue;
                if (!Rand.Chance(ext.cornerTurretChance)) continue;

                SpawnTurret(map, corner, faction);
                placed++;
            }
        }

        private static bool IsWalledCorner(Map map, IntVec3 corner, CellRect interior)
        {
            IntVec3 dx = corner.x == interior.minX ? IntVec3.West : IntVec3.East;
            IntVec3 dz = corner.z == interior.minZ ? IntVec3.South : IntVec3.North;
            return IsSolidWall(map, corner + dx) && IsSolidWall(map, corner + dz);
        }

        private static bool IsSolidWall(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            Building edifice = cell.GetEdifice(map);
            return edifice != null && !edifice.def.IsDoor && edifice.def.Fillage == FillCategory.Full;
        }

        // ── 供電 / Substation ───────────────────────────────────────────────

        /// <summary>
        /// 在走廊側牆掛一座配電盤，並從它拉一條電纜接到中線。找不到位置回傳 false。
        /// Mounts one substation on a side wall with a conduit spur to the strip. False if nowhere fits.
        /// </summary>
        private bool TrySpawnSubstation(Map map, Segment seg, Faction faction)
        {
            if (ext.substationDef == null) return false;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                int along = Rand.RangeInclusive(1, seg.Length - 2);
                int sign = Rand.Bool ? 1 : -1;
                IntVec3 cell = seg.Cell(along, sign * seg.HalfWidth);
                Rot4 rot = sign > 0 ? seg.Side : seg.Side.Opposite;

                if (!VaultRoomUtility.CanAttachToWall(map, cell, rot, seg.WallCell(along, sign))) continue;

                if (ext.conduitDef != null && !ext.conduitAlongWalls)
                {
                    for (int s = 1; s < seg.HalfWidth; s++)
                    {
                        SpawnConduit(map, seg.Cell(along, sign * s));
                    }
                }

                VaultRoomUtility.SpawnSecurity(ext.substationDef, cell, map, rot, faction, ext.initialBatteryPct);
                return true;
            }

            return false;
        }

        // ── 監視 / Cameras ───────────────────────────────────────────────────

        /// <summary>
        /// 走廊兩端各裝一台攝影機。攝影機掛在端牆內側那格、面朝牆，鏡頭往走廊裡看。
        /// One camera per corridor end, on the first interior cell facing the end wall; it scans back
        /// down the corridor.
        /// </summary>
        private void SpawnCameras(Map map, Segment seg, Faction faction)
        {
            if (ext.cameraDef == null) return;

            for (int end = 0; end < 2; end++)
            {
                int along = end == 0 ? 0 : seg.Length - 1;
                Rot4 rot = end == 0 ? seg.Axis.Opposite : seg.Axis;

                // 中線優先；被燈具佔了就往旁邊挪。Centre first; shuffle sideways if a lamp is in the way.
                for (int s = 0; s <= seg.HalfWidth; s++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        IntVec3 cell = seg.Cell(along, sign * s);
                        IntVec3 wall = cell + rot.FacingCell;
                        if (!VaultRoomUtility.CanAttachToWall(map, cell, rot, wall)) continue;

                        if (ext.conduitDef != null && !ext.conduitAlongWalls && s != 0)
                        {
                            SpawnConduit(map, cell);
                        }

                        VaultRoomUtility.SpawnSecurity(ext.cameraDef, cell, map, rot, faction, ext.initialBatteryPct);
                        goto NextEnd;
                    }
                }

            NextEnd: ;
            }
        }
    }
}
