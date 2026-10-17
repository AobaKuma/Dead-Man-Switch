using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DMS
{
    public enum ServerHackRewardKind
    {
        Thing,
        Techprint,
        AccessKey
    }

    /// <summary>
    /// 伺服主機每完成一階段駭入時抽的一個獎勵選項。
    /// One reward option a server rolls each time a hack stage completes.
    /// </summary>
    public class ServerHackReward
    {
        public ServerHackRewardKind kind = ServerHackRewardKind.Thing;
        public float weight = 1f;

        /// <summary>kind == Thing 時的物品。The item when kind == Thing.</summary>
        public ThingDef thingDef;
        public IntRange count = IntRange.One;

        /// <summary>同一座建築最多給幾次，0 = 不限。Times this option may pay out per building; 0 = unlimited.</summary>
        public int maxPerBuilding;

        /// <summary>kind == AccessKey 時的密鑰與權重（count 當權重用）。Keys and weights when kind == AccessKey (count is the weight).</summary>
        public List<ThingDefCountClass> keyWeights;
    }

    public static class ServerHackRewardUtility
    {
        private static ModContentPack dmsContent;

        private static ModContentPack DmsContent
        {
            get
            {
                if (dmsContent == null)
                {
                    System.Reflection.Assembly asm = typeof(ServerHackRewardUtility).Assembly;
                    dmsContent = LoadedModManager.RunningModsListForReading
                        .FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(asm));
                }
                return dmsContent;
            }
        }

        /// <summary>
        /// 產生一件獎勵。Techprint 在無 Royalty 或抽不到專案時回傳 null，由呼叫端換下一項或退回 fallback。
        /// Makes one reward. Techprint yields null without Royalty or when no project qualifies; the caller
        /// then tries another option or falls back.
        /// </summary>
        public static Thing Make(ServerHackReward option)
        {
            switch (option.kind)
            {
                case ServerHackRewardKind.Techprint:
                    ResearchProjectDef proj = RandomTechprintProject();
                    return proj?.Techprint == null ? null : ThingMaker.MakeThing(proj.Techprint);

                case ServerHackRewardKind.AccessKey:
                    if (option.keyWeights.NullOrEmpty()) return null;
                    if (!option.keyWeights.Where(k => k.thingDef != null && k.count > 0)
                            .TryRandomElementByWeight(k => k.count, out ThingDefCountClass key))
                    {
                        return null;
                    }
                    return MakeStack(key.thingDef, IntRange.One);

                default:
                    return option.thingDef == null ? null : MakeStack(option.thingDef, option.count);
            }
        }

        public static Thing MakeStack(ThingDef def, IntRange count)
        {
            Thing t = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            t.stackCount = Mathf.Clamp(count.RandomInRange, 1, def.stackLimit);
            return t;
        }

        /// <summary>
        /// 先抽 DMS 自己的專案，抽不到才放寬到全部。條件同原版：未完成、藍圖未收滿。
        /// DMS projects first, then any mod. Same rule as vanilla: unfinished, techprints not complete.
        /// </summary>
        public static ResearchProjectDef RandomTechprintProject()
        {
            if (!ModsConfig.RoyaltyActive) return null;

            List<ResearchProjectDef> open = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(p => p.TechprintCount > 0 && !p.IsFinished && !p.TechprintRequirementMet && p.Techprint != null)
                .ToList();

            ModContentPack dms = DmsContent;
            if (dms != null && open.Where(p => p.modContentPack == dms).TryRandomElement(out ResearchProjectDef own))
            {
                return own;
            }
            return open.TryRandomElement(out ResearchProjectDef any) ? any : null;
        }
    }
}
