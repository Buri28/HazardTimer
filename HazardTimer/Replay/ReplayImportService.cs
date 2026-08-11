using System.Collections.Generic;
using System.Linq;
using HazardTimer.Markers;

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

            // 読めなかったファイルがあっても、対象にした数と時刻で覚える。
            // 解析できた数で覚えると、壊れたファイルが 1 つあるだけで毎回やり直しになる
            set.ImportedReplayCount = sources.Count;
            set.ImportedLatestTimestamp = sources[sources.Count - 1].Timestamp;

            return new ImportResult(parsed.Count, set.Count - before, failImported);
        }
    }
}
