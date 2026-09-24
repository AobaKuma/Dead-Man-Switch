using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 StructureLayoutDef 上：這種地下設施要放哪一種封鎖中控、依序偏好哪些房間。
    /// On a StructureLayoutDef: which lockdown controller this facility gets, and the room preference order.
    /// </summary>
    public class ModExtension_FacilityLockdown : DefModExtension
    {
        public ThingDef controllerDef;
        public List<LayoutRoomDef> preferredRooms = new List<LayoutRoomDef>();
    }

    /// <summary>
    /// 在程序化地下設施裡放一座封鎖中控（Fortified.CompFacilityLockdownController）。
    /// 不放在電梯廳（出生點，一進來就能駭掉）、走廊、或任何獎勵房／伺服機房（密封門後，封鎖時可能走不到）。
    ///
    /// Places one lockdown controller (Fortified.CompFacilityLockdownController) in a procedural facility.
    /// Never in the lift lobby (the spawn room; it would be hacked on arrival), a corridor, or a
    /// treasury / server hall (behind sealed doors, possibly unreachable during a lockdown).
    /// </summary>
    public static class FacilityLockdownPlacement
    {
        public static void PlaceController(LayoutStructureSketch sketch, Map map, Faction faction, ModExtension_FacilityLockdown ext)
        {
            if (ext?.controllerDef == null) return;
            List<LayoutRoom> rooms = sketch.structureLayout?.Rooms;
            if (rooms.NullOrEmpty()) return;

            Faction defenders = faction ?? VaultRoomUtility.DefenderFaction;
            List<LayoutRoom> allowed = rooms.Where(r => !IsOffLimits(r)).ToList();

            IEnumerable<LayoutRoom> ordered = allowed
                .OrderBy(r => PreferenceIndex(r, ext.preferredRooms))
                .ThenByDescending(r => r.Area);

            // 靠牆放、背面朝牆（邊緣選項的方向本來就指向牆，所以不轉向）；互動格落在房內且要站得住。
            // Against a wall with its back to it (an edge option's direction already points at the wall, so no
            // rotation offset); the interaction cell lands in the room and must be standable.
            ThingDef def = ext.controllerDef;
            bool InteractionClear(IntVec3 cell, Rot4 rot, CellRect _)
            {
                if (!def.hasInteractionCell) return true;
                IntVec3 interaction = ThingUtility.InteractionCellWhenAt(def, cell, rot, map);
                return interaction.InBounds(map) && interaction.Standable(map);
            }

            foreach (LayoutRoom room in ordered)
            {
                List<Thing> spawned = new List<Thing>();
                RoomGenUtility.FillAroundEdges(def, 1, IntRange.One, room, map,
                    InteractionClear, spawned, 1, 0, null, avoidDoors: true, RotationDirection.None, null, defenders);
                if (spawned.Count == 0)
                {
                    RoomGenUtility.FillWithPadding(ext.controllerDef, 1, room, map, null, null, spawned, 1);
                }
                if (spawned.Count > 0)
                {
                    foreach (Thing t in spawned) VaultRoomUtility.SetDefenderFaction(t, defenders);
                    return;
                }
            }

            Log.Warning($"[DMS] No room could take {ext.controllerDef.defName}; this facility has no lockdown controller.");
        }

        private static int PreferenceIndex(LayoutRoom room, List<LayoutRoomDef> preferred)
        {
            if (preferred.NullOrEmpty()) return 0;
            for (int i = 0; i < preferred.Count; i++)
            {
                if (room.HasLayoutDef(preferred[i])) return i;
            }
            return preferred.Count;
        }

        private static bool IsOffLimits(LayoutRoom room)
        {
            if (room.defs.NullOrEmpty()) return true;
            foreach (LayoutRoomDef def in room.defs)
            {
                System.Type worker = def.roomContentsWorkerType;
                if (worker == typeof(RoomContents_VaultEntrance)
                    || VaultTreasuryUtility.IsTreasuryWorker(worker)
                    || typeof(RoomContents_Corridor).IsAssignableFrom(worker))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
