using System;
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
        /// <summary>BSOR のノートイベント種別。1 は切り損ね、2 は見逃し、3 は爆弾。</summary>
        private const int NoteEventBad = 1;
        private const int NoteEventMiss = 2;
        private const int NoteEventBomb = 3;

        /// <summary>
        /// 落とした扱いにするイベントか。
        /// </summary>
        /// <remarks>
        /// 見逃しだけでなく切り損ねも含める。利用者から見ればどちらも「ミス」であり、
        /// 判定が崩れる箇所という意味では区別する理由が無い。
        /// 爆弾は当たっても続きが崩れるとは限らないので含めない。
        /// </remarks>
        private static bool IsMissed(ReplayNoteEvent note) =>
            note.EventType == NoteEventMiss || note.EventType == NoteEventBad;

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
            ImportHits(parsed, set, MarkerSource.Miss,
                       PluginConfig.Instance.MaxMissMarkers, IsMissed, allOn: false);
        }

        /// <summary>
        /// 爆弾に当たった地点を取り込む。
        /// </summary>
        /// <remarks>
        /// 選び方はミスと同じだが、取り込んだものは全部使う指定にする。
        /// 爆弾はミスほど当たらないので数が並ばず、また当たると立て直しが利かない。
        /// ボムリセットのような配置は、事前に分かっていること自体に意味がある。
        /// </remarks>
        private static void ImportBombs(List<BsorReplay> parsed, BeatmapMarkerSet set)
        {
            ImportHits(parsed, set, MarkerSource.Bomb,
                       PluginConfig.Instance.MaxBombMarkers,
                       note => note.EventType == NoteEventBomb, allOn: true);
        }

        /// <summary>
        /// 該当するノートイベントを箇所ごとにまとめ、上限まで取り込む。
        /// </summary>
        /// <param name="allOn">
        /// true なら取り込んだ全てを使う指定にする。
        /// false なら、最も重なっている箇所の先頭 1 つだけを使う指定にする。
        /// </param>
        private static void ImportHits(List<BsorReplay> parsed, BeatmapMarkerSet set,
                                       MarkerSource source, int limit,
                                       Func<ReplayNoteEvent, bool> match, bool allOn)
        {
            if (limit <= 0) return;

            var threshold = PluginConfig.Instance.ClusterThresholdSeconds;
            var clusters = BuildClusters(parsed, threshold, match);

            var ordered = clusters
                .Where(c => c.Value.Count > 1)
                .OrderByDescending(c => c.Value.Count)
                .ThenBy(c => c.Key)
                .Concat(clusters.Where(c => c.Value.Count == 1).OrderBy(c => c.Key))
                .ToList();

            var added = 0;
            var merged = 0;

            foreach (var cluster in ordered)
            {
                var playCount = cluster.Value.Count;

                // 同じ危険地点が既にあるなら、回数だけ足す。
                // 前回までに読んだリプレイの記録を残したまま積み上げるため
                var existing = set.FindNear(cluster.Key, source, threshold);
                if (existing != null)
                {
                    existing.HitCount += playCount;
                    merged++;
                    continue;
                }

                if (set.CountOf(source) >= limit) continue;

                if (set.AddImportedHit(cluster.Key, source,
                                       allOn ? MarkerState.On : MarkerState.Off,
                                       playCount))
                {
                    added++;
                }
            }

            // 使う指定は、積み上げた回数を見て最後に決める。
            // 1 回の取り込みで読むのは、遊んだ直後なら 1 件だけ。
            // その回の重なりだけで決めると、いつまでも候補が現れない
            if (!allOn) set.PromoteMostHit(source);

            Plugin.Log?.Info(
                $"{source}: {clusters.Count} spot(s) in new replay(s), " +
                $"{added} added, {merged} merged into existing (limit {limit}, now {set.CountOf(source)}).");
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
        private static List<MissCluster> BuildClusters(List<BsorReplay> parsed, float thresholdSeconds,
                                                       Func<ReplayNoteEvent, bool> match)
        {
            var points = new List<KeyValuePair<float, int>>();
            for (var index = 0; index < parsed.Count; index++)
            {
                var stoppedAt = LastPlayedTime(parsed[index]);

                foreach (var note in parsed[index].NoteEvents)
                {
                    // チェーンの節の取りこぼしは付随的なものなので数えない
                    if (!match(note) || note.IsChainLink) continue;

                    // やめた後に飛んでいたノートは実際のミスではない
                    if (note.Time > stoppedAt) continue;

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

        /// <summary>
        /// そのプレイで最後に操作していた時刻。
        /// </summary>
        /// <remarks>
        /// 曲を途中でやめると、その瞬間に飛んでいたノートが全部ミスとして記録される。
        /// 実データでは末尾 2 秒に 11 個並んでおり、これを数えると上限枠を食い潰したうえ、
        /// やめた地点が「よく落とす箇所」として上位に来てしまう。
        /// 見逃し以外のイベント（切った・爆弾に当たった）が最後にあった時刻を境にして落とす。
        /// </remarks>
        private static float LastPlayedTime(BsorReplay replay)
        {
            var last = float.NegativeInfinity;
            foreach (var note in replay.NoteEvents)
            {
                // 切り損ねは剣を振っているので操作していた証拠になる。見逃しだけを除く
                if (note.EventType == NoteEventMiss) continue;
                if (note.Time > last) last = note.Time;
            }
            return last;
        }

        /// <summary>この譜面で候補になるリプレイ。新しいものから上限件数まで。</summary>
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

        /// <summary>まだ読んでいないリプレイ。これが取り込みの対象になる。</summary>
        private static List<ReplayFileInfo> UnreadSources(BeatmapKey key, BeatmapMarkerSet set)
            => SelectSources(key).Where(file => !set.HasImported(file.Timestamp)).ToList();

        /// <summary>取り込みが必要か。まだ読んでいないファイルが 1 つでもあれば必要。</summary>
        public static bool NeedsImport(BeatmapKey key, BeatmapMarkerSet set)
            => UnreadSources(key, set).Count > 0;

        /// <summary>
        /// 譜面 1 つ分を取り込む。
        /// </summary>
        /// <remarks>
        /// まだ読んでいないリプレイだけを既存のマーカーへ足していく。
        /// 読んだファイルは覚えるので、何度呼んでも二重に数えない。
        /// 作り直さないのは、BeatLeader が古いリプレイを消すことがあるため。
        /// 作り直す方式では、消えた時点でそれまでの記録も失われる。
        /// </remarks>
        public static ImportResult Import(BeatmapKey key, BeatmapMarkerSet set)
        {
            var sources = UnreadSources(key, set);
            if (sources.Count == 0) return new ImportResult(0, 0, false);

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
            ImportBombs(parsed, set);

            // 読めなかったファイルも読んだ印を付ける。そうしないと、壊れたファイルが
            // 1 つあるだけで曲を選ぶたびに解析をやり直すことになる
            foreach (var file in sources) set.MarkImported(file.Timestamp);

            Plugin.Log?.Info(
                $"Import {BeatmapMarkerKey.From(key)}: " +
                $"{sources.Count} new replay file(s) of {ReplayFileIndex.Find(key).Count} present, " +
                $"{parsed.Count} readable. " +
                $"Total imported {set.ImportedReplayCount}, markers now {set.Count}.");

            foreach (var file in sources)
            {
                Plugin.Log?.Info($"  {System.IO.Path.GetFileName(file.Path)}");
            }

            return new ImportResult(parsed.Count, set.Count - before, failImported);
        }
    }
}
