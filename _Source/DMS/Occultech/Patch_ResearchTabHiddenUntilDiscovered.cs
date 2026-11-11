using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 <see cref="ResearchTabDef"/> 上的標記：分頁內任一專案被探明（<see cref="ResearchProjectDef.IsHidden"/> 為 false）
    /// 之前，整個分頁的研究樹都不顯示，改顯示原生的「尚未發現此研究」（與 Anomaly 分頁未解鎖時相同）。
    /// </summary>
    public class ModExtension_HideTabUntilDiscovered : DefModExtension
    {
    }

    /// <summary>
    /// 接在原生 <see cref="ResearchManager.TabInfoVisible"/>：研究視窗據此決定畫研究樹或「尚未發現此研究」。
    /// 只在原本可見時收窄，不寫入存檔的 tabInfoVisibility，移除本 mod 後不留痕跡。
    /// 任何失敗一律 fail-open（維持可見），避免整個分頁永久消失。
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.TabInfoVisible))]
    public static class Patch_ResearchManager_TabInfoVisible
    {
        // 分頁 -> 其專案清單；未掛標記的分頁對應 null。Def 實例固定，可安全快取。
        private static readonly Dictionary<ResearchTabDef, List<ResearchProjectDef>> tabProjects =
            new Dictionary<ResearchTabDef, List<ResearchProjectDef>>();

        private static bool errorLogged;

        [HarmonyPostfix]
        public static void Postfix(ResearchTabDef tab, ref bool __result)
        {
            if (__result && IsGated(tab))
            {
                __result = false;
            }
        }

        /// <summary>此分頁掛了標記，且底下所有專案都尚未探明。失敗時回傳 false（fail-open）。</summary>
        public static bool IsGated(ResearchTabDef tab)
        {
            if (tab == null)
            {
                return false;
            }
            try
            {
                List<ResearchProjectDef> projects = ProjectsFor(tab);
                if (projects == null)
                {
                    return false;
                }
                for (int i = 0; i < projects.Count; i++)
                {
                    if (!projects[i].IsHidden)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Log.Error($"[DMS] Research tab gating failed for {tab.defName}; tab left visible: {ex}");
                }
                return false;
            }
        }

        private static List<ResearchProjectDef> ProjectsFor(ResearchTabDef tab)
        {
            if (tabProjects.TryGetValue(tab, out List<ResearchProjectDef> cached))
            {
                return cached;
            }
            List<ResearchProjectDef> result = null;
            if (tab.HasModExtension<ModExtension_HideTabUntilDiscovered>())
            {
                result = new List<ResearchProjectDef>();
                foreach (ResearchProjectDef proj in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (proj.tab == tab)
                    {
                        result.Add(proj);
                    }
                }
                // 分頁裡沒有任何專案時不隱藏（避免空分頁變成永遠「尚未發現」）。
                if (result.Count == 0)
                {
                    result = null;
                }
            }
            tabProjects[tab] = result;
            return result;
        }
    }

    /// <summary>
    /// 研究視窗每次開啟都在 PostOpen 重建分頁按鈕清單；在此把仍未探明的標記分頁整個拿掉，連按鈕都不畫。
    /// 探明後下次開啟研究視窗就會出現。若目前分頁剛好是被拿掉的分頁，退回主分頁，
    /// 否則原生 CurTabRecord 會是 null，搜尋時直接 NRE。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.PostOpen))]
    public static class Patch_MainTabWindow_Research_PostOpen
    {
        // ResearchTabRecord 是 MainTabWindow_Research 的 private 巢狀類別，只能以反射取 def。
        private static readonly System.Reflection.FieldInfo recordDefField =
            AccessTools.Field(AccessTools.Inner(typeof(MainTabWindow_Research), "ResearchTabRecord"), "def");

        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Research __instance, System.Collections.IList ___tabs)
        {
            if (___tabs == null || recordDefField == null)
            {
                return;
            }
            try
            {
                bool removed = false;
                for (int i = ___tabs.Count - 1; i >= 0; i--)
                {
                    object record = ___tabs[i];
                    if (record != null && Patch_ResearchManager_TabInfoVisible.IsGated(recordDefField.GetValue(record) as ResearchTabDef))
                    {
                        ___tabs.RemoveAt(i);
                        removed = true;
                    }
                }
                if (removed && Patch_ResearchManager_TabInfoVisible.IsGated(__instance.CurTab))
                {
                    __instance.CurTab = ResearchTabDefOf.Main;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[DMS] Hiding undiscovered research tabs failed: {ex}", 0x0D5A7B01);
            }
        }
    }
}
