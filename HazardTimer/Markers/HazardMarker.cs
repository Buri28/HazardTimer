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

        /// <summary>
        /// カウントダウンに使うかどうかの指定。既定は自動選択。
        /// </summary>
        /// <remarks>
        /// 近接したマーカーは 1 つの危険地点として扱い、その中の 1 つだけを使う。
        /// 自動で選んだ結果が意図と違うときに、利用者が上書きするためのもの。
        /// </remarks>
        [JsonProperty("state")]
        public MarkerState State { get; set; } = MarkerState.Auto;

        /// <summary>
        /// 利用者が手を入れたか。
        /// </summary>
        /// <remarks>
        /// <see cref="State"/> だけでは「取り込みが決めた指定」と「利用者が決めた指定」を
        /// 区別できない。ミス地点は取り込みが必ず On か Off を付けるため、
        /// 状態で判断すると再取り込みで作り直せなくなり、古い集計が残り続ける。
        /// </remarks>
        [JsonProperty("userTouched")]
        public bool UserTouched { get; set; }

        /// <summary>
        /// カウントダウンの対象か。<see cref="BeatmapMarkerSet.RecomputeActive"/> が決めるので保存しない。
        /// </summary>
        [JsonIgnore]
        public bool IsActive { get; internal set; }

        /// <summary>
        /// 表示に使う名前。空なら種別から決まる既定の文字列（WALL / FAIL / MARK）を使う。
        /// </summary>
        /// <remarks>
        /// カウンターは TextMeshPro の既定フォントで描くため、日本語などの
        /// グリフを持たない文字は表示できず崩れる。英数字を推奨。
        /// </remarks>
        [JsonProperty("label")]
        public string? Label { get; set; }

        /// <summary>実際に表示される文字列。</summary>
        [JsonIgnore]
        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(Label) ? DefaultLabelFor(Source) : Label!;

        public static string DefaultLabelFor(MarkerSource source) => source switch
        {
            MarkerSource.Fail => "FAIL",
            MarkerSource.Manual => "MARK",
            MarkerSource.Miss => "MISS",
            _ => "WALL",
        };

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
