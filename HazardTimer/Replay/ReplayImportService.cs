using System.Collections.Generic;
using System.Linq;
using HazardTimer.Markers;

// ミス地点の取り込みで使う集計用の型。譜面時間の近いミスを 1 箇所にまとめる
using MissCluster = System.Collections.Generic.KeyValuePair<float, System.Collections.Generic.HashSet<int>>;

namespace HazardTimer.Replay
{
    /// <summary>取り込み結果。UI へ返す要約。</summary>
    public readonly struct ImportResult
    {
        public readonly int ReplayCount;
        public readonly int MarkerCount;
        public readonly bool FailImported;

        public ImportResult(int replayCount, int markerCount, bool failImported)
        {
            ReplayCount = replayCount;
            MarkerCount = markerCount;
            FailImported = failImported;
        }
    }

    /// <summary>
    /// ローカルの BeatLeader リプレイから、譜面 1 つ分の危険地点を取り込む。
    /// </summary>
    /// <remarks>
    /// 本MODを入れる前のプレイ履歴を使って、初回から警告を出せるようにするためのもの。
    /// 通信は行わない。BeatLeader MOD が既に書き出したファイルだけを読む。
    /// </remarks>
    public static class ReplayImportService
    {
        /// <summary>BSOR のノートイベント種別。2 は見逃し。</summary>
        private const int NoteEventMiss = 2;

        /// <summary>
        /// ミス地点を取り込む。
        /// </summary>
        /// <remarks>
        /// 選び方は次のとおり。
        /// <list type="number">
        /// <item>近い時刻のミスを 1 箇所にまとめ、何回のプレイで落としたかを数える</item>
        /// <item>複数のプレイで重なっている箇所を優先する。回数が多いほど上位</item>
        /// <item>残りは曲の先頭から順に埋める</item>
        /// </list>
        /// 使う指定にするのは<b>先頭の 1 つだけ</b>。最も多く落としている箇所のうち、
        /// 曲の先頭に近いもの。ミスは元々多いので、重なったもの全部を対象にすると
        /// カウントダウンが出っぱなしになる。残りは候補として使わない指定で入れておき、
        /// 必要になったら利用者が切り替える。
        /// </remarks>
        private static void ImportMisses(List<BsorReplay> parsed, BeatmapMarkerSet set)
        {
            var limit = PluginConfig.Instance.MaxMissMarkers;
            if (limit <= 0) return;

            var clusters = BuildMissClusters(parsed, PluginConfig.Instance.ClusterThresholdSeconds);

            var ordered = clusters
                .Where(c => c.Value.Count > 1)
                .OrderByDescending(c => c.Value.Count)
                .ThenBy(c => c.Key)
                .Concat(clusters.Where(c => c.Value.Count == 1).OrderBy(c => c.Key))
                .Take(limit)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                // 並びの先頭が「最も重なっていて、その中で最も早い」箇所になる。
                // 重なりが無い譜面では、どれも使う指定にしない
                var missedPlays = ordered[index].Value.Count;
                var isPrimary = index == 0 && missedPlays > 1;
                set.AddImportedMiss(ordered[index].Key,
                                    isPrimary ? MarkerState.On : MarkerState.Off,
                                    missedPlays);
            }
        }

        /// <summary>
        /// 近い時刻のミスを 1 箇所にまとめる。
        /// 値は、その箇所を落としたリプレイの番号（重なりの数え上げに使う）。
        /// </summary>
        /// <remarks>
        /// 区切りは「先頭からの窓」で行う。壁と同じ「直前からの間隔」で連鎖させると、
        /// ミスは密なので曲全体が 1 つに繋がってしまう。
        /// 実データでは 157 個のミスが 1 クラスタになり、まとめる意味が無くなっていた。
        /// </remarks>
        private static List<MissCluster> BuildMissClusters(List<BsorReplay> parsed, float thresholdSeconds)
        {
            var points = new List<KeyValuePair<float, int>>();
            for (var index = 0; index < parsed.Count; index++)
            {
                foreach (var note in parsed[index].NoteEvents)
                {
                    // チェーンの節の取りこぼしは付随的なものなので数えない
                    if (note.EventType != NoteEventMiss || note.IsChainLink) continue;
                    points.Add(new KeyValuePair<float, int>(note.Time, index));
                }
            }
            points.Sort((a, b) => a.Key.CompareTo(b.Key));

            var clusters = new List<MissCluster>();

            foreach (var point in points)
            {
                if (clusters.Count == 0
                    || point.Key - clusters[clusters.Count - 1].Key >= thresholdSeconds)
                {
                    clusters.Add(new MissCluster(point.Key, new HashSet<int>()));
                }
                clusters[clusters.Count - 1].Value.Add(point.Value);
            }
            return clusters;
        }

        /// <summary>この譜面で実際に読むリプレイ。新しいものから上限件数まで。</summary>
        private static IReadOnlyList<ReplayFileInfo> SelectSources(BeatmapKey key)
        {
            var all = ReplayFileIndex.Find(key);
            var limit = PluginConfig.Instance.MaxImportReplays;
            if (limit <= 0 || all.Count <= limit) return all;

            // Find は古い順。新しい側から上限件数だけ採る
            return all.Skip(all.Count - limit).ToList();
        }

        /// <summary>この譜面で読む対象になるリプレイの件数。</summary>
        public static int CountAvailable(BeatmapKey key) => SelectSources(key).Count;

        /// <summary>
        /// 取り込みが必要か。
        /// </summary>
        /// <remarks>
        /// 上限で件数が頭打ちになるので、件数では新しいプレイに気づけない。
        /// 最も新しいリプレイの時刻で判断する。
        /// </remarks>
        public static bool NeedsImport(BeatmapKey key, BeatmapMarkerSet set)
        {
            var sources = SelectSources(key);
            if (sources.Count == 0) return false;

            return set.ImportedLatestTimestamp != sources[sources.Count - 1].Timestamp
                   || set.ImportedReplayCount != sources.Count;
        }

        /// <summary>
        /// 譜面 1 つ分を取り込む。既存の取り込みマーカーは一度捨ててから作り直すので、
        /// 何度呼んでも二重に増えない。実測マーカーには触れない。
        /// </summary>
        public static ImportResult Import(BeatmapKey key, BeatmapMarkerSet set)
        {
            var sources = SelectSources(key);
            if (sources.Count == 0) return new ImportResult(0, 0, false);

            // 先に全部読む。1 つも読めないファイル群で既存の取り込み結果を捨てないため
            var parsed = new List<BsorReplay>(sources.Count);
            foreach (var file in sources)
            {
                var replay = BsorReader.Read(file.Path);
                if (replay != null) parsed.Add(replay);
            }

            if (parsed.Count == 0)
            {
                Plugin.Log?.Warn($"No readable replay among {sources.Count} file(s); keeping existing markers.");
                return new ImportResult(0, 0, false);
            }

            set.RemoveImported();

            var before = set.Count;
            var failImported = false;

            foreach (var replay in parsed)
            {
                // 壁は全リプレイから集める。どれを警告に使うかは集合側が決める
                foreach (var interval in replay.ObstacleIntervals)
                {
                    set.AddImportedWall(interval.Start);
                }

                // NoFail ではゲームがフェイル時刻を残さないので、その場合だけ模擬で補う。
                // 候補は絞らずに全部入れる。到達点が一番奥のものが警告に使われる
                var failTime = replay.FailTime ?? ReplayEnergyEstimator.EstimateFailTime(replay);
                if (failTime.HasValue && set.AddFail(failTime.Value, imported: true))
                {
                    failImported = true;
                }
            }

            ImportMisses(parsed, set);

            // 読めなかったファイルがあっても、対象にした数と時刻で覚える。
            // 解析できた数で覚えると、壊れたファイルが 1 つあるだけで毎回やり直しになる
            set.ImportedReplayCount = sources.Count;
            set.ImportedLatestTimestamp = sources[sources.Count - 1].Timestamp;

            return new ImportResult(parsed.Count, set.Count - before, failImported);
        }
    }
}
