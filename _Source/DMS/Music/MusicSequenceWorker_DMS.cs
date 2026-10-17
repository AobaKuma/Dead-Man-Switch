using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DMS
{
    /// <summary>
    /// DMS 序列的 worker。
    /// <list type="bullet">
    /// <item>選曲：依遭遇標記、遭遇指定曲與地表／地下情境選曲（<see cref="DMSMusicSongChoice"/>），只在開歌與循環時選，
    /// 同一遭遇不會中途換歌；輪替起點用與遊戲同步無關的亂數。</item>
    /// <item>提前結束：換了地圖或 DMS 整個讓位時，不等 <c>minTimeToPlay</c> 直接淡出（<see cref="DMSMusicSequenceRules"/>）。</item>
    /// <item>限時：設了 <see cref="ModExtension_DMSMusicSequence.maxSongSeconds"/> 的序列，自己的曲目播到時限就淡出。</item>
    /// </list>
    /// The DMS sequence worker. Songs are chosen by encounter tag, encounter song and surface/underground context
    /// (<see cref="DMSMusicSongChoice"/>), only when a song starts or loops, so an encounter never switches mid-song; the
    /// rotation starts from randomness unrelated to game sync. A map change or a full DMS yield fades out at once instead
    /// of waiting for <c>minTimeToPlay</c> (<see cref="DMSMusicSequenceRules"/>). Sequences with
    /// <see cref="ModExtension_DMSMusicSequence.maxSongSeconds"/> fade out when their own song reaches the limit.
    /// </summary>
    public class MusicSequenceWorker_DMS : MusicSequenceWorker
    {
        // 純播放用途，不能動到 Verse.Rand（Multiplayer 同步）。Playback only; must not touch Verse.Rand (Multiplayer sync).
        private static readonly Random rotationSource = new Random();

        private ModExtension_DMSMusicSequence ext;
        private bool extResolved;
        private bool started;
        private Map startMap;
        private int rotation = -1;
        private SongDef selected;

        private ModExtension_DMSMusicSequence Ext
        {
            get
            {
                if (!extResolved)
                {
                    ext = def?.GetModExtension<ModExtension_DMSMusicSequence>();
                    extResolved = true;
                }
                return ext;
            }
        }

        /// <summary>曲目存在且音檔已載入。缺檔的 SongDef 沒有 clip，原版播放時會拋例外。The song exists and its clip loaded; a SongDef with a missing file has no clip and vanilla would throw playing it.</summary>
        public static bool Playable(SongDef song)
        {
            return song?.clip != null;
        }

        /// <summary>序列至少有一首可播放的基本曲目（不含只在標記成立時才用的變奏）。The sequence has at least one playable base song (variants used only under a tag do not count).</summary>
        public static bool HasPlayableSong(MusicSequenceDef sequence)
        {
            return sequence != null && FirstPlayable(sequence, sequence.GetModExtension<ModExtension_DMSMusicSequence>()) != null;
        }

        public override bool ShouldEnd()
        {
            GameComponent_DMSMusic music = GameComponent_DMSMusic.Instance;
            ModExtension_DMSMusicSequence data = Ext;
            if (music != null && data != null)
            {
                if (!started)
                {
                    // 第一次呼叫在原版 StartSequence() 內，此時狀態正是這個序列的狀態。The first call comes from vanilla's StartSequence(), while the state is this sequence's own.
                    if (music.State == data.state)
                    {
                        started = true;
                        startMap = music.StateMap;
                    }
                }
                else if (DMSMusicSequenceRules.EndsEarly(data.state, music.State, music.StateMap == startMap))
                {
                    return true;
                }
                if (data.maxSongSeconds > 0f && selected != null && musicManager.CurrentSong == selected && musicManager.SongTime >= data.maxSongSeconds)
                {
                    return true;
                }
            }
            return base.ShouldEnd();
        }

        public override SongDef SelectSong()
        {
            if (rotation < 0)
            {
                rotation = rotationSource.Next(1 << 16);
            }
            ModExtension_DMSMusicSequence data = Ext;
            GameComponent_DMSMusic music = GameComponent_DMSMusic.Instance;
            DMSMusicFacts facts = music?.Facts ?? default;
            SongDef encounterSong = data?.useEncounterSong == true ? music?.EncounterSong : null;

            selected = DMSMusicSongChoice.Pick(facts.songTags, data?.VariantPairs, encounterSong, data?.facilitySongs, def.songs,
                facts.context == DMSMusicContext.Facility, rotation + timesLooped, Playable)
                ?? FirstPlayable(def, data)
                ?? def.song
                ?? def.songs?.FirstOrDefault();
            return selected;
        }

        private static SongDef FirstPlayable(MusicSequenceDef sequence, ModExtension_DMSMusicSequence data)
        {
            if (Playable(sequence.song))
            {
                return sequence.song;
            }
            SongDef song = FirstPlayable(sequence.songs) ?? FirstPlayable(data?.facilitySongs);
            return song;
        }

        private static SongDef FirstPlayable(List<SongDef> songs)
        {
            if (songs == null)
            {
                return null;
            }
            for (int i = 0; i < songs.Count; i++)
            {
                if (Playable(songs[i]))
                {
                    return songs[i];
                }
            }
            return null;
        }
    }
}
