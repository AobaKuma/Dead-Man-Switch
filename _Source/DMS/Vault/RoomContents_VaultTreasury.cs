using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在獎勵房 LayoutRoomDef 上：要換上哪種密封門、用哪種控制台解鎖。
    /// On the treasury LayoutRoomDef: which sealed door to fit and which console unlocks it.
    /// </summary>
    public class ModExtension_VaultTreasury : DefModExtension
    {
        /// <summary>取代房門的密封門，必須是 FFF 的 Building_RollingDoor_AccessLink。
        /// Replaces the room's doors; must be an FFF Building_RollingDoor_AccessLink.</summary>
        public ThingDef sealedDoorDef;

        /// <summary>帶 <see cref="CompVaultConsole"/> 的控制台。The console carrying <see cref="CompVaultConsole"/>.</summary>
        public ThingDef consoleDef;

        /// <summary>優先擺控制台的房型；沒有就退回任何非獎勵房、非入口、非走廊的房間。
        /// Preferred room for the console; falls back to any room that is not the treasury, entrance or corridor.</summary>
        public LayoutRoomDef preferredConsoleRoom;
    }

    /// <summary>
    /// 獎勵房：房間本身只負責擺戰利品（交給 def 的 prefabs / fillEdges）。
    /// 封門與控制台由 <see cref="VaultTreasuryUtility.SealTreasuries"/> 在整棟結構生成完後統一處理，
    /// 因為要等所有門都生成、也要確定控制台放得下，才能安全地把門封起來。
    ///
    /// Treasury: the room itself only lays out the loot (the def's prefabs / fillEdges).
    /// Sealing the doors and placing the console happen in
    /// <see cref="VaultTreasuryUtility.SealTreasuries"/> once the whole structure is spawned; every
    /// door has to exist and the console has to have found a home before it is safe to seal anything.
    /// </summary>
    public class RoomContents_VaultTreasury : RoomContentsWorker
    {
    }

    public static class VaultTreasuryUtility
    {
        /// <summary>
        /// 對結構裡每一間獎勵房：確認封門不會把入口困住 → 放控制台 → 換上密封門並連結到控制台。
        /// 任一步失敗就放棄封門，房間退化成普通的戰利品房。
        /// For each treasury in the structure: make sure sealing it can't trap the entrance, place the
        /// console, then swap in sealed doors linked to it. If any step fails the room is left open.
        /// </summary>
        public static void SealTreasuries(LayoutStructureSketch sketch, Map map, Faction faction)
        {
            List<LayoutRoom> rooms = sketch.structureLayout?.Rooms;
            if (rooms.NullOrEmpty()) return;

            foreach (LayoutRoom treasury in rooms)
            {
                LayoutRoomDef def = treasury.defs?.FirstOrDefault(d => d.roomContentsWorkerType == typeof(RoomContents_VaultTreasury));
                ModExtension_VaultTreasury ext = def?.GetModExtension<ModExtension_VaultTreasury>();
                if (ext?.sealedDoorDef == null || ext.consoleDef == null) continue;

                if (!SafeToSeal(rooms, treasury))
                {
                    Log.Message($"[DMS] Treasury room {treasury.id} is the only route between other rooms; leaving it unsealed.");
                    continue;
                }

                CompVaultConsole console = SpawnConsole(rooms, treasury, ext, map, faction);
                if (console == null)
                {
                    Log.Warning("[DMS] No room could take the vault console; leaving the treasury unsealed.");
                    continue;
                }

                SealDoors(treasury, ext.sealedDoorDef, map, console);
            }
        }

        /// <summary>
        /// 拿掉獎勵房後，其餘房間彼此還連得起來才可以封。
        /// The other rooms must stay connected to each other once the treasury is removed.
        /// </summary>
        private static bool SafeToSeal(List<LayoutRoom> rooms, LayoutRoom treasury)
        {
            List<LayoutRoom> others = rooms.Where(r => r != treasury).ToList();
            if (others.Count <= 1) return true;

            HashSet<LayoutRoom> seen = new HashSet<LayoutRoom> { others[0] };
            Queue<LayoutRoom> queue = new Queue<LayoutRoom>();
            queue.Enqueue(others[0]);

            while (queue.Count > 0)
            {
                LayoutRoom room = queue.Dequeue();
                if (room.connections == null) continue;

                foreach (LayoutRoom next in room.connections)
                {
                    if (next == treasury || !seen.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }

            return seen.Count == others.Count;
        }

        private static CompVaultConsole SpawnConsole(List<LayoutRoom> rooms, LayoutRoom treasury,
            ModExtension_VaultTreasury ext, Map map, Faction faction)
        {
            IEnumerable<LayoutRoom> candidates = rooms
                .Where(r => r != treasury && !IsOffLimits(r))
                .OrderByDescending(r => ext.preferredConsoleRoom != null && r.HasLayoutDef(ext.preferredConsoleRoom))
                .ThenByDescending(r => r.Area);

            foreach (LayoutRoom room in candidates)
            {
                List<Thing> spawned = new List<Thing>();
                RoomGenUtility.FillAroundEdges(ext.consoleDef, 1, IntRange.One, room, map,
                    null, spawned, 1, 0, null, avoidDoors: true, RotationDirection.Opposite, null, faction);

                if (spawned.Count == 0)
                {
                    // 牆邊沒位置就往房間裡找。No wall space; try the open floor.
                    RoomGenUtility.FillWithPadding(ext.consoleDef, 1, room, map, null, null, spawned, 2);
                }

                if (spawned.Count > 0)
                {
                    return spawned[0].TryGetComp<CompVaultConsole>();
                }
            }

            return null;
        }

        /// <summary>入口、走廊、獎勵房本身都不放控制台。No console in the entrance, a corridor or a treasury.</summary>
        private static bool IsOffLimits(LayoutRoom room)
        {
            if (room.defs.NullOrEmpty()) return true;

            foreach (LayoutRoomDef def in room.defs)
            {
                System.Type worker = def.roomContentsWorkerType;
                if (worker == typeof(RoomContents_VaultEntrance)
                    || worker == typeof(RoomContents_VaultTreasury)
                    || typeof(RoomContents_Corridor).IsAssignableFrom(worker))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把房間邊界上的每一扇門換成密封門，並掛到控制台底下。
        /// Swaps every door on the room's boundary for a sealed one under the console's control.
        /// </summary>
        private static void SealDoors(LayoutRoom room, ThingDef sealedDoorDef, Map map, CompVaultConsole console)
        {
            List<Building_Door> doors = new List<Building_Door>();
            foreach (CellRect rect in room.rects)
            {
                foreach (IntVec3 cell in rect.EdgeCells)
                {
                    Building_Door door = cell.GetDoor(map);
                    if (door != null && door.def != sealedDoorDef && !doors.Contains(door))
                    {
                        doors.Add(door);
                    }
                }
            }

            foreach (Building_Door door in doors)
            {
                IntVec3 cell = door.Position;
                Rot4 rot = door.Rotation;
                door.Destroy();

                Thing sealedDoor = GenSpawn.Spawn(ThingMaker.MakeThing(sealedDoorDef), cell, map, rot);
                if (!console.Link(sealedDoor))
                {
                    Log.Error($"[DMS] {sealedDoorDef.defName} is not an access-link door; the treasury door at {cell} will never open.");
                }
            }
        }
    }
}
