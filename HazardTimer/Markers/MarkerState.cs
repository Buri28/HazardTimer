namespace HazardTimer.Markers
{
    /// <summary>
    /// マーカーをカウントダウンに使うかどうかの指定。
    /// </summary>
    public enum MarkerState
    {
        /// <summary>指定なし。近接した記録の中から自動で選ぶ。</summary>
        Auto = 0,

        /// <summary>必ず使う。時刻が重なる他のマーカーは使わない。</summary>
        On = 1,

        /// <summary>使わない。消さずに残す。</summary>
        Off = 2,
    }
}
