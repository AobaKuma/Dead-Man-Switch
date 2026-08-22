using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 StorytellerDef 上，讓該說書人偏好指定模組來源的任務、事件與突襲派系。
    /// 加權由 Patch_StorytellerBias 裡的三個 postfix 套用，沒掛這個擴充的說書人
    /// 行為完全不變。
    ///
    /// Attach to a StorytellerDef to make it favour quests, incidents and raid
    /// factions that come from the listed mods. The multipliers are applied by
    /// the postfixes in Patch_StorytellerBias; storytellers without this
    /// extension are untouched.
    /// </summary>
    public class ModExtension_StorytellerBias : DefModExtension
    {
        /// <summary>
        /// 要加權的內容來源，填 About.xml 的 packageId。大小寫不拘，Steam 版的
        /// _steam 後綴會自動一併比對。
        /// Content sources to favour, by packageId. Case-insensitive; the
        /// _steam suffix of Workshop copies is matched automatically.
        /// </summary>
        public List<string> packageIds = new List<string>();

        /// <summary>隨機任務池裡 rootSelectionWeight 的倍率。</summary>
        public float questSelectionWeightFactor = 1f;

        /// <summary>隨機事件池裡最終挑選權重的倍率。</summary>
        public float incidentChanceFactor = 1f;

        /// <summary>突襲派系挑選權重的倍率，只作用在 favoredFactions 上。</summary>
        public float raidCommonalityFactor = 1f;

        /// <summary>要提高突襲出現率的派系。</summary>
        public List<FactionDef> favoredFactions = new List<FactionDef>();

        [Unsaved(false)]
        private HashSet<string> packageIdSet;

        private HashSet<string> PackageIdSet
        {
            get
            {
                if (packageIdSet == null)
                {
                    packageIdSet = new HashSet<string>();
                    if (packageIds != null)
                    {
                        foreach (string packageId in packageIds)
                        {
                            if (packageId.NullOrEmpty())
                            {
                                continue;
                            }
                            // ModContentPack.PackageId 一律是小寫，Workshop 版另外帶 _steam 後綴。
                            // ModContentPack.PackageId is always lowercase; Workshop copies carry _steam.
                            string id = packageId.ToLowerInvariant();
                            packageIdSet.Add(id);
                            packageIdSet.Add(id + ModMetaData.SteamModPostfix);
                        }
                    }
                }
                return packageIdSet;
            }
        }

        public bool AppliesTo(Def def)
        {
            if (def == null || def.modContentPack == null)
            {
                return false;
            }
            return PackageIdSet.Contains(def.modContentPack.PackageId);
        }

        public bool Favors(FactionDef faction)
        {
            return faction != null && !favoredFactions.NullOrEmpty() && favoredFactions.Contains(faction);
        }
    }
}
