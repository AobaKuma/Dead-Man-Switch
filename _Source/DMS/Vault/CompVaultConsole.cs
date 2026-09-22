using System.Collections.Generic;
using Fortified;
using RimWorld;
using Verse;

namespace DMS
{
    public class CompProperties_VaultConsole : CompProperties_Hackable
    {
        public CompProperties_VaultConsole()
        {
            compClass = typeof(CompVaultConsole);
        }
    }

    /// <summary>
    /// 獎勵房的安全控制台：駭入後放行所有連結到它的密封門。
    /// 門是 FFF 的 <see cref="Building_RollingDoor_AccessLink"/>（門禁鑰匙系統的「wanter」端），
    /// 這裡用駭入取代鑰匙卡，所以自己實作 <see cref="IAccessKeyActivatable"/>，
    /// 並且允許一座控制台連結多扇門，房間有幾扇門就封幾扇。
    ///
    /// The treasury's security console: hacking it releases every sealed door linked to it.
    /// The doors are FFF's <see cref="Building_RollingDoor_AccessLink"/> (the "wanter" end of the
    /// access-key system); hacking stands in for the key card, so this comp implements
    /// <see cref="IAccessKeyActivatable"/> itself and, unlike the stock comp, links to any number of
    /// doors: a room gets every one of its doors sealed.
    ///
    /// 控制台被拆毀而不是被駭入時也會放行，免得獎勵房永遠打不開。
    /// Destroying the console instead of hacking it releases the doors too, so the room can never
    /// become permanently sealed.
    /// </summary>
    public class CompVaultConsole : CompHackable, IAccessKeyActivatable
    {
        private List<Thing> linkedDoors = new List<Thing>();

        private bool released;

        public IReadOnlyList<Thing> LinkedDoors => linkedDoors;

        // ── IAccessKeyActivatable ──────────────────────────────────────────────

        public Thing ParentThing => parent;

        public Thing LinkedAccessWanter
        {
            get => linkedDoors.Count > 0 ? linkedDoors[0] : null;
            set => Link(value);
        }

        public bool AccessKeyActivated => released;

        public bool CanLinkWanter(Thing wanter)
        {
            return wanter != null && !wanter.Destroyed && wanter != parent && !released && !linkedDoors.Contains(wanter);
        }

        public void Notify_WanterLinked(Thing wanter)
        {
        }

        // ── 連結 / Linking ──────────────────────────────────────────────────────

        /// <summary>把一扇密封門掛到這座控制台底下。Puts a sealed door under this console's control.</summary>
        public bool Link(Thing door)
        {
            if (!CanLinkWanter(door)) return false;

            List<IAccessKeyWanter> wanters = AccessKeyLinkUtility.GetWanters(door);
            if (wanters.Count == 0) return false;

            linkedDoors.Add(door);
            for (int i = 0; i < wanters.Count; i++)
            {
                wanters[i].Notify_LinkedTo(this);
            }
            return true;
        }

        private void Release(Pawn pawn)
        {
            if (released) return;
            released = true;

            for (int i = 0; i < linkedDoors.Count; i++)
            {
                Thing door = linkedDoors[i];
                if (door == null || door.Destroyed) continue;

                List<IAccessKeyWanter> wanters = AccessKeyLinkUtility.GetWanters(door);
                for (int j = 0; j < wanters.Count; j++)
                {
                    wanters[j].Notify_AccessKeyUsed(this, pawn);
                }
            }
        }

        // ── CompHackable ────────────────────────────────────────────────────────

        protected override void OnHacked(Pawn hacker = null, bool suppressMessages = false)
        {
            base.OnHacked(hacker, suppressMessages);
            Release(hacker);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            Release(null);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref linkedDoors, "linkedDoors", LookMode.Reference);
            Scribe_Values.Look(ref released, "released", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                linkedDoors?.RemoveAll(t => t == null);
                linkedDoors ??= new List<Thing>();
            }
        }
    }
}
