using RimWorld;

namespace DMS
{
    /// <summary>
    /// 八個 DMS transition 共用的 worker：所屬狀態取自序列上的 <see cref="ModExtension_DMSMusicSequence"/>，
    /// 只在 <see cref="GameComponent_DMSMusic"/> 本幀選出的狀態正是它、而且序列有可播放曲目時成立。
    /// 不掃描任何東西也不修改狀態；暫停時原版可能每幀呼叫，所以必須是常數時間。
    ///
    /// The worker shared by the eight DMS transitions: its state comes from the sequence's
    /// <see cref="ModExtension_DMSMusicSequence"/>, and it holds only when <see cref="GameComponent_DMSMusic"/> picked
    /// exactly that state this frame and the sequence has a playable song. It scans nothing and changes nothing; vanilla
    /// may call it every frame while paused, so it must stay constant-time.
    /// </summary>
    public class MusicTransition_DMS : MusicTransition
    {
        private bool resolved;
        private DMSMusicState state;

        public DMSMusicState State
        {
            get
            {
                if (!resolved)
                {
                    state = def?.sequence?.GetModExtension<ModExtension_DMSMusicSequence>()?.state ?? DMSMusicState.None;
                    resolved = true;
                }
                return state;
            }
        }

        public override bool IsTransitionSatisfied()
        {
            if (!base.IsTransitionSatisfied() || State == DMSMusicState.None)
            {
                return false;
            }
            GameComponent_DMSMusic music = GameComponent_DMSMusic.Instance;
            return music != null && music.State == State && MusicSequenceWorker_DMS.HasPlayableSong(def.sequence);
        }
    }
}
