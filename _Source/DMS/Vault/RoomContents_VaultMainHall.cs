using System.Collections.Generic;
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
                }

                // 各段的電纜在路口相接，整條走廊共用一座配電盤就夠；長的那段優先。
                // The strips join at the junctions, so one substation serves the whole corridor;
                // try the longest segment first.
                segments.SortByDescending(seg => seg.Length);
                foreach (Segment seg in segments)
                {
                    if (TrySpawnSubstation(map, seg, defenders)) break;
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
        /// 兩段的電網才接得起來。
        /// Lays conduit along the centre line and keeps going past the rect until it leaves the
        /// corridor room, so a T-junction's stem crosses the bar's centre line and the grids join.
        /// </summary>
        private void SpawnStrip(Map map, LayoutRoom room, Segment seg)
        {
            if (ext.conduitDef == null) return;

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

            // 前後砲塔，坐在中線電纜上；有機率是壞的。
            // Turrets fore and aft on the strip conduit; some of them are wrecks.
            if (ext.turretDef != null)
            {
                for (int a = -1; a <= 1; a += 2)
                {
                    IntVec3 cell = seg.Cell(along + a, 0);
                    if (ext.wreckedTurretDef != null && Rand.Chance(ext.turretWreckChance))
                    {
                        // 報廢品不掛防務陣營，玩家可以直接拆。Wrecks are factionless so the player can just clear them.
                        GenSpawn.Spawn(ThingMaker.MakeThing(ext.wreckedTurretDef), cell, map, Rot4.North);
                        continue;
                    }

                    VaultRoomUtility.SpawnSecurity(ext.turretDef, cell, map, Rot4.North, faction, ext.initialBatteryPct);
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

                if (!CanAttachToWall(map, cell, rot, seg.WallCell(along, sign))) continue;

                if (ext.conduitDef != null)
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
                        if (!CanAttachToWall(map, cell, rot, wall)) continue;

                        if (ext.conduitDef != null && s != 0)
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

        /// <summary>
        /// 比照 Placeworker_AttachedToWall：面向的那格要是實牆（非門），本格不能已有同向的壁掛物。
        /// Mirrors Placeworker_AttachedToWall: the faced cell must be solid, doorless wall, and nothing
        /// may already hang on this cell facing the same way.
        /// </summary>
        private static bool CanAttachToWall(Map map, IntVec3 cell, Rot4 rot, IntVec3 wall)
        {
            if (!cell.InBounds(map) || !wall.InBounds(map)) return false;

            Building edifice = wall.GetEdifice(map);
            if (edifice == null || edifice.def.IsDoor || edifice.def.Fillage != FillCategory.Full) return false;

            if (cell.GetEdifice(map) != null) return false;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].def.building != null && things[i].def.building.isAttachment && things[i].Rotation == rot)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
