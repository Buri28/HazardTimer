using System;
using HazardTimer.Markers;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>
    /// プレイ中の 1 譜面分のマーカー集合を保持する。
    /// 記録側（<see cref="HazardRecorder"/>）と表示側（<see cref="CountdownService"/>）が
    /// この 1 つの状態を共有する。
    /// </summary>
    public class HazardMarkerSession : IInitializable, IDisposable
    {
        [Inject] private readonly GameplayCoreSceneSetupData sceneSetupData = null!;

        private BeatmapMarkerSet? markerSet;
        private bool changed;

        /// <summary>現在の譜面のマーカー集合。初期化前は null。</summary>
        public BeatmapMarkerSet? Markers => markerSet;

        /// <summary>マーカー集合が更新されたときに発火する。</summary>
        public event Action? MarkersChanged;

        public void Initialize()
        {
            markerSet = MarkerStore.Instance.GetOrCreate(sceneSetupData.beatmapKey);

            // 自動取り込みで溜まった未保存ぶんを、プレイに入る前に確定させる
            MarkerStore.Instance.Save();
        }

        /// <summary>マーカーを変更した後に呼ぶ。保存予約と通知を行う。</summary>
        public void NotifyChanged()
        {
            changed = true;
            MarkerStore.Instance.MarkDirty();
            MarkersChanged?.Invoke();
        }

        public void Dispose()
        {
            // プレイ終了時にまとめて書き出す。プレイ中の I/O を避けるため
            if (changed) MarkerStore.Instance.Save();

            // このプレイのリプレイが増えているので、索引を作り直させる
            Replay.ReplayFileIndex.Invalidate();

            markerSet = null;
            MarkersChanged = null;
        }
    }
}
