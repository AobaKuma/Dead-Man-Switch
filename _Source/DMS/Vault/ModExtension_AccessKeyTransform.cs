using System.Collections.Generic;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 <see cref="Building_AccessKeyTransformer"/> 類的 ThingDef 上，指定被門禁鑰匙解鎖後要換成哪個建築。
    /// Attached to a <see cref="Building_AccessKeyTransformer"/> ThingDef to say which building it
    /// turns into once an access key unlocks it.
    /// </summary>
    public class ModExtension_AccessKeyTransform : DefModExtension
    {
        /// <summary>解鎖後換成的建築，會沿用原本的位置與朝向。Spawned in place (same cell and rotation) on unlock.</summary>
        public ThingDef unlockedDef;

        /// <summary>
        /// 新建築若掛有 CompHackable，直接視為已駭入，鑰匙卡已經是一道門檻，不再要求玩家駭第二次。
        /// If the new building carries a CompHackable, mark it hacked outright; the key card was the gate.
        /// </summary>
        public bool markHacked = true;

        /// <summary>解鎖時在建築位置播放的音效，留空則不播。Played at the building on unlock; null = silent.</summary>
        public SoundDef unlockSound;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (unlockedDef == null)
            {
                yield return "ModExtension_AccessKeyTransform requires unlockedDef.";
            }
        }
    }
}
