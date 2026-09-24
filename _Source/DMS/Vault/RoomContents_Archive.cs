using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 設施檔案庫的伺服機房：中央一台設施伺服核心，角落一個檢修開口；機櫃列交給 LayoutRoomDef 的 prefabs。
    /// 機櫃之後才在左右兩側的牆邊放毒氣釋放口，沿牆內緣鋪一圈隱藏導線；結構生成完後由 <see cref="ConnectPower"/>
    /// 把這圈導線穿牆接到走廊電網（電源是檢修通道裡的配電盤），接不上才在機房裡自掛一座配電盤。
    /// 繼承獎勵房，所以密封門與運維室裡的安全控制台由 VaultTreasuryUtility 一併處理。
    /// 核心不掛防務陣營（它是駭入目標，不是防務）；開口、釋放口與配電盤掛防務陣營。
    ///
    /// The data archive's server hall: a facility server core in the middle and a service opening in a corner;
    /// rack rows come from the room def's prefabs. After the racks, gas vents go against the walls either side with
    /// hidden conduit round the inner edge; once the structure is up, <see cref="ConnectPower"/> runs that ring
    /// through the wall into the corridor grid (fed by the substations in the maintenance tunnels), and only hangs
    /// a substation in the hall if it can't. Derives from the treasury, so VaultTreasuryUtility handles the sealed
    /// doors and the console in the operations room. The core stays factionless (a hack target, not a defence);
    /// the opening, vents and any substation get the defenders.
    /// </summary>
    public class RoomContents_ArchiveServerHall : RoomContents_VaultTreasury
    {
        private const string CoreDefName = "DMS_FacilityServerCore";
        private const string HoleDefName = "DMS_Hole_3x3Empty";
        private const string VentDefName = "DMS_Building_GasVent";
        private const string SubstationDefName = "DMS_SubstationCabinet_Wall";
        private const string ConduitDefName = "HiddenConduit";

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            Faction defenders = faction ?? VaultRoomUtility.DefenderFaction;

            // 先放核心：它最大，放不下時逐步縮小留白，最後手段直接放在最大矩形中央。
            // Core first, it's the biggest piece: shrink the padding if needed, else force it at the centre.
            Thing core = PlaceCore(map, room);
            if (core == null)
            {
                Log.Warning($"[DMS] Archive server hall {room.id}: could not place {CoreDefName}.");
            }

            ThingDef holeDef = VaultRoomUtility.ThingNamed(HoleDefName);
            if (holeDef != null)
            {
                List<Thing> holes = new List<Thing>();
                RoomGenUtility.FillWithPadding(holeDef, 1, room, map, null, null, holes, 1);
                foreach (Thing h in holes) VaultRoomUtility.SetDefenderFaction(h, defenders);
            }

            // 機櫃列與其他裝飾：原版 prefab 放置會避開已佔用的格子，所以會自動繞開核心。
            // Rack rows and dressing: vanilla prefab placement avoids occupied cells, so they flow around the core.
            base.FillRoom(map, room, faction, threatPoints);

            // 釋放口與供電放在機櫃之後：機櫃生成時會把同格的釋放口清掉。
            // Vents and power go in after the racks, which would wipe a vent under them.
            PlaceVentsAndPower(map, room, core, defenders);
        }

        private static Thing PlaceCore(Map map, LayoutRoom room)
        {
            ThingDef coreDef = VaultRoomUtility.ThingNamed(CoreDefName);
            if (coreDef == null) return null;

            List<Thing> spawned = new List<Thing>();
            for (int padding = 3; padding >= 1 && spawned.Count == 0; padding--)
            {
                RoomGenUtility.FillWithPadding(coreDef, 1, room, map, null, null, spawned, padding);
            }
            if (spawned.Count > 0) return spawned[0];

            CellRect largest = room.rects.MaxBy(r => r.Area);
            return GenSpawn.Spawn(ThingMaker.MakeThing(coreDef), largest.CenterCell, map, Rot4.North);
        }

        /// <summary>
        /// 在最大矩形的東西兩側牆邊各放一個釋放口（從核心那一列往外找空格），再沿牆內緣整圈鋪隱藏導線。
        /// 一個釋放口都放不下就不鋪。
        /// One vent against each of the east and west walls of the largest rect (searching out from the core's
        /// row), then hidden conduit round the whole inner edge. No vent, no conduit.
        /// </summary>
        private static void PlaceVentsAndPower(Map map, LayoutRoom room, Thing core, Faction defenders)
        {
            ThingDef ventDef = VaultRoomUtility.ThingNamed(VentDefName);
            if (ventDef == null) return;

            CellRect interior = room.rects.MaxBy(r => r.Area).ContractedBy(1);
            int anchorZ = core?.Position.z ?? interior.CenterCell.z;

            int vents = 0;
            foreach (int x in new[] { interior.minX, interior.maxX })
            {
                if (TryPlaceEdgeVent(map, interior, x, anchorZ, ventDef, defenders)) vents++;
            }
            if (vents == 0) return;

            ThingDef conduitDef = VaultRoomUtility.ThingNamed(ConduitDefName);
            if (conduitDef == null) return;
            foreach (IntVec3 cell in interior.EdgeCells)
            {
                if (cell.InBounds(map) && cell.GetTransmitter(map) == null)
                {
                    GenSpawn.Spawn(conduitDef, cell, map);
                }
            }
        }

        /// <summary>沿某一側牆，從核心那一列往南北交替找第一個空格。Walks one side wall outward from the core's row.</summary>
        private static bool TryPlaceEdgeVent(Map map, CellRect interior, int x, int anchorZ, ThingDef ventDef, Faction defenders)
        {
            int z0 = Mathf.Clamp(anchorZ, interior.minZ, interior.maxZ);
            for (int step = 0; step <= interior.Height; step++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (step == 0 && sign < 0) continue;
                    int z = z0 + sign * step;
                    if (z < interior.minZ || z > interior.maxZ) continue;

                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!IsFreeFloor(map, cell)) continue;
                    VaultRoomUtility.SpawnSecurity(ventDef, cell, map, Rot4.North, defenders);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 結構生成完後呼叫：把機房的導線圈穿過與走廊共用的牆，接到走廊中線的電纜。接不上（或走廊沒電纜）就在機房裡
        /// 掛一座配電盤當備援。沒有導線圈（沒放釋放口）就什麼都不做。
        /// Called once the structure is up: run the hall's conduit ring through a wall it shares with the corridor
        /// and on to the corridor strip. If that can't be done, hang a substation in the hall instead. No ring
        /// (no vents) means nothing to power.
        /// </summary>
        public static void ConnectPower(Map map, LayoutRoom room, List<CellRect> corridorInteriors, Faction defenders)
        {
            CellRect largest = room.rects.MaxBy(r => r.Area);
            CellRect interior = largest.ContractedBy(1);
            if (!interior.EdgeCells.Any(c => c.InBounds(map) && c.GetTransmitter(map) != null)) return;

            // 先找直直接到中線電纜的牆；都沒有才接受擦邊接上（例如從走廊端牆平行走、剛好貼著中線）。
            // Prefer a wall whose run lands on the strip; only then accept one that merely brushes it (e.g. a
            // parallel run off the corridor's end wall, one cell beside the centre line).
            ThingDef conduitDef = VaultRoomUtility.ThingNamed(ConduitDefName);
            if (conduitDef != null && (TryLinkToCorridor(map, largest, interior, corridorInteriors, conduitDef, false)
                || TryLinkToCorridor(map, largest, interior, corridorInteriors, conduitDef, true))) return;

            ThingDef substationDef = VaultRoomUtility.ThingNamed(SubstationDefName);
            if (substationDef == null || !TryPlaceSubstation(map, interior, substationDef, defenders))
            {
                Log.Warning($"[DMS] Archive server hall {room.id}: no corridor link and no wall for {SubstationDefName}; gas vents are unpowered.");
            }
        }

        /// <summary>找一格與走廊共用的實牆，從導線圈穿過去一路鋪到走廊中線的電纜。Finds a shared solid wall and runs conduit from the ring to the strip.</summary>
        private static bool TryLinkToCorridor(Map map, CellRect rect, CellRect interior, List<CellRect> corridorInteriors, ThingDef conduitDef,
            bool acceptTouch)
        {
            foreach (IntVec3 wall in rect.EdgeCells.InRandomOrder())
            {
                if (rect.IsCorner(wall) || !wall.InBounds(map)) continue;
                Building edifice = wall.GetEdifice(map);
                if (edifice == null || edifice.def.IsDoor) continue;

                IntVec3 dir = VaultRoomUtility.OutwardDirection(rect, wall);
                IntVec3 inside = wall - dir;
                if (!interior.Contains(inside) || inside.GetTransmitter(map) == null) continue;

                int index = corridorInteriors.FindIndex(c => c.Contains(wall + dir));
                if (index < 0) continue;

                // 走廊電纜沿牆鋪時，共用那道牆下本來就有電纜，導線圈貼著它就接上了。
                // With the corridor conduit along the walls, the shared wall already carries it and the ring touches it.
                if (wall.GetTransmitter(map) != null) return true;
                if (!VaultRoomUtility.TryRunConduit(map, conduitDef, wall + dir, dir, corridorInteriors[index], wall, acceptTouch)) continue;

                // 牆下那格最後才鋪，免得走廊那段誤把它當成已接上的電網。
                // The cell under the wall goes in last so the corridor run can't mistake it for the grid.
                GenSpawn.Spawn(conduitDef, wall, map);
                return true;
            }
            return false;
        }

        /// <summary>找一格貼著實牆、前方可站的內緣格掛配電盤。A wall-backed inner-edge cell with standable space in front.</summary>
        private static bool TryPlaceSubstation(Map map, CellRect interior, ThingDef substationDef, Faction defenders)
        {
            foreach (IntVec3 cell in interior.EdgeCells.InRandomOrder())
            {
                if (!IsFreeFloor(map, cell)) continue;
                for (int r = 0; r < 4; r++)
                {
                    Rot4 rot = new Rot4(r);
                    IntVec3 wall = cell + rot.FacingCell;
                    IntVec3 front = cell - rot.FacingCell;   // 互動格 / interaction cell
                    if (interior.Contains(wall) || !front.InBounds(map) || !front.Standable(map)) continue;
                    if (!VaultRoomUtility.CanAttachToWall(map, cell, rot, wall)) continue;

                    VaultRoomUtility.SpawnSecurity(substationDef, cell, map, rot, defenders);
                    return true;
                }
            }
            return false;
        }

        /// <summary>可站、沒有任何建築（含壁掛物與釋放口）。Standable with no building of any kind on it.</summary>
        private static bool IsFreeFloor(Map map, IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null;
        }
    }

    /// <summary>
    /// 磁帶庫：機櫃列與貨架由 LayoutRoomDef（prefabs / fillEdges）放，這裡只把 DMS_ArchiveShelfLoot 擺上貨架；
    /// 貨架放不下的散在房間地上。
    /// Tape store: racks and shelves come from the room def; this only stocks the shelves from
    /// DMS_ArchiveShelfLoot, dropping any overflow on the floor.
    /// </summary>
    public class RoomContents_ArchiveTapeStore : RoomContentsWorker
    {
        private const string LootDefName = "DMS_ArchiveShelfLoot";

        public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
        {
            base.FillRoom(map, room, faction, threatPoints);   // 先有貨架 / shelves first

            ThingSetMakerDef loot = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail(LootDefName);
            if (loot == null) return;

            List<Thing> items = loot.root.Generate(new ThingSetMakerParams());
            if (VaultRoomUtility.PlaceOnShelves(map, room, items)) return;

            // PlaceOnShelves 會把放上去的移出清單，剩下的都還沒生成。
            // PlaceOnShelves removes what it placed, so everything left is unspawned.
            IntVec3 center = room.rects.MaxBy(r => r.Area).CenterCell;
            foreach (Thing t in items.ToList())
            {
                if (GenPlace.TryPlaceThing(t, center, map, ThingPlaceMode.Near, out Thing placed))
                {
                    placed.SetForbidden(true, warnOnFail: false);
                }
            }
        }
    }
}
