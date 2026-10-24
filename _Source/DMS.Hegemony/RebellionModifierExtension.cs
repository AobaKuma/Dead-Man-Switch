using System.Collections.Generic;
using Verse;

namespace DMS.Hegemony
{
    /// <summary>
    /// 掛在 MemeDef 上:統治派系的主要意識形態帶有這個 meme 時,Hegemony 的聚落反叛機率乘上 chanceFactor。
    /// 只影響一般反叛,不影響奴隸起義;多個 meme 都有時取最低的係數,不疊乘。
    /// On a MemeDef: when the ruling faction's primary ideoligion has this meme, Hegemony's settlement rebellion chance is
    /// multiplied by chanceFactor. Ordinary rebellions only, not slave uprisings; with several such memes the lowest
    /// factor applies rather than multiplying them together.
    /// </summary>
    public class RebellionModifierExtension : DefModExtension
    {
        public float chanceFactor = 1f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (chanceFactor < 0f || chanceFactor > 1f)
            {
                yield return "RebellionModifierExtension chanceFactor must be between 0 and 1.";
            }
        }
    }
}
