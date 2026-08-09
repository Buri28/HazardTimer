using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using HazardTimer.Markers;
using HazardTimer.Replay;
using HazardTimer.Services;
using TMPro;

namespace HazardTimer.UI
{
    /// <summary>
    /// 曲選択画面の Mods タブに出す手動マーカーの編集 UI。
    /// 選択中の譜面に対して、分＋秒で危険地点を足したり消したりする。
    /// </summary>
    public class ManualMarkerController : System.IDisposable
    {
        internal const string TabName = "HazardTimer";
        internal const string Resource = "HazardTimer.Resources.ManualMarker.bsml";

        [UIComponent("status-text")] private readonly TextMeshProUGUI? statusText = null;

        private int minutes;
        private int seconds;

        /// <summary>直近の取り込み結果。譜面を選び直したら消す。</summary>
        private string? importMessage;

        public ManualMarkerController()
        {
            SelectedBeatmapTracker.SelectionChanged += OnSelectionChanged;
            // 自動取り込みは選択変更のあとに走るので、その完了でも表示を更新する
            AutoImportService.ImportCompleted += RefreshStatus;
        }

        private void OnSelectionChanged()
        {
            importMessage = null;
            RefreshStatus();
        }

        public void Dispose()
        {
            SelectedBeatmapTracker.SelectionChanged -= OnSelectionChanged;
            AutoImportService.ImportCompleted -= RefreshStatus;
        }

        [UIValue("minutes")]
        public int Minutes
        {
            get => minutes;
            set => minutes = value;
        }

        [UIValue("seconds")]
        public int Seconds
        {
            get => seconds;
            set => seconds = value;
        }

        [UIAction("#post-parse")]
        public void PostParse() => RefreshStatus();

        [UIAction("add-marker")]
        public void AddMarker()
        {
            var set = CurrentSet();
            if (set == null)
            {
                SetStatus("譜面が選択されていません");
                return;
            }

            if (set.AddManual(minutes * 60 + seconds))
            {
                MarkerStore.Instance.MarkDirty();
                MarkerStore.Instance.Save();
            }
            RefreshStatus();
        }

        [UIAction("import-replays")]
        public void ImportReplays()
        {
            var key = SelectedBeatmapTracker.Current;
            if (!key.HasValue)
            {
                SetStatus("譜面が選択されていません");
                return;
            }

            if (!ReplayFileIndex.DirectoryExists)
            {
                SetStatus("BeatLeader のリプレイフォルダが見つかりません");
                return;
            }

            // 手動で取り込み直したなら、以後の自動取り込みも許可する
            AutoImportService.Allow(key.Value);

            var set = MarkerStore.Instance.GetOrCreate(key.Value);
            set.AutoImportSuppressed = false;
            var result = ReplayImportService.Import(key.Value, set,
                                                    PluginConfig.Instance.ClusterThresholdSeconds);

            if (result.ReplayCount == 0)
            {
                SetStatus("この譜面のリプレイはありません");
                return;
            }

            MarkerStore.Instance.MarkDirty();
            MarkerStore.Instance.Save();

            importMessage = $"リプレイ {result.ReplayCount} 件から {result.MarkerCount} 個追加";
            RefreshStatus();
        }

        [UIAction("clear-manual")]
        public void ClearManual()
        {
            var set = CurrentSet();
            if (set == null) return;

            if (set.RemoveAll(MarkerSource.Manual))
            {
                MarkerStore.Instance.MarkDirty();
                MarkerStore.Instance.Save();
            }
            RefreshStatus();
        }

        [UIAction("clear-all")]
        public void ClearAll()
        {
            var set = CurrentSet();
            if (set == null) return;

            // 消した直後に自動取り込みで戻ってこないよう、この譜面を対象外にする。
            // 再起動しても戻らないよう、集合側にも印を残して永続化する
            var key = SelectedBeatmapTracker.Current;
            if (key.HasValue) AutoImportService.Suppress(key.Value);
            set.AutoImportSuppressed = true;

            set.Clear();
            // 印そのものが状態変化なので、消すものが無くても必ず保存する
            MarkerStore.Instance.MarkDirty();
            MarkerStore.Instance.Save();
            RefreshStatus();
        }

        private static BeatmapMarkerSet? CurrentSet()
        {
            var key = SelectedBeatmapTracker.Current;
            return key.HasValue ? MarkerStore.Instance.GetOrCreate(key.Value) : null;
        }

        private void RefreshStatus()
        {
            var set = CurrentSet();
            if (set == null)
            {
                importMessage = null;
                SetStatus("譜面が選択されていません");
                return;
            }

            var measuredWall = set.Markers.Count(m => m.Source == MarkerSource.Wall && !m.Imported);
            var importedWall = set.Markers.Count(m => m.Source == MarkerSource.Wall && m.Imported);
            var manual = set.Markers.Count(m => m.Source == MarkerSource.Manual);
            var fail = set.FailMarker;

            var sb = new StringBuilder();
            sb.Append($"Wall {measuredWall}");
            // 取り込みは進入時刻が確かでないので、実測と分けて見せる
            if (importedWall > 0) sb.Append($" (+{importedWall} imported)");
            sb.Append($" / Manual {manual}");
            if (fail != null) sb.Append($" / Fail {FormatTime(fail.SongTime)}");

            if (manual > 0)
            {
                sb.Append('\n');
                sb.Append(string.Join(", ", set.Markers
                    .Where(m => m.Source == MarkerSource.Manual)
                    .Select(m => FormatTime(m.SongTime))));
            }

            sb.Append('\n');
            if (importMessage != null)
            {
                sb.Append(importMessage);
            }
            else if (set.ImportedReplayCount > 0)
            {
                sb.Append($"取り込み済み（リプレイ {set.ImportedReplayCount} 件）");
            }
            else
            {
                var available = ReplayImportService.CountAvailable(SelectedBeatmapTracker.Current!.Value);
                sb.Append(available > 0
                    ? $"取り込めるリプレイ {available} 件"
                    : "取り込めるリプレイはありません");
            }

            SetStatus(sb.ToString());
        }

        private static string FormatTime(float songTime)
        {
            var total = (int)songTime;
            return $"{total / 60}:{total % 60:00}";
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }
    }
}
