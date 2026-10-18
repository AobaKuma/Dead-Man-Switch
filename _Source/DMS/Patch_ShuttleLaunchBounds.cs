using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    // Vanilla's rotation-less Spawn overload forces North, even though the active
    // transporter preserves the shuttle's rotation. Check before any launch mutations.
    [HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
    public static class Patch_ShuttleLaunchBounds
    {
        private const string LeavingDefName = "DMS_PlayerShip_TransportShuttleLeaving";

        public static bool Prefix(CompLaunchable __instance)
        {
            if (__instance.Props.skyfallerLeaving?.defName != LeavingDefName || !__instance.parent.Spawned)
                return true;

            Map map = __instance.parent.Map;
            var group = __instance.parent.GetComp<CompTransporter>()?.TransportersInGroup(map);
            if (group == null) return true; // Let vanilla report an invalid group.

            // Vanilla uses the initiating launchable's def and rotation for the group.
            foreach (CompTransporter transporter in group)
            {
                if (!transporter.parent.Spawned || transporter.parent.Map != map ||
                    !GenAdj.OccupiedRect(transporter.parent.Position, __instance.parent.Rotation,
                        __instance.Props.skyfallerLeaving.Size).InBounds(map))
                {
                    Messages.Message("DMS_ShuttleLaunchOutOfBounds".Translate(), transporter.parent,
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
            }
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var spawn = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn),
                new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
            var replacement = AccessTools.Method(typeof(Patch_ShuttleLaunchBounds), nameof(SpawnLeaving));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(spawn))
                {
                    // Keep labels/exception blocks on the first instruction at this location.
                    var loadInstance = new CodeInstruction(OpCodes.Ldarg_0);
                    loadInstance.labels.AddRange(instruction.labels);
                    loadInstance.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    yield return loadInstance;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }

        private static Thing SpawnLeaving(Thing leaving, IntVec3 position, Map map, WipeMode wipeMode,
            CompLaunchable launchable)
        {
            return leaving.def.defName == LeavingDefName
                ? GenSpawn.Spawn(leaving, position, map, launchable.parent.Rotation, wipeMode)
                : GenSpawn.Spawn(leaving, position, map, wipeMode);
        }
    }
}
