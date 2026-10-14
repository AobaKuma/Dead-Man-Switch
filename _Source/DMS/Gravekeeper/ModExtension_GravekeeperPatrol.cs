using System.Collections.Generic;
using Verse;

namespace DMS
{
    /// <summary>守墓人分隊裡的一種成員與數量。One kind of gravekeeper in a squad, and how many of it.</summary>
    public class GravekeeperSquadMember
    {
        public PawnKindDef kind;
        public int count = 1;
    }

    /// <summary>
    /// 掛在 StructureLayoutDef 上：這座地下設施要不要放守墓人巡邏隊、放幾隊、怎麼編，以及交戰規則的門檻。
    /// 巡邏隊由 <see cref="GravekeeperPatrolPlacement"/> 在設施生成的最後一步放下，由
    /// <see cref="LordJob_GravekeeperPatrol"/> 指揮。
    ///
    /// On a StructureLayoutDef: whether this underground facility gets gravekeeper patrols, how many squads,
    /// how each is made up, and the thresholds of their rules of engagement. The squads are placed by
    /// <see cref="GravekeeperPatrolPlacement"/> as the facility's last spawning step and commanded by
    /// <see cref="LordJob_GravekeeperPatrol"/>.
    /// </summary>
    public class ModExtension_GravekeeperPatrol : DefModExtension
    {
        /// <summary>一隊的編制，依序生成；第一個能當領隊的成員帶隊。A squad's make-up, spawned in order; the first eligible member leads.</summary>
        public List<GravekeeperSquadMember> squad = new List<GravekeeperSquadMember>();

        /// <summary>隊數；各隊從路線上錯開的位置出發。Number of squads; each starts from a staggered point on the route.</summary>
        public IntRange squadCount = IntRange.One;

        /// <summary>這座設施放巡邏隊的機率。Chance this facility gets patrols at all.</summary>
        public float chance = 1f;

        /// <summary>據點威脅點數低於這個值就不放（沒有據點、不帶點數時不擋）。No patrols below these site threat points (not checked when the map has none).</summary>
        public float minThreatPoints;

        /// <summary>殿後的種類：跟領隊保持較遠的距離，停下時就地監看。Rear-guard kinds: they trail further behind and watch from where they stop.</summary>
        public List<PawnKindDef> overwatchKinds = new List<PawnKindDef>();

        public float followRadius = 3f;

        public float overwatchFollowRadius = 8f;

        /// <summary>發現入侵者的視距（還要有視線）。How far a gravekeeper spots intruders (line of sight required too).</summary>
        public float sightRange = 26f;

        /// <summary>設施警戒值（0~1）到這個比例後，看到入侵者就直接開火。At this facility alert level (0~1) intruders in sight are fired on outright.</summary>
        public float engageAlertLevel = 0.6f;

        /// <summary>交戰後多久沒再看到入侵者就回到巡邏。How long without seeing an intruder before an engaged squad resumes its patrol.</summary>
        public int calmDownTicks = 2500;

        /// <summary>在獎勵房門前停留多久。How long the squad stands at the treasury door.</summary>
        public int tombPauseTicks = 900;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (squad.NullOrEmpty())
            {
                yield return "ModExtension_GravekeeperPatrol.squad is empty.";
            }
            else if (squad.Exists(m => m?.kind == null || m.count <= 0))
            {
                yield return "ModExtension_GravekeeperPatrol.squad has an entry without a kind or with a count below 1.";
            }
        }
    }
}
