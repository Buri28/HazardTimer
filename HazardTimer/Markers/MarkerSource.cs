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
    }
}
