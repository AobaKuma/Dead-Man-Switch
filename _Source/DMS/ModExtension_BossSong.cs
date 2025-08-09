using Verse;
using System.Collections.Generic;

namespace DMS
{
    public class ModExtension_BossSong : DefModExtension
    {
        public List<SongDef> songDefs = new List<SongDef>();

        public SongDef GetSong()
        {
            return songDefs.RandomElement();
        }
    }
}