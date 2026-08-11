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
        // マルチプレイのコンテナでも解決できるか分からないので必須にしない。
        // 取れなければマーカーを持たないだけで、他のMODの初期化を巻き込んで落とさない
        [Inject(Optional = true)] private readonly GameplayCoreSceneSetupData? sceneSetupData = null;

        private BeatmapMarkerSet? markerSet;
        private bool changed;

        /// <summary>
        /// 直前にプレイした譜面。メニューへ戻ったときの取り込み対象に使う。
        /// </summary>
        /// <remarks>
        /// MOD 独自の選曲画面（AccSaber のキャンペーンなど）では選択中の譜面を追えないので、
        /// 曲選択時の自動取り込みが働かない。プレイ開始時に取り込むのは
        /// FPS 半減の判定に触れるので避けたい。
        /// 実際に遊んだ譜面だけはここで覚えておき、メニューに戻ってから取り込む。
        /// </remarks>
        public static BeatmapKey? LastPlayedBeatmapKey { get; private set; }

        /// <summary>現在の譜面のマーカー集合。初期化前は null。</summary>
        public BeatmapMarkerSet? Markers => markerSet;

        /// <summary>マーカー集合が更新されたときに発火する。</summary>
        public event Action? MarkersChanged;

        public void Initialize()
        {
            if (sceneSetupData == null)
            {
                Plugin.Log?.Warn("GameplayCoreSceneSetupData unavailable; markers are disabled for this session.");
                return;
            }

            LastPlayedBeatmapKey = sceneSetupData.beatmapKey;
            markerSet = MarkerStore.Instance.GetOrCreate(sceneSetupData.beatmapKey);

            // 自動取り込みで溜まった未保存ぶんを、プレイに入る前に確定させる。
            // ここでリプレイの解析はしない。プレイ開始直後は FPS 半減の判定が入るため、
            // 少しでも重い処理を挟むとフレームレートが落ちたまま固定されてしまう
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
