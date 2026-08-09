using System.Collections.Generic;

namespace HazardTimer.Replay
{
    /// <summary>壁の中にいた 1 区間。</summary>
    public readonly struct ReplayObstacleInterval
    {
        /// <summary>進入時刻（秒）。</summary>
        public readonly float Start;

        /// <summary>退出時刻（秒）。</summary>
        public readonly float End;

        public ReplayObstacleInterval(float start, float end)
        {
            Start = start;
            End = end;
        }

        public float Duration => End - Start;
    }

    /// <summary>ノート 1 個分のイベント。エネルギー模擬に必要な項目だけ持つ。</summary>
    public readonly struct ReplayNoteEvent
    {
        public readonly float Time;

        /// <summary>0=good, 1=bad, 2=miss, 3=bomb。</summary>
        public readonly int EventType;

        /// <summary>チェーンの節（BurstSliderElement）か。減少量が桁違いに小さい。</summary>
        public readonly bool IsChainLink;

        public ReplayNoteEvent(float time, int eventType, bool isChainLink)
        {
            Time = time;
            EventType = eventType;
            IsChainLink = isChainLink;
        }
    }

    /// <summary>
    /// 1 つの <c>.bsor</c> から、危険地点の算出に必要な部分だけを取り出したもの。
    /// </summary>
    public class BsorReplay
    {
        /// <summary>壁の中にいた区間。時刻は曲内時刻（秒）。</summary>
        public List<ReplayObstacleInterval> ObstacleIntervals { get; } = new List<ReplayObstacleInterval>();

        /// <summary>ノートイベント。エネルギー模擬に使う。</summary>
        public List<ReplayNoteEvent> NoteEvents { get; } = new List<ReplayNoteEvent>();

        /// <summary>ゲームが記録したフェイル時刻（秒）。NoFail では常に未設定。</summary>
        public float? FailTime { get; set; }

        /// <summary>練習モードなどでの開始時刻（秒）。</summary>
        public float StartTime { get; set; }

        /// <summary>適用されていたモディファイア文字列（カンマ区切り）。</summary>
        public string Modifiers { get; set; } = string.Empty;
    }
}
