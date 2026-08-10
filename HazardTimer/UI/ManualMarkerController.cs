using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
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
    public class ManualMarkerController : IDisposable, INotifyPropertyChanged
    {
        /// <summary>
        /// 一覧でマーカーを選んだときに Rename 用の入力欄へ名前を流し込むため、
        /// BSML の bind-value に変更を通知する。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        internal const string TabName = "HazardTimer";
        internal const string Resource = "HazardTimer.Resources.ManualMarker.bsml";

        [UIComponent("status-text")] private readonly TextMeshProUGUI? statusText = null;
        [UIComponent("marker-list")] private readonly CustomListTableData? markerList = null;
        [UIParams] private readonly BSMLParserParams? parserParams = null;

        /// <summary>
        /// 設定部品がホストの値を読み直すイベント名。BSML の既定値。
        /// </summary>
        private const string RefreshValuesEvent = "cancel";

        /// <summary>一覧に出している順のマーカー。選択位置と対応させる。</summary>
        private readonly List<HazardMarker> listed = new List<HazardMarker>();

        /// <summary>
        /// 選択中のマーカー。位置ではなく実体で覚える。
        /// 時刻を書き換えると並び順が変わるため、位置では追えなくなる。
        /// </summary>
        private HazardMarker? selectedMarker;

        // 入力欄は 1 組。一覧でマーカーを選ぶとその内容が入り、
        // Update で選択中のマーカーへ、Add で新しいマーカーとして反映する
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
            set
            {
                minutes = value;
                NotifyChanged(nameof(Minutes));
            }
        }

        [UIValue("seconds")]
        public int Seconds
        {
            get => seconds;
            set
            {
                seconds = value;
                NotifyChanged(nameof(Seconds));
            }
        }

        /// <summary>
        /// マーカーの表示名。空なら種別ごとの既定（WALL / FAIL / MARK）になる。
        /// </summary>
        [UIValue("label")]
        public string Label
        {
            get => label;
            set
            {
                label = value ?? string.Empty;
                NotifyChanged(nameof(Label));
            }
        }

        // ───── 挙動の設定 ─────
        // Counters+ の画面はカウンターの配置を決める場所なので、そちらには見た目だけを置き、
        // 記録の取り方やカウントダウンの秒数はここ（曲選択画面）に集約する。

        [UIValue("lead-time")]
        public float LeadTimeSeconds
        {
            get => PluginConfig.Instance.LeadTimeSeconds;
            set => PluginConfig.Instance.LeadTimeSeconds = value;
        }

        [UIValue("cluster-threshold")]
        public float ClusterThresholdSeconds
        {
            get => PluginConfig.Instance.ClusterThresholdSeconds;
            set => PluginConfig.Instance.ClusterThresholdSeconds = value;
        }

        [UIValue("record-wall-hits")]
        public bool RecordWallHits
        {
            get => PluginConfig.Instance.RecordWallHits;
            set => PluginConfig.Instance.RecordWallHits = value;
        }

        [UIValue("record-fails")]
        public bool RecordFails
        {
            get => PluginConfig.Instance.RecordFails;
            set => PluginConfig.Instance.RecordFails = value;
        }

        [UIValue("show-fail-marker")]
        public bool ShowFailMarker
        {
            get => PluginConfig.Instance.ShowFailMarker;
            set => PluginConfig.Instance.ShowFailMarker = value;
        }

        [UIValue("auto-import-replays")]
        public bool AutoImportReplays
        {
            get => PluginConfig.Instance.AutoImportReplays;
            set => PluginConfig.Instance.AutoImportReplays = value;
        }

        [UIValue("FormatSeconds")]
        public string FormatSeconds(float value) => $"{value:F1} s";

        [UIAction("#post-parse")]
        public void PostParse() => Refresh();

        [UIAction("marker-selected")]
        public void OnMarkerSelected(TableView _, int index)
        {
            selectedMarker = index >= 0 && index < listed.Count ? listed[index] : null;
            if (selectedMarker != null)
            {
                // 選んだマーカーの内容を入力欄へ読み込む。そのまま Update で書き戻せる
                var marker = selectedMarker;
                var total = (int)marker.SongTime;
                Label = marker.Label ?? string.Empty;
                Minutes = total / 60;
                Seconds = total % 60;

                // ホスト側の値を変えただけでは表示が古いままなので、読み直させる。
                // これを忘れると、画面の値と実際に使われる値が食い違う
                parserParams?.EmitEvent(RefreshValuesEvent);
            }
            RefreshStatus();
        }

        [UIAction("delete-selected")]
        public void DeleteSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            var marker = selectedMarker;
            if (marker == null)
            {
                actionMessage = "Select a marker to delete";
                RefreshStatus();
                return;
            }

            if (!set.Remove(marker)) return;

            actionMessage = marker.Imported
                ? $"Deleted {FormatTime(marker.SongTime)} - Import restores it"
                : $"Deleted {FormatTime(marker.SongTime)}";

            selectedMarker = null;
            Persist();
            Refresh();
        }

        /// <summary>選択中のマーカーへ、入力欄の時刻と名前を書き戻す。</summary>
        [UIAction("update-selected")]
        public void UpdateSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            if (selectedMarker == null)
            {
                actionMessage = "Select a marker to update";
                RefreshStatus();
                return;
            }

            // 選択は解除しない。続けて微調整できるようにする
            if (set.Update(selectedMarker, minutes * 60 + seconds, label))
            {
                actionMessage = $"Updated to {minutes}:{seconds:00}";
                Persist();
            }
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

            selectedMarker = null;
            actionMessage = "Deleted all - auto import disabled";
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
            selectedMarker = null;
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

            // 並べ直しで位置が変わるので、実体から探し直して選択を戻す
            var selectedIndex = selectedMarker != null ? listed.IndexOf(selectedMarker) : -1;
            if (selectedIndex >= 0)
            {
                markerList.TableView.SelectCellWithIdx(selectedIndex, false);
                markerList.TableView.ScrollToCellWithIdx(
                    selectedIndex, TableView.ScrollPositionType.Center, false);
            }
            else
            {
                selectedMarker = null;
                markerList.TableView.ClearSelection();
            }
        }

        /// <summary>
        /// 状態表示は 1 行だけにする。マーカーの内訳は Markers タブの一覧で見えるので、
        /// 件数を並べても場所を取るだけだった。
        /// </summary>
        private void RefreshStatus()
        {
            var set = CurrentSet();
            if (set == null)
            {
                SetStatus("No beatmap selected");
                return;
            }

            if (actionMessage != null)
            {
                SetStatus(actionMessage);
                return;
            }

            if (set.ImportedReplayCount > 0)
            {
                SetStatus($"Imported from {set.ImportedReplayCount} replay(s)");
                return;
            }

            var key = SelectedBeatmapTracker.Current;
            var available = key.HasValue ? ReplayImportService.CountAvailable(key.Value) : 0;
            SetStatus(available > 0 ? $"{available} replay(s) available" : "No replays");
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

