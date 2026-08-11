namespace HazardTimer.Markers
{
    /// <summary>
    /// マーカーの入力ソース。表示ロジックは共通で、同時到達時の優先度にだけ使う。
    /// </summary>
    public enum MarkerSource
    {
        /// <summary>壁への接触を実測して記録したもの。</summary>
        Wall = 0,

        /// <summary>体力ゼロになった地点。1譜面につき最大1点。</summary>
        Fail = 1,

        /// <summary>プレイヤーが分＋秒で指定したもの。</summary>
        Manual = 2,

        /// <summary>
        /// ノートを見逃した地点。リプレイからのみ取り込む。
        /// </summary>
        /// <remarks>
        /// 壁と違って数が多く、たまたま外しただけの箇所も混ざる。
        /// 複数のプレイで同じ箇所を落としているものほど、実際の難所である見込みが高い。
        /// </remarks>
        Miss = 3,

        /// <summary>
        /// 爆弾に当たった地点。リプレイからのみ取り込む。
        /// </summary>
        /// <remarks>
        /// ボムリセットのような、当たると体力を持っていかれる配置を拾うためのもの。
        /// ミスと違って数が少ないので、取り込んだものは既定で使う指定にする。
        /// </remarks>
        Bomb = 4,
    }
}
