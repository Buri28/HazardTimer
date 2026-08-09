using System;
using System.Collections.Generic;
using HazardTimer.Markers;
using HazardTimer.Replay;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>
    /// 曲を選ぶたびに、その譜面のリプレイを自動で取り込む。
    /// </summary>
    /// <remarks>
    /// 実測方式は初見の譜面で何も出せないので、既に持っているプレイ履歴は
    /// 黙って使えるようにしておく方が自然。手動ボタンは残してある。
    /// </remarks>
    public class AutoImportService : IInitializable, IDisposable
    {
        /// <summary>
        /// この回数ぶん溜まったらファイルへ書き出す。
        /// 1 譜面ごとに書くと、曲をスクロールしている間ずっと書き込みが走る。
        /// </summary>
        private const int PendingSaveThreshold = 20;

        /// <summary>
        /// 手動で記録を消した譜面。ゲームを再起動するまで自動取り込みの対象外にする。
        /// これが無いと、消した直後に選び直しただけで戻ってしまう。
        /// </summary>
        private static readonly HashSet<string> Suppressed = new HashSet<string>(StringComparer.Ordinal);

        private int pendingSaves;

        /// <summary>自動取り込みが実際に何かを取り込んだときに発火する。</summary>
        public static event Action? ImportCompleted;

        /// <summary>この譜面を自動取り込みの対象外にする。</summary>
        public static void Suppress(BeatmapKey key) => Suppressed.Add(BeatmapMarkerKey.From(key));

        /// <summary>対象外の指定を解除する。手動で取り込み直したとき用。</summary>
        public static void Allow(BeatmapKey key) => Suppressed.Remove(BeatmapMarkerKey.From(key));

        public void Initialize()
        {
            // メニューに入り直したということは、直前のプレイのリプレイが増えている可能性がある。
            // BeatLeader の書き出しはリザルト前後なので、ゲームプレイ終了時点では間に合わない
            ReplayFileIndex.Invalidate();

            SelectedBeatmapTracker.SelectionChanged += OnSelectionChanged;

            // 選択追跡はこのサービスより先に初期化され、最初の選択通知を撃ち終えている。
            // プレイ直後に戻ってきたときの「今まさに選ばれている譜面」がそれなので、
            // ここで拾い直さないと、いま遊んだ譜面だけが取り込まれない
            OnSelectionChanged();
        }

        public void Dispose()
        {
            SelectedBeatmapTracker.SelectionChanged -= OnSelectionChanged;
            Flush();
        }

        private void OnSelectionChanged()
        {
            if (!PluginConfig.Instance.AutoImportReplays) return;

            var key = SelectedBeatmapTracker.Current;
            if (!key.HasValue) return;
            if (Suppressed.Contains(BeatmapMarkerKey.From(key.Value))) return;

            // 索引を引くだけで済むうちに打ち切る。GetOrCreate を先に呼ぶと、
            // 曲を眺めただけの譜面が全部メモリ上の辞書に残ってしまう
            if (ReplayImportService.CountAvailable(key.Value) == 0) return;

            var set = MarkerStore.Instance.GetOrCreate(key.Value);
            if (set.AutoImportSuppressed) return;
            if (!ReplayImportService.NeedsImport(key.Value, set)) return;

            var result = ReplayImportService.Import(key.Value, set,
                                                    PluginConfig.Instance.ClusterThresholdSeconds);
            if (result.ReplayCount == 0) return;

            MarkerStore.Instance.MarkDirty();
            if (++pendingSaves >= PendingSaveThreshold) Flush();

            ImportCompleted?.Invoke();
        }

        /// <summary>溜まっている変更を書き出す。</summary>
        public void Flush()
        {
            if (pendingSaves == 0) return;
            pendingSaves = 0;
            MarkerStore.Instance.Save();
        }
    }
}
