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

        /// <summary>カウントダウンに使われるマーカーの色。</summary>
        private const string ActiveColor = "#7CFC00";

        /// <summary>同じ危険地点の記録だが、カウントダウンには使われないものの色。</summary>
        private const string InactiveColor = "#909090";

        /// <summary>利用者が明示的に無効にしたものの色。</summary>
        private const string DisabledColor = "#585858";

        /// <summary>
        /// 選ばれてはいるが、種別の表示設定で切られているものの色。
        /// </summary>
        /// <remarks>
        /// 灰色にすると「近接した記録で選ばれなかった」ものと見分けが付かず、
        /// Turn On を押しても点かない理由が読み取れない。
        /// 有効色と同じ緑の系統で明度だけ落とし、「選ばれてはいるが出ない」ことを
        /// 一目で読めるようにする。
        /// </remarks>
        private const string HiddenBySettingColor = "#2E7D32";

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

        /// <summary>最後に一覧へ描いた内容の版。作り直しの要否を判定するのに使う。</summary>
        private int renderedVersion = -1;

        public ManualMarkerController()
        {
            SelectedBeatmapTracker.SelectionChanged += OnSelectionChanged;
            // 自動取り込みは選択変更のあとに走るので、その完了でも表示を更新する
            AutoImportService.ImportCompleted += Refresh;
            SelectedBeatmapTracker.Polled += OnPolled;
        }

        public void Dispose()
        {
            SelectedBeatmapTracker.SelectionChanged -= OnSelectionChanged;
            AutoImportService.ImportCompleted -= Refresh;
            SelectedBeatmapTracker.Polled -= OnPolled;
        }

        /// <summary>
        /// 内容が変わっていたら一覧を作り直す。
        /// </summary>
        /// <remarks>
        /// 同じ譜面を続けて遊ぶと選択が変わらないので、プレイ中に増えたマーカーを
        /// 反映する契機が無い。版を比べるだけなので、毎回呼ばれても負担にならない。
        /// </remarks>
        private void OnPolled()
        {
            var set = CurrentSetIfAny();
            var version = set?.Version ?? -1;
            if (version == renderedVersion) return;

            Refresh();
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
            set
            {
                PluginConfig.Instance.ClusterThresholdSeconds = value;
                // どれを 1 つの危険地点とみなすかが変わるので、判定をやり直す。
                // やらないと、次に何か編集するまで古い判定のまま使われる
                MarkerStore.Instance.RecomputeAll();
            }
        }

        [UIValue("show-fail-marker")]
        public bool ShowFailMarker
        {
            get => PluginConfig.Instance.ShowFailMarker;
            set
            {
                PluginConfig.Instance.ShowFailMarker = value;
                Refresh();
            }
        }

        // 表示するかどうかは全譜面に効く。切り替えたら一覧の色も変わるので描き直す

        [UIValue("show-miss-markers")]
        public bool ShowMissMarkers
        {
            get => PluginConfig.Instance.ShowMissMarkers;
            set
            {
                PluginConfig.Instance.ShowMissMarkers = value;
                Refresh();
            }
        }

        [UIValue("show-bomb-markers")]
        public bool ShowBombMarkers
        {
            get => PluginConfig.Instance.ShowBombMarkers;
            set
            {
                PluginConfig.Instance.ShowBombMarkers = value;
                Refresh();
            }
        }

        [UIValue("show-wall-markers")]
        public bool ShowWallMarkers
        {
            get => PluginConfig.Instance.ShowWallMarkers;
            set
            {
                PluginConfig.Instance.ShowWallMarkers = value;
                Refresh();
            }
        }

        [UIValue("auto-import-replays")]
        public bool AutoImportReplays
        {
            get => PluginConfig.Instance.AutoImportReplays;
            set => PluginConfig.Instance.AutoImportReplays = value;
        }

        [UIValue("max-import-replays")]
        public int MaxImportReplays
        {
            get => PluginConfig.Instance.MaxImportReplays;
            set => PluginConfig.Instance.MaxImportReplays = value;
        }

        [UIValue("max-miss-markers")]
        public int MaxMissMarkers
        {
            get => PluginConfig.Instance.MaxMissMarkers;
            set => PluginConfig.Instance.MaxMissMarkers = value;
        }

        [UIValue("max-bomb-markers")]
        public int MaxBombMarkers
        {
            get => PluginConfig.Instance.MaxBombMarkers;
            set => PluginConfig.Instance.MaxBombMarkers = value;
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

        /// <summary>
        /// 選択中のマーカーを必ず使う指定にする。時刻が重なるものは使わない指定になる。
        /// </summary>
        [UIAction("turn-on")]
        public void TurnOnSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            var marker = selectedMarker;
            if (marker == null)
            {
                actionMessage = "Select a marker to turn on";
                RefreshStatus();
                return;
            }

            actionMessage = set.TurnOn(marker)
                ? $"On: {FormatTime(marker.SongTime)}"
                : "Could not change the marker";
            Persist();
            Refresh();
        }

        /// <summary>選択中のマーカーを、消さずに使わない指定にする。</summary>
        [UIAction("turn-off")]
        public void TurnOffSelected()
        {
            var set = CurrentSet();
            if (set == null) return;

            var marker = selectedMarker;
            if (marker == null)
            {
                actionMessage = "Select a marker to turn off";
                RefreshStatus();
                return;
            }

            actionMessage = set.TurnOff(marker)
                ? $"Off: {FormatTime(marker.SongTime)}"
                : "Could not change the marker";
            Persist();
            Refresh();
        }

        /// <summary>この譜面のマーカーをすべて使わない指定にする。</summary>
        [UIAction("all-off")]
        public void AllOff()
        {
            var set = CurrentSet();
            if (set == null) return;

            actionMessage = set.AllOff() ? "All markers off" : "Nothing to turn off";
            Persist();
            Refresh();
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

            if (!set.Remove(marker))
            {
                // 一覧の再構築とすれ違って参照が古くなっている。選択を捨てて出し直す
                selectedMarker = null;
                actionMessage = "Select a marker to delete";
                Refresh();
                return;
            }

            // 取り込み済みのリプレイは読んだ印が残るので、Import を押しても戻らない。
            // 戻せるのは Delete All のあとだけ
            actionMessage = marker.Imported
                ? $"Deleted {FormatTime(marker.SongTime)} - not restored by Import"
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

            var marker = selectedMarker;
            if (marker == null)
            {
                actionMessage = "Select a marker to update";
                RefreshStatus();
                return;
            }

            var newTime = ResolveEditedTime(marker);
            if (!set.CanMoveTo(marker, newTime))
            {
                actionMessage = "Too close to another marker";
                RefreshStatus();
                return;
            }

            // 選択は解除しない。続けて微調整できるようにする
            actionMessage = set.Update(marker, newTime, label)
                ? $"Updated to {FormatTime(newTime)}"
                : "Could not update";
            Persist();
            Refresh();
        }

        /// <summary>
        /// 入力欄の分秒から、書き戻す時刻を決める。
        /// </summary>
        /// <remarks>
        /// 実測や取り込みのマーカーは秒未満の端数を持つ。入力欄は秒単位なので、
        /// 表示された値をそのまま書き戻すと端数が切り捨てられ、名前を変えただけで
        /// マーカーが最大 1 秒手前へ動いてしまう。表示上は同じ秒に見えるので気づけない。
        /// 分秒が読み込んだときのままなら、元の時刻を保つ。
        /// </remarks>
        private float ResolveEditedTime(HazardMarker marker)
        {
            var entered = minutes * 60 + seconds;
            return (int)marker.SongTime == entered ? marker.SongTime : entered;
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
            else
            {
                // 何も起きなかったことを伝えないと、前回の成功メッセージが残って
                // 追加できたように見えてしまう
                actionMessage = "A manual marker is already there";
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

            // 押した時点で読み直す。索引はプレイ終了時とメニュー入場時にしか
            // 作り直しておらず、BeatLeader の書き出しと前後すると
            // 直前のプレイのリプレイが載っていないことがある
            ReplayFileIndex.Invalidate();

            var set = MarkerStore.Instance.GetOrCreate(key.Value);
            var result = ReplayImportService.Import(key.Value, set);

            if (result.ReplayCount == 0)
            {
                // 取り込みは未読のリプレイだけを読む。既に全部読んでいるなら、
                // 何も起きないのが正しい動作なので、失敗として見せない
                if (set.ImportedReplayCount > 0)
                {
                    AutoImportService.Allow(key.Value);
                    set.AutoImportSuppressed = false;
                    Persist();
                    SetStatus($"Up to date ({set.ImportedReplayCount} replay(s) imported)");
                    Refresh();
                    return;
                }

                // 抑制は解除しない。取り込めていないのに解除すると、
                // メモリ上とファイルで状態が食い違い、あとから勝手に書き戻る
                SetStatus("No readable replays");
                return;
            }

            // 取り込めたときだけ、以後の自動取り込みも許可する
            AutoImportService.Allow(key.Value);
            set.AutoImportSuppressed = false;

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

        /// <summary>入力欄を空に戻し、表示にも反映させる。</summary>
        private void ClearInputs()
        {
            Label = string.Empty;
            Minutes = 0;
            Seconds = 0;
            parserParams?.EmitEvent(RefreshValuesEvent);
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

            // 譜面が変わったときだけ入力欄を捨てる。
            // 画面遷移で一時的に譜面を見失うことがあり、そこで消すと打ち込んだ値が飛ぶ。
            // 別の譜面に移った場合は、打ち込んでいない時刻で Add できてしまうので消す
            if (SelectedBeatmapTracker.Current.HasValue) ClearInputs();

            Refresh();
        }

        /// <summary>編集対象の集合。無ければ作る。書き換える操作から呼ぶ。</summary>
        private static BeatmapMarkerSet? CurrentSet()
        {
            var key = SelectedBeatmapTracker.Current;
            return key.HasValue ? MarkerStore.Instance.GetOrCreate(key.Value) : null;
        }

        /// <summary>
        /// 表示用の集合。存在しなければ作らずに null を返す。
        /// 定期的に呼ばれるので、曲を眺めただけの譜面を辞書に残さない。
        /// </summary>
        private static BeatmapMarkerSet? CurrentSetIfAny()
        {
            var key = SelectedBeatmapTracker.Current;
            return key.HasValue ? MarkerStore.Instance.Find(key.Value) : null;
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

            var set = CurrentSetIfAny();
            if (set != null) listed.AddRange(set.Markers);
            renderedVersion = set?.Version ?? -1;

            // パース前に選択変更が飛んでくることがあるので、部品が揃うまで触らない
            if (markerList == null || markerList.Data == null || markerList.TableView == null) return;

            markerList.Data.Clear();
            foreach (var marker in listed)
            {
                // 一覧の幅が狭いので短くまとめる。長いと途中で切れて、
                // 一番大事な On / Off が読めなくなる
                var source = DescribeSource(marker);

                // カウントダウンに使われる 1 つを色で示す。
                // 近接した記録は全部残しているので、どれが選ばれたのか見えないと選び直せない。
                // 色を付けられるのは本文だけ。サブテキストはリッチテキストが効かず、
                // タグがそのまま表示されてしまう
                var color = marker.State == MarkerState.Off ? DisabledColor
                          : !marker.IsActive ? InactiveColor
                          : PluginConfig.Instance.IsShown(marker.Source) ? ActiveColor
                          : HiddenBySettingColor;

                markerList.Data.Add(new CustomListTableData.CustomCellInfo(
                    $"<color={color}>{FormatTime(marker.SongTime)}  {marker.DisplayLabel}</color>",
                    source));
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
            var set = CurrentSetIfAny();
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

        /// <summary>
        /// 一覧のサブテキスト。種別・回数・出所・指定を短くまとめる。
        /// </summary>
        /// <remarks>
        /// ミスの回数は「何回のプレイでそこを落としたか」。多いほど本当の難所。
        /// ミスは取り込みでしか作られないので、出所は書かない。
        /// </remarks>
        private static string DescribeSource(HazardMarker marker)
        {
            var parts = new List<string> { marker.Source.ToString() };

            if (marker.HitCount > 1) parts.Add($"x{marker.HitCount}");
            if (marker.Imported
                && marker.Source != MarkerSource.Miss
                && marker.Source != MarkerSource.Bomb)
            {
                parts.Add("Imp");
            }

            if (marker.State == MarkerState.On) parts.Add("On");
            else if (marker.State == MarkerState.Off) parts.Add("Off");

            // 色だけでは理由まで伝わらないので、設定で切られていることは字でも出す
            if (marker.State != MarkerState.Off
                && marker.IsActive
                && !PluginConfig.Instance.IsShown(marker.Source))
            {
                parts.Add("Hidden");
            }

            return string.Join(" ", parts);
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



