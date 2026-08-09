using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using HazardTimer.Markers;
using HazardTimer.Replay;
using HazardTimer.Services;
using HMUI;
using TMPro;

namespace HazardTimer.UI
{
    /// <summary>
    /// 曲選択画面の Mods タブに出すマーカー編集 UI。
    /// 一覧からの削除、名前の変更、分＋秒での追加、リプレイの取り込みを行う。
    /// </summary>
    public class ManualMarkerController : IDisposable
    {
        internal const string TabName = "HazardTimer";
        internal const string Resource = "HazardTimer.Resources.ManualMarker.bsml";

        [UIComponent("status-text")] private readonly TextMeshProUGUI? statusText = null;
        [UIComponent("marker-list")] private readonly CustomListTableData? markerList = null;

        /// <summary>一覧に出している順のマーカー。選択位置と対応させる。</summary>
        private readonly List<HazardMarker> listed = new List<HazardMarker>();

        private int selectedIndex = -1;
        private int minutes;
        private int seconds;
        private string label = string.Empty;

        /// <summary>直近の操作結果。譜面を選び直したら消す。</summary>
        private string? actionMessage;

        public ManualMarkerController()
        {
            SelectedBeatmapTracker.SelectionChanged += OnSelectionChanged;
            // 自動取り込みは選択変更のあとに走るので、その完了でも表示を更新する
            AutoImportService.ImportCompleted += Refresh;
        }

        public void Dispose()
        {
            SelectedBeatmapTracker.SelectionChanged -= OnSelectionChanged;
            AutoImportService.ImportCompleted -= Refresh;
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

        /// <summary>
        /// 追加時に付ける名前。空なら種別ごとの既定（MARK など）になる。
        /// 選択中のマーカーがあれば Rename でそれに付け替えられる。
        /// </summary>
        [UIValue("label")]
        public string Label
        {
            get => label;
            set => label = value ?? string.Empty;
        }

        [UIAction("#post-parse")]
        public void PostParse() => Refresh();

        [UIAction("marker-selected")]
        public void OnMarkerSelected(TableView _, int index)
        {
            selectedIndex = index;
            if (index >= 0 && index < listed.Count)
            {
                // 選んだマーカーの名前を編集欄へ引き継ぐ
                label = listed[index].Label ?? string.Empty;
            }
            RefreshStatus();
        }

        [UIAction("delete-selected")]
        public void DeleteSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            if (selectedIndex < 0 || selectedIndex >= listed.Count)
            {
                actionMessage = "Select a marker to delete";
                RefreshStatus();
                return;
            }

            var marker = listed[selectedIndex];
            if (!set.Remove(marker)) return;

            actionMessage = marker.Imported
                ? $"Deleted {FormatTime(marker.SongTime)} - Import restores it"
                : $"Deleted {FormatTime(marker.SongTime)}";

            selectedIndex = -1;
            Persist();
            Refresh();
        }

        [UIAction("rename-selected")]
        public void RenameSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            if (selectedIndex < 0 || selectedIndex >= listed.Count)
            {
                actionMessage = "Select a marker to rename";
                RefreshStatus();
                return;
            }

            listed[selectedIndex].Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            actionMessage = "Renamed";
            Persist();
            Refresh();
        }

        [UIAction("add-marker")]
        public void AddMarker()
        {
            var set = CurrentSet();
            if (set == null)
            {
                SetStatus("No beatmap selected");
                return;
            }

            if (set.AddManual(minutes * 60 + seconds, label))
            {
                actionMessage = $"Added at {minutes}:{seconds:00}";
                Persist();
            }
            Refresh();
        }

        [UIAction("import-replays")]
        public void ImportReplays()
        {
            var key = SelectedBeatmapTracker.Current;
            if (!key.HasValue)
            {
                SetStatus("No beatmap selected");
                return;
            }

            if (!ReplayFileIndex.DirectoryExists)
            {
                SetStatus("BeatLeader replay folder not found");
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
                SetStatus("No readable replays");
                return;
            }

            Persist();
            actionMessage = $"Imported from {result.ReplayCount} replay(s)";
            Refresh();
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

            selectedIndex = -1;
            actionMessage = "Cleared - auto import disabled";
            // 印そのものが状態変化なので、消すものが無くても必ず保存する
            Persist();
            Refresh();
        }

        private static void Persist()
        {
            MarkerStore.Instance.MarkDirty();
            MarkerStore.Instance.Save();
        }

        private void OnSelectionChanged()
        {
            actionMessage = null;
            selectedIndex = -1;
            Refresh();
        }

        private static BeatmapMarkerSet? CurrentSet()
        {
            var key = SelectedBeatmapTracker.Current;
            return key.HasValue ? MarkerStore.Instance.GetOrCreate(key.Value) : null;
        }

        /// <summary>一覧と状態表示をまとめて作り直す。</summary>
        private void Refresh()
        {
            RefreshList();
            RefreshStatus();
        }

        private void RefreshList()
        {
            listed.Clear();

            var set = CurrentSet();
            if (set != null) listed.AddRange(set.Markers);

            // パース前に選択変更が飛んでくることがあるので、部品が揃うまで触らない
            if (markerList == null || markerList.Data == null || markerList.TableView == null) return;

            markerList.Data.Clear();
            foreach (var marker in listed)
            {
                var origin = marker.Imported ? "Imported" : "Measured";
                var source = marker.Source == MarkerSource.Manual ? "Manual" : origin;
                var hits = marker.HitCount > 1 ? $" x{marker.HitCount}" : string.Empty;

                markerList.Data.Add(new CustomListTableData.CustomCellInfo(
                    $"{FormatTime(marker.SongTime)}  {marker.DisplayLabel}",
                    $"{source}{hits}"));
            }

            markerList.TableView.ReloadData();
            markerList.TableView.ClearSelection();
        }

        private void RefreshStatus()
        {
            var set = CurrentSet();
            if (set == null)
            {
                SetStatus("No beatmap selected");
                return;
            }

            var measuredWall = set.Markers.Count(m => m.Source == MarkerSource.Wall && !m.Imported);
            var importedWall = set.Markers.Count(m => m.Source == MarkerSource.Wall && m.Imported);
            var manual = set.Markers.Count(m => m.Source == MarkerSource.Manual);

            var sb = new StringBuilder();
            sb.Append($"Wall {measuredWall}");
            if (importedWall > 0) sb.Append($" (+{importedWall})");
            sb.Append($" / Manual {manual}");
            if (set.FailMarker != null) sb.Append(" / Fail");

            sb.Append('\n');
            if (actionMessage != null)
            {
                sb.Append(actionMessage);
            }
            else if (set.ImportedReplayCount > 0)
            {
                sb.Append($"Imported from {set.ImportedReplayCount} replay(s)");
            }
            else
            {
                var key = SelectedBeatmapTracker.Current;
                var available = key.HasValue ? ReplayImportService.CountAvailable(key.Value) : 0;
                sb.Append(available > 0 ? $"{available} replay(s) available" : "No replays");
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

