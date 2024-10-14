using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static Unity.Burst.Intrinsics.X86.Avx;

namespace DMS
{
    //機械體控制延伸，但這個不限於同個機械師的機體。
    public class CompSubRelay : ThingComp
    {
        public float SquaredDistance
        {
            get
            {
                return cacheDistance != 0 ? cacheDistance : GetCacheDistance();
            }
        }
        private float cacheDistance = 0;
        private float GetCacheDistance()
        {
            cacheDistance = Mathf.Pow(CurrentRadius, 2);
            return cacheDistance;
        }
        private Pawn Pawn => GetPawn();
        public bool IsActive => isActive;
        private bool isActive = false;
        public override void CompTick()
        {
            base.CompTick();
            if (parent is Building b && parent.Spawned)
            {
                isActive = true;
                if (parent.Faction != Faction.OfPlayer) isActive = false;
                if (b.TryGetComp<CompPowerTrader>(out var p) && !p.PowerOn) isActive = false;
                if(b.IsBrokenDown()) isActive = false;
                if(!b.IsWorking()) isActive = false;
            }
        }
        private Pawn GetPawn()
        {
            if (this.parent is Pawn p) return p; //本體
            //if (this.parent is ThingWithComps t && t.TryGetComp<CompEquippable>(out var e)) return e.ParentHolder as Pawn ?? null; //腰帶
            if (this.parent is Apparel a) return a.Wearer ?? null;//衣服
            return null;
        }
        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();
            if (Pawn.Spawned && Pawn != null && Pawn.Map == Find.CurrentMap && Pawn.Drafted)
            {
                GenDraw.DrawRadiusRing(Pawn.Position, this.CurrentRadius, Color.cyan);
            }
        }
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (IsActive) GenDraw.DrawRadiusRing(Pawn.Position, this.CurrentRadius, Color.cyan);
        }
        public float CurrentRadius => Props.relayRange;
        public CompProperties_SubRelay Props => (CompProperties_SubRelay)this.props;

    }

    public class CompProperties_SubRelay : CompProperties
    {
        public float relayRange = 10f;
        public CompProperties_SubRelay() => this.compClass = typeof(CompSubRelay);
    }
}