using System.Collections.Generic;
using System.Linq;
using Fortified;
using Fortified.Structures;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 地下設施房間填充的共用工具。
    /// Shared helpers for filling underground facility rooms.
    /// </summary>
    public static class VaultRoomUtility
    {
        /// <summary>
        /// 在房間中央蓋一張帶指定標籤的 FFF 預製結構。房間塞不下就回傳 false，
        /// 呼叫端應該退回程序化填充。
        /// Stamps a tagged FFF structure in the middle of the room. Returns false when nothing fits,
        /// in which case the caller should fall back to procedural filling.
        /// </summary>
        public static bool TryStampStructure(Map map, LayoutRoom room, string tag, Faction faction)
        {
            if (tag.NullOrEmpty()) return false;

            List<FFF_StructureDef> candidates = DefDatabase<FFF_StructureDef>.AllDefs
                .Where(d => d.tags != null && d.tags.Contains(tag))
                .ToList();

            if (candidates.Count == 0)
            {
                Log.WarningOnce($"[DMS] No FFF_StructureDef carries the tag '{tag}'.", tag.GetHashCode());
                return false;
            }

            // 大的先試，這樣房間夠大時會用上內容比較豐富的那張。
            // Try the biggest first so roomy rooms get the richer block.
            foreach (FFF_StructureDef def in candidates.OrderByDescending(d => d.size.x * d.size.z))
            {
                // 多留 1 格，避免預製塊直接貼到房間牆上。
                // One cell of slack so the block doesn't press against the room walls.
                if (!room.TryGetRectOfSize(def.size.x + 1, def.size.z + 1, out CellRect rect)) continue;

                FFF_StructureUtility.Generate(def, rect.CenterCell, map, faction, Rot4.North);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 用 ThingSetMaker 產物填滿房間裡的貨架。回傳 false 代表貨架已滿。
        /// Fills the room's shelves from a ThingSetMaker. Returns false once they are full.
        /// </summary>
        public static bool PlaceOnShelves(Map map, LayoutRoom room, List<Thing> items)
        {
            int safety = 999;
            while (items.Count > 0 && safety-- > 0)
            {
                Thing item = items[items.Count - 1];

                if (!room.TryGetRandomCellInRoom(map, out IntVec3 cell, 0, 0,
                        (IntVec3 c) => ShelfValidator(map, c, item.def), ignoreBuildings: true))
                {
                    return false;
                }

                items.RemoveAt(items.Count - 1);
                GenSpawn.Spawn(item, cell, map).SetForbidden(value: true);
            }

            return true;
        }

        private static bool ShelfValidator(Map map, IntVec3 c, ThingDef itemDef)
        {
            if (!(c.GetFirstThing(map, ThingDefOf.Shelf) is Building_Storage storage)) return false;
            return storage.SpaceRemainingFor(itemDef) > 0;
        }

        /// <summary>依 defName 取 ThingDef，缺了就記一次錯誤。Look up a ThingDef, warning once if absent.</summary>
        public static ThingDef ThingNamed(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Log.ErrorOnce($"[DMS] Vault generation: missing ThingDef {defName}", defName.GetHashCode());
            }
            return def;
        }

        /// <summary>用 RoomGenUtility 在房內平均放置若干建築。Spread N buildings through the room.</summary>
        public static void FillWithPadding(Map map, LayoutRoom room, string defName, int count, int contractedBy)
        {
            if (count <= 0) return;

            ThingDef def = ThingNamed(defName);
            if (def == null) return;

            List<Thing> spawned = new List<Thing>();
            RoomGenUtility.FillWithPadding(def, count, room, map, null, null, spawned, contractedBy);
        }

        // ── 警戒設施 / Security fixtures ──────────────────────────────────────

        private const string DefenderFactionDefName = "DMS_Legacy";

        /// <summary>
        /// 設施殘留防務的陣營。沒有陣營的砲塔與感測器不會把玩家當敵人，所以一定要給一個。
        /// 優先用 DMS_Legacy（殖民遺留，對所有人永久敵對），沒有就退回原版的敵對遠古陣營。
        /// Faction for the vault's leftover defences. Factionless turrets and scanners never treat the
        /// player as hostile, so one is mandatory. Prefers DMS_Legacy, falls back to vanilla's ancients.
        /// </summary>
        public static Faction DefenderFaction
        {
            get
            {
                FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(DefenderFactionDefName);
                Faction faction = def != null ? Find.FactionManager.FirstFactionOfDef(def) : null;
                return faction ?? Faction.OfAncientsHostile;
            }
        }

        /// <summary>
        /// 把生成後的防務設施掛到陣營。封存艙（Building_MechCapsule）裡的機兵一併換：
        /// 艙在 SpawnSetup 時就用「當下的艙陣營」生成機兵，無陣營生成的艙會給出遠古陣營的機兵，
        /// 之後只改艙不改機兵，就會出現艙是遺留部隊、機兵卻是遠古的錯配——被警報放出來時甚至不敵對。
        /// Assigns a defender faction to a spawned fixture. Mech capsules (Building_MechCapsule) generate their
        /// mech in SpawnSetup using whatever faction the capsule has at that moment, so a factionless capsule holds
        /// an Ancients mech; changing only the capsule afterwards leaves the mech mismatched (and, once an alarm
        /// releases it, not even hostile). The held mech is therefore re-factioned together with the capsule.
        /// </summary>
        public static void SetDefenderFaction(Thing thing, Faction faction)
        {
            if (thing == null || faction == null) return;

            if (thing.def.CanHaveFaction && thing.Faction != faction)
            {
                thing.SetFaction(faction);
            }

            if (thing is Building_MechCapsule capsule && capsule.HasMech && capsule.Mech.Faction != faction)
            {
                capsule.Mech.SetFaction(faction);
            }
        }

        /// <summary>
        /// 把內建電池充到指定比例，讓砲塔一生成就有電可以開火。沒有內建電池的東西直接略過。
        /// （FFF 的 CompPowerTrader_InternalBattery 對非玩家陣營的新生成建築本來就會隨機灌 50~100%，
        /// 這裡是要「精確指定」時用，例如儲存庫的警戒設施要滿電。）
        /// Charges an internal battery so the thing is live from the moment it spawns. No-op otherwise.
        /// (FFF's CompPowerTrader_InternalBattery already rolls 50~100% for fresh non-player spawns; this is
        /// for when an exact value is wanted, e.g. vault fixtures at full charge.)
        /// </summary>
        public static void ChargeInternalBattery(Thing thing, float pct)
        {
            thing.TryGetComp<CompPowerTrader_InternalBattery>()?.SetStoredEnergyPct(pct);
        }

        /// <summary>
        /// 生成一件警戒設施：套上防務陣營、充飽內建電池。
        /// Spawns a security fixture with the defender faction and a charged internal battery.
        /// </summary>
        public static Thing SpawnSecurity(ThingDef def, IntVec3 cell, Map map, Rot4 rot, Faction faction,
            float batteryPct = 1f, ThingDef stuff = null)
        {
            Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
            if (def.CanHaveFaction)
            {
                thing.SetFactionDirect(faction ?? DefenderFaction);
            }
            ChargeInternalBattery(thing, batteryPct);
            return GenSpawn.Spawn(thing, cell, map, rot);
        }
    }
}
