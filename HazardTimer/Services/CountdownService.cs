using HazardTimer.Markers;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>カウントダウン 1 行分の表示状態。</summary>
    public readonly struct CountdownEntry
    {
        public readonly MarkerSource Source;

        /// <summary>到達までの残り「実時間」秒。</summary>
        public readonly float RemainingSeconds;

        /// <summary>マーカーに付けられた表示名。</summary>
        public readonly string Label;

        public CountdownEntry(MarkerSource source, float remainingSeconds, string label)
        {
            Source = source;
            RemainingSeconds = remainingSeconds;
            Label = label;
        }
    }

    /// <summary>
    /// 現在の曲内時刻から、表示すべきカウントダウンを求める。
    /// 状態は持たず毎フレーム問い合わせる方式にして、
    /// 一時停止・巻き戻し・練習モードの開始位置を特別扱いしなくて済むようにしている。
    /// </summary>
    public class CountdownService
    {
        [Inject] private readonly HazardMarkerSession session = null!;
        [Inject] private readonly AudioTimeSyncController audioTimeSyncController = null!;

        /// <summary>
        /// 通常枠（壁・手動）のカウントダウン。到達順で次に来る 1 つだけを返す。
        /// </summary>
        /// <remarks>
        /// ソース優先度ではなく到達順で選ぶ。優先度で決めると、後ろの手動マーカーが
        /// 手前の壁マーカーの窓を食い、一番必要な直前数秒で警告が消えてしまう（2.3）。
        /// </remarks>
        public CountdownEntry? GetPrimary()
        {
            var set = session.Markers;
            if (set == null) return null;

            var songTime = audioTimeSyncController.songTime;
            var leadTime = PluginConfig.Instance.LeadTimeSeconds;

            HazardMarker? best = null;
            foreach (var marker in set.Markers)
            {
                if (marker.Source == MarkerSource.Fail) continue;
                // 近接した記録のうち、対象に選ばれた 1 つだけを見る
                if (!marker.IsActive) continue;
                // 表示しない種別は、選ばれていても飛ばす。ここで抜くのは、
                // 選び直し（RecomputeActive）に混ぜると設定を戻したときに
                // 元の指定へ復帰できなくなるため
                if (!PluginConfig.Instance.IsShown(marker.Source)) continue;
                if (marker.SongTime < songTime) continue;
                if (!IsWithinWindow(marker, songTime, leadTime)) break; // 昇順なのでこれ以降も窓の外

                if (best == null
                    || marker.SongTime < best.SongTime
                    // 同時到達のときだけ優先度で決める（手動 > 壁）
                    || (marker.SongTime == best.SongTime && marker.Priority > best.Priority))
                {
                    best = marker;
                }
            }

            return best == null ? (CountdownEntry?)null : ToEntry(best, songTime);
        }

        /// <summary>
        /// フェイル枠。1 譜面に 1 点しかないので通常枠と competing させず、常に別行で扱う。
        /// </summary>
        public CountdownEntry? GetFail()
        {
            if (!PluginConfig.Instance.IsShown(MarkerSource.Fail)) return null;

            var marker = session.Markers?.ActiveFail;
            if (marker == null) return null;

            var songTime = audioTimeSyncController.songTime;
            if (marker.SongTime < songTime) return null;
            if (!IsWithinWindow(marker, songTime, PluginConfig.Instance.LeadTimeSeconds)) return null;

            return ToEntry(marker, songTime);
        }

        private bool IsWithinWindow(HazardMarker marker, float songTime, float leadTime)
            => ToRealSeconds(marker.SongTime - songTime) <= leadTime;

        private CountdownEntry ToEntry(HazardMarker marker, float songTime)
            => new CountdownEntry(marker.Source,
                                  ToRealSeconds(marker.SongTime - songTime),
                                  marker.DisplayLabel);

        /// <summary>
        /// 譜面時間の差を実時間の秒に直す。スピード系モディファイアや練習モードの
        /// 速度変更でも、プレイヤーが体感する残り秒数と一致させるため。
        /// </summary>
        private float ToRealSeconds(float songTimeDelta)
        {
            var timeScale = audioTimeSyncController.timeScale;
            return timeScale > 0.0001f ? songTimeDelta / timeScale : songTimeDelta;
        }
    }
}
