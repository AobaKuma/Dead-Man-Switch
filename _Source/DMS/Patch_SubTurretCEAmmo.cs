using System;
using System.Reflection;
using Fortified;
using HarmonyLib;
using Verse;

namespace DMS
{
    /// <summary>
    /// Shared mounted-gun defs also serve as equipped vehicle weapons, which need CE magazines.
    /// A sub-turret instead consumes inventory ammo through FortifiedCE. Remove the magazine
    /// only from that weapon instance, including instances restored from existing saves.
    /// </summary>
    [HarmonyPatch(typeof(SubTurret), "UpdateGunVerbs")]
    public static class Patch_SubTurretCEAmmo
    {
        // CE is optional; keep its types out of DMS's assembly references.
        private static readonly Type AmmoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        private static readonly Type ProjectileVerbType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
        private static readonly FieldInfo CachedAmmo = ProjectileVerbType == null
            ? null : AccessTools.Field(ProjectileVerbType, "compAmmo");

        public static void Prefix(SubTurret __instance)
        {
            ThingWithComps weapon = __instance.turret as ThingWithComps;
            if (AmmoUserType == null || weapon?.def?.defName == null
                || !weapon.def.defName.StartsWith("DMS_", StringComparison.Ordinal)
                || weapon.TryGetComp<CompTurretProjectile>()?.GetType().FullName
                    != "FortifiedCE.CompTurretProjectileCE")
            {
                return;
            }

            for (int i = weapon.AllComps.Count - 1; i >= 0; i--)
            {
                if (AmmoUserType.IsInstanceOfType(weapon.AllComps[i]))
                {
                    weapon.AllComps.RemoveAt(i);
                }
            }

            // CE can cache the magazine while constructing/loading verbs. Clear that reference
            // before Fortified binds the verbs to the pawn and initializes inventory feeding.
            if (CachedAmmo != null && weapon.TryGetComp<CompEquippable>() is CompEquippable equippable)
            {
                foreach (Verb verb in equippable.AllVerbs)
                {
                    if (ProjectileVerbType.IsInstanceOfType(verb))
                    {
                        CachedAmmo.SetValue(verb, null);
                    }
                }
            }
        }
    }
}
