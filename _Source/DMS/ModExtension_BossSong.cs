using Verse;
using System.Collections.Generic;

namespace DMS
{
    /// <summary>
    /// 掛在 BossgroupDef 上的 Boss 曲變奏清單。召喚時選一首記在 <see cref="QuestPart_BossgroupArrivesWithMusic.song"/>，
    /// 供情境音樂的 Boss 選曲使用；不會強制播放。沒有設定或清單為空時回傳 null，改用通用 Boss 曲。
    ///
    /// Boss-song variants on a BossgroupDef. One is picked at summoning and kept in
    /// <see cref="QuestPart_BossgroupArrivesWithMusic.song"/> for situational music's boss selection; it is never
    /// force-played. Null when unset or empty, which falls back to the generic boss music.
    /// </summary>
    public class ModExtension_BossSong : DefModExtension
    {
        public List<SongDef> songDefs = new List<SongDef>();

        public SongDef GetSong()
        {
            return songDefs.NullOrEmpty() ? null : songDefs.RandomElement();
        }
    }
}