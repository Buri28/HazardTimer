namespace HazardTimer.Markers
{
    /// <summary>
    /// 保存単位のキーを組み立てるヘルパー。
    /// 譜面ハッシュ + characteristic + 難易度。
    /// </summary>
    /// <remarks>
    /// カスタム譜面の <c>levelId</c> は <c>custom_level_&lt;HASH&gt;</c> という形式で、
    /// ハッシュそのものを含んでいる。したがって譜面が更新されればキーが変わり、
    /// 古い記録は自動的に参照されなくなる（＝設計上の「ハッシュが変わったら破棄」が
    /// 明示的な破棄処理なしに成立する）。
    /// 公式曲は levelId が固定なので、そもそも更新でずれる問題がない。
    /// </remarks>
    internal static class BeatmapMarkerKey
    {
        public static string From(BeatmapKey key)
        {
            var characteristic = key.beatmapCharacteristic != null
                ? key.beatmapCharacteristic.serializedName
                : "Unknown";
            return $"{key.levelId}|{characteristic}|{key.difficulty}";
        }
    }
}
