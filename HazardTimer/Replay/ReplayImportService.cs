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
        /// <summary>この譜面にリプレイが何件あるか。取り込み前の見積もり用。</summary>
        public static int CountAvailable(BeatmapKey key) => ReplayFileIndex.Find(key).Count;

        /// <summary>
        /// 取り込みが必要か。ファイル数が記録と一致していれば済んでいるとみなす。
        /// </summary>
        /// <remarks>
        /// 件数で比べているので、その譜面を新しく遊んでリプレイが増えたときは
        /// 自動的にやり直しになる。索引の参照と整数比較だけなので、
        /// 曲を高速にスクロールされても負荷にならない。
        /// </remarks>
        public static bool NeedsImport(BeatmapKey key, BeatmapMarkerSet set)
        {
            var available = ReplayFileIndex.Find(key).Count;
            return available > 0 && set.ImportedReplayCount != available;
        }

        /// <summary>
        /// 譜面 1 つ分を取り込む。既存の取り込みマーカーは一度捨ててから作り直すので、
        /// 何度呼んでも二重に増えない。実測マーカーには触れない。
        /// </summary>
        public static ImportResult Import(BeatmapKey key, BeatmapMarkerSet set, float clusterThresholdSeconds)
        {
            var paths = ReplayFileIndex.Find(key);
            if (paths.Count == 0) return new ImportResult(0, 0, false);

            // 先に全部読む。1 つも読めないファイル群で既存の取り込み結果を捨てないため。
            // 途中まで書き込まれたリプレイなどで、黙って記録が消えるのを防ぐ
            var parsed = new List<(ReplayFileInfo File, BsorReplay Replay)>(paths.Count);
            foreach (var file in paths)
            {
                var replay = BsorReader.Read(file.Path);
                if (replay != null) parsed.Add((file, replay));
            }

            if (parsed.Count == 0)
            {
                Plugin.Log?.Warn($"No readable replay among {paths.Count} file(s); keeping existing markers.");
                return new ImportResult(0, 0, false);
            }

            set.RemoveImported();

            var before = set.Count;
            var replayCount = parsed.Count;
            var failImported = false;

            // フェイル地点の候補。どのリプレイのものを採るかは全部読んでから決める
            var failCandidates = new List<(ReplayFileInfo File, float? FailTime)>(parsed.Count);

            foreach (var (file, replay) in parsed)
            {
                // 壁は全リプレイから集める。過去に一度でも当たった地点は等しく危険地点
                var entryTimes = replay.ObstacleIntervals.Select(i => i.Start).ToList();
                foreach (var time in ClusterHeads(entryTimes, clusterThresholdSeconds))
                {
                    set.AddImportedWall(time, clusterThresholdSeconds);
                }

                // NoFail ではゲームがフェイル時刻を残さないので、その場合だけ模擬で補う
                failCandidates.Add((file, replay.FailTime ?? ReplayEnergyEstimator.EstimateFailTime(replay)));
            }

            var chosen = ChooseFailSource(failCandidates);
            if (chosen.HasValue && set.SetFail(chosen.Value, imported: true))
            {
                failImported = true;
            }

            // 読めなかったファイルがあっても、見つかった数で覚える。
            // 解析できた数で覚えると、壊れたファイルが 1 つあるだけで毎回やり直しになる
            set.ImportedReplayCount = paths.Count;
            return new ImportResult(replayCount, set.Count - before, failImported);
        }

        /// <summary>
        /// フェイル地点をどのリプレイから採るかを決める。
        /// </summary>
        /// <remarks>
        /// <b>最後に完走したプレイ</b>を見る。完走が 1 つも無ければ、最後の未完走を見る。
        /// <para>
        /// 最新の完走で落ちていないなら、今の実力では落ちない譜面ということなので
        /// マーカーは立てない。古いプレイまで遡って探さないのは意図的で、
        /// 上達して通れるようになった地点が残り続けるのを防ぐ。
        /// </para>
        /// </remarks>
        private static float? ChooseFailSource(List<(ReplayFileInfo File, float? FailTime)> candidates)
        {
            if (candidates.Count == 0) return null;

            var completed = candidates.Where(c => c.File.IsCompleted).ToList();
            var pool = completed.Count > 0 ? completed : candidates;

            return pool.OrderByDescending(c => c.File.Timestamp).First().FailTime;
        }

        /// <summary>
        /// 1 プレイ分のイベント列をクラスタに分け、それぞれの先頭時刻を返す。
        /// 判定は「直前のイベントからの間隔」で行う（設計 2.2）。
        /// </summary>
        private static IEnumerable<float> ClusterHeads(List<float> sortedEventTimes, float thresholdSeconds)
        {
            var lastEventTime = float.NegativeInfinity;
            foreach (var time in sortedEventTimes)
            {
                if (time - lastEventTime >= thresholdSeconds) yield return time;
                lastEventTime = time;
            }
        }

    }
}
