using HarmonyLib;
using RimWorld;
using System;
using Verse;
using VFECore;

namespace DMS
{
    [HarmonyPatch(
        typeof(EquipmentUtility),
        nameof(EquipmentUtility.CanEquip),
        new Type[] { typeof(Thing), typeof(Pawn), typeof(string), typeof(bool) },
        new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    internal static class Patch_EquipmentUtility_CanEquip
    {
        static void Postfix(ref bool __result, object __0, object __1, object __2, object __3)
        {
            if (__result != true) return;//因為其他原因的不可行(生物鎖之類的)
            if (!(__0 is Thing thing) || !(__1 is Pawn pawn)) return;//天知道拿了個不知道啥的鬼東西，反正也跳過。
            if (thing.TryGetComp<CompEquippable>() != null)
            {
                if (pawn is IWeaponUsable)
                {
                    //Log.Error("pawn is weapon usable");
                    if (CheckUtility.IsMechUseable(pawn, thing as ThingWithComps))
                    {
                        __result = true;
                    }
                    else
                    {
                        __2 = "CannotEquip_TooHeavy".Translate();
                        __result = false;
                    }
                }
                else if (thing.def.HasModExtension<HeavyEquippableExtension>())
                {
                    //Log.Error("pawn isn't weapon usable, and weapon is heavy");
                    if (thing.def.GetModExtension<HeavyEquippableExtension>().CanEquippedBy(pawn))
                    {
                        __result = true;
                    }
                    else
                    {
                        __2 = "CannotEquip_TooHeavy".Translate();
                        __result = false;
                    }
                }
            }
        }
    }
}
