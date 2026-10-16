using System.Collections.Generic;
using Verse;

namespace DMS
{
    /// <summary>
    /// 掛在 DMS 的 <c>MusicSequenceDef</c> 上：這個序列服務哪個狀態，以及選曲資料。一般曲庫用原版的 <c>songs</c>。
    /// On a DMS <c>MusicSequenceDef</c>: which state the sequence serves, plus the song-selection data. The general pool
    /// is vanilla's <c>songs</c>.
    /// </summary>
    public class ModExtension_DMSMusicSequence : DefModExtension
    {
        public DMSMusicState state;

        /// <summary>地下設施改用的曲庫；沒有可播放曲目時退回一般曲庫。The pool used in underground facilities; falls back to the general pool when none can play.</summary>
        public List<SongDef> facilitySongs;

        /// <summary>依序檢查的遭遇變奏（先符合者優先）。Encounter variants, checked in order (first match wins).</summary>
        public List<DMSMusicSongVariant> variants;

        /// <summary>優先使用遭遇指定的曲目（Boss 召喚時選定的變奏）。Prefer the encounter's own song (the variant picked when the boss was summoned).</summary>
        public bool useEncounterSong;

        /// <summary>
        /// 單曲最多播放幾秒（0 為不限制），到時淡出結束序列。用在餘波：正式短曲完成前以長曲暫代時，只播開頭一段。
        /// The longest a song may play, in seconds (0 for no limit); the sequence fades out after that. Used by the
        /// aftermath so a long stand-in track only plays its opening until the real short cue exists.
        /// </summary>
        public float maxSongSeconds;

        /// <summary>曲目為暫代（正式資產尚未完成），只顯示在開發者面板。The songs are stand-ins pending the real assets; shown on the developer overlay only.</summary>
        public bool placeholder;

        private List<KeyValuePair<DMSMusicSongTag, SongDef>> variantPairs;

        public IReadOnlyList<KeyValuePair<DMSMusicSongTag, SongDef>> VariantPairs
        {
            get
            {
                if (variantPairs == null)
                {
                    variantPairs = new List<KeyValuePair<DMSMusicSongTag, SongDef>>();
                    if (variants != null)
                    {
                        foreach (DMSMusicSongVariant variant in variants)
                        {
                            variantPairs.Add(new KeyValuePair<DMSMusicSongTag, SongDef>(variant.tag, variant.song));
                        }
                    }
                }
                return variantPairs;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (state == DMSMusicState.None)
            {
                yield return "ModExtension_DMSMusicSequence.state must name a DMS music state.";
            }
            if (variants != null)
            {
                foreach (DMSMusicSongVariant variant in variants)
                {
                    if (variant?.song == null || variant.tag == DMSMusicSongTag.None)
                    {
                        yield return "ModExtension_DMSMusicSequence.variants entries need both a tag and a song.";
                    }
                }
            }
        }
    }

    /// <summary>遭遇標記成立時改用的曲目。The song used when an encounter tag is present.</summary>
    public class DMSMusicSongVariant
    {
        public DMSMusicSongTag tag;
        public SongDef song;
    }
}
