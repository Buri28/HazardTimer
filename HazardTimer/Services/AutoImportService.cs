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

        /// <summary>
        /// 直前のプレイの取り込みを、メニューへ戻ってから何回まで試すか。
        /// </summary>
        /// <remarks>
        /// BeatLeader がリプレイを書き出すのはリザルト画面の前後で、
        /// メニューの初期化より後になることがある。1 回試して終わりだと、
        /// そのプレイぶんだけ取り込まれないまま残る。
        /// </remarks>
        private const int LastPlayedRetryCount = 8;

        /// <summary>次の再試行までの待ち時間。試すたびに倍にする。</summary>
        /// <remarks>
        /// 再試行のたびにリプレイ索引を作り直すため、等間隔で詰めると
        /// メニューにいる間ずっとディレクトリ走査が走る。間隔を広げて回数を抑える。
        /// </remarks>
        private const float LastPlayedFirstDelaySeconds = 1.0f;

        /// <summary>再試行の間隔の上限。</summary>
        /// <remarks>
        /// 倍にし続けると最後の 1 回だけが極端に遠くなる。書き出しはリザルト画面の
        /// 前後なので、そこに留まっている間ずっと待てるよう、頭打ちにして
        /// 全体の待ち時間（およそ 90 秒）を稼ぐ。
        /// </remarks>
        private const float LastPlayedMaxDelaySeconds = 30.0f;

        private int pendingSaves;

        /// <summary>取り込みを待っている直前のプレイ。終わったら null。</summary>
        private BeatmapKey? pendingLastPlayed;

        private int lastPlayedAttemptsLeft;
        private float nextLastPlayedAttemptTime;
        private float lastPlayedDelay;

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
            SelectedBeatmapTracker.Polled += OnPolled;

            // 選択追跡はこのサービスより先に初期化され、最初の選択通知を撃ち終えている。
            // プレイ直後に戻ってきたときの「今まさに選ばれている譜面」がそれなので、
            // ここで拾い直さないと、いま遊んだ譜面だけが取り込まれない
            OnSelectionChanged();

            pendingLastPlayed = HazardMarkerSession.LastPlayedBeatmapKey;
            lastPlayedAttemptsLeft = LastPlayedRetryCount;
            lastPlayedDelay = LastPlayedFirstDelaySeconds;
            TryImportLastPlayed();
        }

        /// <summary>
        /// 直前のプレイのリプレイが書き出されるのを待って、取り込みを試し直す。
        /// </summary>
        private void OnPolled()
        {
            if (!pendingLastPlayed.HasValue) return;
            if (UnityEngine.Time.unscaledTime < nextLastPlayedAttemptTime) return;

            // 索引はメニュー入場時に作り直したきり。その時点でまだ書き出されていない
            // リプレイは載っていないので、試すたびに作り直す
            ReplayFileIndex.Invalidate();
            TryImportLastPlayed();
        }

        /// <summary>
        /// 直前に遊んだ譜面を取り込む。
        /// </summary>
        /// <remarks>
        /// MOD 独自の選曲画面から遊んだ場合、曲を選んだ時点では譜面が分からず
        /// 自動取り込みが働かない。遊んだ事実だけは残るので、メニューへ戻ったここで拾う。
        /// プレイ中ではないので、解析の重さがフレームレートに響かない。
        /// </remarks>
        private void TryImportLastPlayed()
        {
            // 待ち時間は先に進めておく。どの経路で抜けても再試行の間隔が空くように
            if (--lastPlayedAttemptsLeft <= 0)
            {
                pendingLastPlayed = null;
            }
            else
            {
                nextLastPlayedAttemptTime = UnityEngine.Time.unscaledTime + lastPlayedDelay;
                lastPlayedDelay = Math.Min(lastPlayedDelay * 2f, LastPlayedMaxDelaySeconds);
            }

            if (!PluginConfig.Instance.AutoImportReplays) { pendingLastPlayed = null; return; }

            var key = HazardMarkerSession.LastPlayedBeatmapKey;
            if (!key.HasValue) { pendingLastPlayed = null; return; }
            if (Suppressed.Contains(BeatmapMarkerKey.From(key.Value))) { pendingLastPlayed = null; return; }

            // リプレイがまだ無い＝書き出しを待っている状態。ここで打ち切らずに再試行へ回す
            if (ReplayImportService.CountAvailable(key.Value) == 0) return;

            var set = MarkerStore.Instance.GetOrCreate(key.Value);
            if (set.AutoImportSuppressed) { pendingLastPlayed = null; return; }

            // 未読が無いのは「今のプレイのリプレイがまだ書き出されていない」場合も含む。
            // 2 回目以降は前回のリプレイが残っているため、件数だけでは待ち状態と
            // 区別できず、ここで打ち切ると直前のプレイぶんが取り込まれない
            if (!ReplayImportService.NeedsImport(key.Value, set)) return;

            var result = ReplayImportService.Import(key.Value, set);
            if (result.ReplayCount == 0) return;

            // 取り込めたので、これ以上の再試行は要らない
            pendingLastPlayed = null;

            MarkerStore.Instance.MarkDirty();
            Flush();
            Plugin.Log?.Info($"Imported {result.ReplayCount} replay(s) for the beatmap just played.");

            // 遊んだ直後は、その譜面の一覧を開いたままメニューへ戻ってくることが多い。
            // 通知しないと、増えたマーカーが画面に出ない
            ImportCompleted?.Invoke();
        }

        public void Dispose()
        {
            SelectedBeatmapTracker.SelectionChanged -= OnSelectionChanged;
            SelectedBeatmapTracker.Polled -= OnPolled;
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

            var result = ReplayImportService.Import(key.Value, set);
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

