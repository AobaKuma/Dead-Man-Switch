using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在軍火庫舊址的 SitePartDef 上：巡視設施的守墓人分隊怎麼編、依威脅點數放幾架，以及它們的巡邏規則。
    /// <see cref="GenStep_ArsenalSite"/> 生成地圖時讀它放分隊，<see cref="SitePartWorker_ArsenalSite"/> 用同一個算式
    /// 寫任務描述與世界地圖上的威脅標籤，所以兩邊的數量一定一致。
    /// 駭蜥（Boss）不在這裡：它要玩家用停機坪上的誘餌雷達召喚，牠死了洞庫的 Boss 擊殺中繼器才會放行。
    ///
    /// On the arsenal site's SitePartDef: how the gravekeeper squad walking the facility is made up, how many by threat
    /// points, and their patrol rules. <see cref="GenStep_ArsenalSite"/> reads it to place the squad and
    /// <see cref="SitePartWorker_ArsenalSite"/> uses the same formula for the quest text and the world-map threat label,
    /// so the two always agree. The tarbosaurus (the boss) is not here: the player calls it in with the decoy radar on the
    /// pad, and the bunkers' boss-kill relays only release once it dies.
    /// </summary>
    public class ModExtension_ArsenalSite : DefModExtension
    {
        /// <summary>分隊編制（同 <see cref="ModExtension_GravekeeperPatrol.squad"/>），排前面的優先當領隊。The roster, as in the vault; earlier entries lead first.</summary>
        public List<GravekeeperSquadMember> squad = new List<GravekeeperSquadMember>();

        /// <summary>殿後的種類：跟領隊保持較遠的距離。Rear-guard kinds that trail further behind.</summary>
        public List<PawnKindDef> overwatchKinds = new List<PawnKindDef>();

        /// <summary>每這麼多威脅點數一架，結果夾在 <see cref="guardCount"/> 與編制名額之間。One guard per this many threat points, clamped to <see cref="guardCount"/> and the roster's slots.</summary>
        public float pointsPerGuard = 1000f;

        public IntRange guardCount = new IntRange(1, 5);

        /// <summary>任務文字與威脅標籤上的名稱（單複數取自這個種類）。The kind whose label (singular / plural) names the guards in the quest text.</summary>
        public PawnKindDef labelKind;

        public float followRadius = 3f;
        public float overwatchFollowRadius = 8f;

        /// <summary>發現入侵者的視距（還要有視線）。How far a guard spots intruders (line of sight required too).</summary>
        public float sightRange = 26f;

        /// <summary>設施警戒值（0~1）到這個比例後，看到入侵者就直接開火。At this facility alert level (0~1) intruders in sight are fired on outright.</summary>
        public float engageAlertLevel = 0.6f;

        /// <summary>看到入侵者就開火，跳過盤查。Fire on sight, skipping the challenge.</summary>
        public bool engageOnSight;

        /// <summary>交戰後多久沒再看到入侵者就回去巡邏。How long without seeing an intruder before the squad resumes its patrol.</summary>
        public int calmDownTicks = 2500;

        /// <summary>在洞庫門前停留多久。How long the squad stands at each bunker entrance.</summary>
        public int postPauseTicks = 600;

        public string challengeMessageKey = "DMS_Gravekeeper_Challenge";
        public string engageMessageKey = "DMS_Gravekeeper_Engage";
        public string searchMessageKey = "DMS_Gravekeeper_TombOpened";

        private int RosterSlots => squad?.Where(m => m?.kind != null).Sum(m => Mathf.Max(0, m.count)) ?? 0;

        public int GuardCountFor(float threatPoints)
        {
            int count = Mathf.Clamp(Mathf.FloorToInt(threatPoints / Mathf.Max(1f, pointsPerGuard)), guardCount.min, guardCount.max);
            return Mathf.Min(count, RosterSlots);
        }

        public PawnKindDef LabelKind => labelKind ?? squad?.FirstOrDefault(m => m?.kind != null)?.kind;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (squad.NullOrEmpty())
            {
                yield return "ModExtension_ArsenalSite.squad is empty.";
            }
            else if (squad.Exists(m => m?.kind == null || m.count <= 0))
            {
                yield return "ModExtension_ArsenalSite.squad has an entry without a kind or with a count below 1.";
            }
            if (guardCount.min < 1 || guardCount.max < guardCount.min)
            {
                yield return "ModExtension_ArsenalSite.guardCount must be a range like 1~5.";
            }
        }
    }
}
