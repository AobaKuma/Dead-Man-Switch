using Verse;
using DMS;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using System;
using static RimWorld.MechClusterSketch;
using static RimWorld.PsychicRitualRoleDef;

public static partial class CheckUtility
{
    public static bool IsMech(Pawn pawn)
    {
        bool flag1 = pawn.GetComp<CompOverseerSubject>() != null;
        bool flag2 = pawn.Faction == Faction.OfPlayer;
        return flag1 && flag2;
    }
    public static bool MechanitorCheck(Map map, out Pawn mechanitor)
    {
        mechanitor = null;
        if (map == null) return false;
        List<Pawn> colonists = map.mapPawns.FreeColonists;
        for (int i = 0; i < colonists.Count; i++)
        {
            if (MechanitorUtility.IsMechanitor(colonists[i]))
            {
                mechanitor = colonists[i];
                return true;
            }
        }
        return false;
    }
    public static bool IsMechUseable(Thing mech, ThingWithComps weapon)
    {
        if (IsMechUseable(mech.def.GetModExtension<MechWeaponExtension>(), weapon))
        {
            return true;
        }
        else if (UseableInRuntime(mech, weapon))//遊戲中透過改造所定義的可用
        {
            return true;
        }
        return false;
    }
    public static bool IsMechUseable(MechWeaponExtension extension, ThingWithComps thing)
    {
        if (extension == null)
        {
            return false;
        }
        if (thing == null)
        {
            return false;
        }
        if (BypassedUseable(extension, thing.def.defName)) return true; //指定可用
        if (!InTechLevel(extension, thing)) return false;//科技等級可用

        if (extension.EnableWeaponFilter)
        {
            if (!thing.def.weaponTags.ContainsAny(t => extension.UsableWeaponTags.Contains(t)))
            {
                return false;
            }
        } 
        else
        {
            HeavyEquippableExtension heavyEquippableExtension = thing.def.GetModExtension<HeavyEquippableExtension>();
            if (extension == null && extension.EnableWeaponFilter) return false; 
        }
        return true;
    }
    public static bool UseableInRuntime(Thing mech, ThingWithComps weapon)//透過改造或別的因素所以可以用的狀況
    {
        HeavyEquippableExtension extension = weapon.def.GetModExtension<HeavyEquippableExtension>();
        if (extension ==null ||mech.def.GetModExtension<MechWeaponExtension>().EnableWeaponFilter) return false;//使用過濾器的狀況下就不啟用這個了
        return extension.CanEquippedBy(mech as Pawn);
    }
    public static bool HasAnyHediffOf(Pawn pawn, List<HediffDef> hediffDefs)
    {
        if (pawn is null)
        {
            throw new ArgumentNullException(nameof(pawn));
        }

        if (hediffDefs.NullOrEmpty()) return false;
        foreach (var item in hediffDefs)
        {
            if (pawn.health.hediffSet.HasHediff(item)) return true;
        }
        return false;
    }
    public static bool HasAnyApparelOf(Pawn pawn, List<ThingDef> apparels)
    {
        if (pawn is null)
        {
            throw new ArgumentNullException(nameof(pawn));
        }

        if (apparels.NullOrEmpty()) return false; 
        foreach (ThingDef apparel in apparels)//裝備上可用
        {
            if (CheckUtility.WearsApparel(pawn, apparel)) return true;
        }
        return false;
    }
    public static bool Wearable(MechWeaponExtension extension, ThingWithComps equipment)//為可用的科技等級。
    {
        foreach (ApparelLayerDef item in extension.acceptedLayers)
        {
            if (equipment.def.apparel.layers.Contains(item))
            {
                return true;
            }
        }
        return false;
    }
    public static bool InTechLevel(MechWeaponExtension extension, ThingWithComps thing)//為可用的科技等級。
    {
        if (!extension.EnableTechLevelFilter || (extension.EnableTechLevelFilter && extension.UsableTechLevels.NullOrEmpty())) return true;
        else return extension.UsableTechLevels.NotNullAndContains(thing.def.techLevel);
    }

    public static bool IsMannable(TurretMannableExtension extension, Building_Turret turret)
    {
        if (extension == null) return false;
        if (turret is Building_Turret && turret.GetComp<CompMannable>() == null) return false;
        if (extension.mannableByDefault) return true;
        return extension.BypassMannable.NotNullAndContains(turret.def.defName);
    }
    public static bool BypassedUseable(MechWeaponExtension extension, string defName)//白名單直接可用
    {
        if (extension.BypassUsableWeapons.NullOrEmpty()) return false;
        return extension.BypassUsableWeapons?.FirstOrDefault(p => p == defName) != null;
    }
    public static bool WearsApparel(Pawn pawn, ThingDef thingDef)
    {
        if (pawn.apparel?.WornApparel != null)
        {
            return (pawn.apparel.WornApparel.Where(e => e.def == thingDef).FirstOrDefault() != null);
        }
        return false;
    }
}
