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

        // 情境音樂（見 GameComponent_DMSMusic）。只影響本機播放：遊戲端的狀態照常計算，所以 Multiplayer 各端可以各自開關。
        // Situational music (see GameComponent_DMSMusic). Local playback only: the game-side state is computed either way,
        // so Multiplayer clients may each choose.
        public bool situationalMusic = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref artifuscleRots, "artifuscleRots", true);
            Scribe_Values.Look(ref situationalMusic, "situationalMusic", true);
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
            listing.CheckboxLabeled("DMS_Settings_SituationalMusic".Translate(), ref Settings.situationalMusic, "DMS_Settings_SituationalMusicDesc".Translate());
            listing.End();
        }
    }
}
