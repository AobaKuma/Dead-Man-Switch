using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 模組選項。存在 Config/Mod_&lt;資料夾&gt;_DMS_Mod.xml。
    /// Mod options, saved to Config/Mod_&lt;folder&gt;_DMS_Mod.xml.
    /// </summary>
    public class DMS_Settings : ModSettings
    {
        // 人工肌肉是否會腐爛(見 Patch_CompRottable_Active)。預設保留原本的腐爛行為。
        // Whether Artifuscle rots (see Patch_CompRottable_Active). Defaults to the original rotting behaviour.
        public bool artifuscleRots = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref artifuscleRots, "artifuscleRots", true);
        }
    }

    public class DMS_Mod : Mod
    {
        public static DMS_Settings Settings;

        public DMS_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DMS_Settings>();
        }

        public override string SettingsCategory()
        {
            return Content.Name;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("DMS_Settings_ArtifuscleRots".Translate(), ref Settings.artifuscleRots, "DMS_Settings_ArtifuscleRotsDesc".Translate());
            listing.End();
        }
    }
}
