using System;

namespace HazardTimer.Replay
{
    /// <summary>
    /// リプレイ 1 ファイル。ファイル名から分かることだけを持つ。
    /// </summary>
    /// <remarks>
    /// BeatLeader はリプレイを<b>1 プレイごと</b>に残す。曲を途中で抜けたものも
    /// <c>exit-</c> という接頭辞付きで保存される。
    /// </remarks>
    public readonly struct ReplayFileInfo
    {
        public readonly string Path;

        /// <summary>最後まで通したプレイなら true。<c>exit-</c> 付きは false。</summary>
        public readonly bool IsCompleted;

        /// <summary>ファイル名末尾の UNIX 時刻。新しいものを選ぶのに使う。</summary>
        public readonly long Timestamp;

        public ReplayFileInfo(string path, bool isCompleted, long timestamp)
        {
            Path = path;
            IsCompleted = isCompleted;
            Timestamp = timestamp;
        }

        public DateTime TimestampUtc => DateTimeOffset.FromUnixTimeSeconds(Timestamp).UtcDateTime;
    }
}
