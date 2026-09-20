using System.Collections.Generic;
using System.Linq;
using Fortified.Structures;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 在地圖的空地上隨機散布數座帶指定標籤的 <see cref="FFF_StructureDef"/>。
    /// Scatters a few tagged <see cref="FFF_StructureDef"/>s onto open ground anywhere on the map.
    ///
    /// 與 GenStep_FFFGarrisonedStructure 的 satelliteScatter 不同：那是繞著主結構的環帶，
    /// 而且跑在 order 400；這個步驟排在 Fog 之前（order 1400 左右），此時主結構、衛星、
    /// 岩塊、植物都已經就位，所以可以只挑「真正沒被任何東西占用」的空地：
    ///   * 整個足跡（外擴 minSpacing）都在邊緣安全帶以內
    ///   * 沒有任何建築（含天然岩石、圍欄、沙包）
    ///   * 沒有屋頂（不鑽進山體）
    ///   * 地形不是水，也不是鋪設過的地板（避免壓到既有結構的水泥坪或跑道）
    ///   * 不與 MapGenerator 的 UsedRects 重疊（地標建築與其外圍牆帶、其他站點結構）
    /// 找不到位置就跳過，不會硬塞。
    ///
    /// Unlike satelliteScatter (a ring around the main structure, run at order 400), this
    /// runs just before Fog, when the main structure, satellites, chunks and plants are all
    /// in place, so it can insist on ground that is genuinely unused: the whole footprint
    /// (padded by minSpacing) inside the edge band, no buildings at all (natural rock, fences
    /// and sandbags included), unroofed, and on terrain that is neither water nor a laid floor
    /// (so it never lands on an existing compound's concrete), and not overlapping MapGenerator's
    /// UsedRects (landmark structures with their perimeter band, other site structures). Pieces
    /// that find no room are skipped.
    /// </summary>
    public class DMS_GenStep_ScatterStructures : GenStep
    {
        /// <summary>候選結構標籤。Tag the candidate structures must carry.</summary>
        public string tag;

        /// <summary>或直接列出候選池；與 tag 同時給時兩者合併。Explicit pool; merged with the tag matches when both are set.</summary>
        public List<FFF_StructureDef> structureDefs;

        /// <summary>要放幾座。How many to place.</summary>
        public IntRange countRange = new IntRange(2, 4);

        /// <summary>足跡外圍要留幾格空地（與任何建築、彼此之間）。Clear gap around each footprint, in cells.</summary>
        public int minSpacing = 6;

        public bool randomRotation = true;

        /// <summary>每座最多試幾個落點。Placement attempts per piece.</summary>
        public int attemptsPerPiece = 60;

        /// <summary>
        /// 是否把建築交給站點派系（攝影機、無人機巢等才會當作防務運作）。
        /// Hand buildings to the site faction so cameras, drone nests and the like act as defences.
        /// </summary>
        public bool useSiteFaction = true;

        public override int SeedPart => 1487215233;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map == null) return;

            List<FFF_StructureDef> pool = BuildPool();
            if (pool.Count == 0)
            {
                Log.Warning($"[DMS] DMS_GenStep_ScatterStructures: no FFF_StructureDef matches tag '{tag}' and structureDefs is empty; nothing scattered.");
                return;
            }

            int count = countRange.RandomInRange;
            if (count <= 0) return;

            Faction faction = useSiteFaction ? (map.ParentFaction ?? parms.sitePart?.site?.Faction) : null;
            CellRect usable = FFF_StructureUtility.UsableRect(map);
            if (usable.Width <= 0 || usable.Height <= 0) return;

            List<CellRect> placed = new List<CellRect>();
            int done = 0;
            for (int i = 0; i < count; i++)
            {
                FFF_StructureDef def = pool.RandomElement();
                Rot4 rot = randomRotation ? Rot4.Random : Rot4.North;

                if (!TryFindSpot(map, def, rot, usable, placed, out IntVec3 center)) continue;

                // 生成期一律 reconnectPower:false，理由見 FFF_StructureUtility.Generate 的註解。
                // Always reconnectPower:false during generation; see the note on FFF_StructureUtility.Generate.
                FFF_StructureUtility.Generate(def, center, map, faction, rot, reconnectPower: false);
                CellRect foot = FFF_StructureUtility.FootprintAt(def, center, rot);
                placed.Add(foot);
                FFF_StructureUtility.ReserveUsedRect(map, foot, 1);
                done++;
            }

            if (done < count && Prefs.DevMode)
            {
                Log.Message($"[DMS] DMS_GenStep_ScatterStructures: placed {done}/{count} on a {map.Size.x}x{map.Size.z} map; the rest found no open ground.");
            }
        }

        private List<FFF_StructureDef> BuildPool()
        {
            List<FFF_StructureDef> pool = new List<FFF_StructureDef>();
            if (!structureDefs.NullOrEmpty())
            {
                pool.AddRange(structureDefs.Where(d => d != null));
            }
            if (!tag.NullOrEmpty())
            {
                pool.AddRange(DefDatabase<FFF_StructureDef>.AllDefsListForReading
                    .Where(d => d.tags != null && d.tags.Contains(tag) && !pool.Contains(d)));
            }
            return pool;
        }

        private bool TryFindSpot(Map map, FFF_StructureDef def, Rot4 rot, CellRect usable, List<CellRect> placed, out IntVec3 center)
        {
            for (int attempt = 0; attempt < attemptsPerPiece; attempt++)
            {
                IntVec3 candidate = usable.RandomCell;
                CellRect foot = FFF_StructureUtility.FootprintAt(def, candidate, rot);
                CellRect padded = foot.ExpandedBy(minSpacing);

                if (!Contains(usable, foot)) continue;
                if (placed.Any(p => p.Overlaps(padded))) continue;
                // 地標建築等已登記的保留區域（含其外圍牆帶）一律避開。
                // Keep clear of anything registered in UsedRects, landmark structures and their perimeter included.
                if (FFF_StructureUtility.OverlapsUsedRect(map, padded)) continue;
                if (!IsOpenGround(map, padded)) continue;

                center = candidate;
                return true;
            }
            center = IntVec3.Invalid;
            return false;
        }

        /// <summary>
        /// 「空地」的定義：範圍內每一格都在地圖內、沒有建築、沒有屋頂、不是水、不是鋪設地板。
        /// 超出地圖的外擴格直接略過（安全帶已保證足跡本體在內）。
        /// What counts as open ground: every in-bounds cell has no building, no roof, and terrain that is
        /// neither water nor a laid floor. Padding cells outside the map are ignored.
        /// </summary>
        private static bool IsOpenGround(Map map, CellRect rect)
        {
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) continue;
                if (c.GetFirstBuilding(map) != null) return false;
                if (c.Roofed(map)) return false;
                TerrainDef terrain = c.GetTerrain(map);
                if (terrain == null || terrain.IsWater || terrain.layerable) return false;
            }
            return true;
        }

        private static bool Contains(CellRect outer, CellRect inner)
        {
            return inner.minX >= outer.minX && inner.maxX <= outer.maxX
                && inner.minZ >= outer.minZ && inner.maxZ <= outer.maxZ;
        }
    }
}
