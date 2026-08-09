using Newtonsoft.Json;

namespace HazardTimer.Markers
{
    /// <summary>
    /// 危険地点を表す 1 点。譜面時間軸上の秒で保持する。
    /// 実時間で持つと練習モードやスピード系モディファイアでずれるため、
    /// 表示側で速度倍率を割って実時間に直す。
    /// </summary>
    public class HazardMarker
    {
        /// <summary>譜面時間軸上の到達時刻（秒）。</summary>
        [JsonProperty("time")]
        public float SongTime { get; set; }

        /// <summary>入力ソース。</summary>
        [JsonProperty("source")]
        public MarkerSource Source { get; set; }

        /// <summary>
        /// 統合されたイベント数。1 なら単発。
        /// 将来、重大度として表示優先度や集計精度に使う余地を残す。
        /// </summary>
        [JsonProperty("hits")]
        public int HitCount { get; set; } = 1;

        /// <summary>
        /// リプレイからの取り込みで作られたマーカーなら true。
        /// </summary>
        /// <remarks>
        /// 壁マーカーはリプレイからも進入時刻で取れるため、実測との精度差は小さい。
        /// それでも区別を残すのは、フェイル地点が模擬値であることと、
        /// 「実際に自分が当たった」という事実の重みが違うため。
        /// 同じ地点に実測が入ったら、そちらの時刻で上書きする。
        /// </remarks>
        [JsonProperty("imported")]
        public bool Imported { get; set; }

        public HazardMarker() { }

        public HazardMarker(float songTime, MarkerSource source, bool imported = false)
        {
            SongTime = songTime;
            Source = source;
            Imported = imported;
        }

        /// <summary>
        /// 同時到達時の優先度。大きいほど優先。手動 &gt; 壁。
        /// フェイルは専用枠で常時表示するため、この比較には乗らない。
        /// </summary>
        [JsonIgnore]
        public int Priority => Source == MarkerSource.Manual ? 1 : 0;
    }
}
