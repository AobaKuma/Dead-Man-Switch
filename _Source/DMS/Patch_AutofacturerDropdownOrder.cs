using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 原版下拉選單依 Def 載入順序加入元素、第一個當預設圖示（不看 uiOrder）。CE 資料夾比 1.7 先載入，
    /// CE 的彈藥打印機會搶到 DMS_Autofacturers 的預設位置；這裡把該選單依 uiOrder 重排，讓自動磨床不論開不開 CE 都排第一。
    /// Vanilla dropdowns add entries in def load order and show the first one (uiOrder is ignored). 1.7/CE loads
    /// before 1.7, so CE's ammo printer would take the DMS_Autofacturers default; this re-sorts that dropdown by
    /// uiOrder so the auto mill stays first with or without CE.
    /// </summary>
    [HarmonyPatch(typeof(DesignationCategoryDef), "ResolveDesignators")]
    public static class Patch_AutofacturerDropdownOrder
    {
        private static readonly AccessTools.FieldRef<DesignationCategoryDef, List<Designator>> resolvedDesignators =
            AccessTools.FieldRefAccess<DesignationCategoryDef, List<Designator>>("resolvedDesignators");

        [HarmonyPostfix]
        public static void Postfix(DesignationCategoryDef __instance)
        {
            DesignatorDropdownGroupDef group = DefDatabase<DesignatorDropdownGroupDef>.GetNamedSilentFail("DMS_Autofacturers");
            if (group == null) return;
            foreach (Designator designator in resolvedDesignators(__instance))
            {
                if (!(designator is Designator_Dropdown dropdown) || dropdown.Elements.Count < 2) continue;
                if (!(dropdown.Elements[0] is Designator_Build first) || first.PlacingDef.designatorDropdown != group) continue;

                List<Designator> sorted = dropdown.Elements.OrderBy(d => (d as Designator_Build)?.PlacingDef.uiOrder ?? float.MaxValue).ToList();
                dropdown.Elements.Clear();
                foreach (Designator element in sorted)
                {
                    dropdown.Add(element);
                }
                dropdown.SetActiveDesignator(sorted[0], explicitySet: false);
                dropdown.Order = sorted.Min(d => (d as Designator_Build)?.PlacingDef.uiOrder ?? float.MaxValue);
            }
        }
    }
}
