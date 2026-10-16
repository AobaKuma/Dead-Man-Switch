using RimWorld.Planet;
using Verse;

namespace DMS
{
    /// <summary>
    /// 由存檔裡本來就有的資料推導地圖情境：地下設施看 <c>Map.generatorDef</c>，地表站點看 <c>Site.parts</c>。
    /// 不寫入任何資料，所以舊存檔不需要補建；沒有標記的地圖回傳 <see cref="DMSMusicContext.None"/>，交還原版。
    ///
    /// Derives a map's context from data saves already hold: <c>Map.generatorDef</c> for underground facilities,
    /// <c>Site.parts</c> for surface sites. Nothing is written, so old saves need no backfill; unmarked maps return
    /// <see cref="DMSMusicContext.None"/> and stay vanilla.
    /// </summary>
    public static class DMSMusicContextUtility
    {
        public static DMSMusicContext ContextFor(Map map)
        {
            if (map == null)
            {
                return DMSMusicContext.None;
            }

            ModExtension_DMSMusicContext generatorExt = map.generatorDef?.GetModExtension<ModExtension_DMSMusicContext>();
            if (generatorExt != null)
            {
                return generatorExt.context;
            }

            if (!(map.Parent is Site site) || site.parts == null)
            {
                return DMSMusicContext.None;
            }

            // 以優先值、再以情境比較，與 parts 的順序無關。Compared by priority then context, independent of part order.
            ModExtension_DMSMusicContext best = null;
            for (int i = 0; i < site.parts.Count; i++)
            {
                ModExtension_DMSMusicContext ext = site.parts[i]?.def?.GetModExtension<ModExtension_DMSMusicContext>();
                if (ext == null)
                {
                    continue;
                }
                if (best == null || ext.priority > best.priority || (ext.priority == best.priority && ext.context > best.context))
                {
                    best = ext;
                }
            }
            return best?.context ?? DMSMusicContext.None;
        }
    }
}
