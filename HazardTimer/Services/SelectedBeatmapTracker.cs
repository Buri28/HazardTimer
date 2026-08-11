using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>
    /// 今どの譜面が選ばれているかを追跡する。
    /// 手動マーカーの追加先と、自動取り込みの対象を決めるために必要。
    /// </summary>
    /// <remarks>
    /// Zenject で受け取ったコントローラーのイベントを購読する形だと、標準の曲選択でしか動かない。
    /// MOD が用意した選曲画面（AccSaber のキャンペーンなど）は自前のインスタンスを使うため、
    /// 注入されたものには変化が来ない。AccSaber 自身が
    /// <c>FindObjectOfType&lt;StandardLevelDetailViewController&gt;()</c> で探しているのも同じ理由。
    /// <para>
    /// そこで、いま画面に出ているコントローラーを定期的に探しに行く方式にした。
    /// 探索結果は使えなくなるまで持ち回すので、走査そのものはめったに起きない。
    /// </para>
    /// </remarks>
    public class SelectedBeatmapTracker : IInitializable, IDisposable
    {
        /// <summary>選択を見に行く間隔。曲を切り替えてから表示が追いつくまでの遅れになる。</summary>
        private const float PollIntervalSeconds = 0.25f;

        /// <summary>
        /// 探索が空振りしたあと、次に探すまでの待ち時間。
        /// </summary>
        /// <remarks>
        /// <see cref="Resources.FindObjectsOfTypeAll{T}"/> は読み込み済みオブジェクトを
        /// 走査するので安くない。曲選択にいない間は毎回空振りするため、
        /// 間隔を空けないと待っているだけで走査し続けることになる。
        /// </remarks>
        private const float RescanBackoffSeconds = 2.0f;

        private float nextScanTime;

        /// <summary>この巡回で探索を許すか。1 巡につき 1 回だけ判定する。</summary>
        private bool scanning;

        [Inject] private readonly DiContainer container = null!;

        /// <summary>
        /// リーダーボードが表示中の譜面を保持しているフィールド。
        /// </summary>
        /// <remarks>
        /// MOD 独自の選曲画面でも、リーダーボードを出す以上はゲーム本体の
        /// <c>PlatformLeaderboardViewController</c> に譜面を渡すことになる。
        /// 公開されていないので直接は読めないが、ゲーム本体の型なので
        /// MOD の内部実装に踏み込むより壊れにくい。
        /// </remarks>
        private static readonly FieldInfo? LeaderboardBeatmapKeyField =
            typeof(PlatformLeaderboardViewController)
                .GetField("_beatmapKey", BindingFlags.NonPublic | BindingFlags.Instance);

        private SelectionPoller? poller;
        private StandardLevelDetailViewController? detailView;
        private LevelSelectionNavigationController? selectionNav;
        private PlatformLeaderboardViewController? leaderboard;

        private static SelectedBeatmapTracker? active;

        /// <summary>現在選択中の譜面。特定できない画面では null。</summary>
        public static BeatmapKey? Current { get; private set; }

        /// <summary>現在選択中の曲名。表示用。</summary>
        public static string CurrentSongName { get; private set; } = string.Empty;

        /// <summary>選択が変わったときに発火する。</summary>
        public static event Action? SelectionChanged;

        /// <summary>
        /// 選択を見に行くたびに発火する。変化の有無にかかわらず毎回呼ばれる。
        /// </summary>
        /// <remarks>
        /// 同じ譜面を続けて遊ぶと選択は変わらないので、プレイ中に増えたマーカーを
        /// 表示へ反映する契機が無くなる。表示側が安い判定で追随できるように出しておく。
        /// </remarks>
        public static event Action? Polled;

        public void Initialize()
        {
            active = this;

            poller = container.InstantiateComponentOnNewGameObject<SelectionPoller>(
                "HazardTimer_SelectionPoller");
            poller.Initialize(Poll, PollIntervalSeconds);

            Poll();
        }

        public void Dispose()
        {
            if (poller != null)
            {
                UnityEngine.Object.Destroy(poller.gameObject);
                poller = null;
            }

            if (active == this)
            {
                active = null;
                Set(null, string.Empty);
            }
        }

        private void Poll()
        {
            UpdateSelection();
            Polled?.Invoke();
        }

        private void UpdateSelection()
        {
            scanning = ShouldScan();

            // 譜面そのものを持っている詳細画面を優先し、無ければ 1 つ上の階層を見る
            var fromDetail = FindDetailView();
            if (fromDetail != null && TryTake(fromDetail.beatmapKey, fromDetail.beatmapLevel)) return;

            var fromSelection = FindSelectionNav();
            if (fromSelection != null && TryTake(fromSelection.beatmapKey, fromSelection.beatmapLevel)) return;

            // MOD 独自の選曲画面は上の 2 つを使わないことがある。
            // その場合でもリーダーボードには譜面が渡っているので、そこから拾う
            var fromLeaderboard = ReadLeaderboardBeatmapKey();
            if (fromLeaderboard.HasValue && TryTake(fromLeaderboard.Value, null)) return;

            Set(null, string.Empty);
        }

        private BeatmapKey? ReadLeaderboardBeatmapKey()
        {
            if (LeaderboardBeatmapKeyField == null) return null;

            if (!IsUsable(leaderboard))
            {
                if (!scanning) return null;
                leaderboard = FindActive<PlatformLeaderboardViewController>();
            }
            if (leaderboard == null) return null;

            try
            {
                return (BeatmapKey?)LeaderboardBeatmapKeyField.GetValue(leaderboard);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn($"Could not read the leaderboard beatmap: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 掴んだものが使えなくなったときだけ探し直す。
        /// 空振りが続く間は間隔を空け、走査そのものを減らす。
        /// </summary>
        private bool ShouldScan()
        {
            if (Time.unscaledTime < nextScanTime) return false;

            nextScanTime = Time.unscaledTime + RescanBackoffSeconds;
            return true;
        }

        private StandardLevelDetailViewController? FindDetailView()
        {
            if (IsUsable(detailView)) return detailView;
            if (!scanning) return null;

            detailView = FindActive<StandardLevelDetailViewController>();
            return detailView;
        }

        private LevelSelectionNavigationController? FindSelectionNav()
        {
            if (IsUsable(selectionNav)) return selectionNav;
            if (!scanning) return null;

            selectionNav = FindActive<LevelSelectionNavigationController>();
            return selectionNav;
        }

        /// <summary>
        /// いま画面に出ているものを探す。非表示のものまで含めて拾うと、
        /// 前の画面に残った古いインスタンスを掴んでしまう。
        /// </summary>
        private static T? FindActive<T>() where T : MonoBehaviour
            => Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(IsUsable);

        private static bool IsUsable<T>(T? behaviour) where T : MonoBehaviour
            => behaviour != null && behaviour.isActiveAndEnabled;

        private static bool TryTake(BeatmapKey key, BeatmapLevel? level)
        {
            if (!key.IsValid()) return false;

            // 曲名が取れない経路もある。表示用なので無くても支障はない
            Set(key, level?.songName ?? string.Empty);
            return true;
        }

        private static void Set(BeatmapKey? key, string songName)
        {
            // 定期的に見に行くので、変わっていないときに通知を出さないようにする
            if (!key.HasValue && !Current.HasValue) return;
            if (key.HasValue && Current.HasValue && key.Value == Current.Value) return;

            Current = key;
            CurrentSongName = songName;
            SelectionChanged?.Invoke();
        }
    }
}
