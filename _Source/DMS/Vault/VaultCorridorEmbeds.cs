using Fortified.Structures;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 走廊嵌入結構的生成：位置在版面階段就排好了（<see cref="VaultLayoutPlan.embeds"/>，見 VaultLayoutGenerator.PlaceEmbeds），
    /// 這裡等房間都填完才蓋。先把佔地裡的東西拆掉（岩層、那一段走廊牆），再蓋結構，牆、開口、門都照結構畫的。
    /// 拆牆用 WillReplace，走廊掛在那段牆上的燈留給最後的孤兒清掃處理，不會掉成地上的物品。
    /// Spawns the corridor embeds. Their places were settled at layout time (<see cref="VaultLayoutPlan.embeds"/>, see
    /// VaultLayoutGenerator.PlaceEmbeds); they go in once the rooms are filled. The footprint is cleared first (rock and
    /// that stretch of corridor wall), then the structure is stamped so its walls, openings and doors land as drawn.
    /// Walls come down with WillReplace, so lamps the corridor hung there are left for the final orphan sweep instead of
    /// dropping as items.
    /// </summary>
    public static class VaultCorridorEmbeds
    {
        public static void Spawn(VaultLayoutPlan plan, Map map, Faction faction)
        {
            if (plan == null) return;

            foreach (VaultLayoutPlan.Embed embed in plan.embeds)
            {
                CellRect rect = embed.rect;
                if (!rect.InBounds(map)) continue;

                foreach (IntVec3 cell in rect)
                {
                    VaultRoomUtility.RemoveEdifice(map, cell);
                }
                // 生成期間電網由遊戲在最後統一建立。The game builds power nets once generation ends.
                FFF_StructureUtility.Generate(embed.structure, rect.CenterCell, map, faction, embed.rot, reconnectPower: false);
            }
        }
    }
}
