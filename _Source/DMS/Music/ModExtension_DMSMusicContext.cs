using System.Collections.Generic;
using Verse;

namespace DMS
{
    /// <summary>
    /// 標記一個 DMS 地表站點（掛在 <c>SitePartDef</c>）或地下設施（掛在 <c>MapGeneratorDef</c>）的音樂情境。
    /// 情境只從這個標記推導：洞穴、口袋地圖、建築或陣營都不會讓地圖變成 DMS 地圖。
    ///
    /// Marks the music context of a DMS surface site (on a <c>SitePartDef</c>) or underground facility (on a
    /// <c>MapGeneratorDef</c>). The context comes from this marker only: caves, pocket maps, buildings or factions never
    /// make a map a DMS map.
    /// </summary>
    public class ModExtension_DMSMusicContext : DefModExtension
    {
        public DMSMusicContext context = DMSMusicContext.Installation;

        /// <summary>
        /// 同一個站點有多個帶標記的 site part 時，取優先值最高者（同值時地下設施優先）。
        /// When a site has several marked parts the highest priority wins (on a tie, Facility wins).
        /// </summary>
        public int priority;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (context == DMSMusicContext.None)
            {
                yield return "ModExtension_DMSMusicContext.context must be Installation or Facility.";
            }
        }
    }
}
