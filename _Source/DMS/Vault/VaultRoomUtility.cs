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
                        (IntVec3 c) => ShelfValidator(map, c), ignoreBuildings: true))
                {
                    return false;
                }

                items.RemoveAt(items.Count - 1);
                GenSpawn.Spawn(item, cell, map).SetForbidden(value: true);
            }

            return true;
        }

        /// <summary>
        /// 貨架格還放得下一疊。原版 Building_Storage.SpaceRemainingFor 正負號反了（已存數 − 容量，空貨架是負數），
        /// 用它判斷會讓每座貨架都被當成滿的，所以自己按格數：這一格的物品疊數少於 maxItemsInCell 就還有位置。
        /// A shelf cell with room for one more stack. Vanilla Building_Storage.SpaceRemainingFor has its sign flipped
        /// (held − capacity, negative on an empty shelf), which reads every shelf as full, so count per cell instead:
        /// fewer stacks than maxItemsInCell means there's room.
        /// </summary>
        private static bool ShelfValidator(Map map, IntVec3 c)
        {
            if (!(c.GetFirstThing(map, ThingDefOf.Shelf) is Building_Storage storage)) return false;
            int stacks = 0;
            foreach (Thing t in c.GetThingList(map))
            {
                if (t.def.category == ThingCategory.Item) stacks++;
            }
            return stacks < storage.def.building.maxItemsInCell;
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
        /// 之後只改艙不改機兵，就會出現艙是遺留部隊、機兵卻是遠古的錯配，被警報放出來時甚至不敵對。
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
            Thing thing = MakeThing(def, stuff);
            if (def.CanHaveFaction)
            {
                thing.SetFactionDirect(faction ?? DefenderFaction);
            }
            ChargeInternalBattery(thing, batteryPct);
            return GenSpawn.Spawn(thing, cell, map, rot);
        }

        /// <summary>造一件東西；需要材質時用 stuff，沒給就用預設材質。Makes a thing, with stuff (or the default stuff) when it needs one.</summary>
        public static Thing MakeThing(ThingDef def, ThingDef stuff = null)
        {
            return ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
        }

        /// <summary>這格還沒有輸電物就鋪一格電纜。Lays conduit on a cell that has no transmitter yet.</summary>
        public static void TrySpawnConduit(Map map, ThingDef conduitDef, IntVec3 cell)
        {
            if (conduitDef == null || !cell.InBounds(map) || cell.GetTransmitter(map) != null) return;
            GenSpawn.Spawn(conduitDef, cell, map);
        }

        // ── 房型 / Room kinds ────────────────────────────────────────────────

        /// <summary>房間的某個房型用的是這個 worker。Whether one of the room's defs uses this contents worker.</summary>
        public static bool HasWorker(LayoutRoom room, System.Type worker)
        {
            return room?.defs != null && room.defs.Any(d => d.roomContentsWorkerType == worker);
        }

        public static bool IsEntrance(LayoutRoom room) => HasWorker(room, typeof(RoomContents_VaultEntrance));

        /// <summary>
        /// 不放額外設施（檢修口、控制台、封鎖中控）的房間：沒有房型的、入口檢查點（太小又塞滿結構）、走廊、
        /// 獎勵房（含伺服機房）、電梯廳（出生點）。
        /// Rooms that take no extra fixtures (hatches, consoles, the lockdown controller): untyped rooms, entrance
        /// checkpoints (small and full of their structure), corridors, treasuries (server halls included) and the lift
        /// lobby (the spawn room).
        /// </summary>
        public static bool IsOffLimits(LayoutRoom room)
        {
            if (room.defs.NullOrEmpty() || VaultCheckpointUtility.IsCheckpoint(room)) return true;
            return room.defs.Any(d =>
            {
                System.Type worker = d.roomContentsWorkerType;
                return worker == typeof(RoomContents_VaultEntrance)
                    || VaultTreasuryUtility.IsTreasuryWorker(worker)
                    || (worker != null && typeof(RoomContents_Corridor).IsAssignableFrom(worker));
            });
        }

        // ── 格子 / Cells ────────────────────────────────────────────────────

        /// <summary>實心、不是門的建築（牆、岩石）。A full-fillage edifice that isn't a door (wall, rock).</summary>
        public static bool IsSolidWall(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            Building edifice = cell.GetEdifice(map);
            return edifice != null && !edifice.def.IsDoor && edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>矩形邊上某格往外的方向（牆角取 x 方向）。Outward direction from a rect edge cell (x wins at corners).</summary>
        public static IntVec3 OutwardDirection(CellRect rect, IntVec3 edgeCell)
        {
            if (edgeCell.x == rect.minX) return IntVec3.West;
            if (edgeCell.x == rect.maxX) return IntVec3.East;
            if (edgeCell.z == rect.minZ) return IntVec3.South;
            return IntVec3.North;
        }

        /// <summary>
        /// 從 start 沿 step 一格格往前鋪電纜（不超出 bounds），走到既有的輸電物上就停；acceptTouch 時側鄰格有輸電物
        /// （來的那格不算）也算接上。接得上才真的鋪，接不上什麼都不鋪、回傳 false。
        /// Runs conduit from start along step (staying inside bounds) until it reaches an existing transmitter; with
        /// acceptTouch, one beside it (not the cell it came from) counts too. Lays it only if it connects; otherwise
        /// lays nothing and returns false.
        /// </summary>
        public static bool TryRunConduit(Map map, ThingDef conduitDef, IntVec3 start, IntVec3 step, CellRect bounds, IntVec3 cameFrom,
            bool acceptTouch = false)
        {
            List<IntVec3> cells = new List<IntVec3>();
            bool reached = false;
            IntVec3 prev = cameFrom;
            for (IntVec3 c = start; bounds.Contains(c) && c.InBounds(map); prev = c, c += step)
            {
                if (c.GetTransmitter(map) != null)
                {
                    reached = true;
                    break;
                }
                cells.Add(c);
                if (!acceptTouch) continue;
                foreach (IntVec3 d in GenAdj.CardinalDirections)
                {
                    IntVec3 n = c + d;
                    if (n != prev && n.InBounds(map) && n.GetTransmitter(map) != null) reached = true;
                }
                if (reached) break;
            }
            if (!reached) return false;

            foreach (IntVec3 c in cells) GenSpawn.Spawn(conduitDef, c, map);
            return true;
        }

        /// <summary>可站、除了電纜之外沒有任何建築。Standable with no building on it except conduit.</summary>
        public static bool IsClearFloor(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map) || !cell.Standable(map)) return false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                ThingDef def = things[i].def;
                if (def.category == ThingCategory.Building && (def.building == null || !def.building.isPowerConduit)) return false;
            }
            return true;
        }

        /// <summary>
        /// 比照 Placeworker_AttachedToWall：面向的那格要是實牆（非門），本格不能已有同向的壁掛物。
        /// Mirrors Placeworker_AttachedToWall: the faced cell must be solid, doorless wall, and nothing
        /// may already hang on this cell facing the same way.
        /// </summary>
        public static bool CanAttachToWall(Map map, IntVec3 cell, Rot4 rot, IntVec3 wall)
        {
            if (!cell.InBounds(map) || !IsSolidWall(map, wall) || cell.GetEdifice(map) != null) return false;

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

        // ── 壁掛物 / Wall attachments ────────────────────────────────────────

        /// <summary>
        /// 生成期間拆掉一格的建築（牆、門、岩石，含不可破壞的設施牆）。用 WillReplace：原版 Building.DeSpawn 在其他模式下
        /// 會把掛在上面的燈、配電盤等打包成迷你化物品丟在地上；WillReplace 讓它們留在原地，由
        /// <see cref="RemoveOrphanedAttachments"/> 在最後清掉真正沒牆可掛的。WillReplace 也不留資源、不標記屋頂坍塌。
        /// Takes down whatever edifice is on a cell during generation (wall, door, rock, indestructible facility walls
        /// included). Uses WillReplace: in any other mode vanilla Building.DeSpawn packs the lamps, substations and so on
        /// hanging on it into minified items on the floor; WillReplace leaves them in place for
        /// <see cref="RemoveOrphanedAttachments"/> to clear the ones really left without a wall. WillReplace also leaves
        /// no resources and marks no roof collapse.
        /// </summary>
        public static void RemoveEdifice(Map map, IntVec3 cell)
        {
            Building edifice = cell.GetEdifice(map);
            if (edifice != null) AllowingIndestructibleDestroy(() => edifice.Destroy(DestroyMode.WillReplace));
        }

        /// <summary>執行期間允許拆掉不可破壞的東西（設施牆等）。Runs action with indestructible things (facility walls, …) destroyable.</summary>
        public static void AllowingIndestructibleDestroy(System.Action action)
        {
            bool allow = Thing.allowDestroyNonDestroyable;
            Thing.allowDestroyNonDestroyable = true;
            try
            {
                action();
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = allow;
            }
        }

        /// <summary>
        /// 清掉失去支撐的壁掛物（燈、配電盤、攝影機……）：面向的那格已經沒有能掛東西的牆。
        /// 檢查點改寫牆面、檢修通道開口、閘門換牆都會拆牆，掛在上面的東西不會自己跟著消失，
        /// 所以整座設施生成完之後統一掃一遍。回傳清掉幾件。
        /// Removes wall attachments (lamps, substations, cameras, …) left with nothing to hang on: the cell they face
        /// no longer holds anything that supports attachments. Checkpoint wall rewrites, tunnel hatches and gates all
        /// take walls down without taking what hangs on them, so the whole facility is swept once when it's done.
        /// Returns how many were removed.
        /// </summary>
        public static int RemoveOrphanedAttachments(Map map)
        {
            List<Thing> orphans = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => t.def.building != null && t.def.building.isAttachment && GenConstruct.GetWallAttachedTo(t) == null)
                .ToList();

            AllowingIndestructibleDestroy(() =>
            {
                foreach (Thing orphan in orphans)
                {
                    if (!orphan.Destroyed) orphan.Destroy(DestroyMode.Vanish);
                }
            });
            return orphans.Count;
        }
    }
}
