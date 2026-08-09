using System;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>
    /// 曲選択画面で今どの譜面が選ばれているかを追跡する。
    /// 手動マーカーの追加先を決めるために必要。
    /// </summary>
    public class SelectedBeatmapTracker : IInitializable, IDisposable
    {
        [Inject(Optional = true)] private readonly StandardLevelDetailViewController? levelDetail = null;

        private static SelectedBeatmapTracker? active;

        /// <summary>現在選択中の譜面。メニュー外にいる間は null。</summary>
        public static BeatmapKey? Current { get; private set; }

        /// <summary>現在選択中の曲名。表示用。</summary>
        public static string CurrentSongName { get; private set; } = string.Empty;

        /// <summary>選択が変わったときに発火する。</summary>
        public static event Action? SelectionChanged;

        public void Initialize()
        {
            active = this;
            if (levelDetail == null)
            {
                Plugin.Log?.Warn("StandardLevelDetailViewController not available; manual markers are disabled.");
                return;
            }

            levelDetail.didChangeContentEvent += OnContentChanged;
            levelDetail.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            Refresh();
        }

        private void OnContentChanged(StandardLevelDetailViewController _, StandardLevelDetailViewController.ContentType __)
            => Refresh();

        private void OnDifficultyChanged(StandardLevelDetailViewController _) => Refresh();

        private void Refresh()
        {
            if (levelDetail == null) return;

            var key = levelDetail.beatmapKey;
            if (!key.IsValid())
            {
                Set(null, string.Empty);
                return;
            }

            Set(key, levelDetail.beatmapLevel?.songName ?? string.Empty);
        }

        private static void Set(BeatmapKey? key, string songName)
        {
            Current = key;
            CurrentSongName = songName;
            SelectionChanged?.Invoke();
        }

        public void Dispose()
        {
            if (levelDetail != null)
            {
                levelDetail.didChangeContentEvent -= OnContentChanged;
                levelDetail.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
            }
            if (active == this)
            {
                active = null;
                Set(null, string.Empty);
            }
        }
    }
}
