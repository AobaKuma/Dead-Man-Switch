using System.Collections.Generic;
using Fortified.Structures;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 在口袋地圖中央鋪一張手工的 <see cref="FFF_StructureDef"/>，作為整張地下地圖。
    /// Lays one hand-authored <see cref="FFF_StructureDef"/> at the pocket map centre as the whole underground map.
    ///
    /// 與 <see cref="DMS_GenStep_Vault"/>（程序化 StructureLayoutDef）相對：這條路線給的是固定版面的
    /// 「單體大空間 + 小房間」地圖，見 DMS_Vault_UnderMaps.xml。
    /// FFF 的 GenStep_FFFStructureDef 是隨機落點、也不會設定玩家出生點，所以這裡自己做三件事：
    ///   1. 置中生成（不帶陣營，門與家具才不會被鎖成敵方所有）。
    ///   2. 把 <see cref="defenderDefs"/> 列出的警戒設施掛到防務陣營並充飽內建電池。
    ///   3. 找到出口電梯（PocketMapExit）並把玩家出生點設在那裡，否則 GenStep_Fog 會報錯。
    /// The counterpart to <see cref="DMS_GenStep_Vault"/>: fixed "one big hall + small rooms" layouts instead of a
    /// procedural StructureLayoutDef. FFF's own GenStep_FFFStructureDef picks a random spot and never sets the player
    /// start, so this one centres the structure (factionless, or doors and furniture would belong to the enemy),
    /// hands the listed security fixtures to the defender faction with charged batteries (rolling NPC dormancy for
    /// those that support it), and puts the player start on the exit lift.
    /// </summary>
    public class DMS_GenStep_UndergroundStructure : GenStep
    {
        /// <summary>候選版面，隨機取一張。Candidate layouts; one is picked at random.</summary>
        public List<FFF_StructureDef> structureDefs = new List<FFF_StructureDef>();

        /// <summary>生成後改掛防務陣營的 ThingDef（攝影機、掃描器、無人機巢、封存艙……）。
        /// ThingDefs handed to the defender faction after spawning (cameras, scanners, drone nests, mech cradles...).</summary>
        public List<ThingDef> defenderDefs = new List<ThingDef>();

        /// <summary>警戒設施內建電池的初始電量比例。Initial internal-battery charge for the security fixtures.</summary>
        public float initialBatteryPct = 1f;

        public override int SeedPart => 1934420117;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (structureDefs.NullOrEmpty())
            {
                Log.Error("[DMS] DMS_GenStep_UndergroundStructure: structureDefs is empty; the pocket map will be bare rock.");
                return;
            }

            FFF_StructureDef def = structureDefs.RandomElement();
            IntVec3 center = map.Center;
            CellRect footprint = FFF_StructureUtility.FootprintAt(def, center, Rot4.North);
            if (!footprint.InBounds(map))
            {
                Log.Error($"[DMS] DMS_GenStep_UndergroundStructure: {def.defName} ({footprint.Width}x{footprint.Height}) does not fit a {map.Size.x}x{map.Size.z} map; raise pocketMapSize on the portal.");
                return;
            }

            // 生成期一律 reconnectPower:false，理由見 FFF_StructureUtility.Generate 的註解。
            // Always reconnectPower:false during generation; see the note on FFF_StructureUtility.Generate.
            FFF_StructureUtility.Generate(def, center, map, null, Rot4.North, reconnectPower: false);

            Faction defenders = VaultRoomUtility.DefenderFaction;
            HashSet<Thing> seen = new HashSet<Thing>();
            Thing exit = null;
            foreach (IntVec3 c in footprint)
            {
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!seen.Add(t)) continue;

                    if (t is PocketMapExit)
                    {
                        exit = t;
                    }
                    else if (defenderDefs.Contains(t.def))
                    {
                        // 封存艙裡的機兵也一併換陣營，見 VaultRoomUtility.SetDefenderFaction。
                        // Re-factions the mech inside a capsule too; see VaultRoomUtility.SetDefenderFaction.
                        VaultRoomUtility.SetDefenderFaction(t, defenders);
                        VaultRoomUtility.ChargeInternalBattery(t, initialBatteryPct);

                        // 結構是無陣營生成的，所以 NPC 休眠擲骰在 PostSpawnSetup 時沒有成立；補上陣營後再擲一次。
                        // The structure spawned factionless, so the NPC dormancy roll skipped itself in PostSpawnSetup; roll now.
                        t.TryGetComp<CompCanBeDormant_NPCChance>()?.TryRollNpcDormancy();
                    }
                }
            }

            if (exit != null)
            {
                MapGenerator.PlayerStartSpot = exit.Position;
            }
            else
            {
                Log.Error($"[DMS] DMS_GenStep_UndergroundStructure: {def.defName} has no PocketMapExit; pawns would be trapped. Falling back to the structure centre as the start spot.");
                MapGenerator.PlayerStartSpot = footprint.CenterCell;
            }
        }
    }
}
