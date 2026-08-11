using System;
using Zenject;

namespace HazardTimer.Services
{
    /// <summary>
    /// 実測によるマーカー記録。壁への接触開始とフェイル地点を、
    /// 曲内時刻（譜面時間軸）で <see cref="HazardMarkerSession"/> に書き込む。
    /// </summary>
    public class HazardRecorder : IInitializable, IDisposable
    {
        [Inject] private readonly DiContainer container = null!;
        [Inject] private readonly HazardMarkerSession session = null!;
        [Inject] private readonly AudioTimeSyncController audioTimeSyncController = null!;

        [Inject(Optional = true)] private readonly PlayerHeadAndObstacleInteraction? interaction = null;
        [Inject(Optional = true)] private readonly GameEnergyCounter? energyCounter = null;
        [Inject(Optional = true)] private readonly BeatmapObjectManager? beatmapObjectManager = null;

        private ObstacleMonitor? obstacleMonitor;

        /// <summary>
        /// NoFail 中だけ動かす影のエネルギー計。本体はこのとき計算自体を行わないので、
        /// 「NoFail が無ければ落ちていた地点」はこれでしか分からない。
        /// </summary>
        private EnergySimulator? energySimulator;

        /// <summary>
        /// 同じ接触を何度も記録しないための最小間隔。
        /// 長い壁の中では接触判定が断続的に立つことがある。
        /// </summary>
        private const float MinimumEventGapSeconds = 0.25f;

        /// <summary>直前の接触イベントの時刻。</summary>
        private float lastObstacleEventTime = float.NegativeInfinity;

        public void Initialize()
        {
            if (!PluginConfig.Instance.RecordFails || energyCounter == null)
            {
                SetupObstacleMonitor();
                return;
            }

            if (ShouldSimulateEnergy())
            {
                energySimulator = new EnergySimulator();
                if (beatmapObjectManager != null)
                {
                    beatmapObjectManager.noteWasCutEvent += OnNoteWasCut;
                    beatmapObjectManager.noteWasMissedEvent += OnNoteWasMissed;
                }
                else
                {
                    Plugin.Log?.Warn("BeatmapObjectManager unavailable; fail point cannot be simulated.");
                }
            }
            else
            {
                energyCounter.gameEnergyDidReach0Event += OnEnergyDidReach0;
            }

            SetupObstacleMonitor();
        }

        /// <summary>
        /// 影のエネルギー計を動かすべきか。
        /// NoFail のときだけ必要で、バッテリー／インスタフェイルは規則が別なので対象外。
        /// </summary>
        private bool ShouldSimulateEnergy()
            => energyCounter != null
               && energyCounter.noFail
               && !energyCounter.instaFail
               && energyCounter.energyType == GameplayModifiers.EnergyType.Bar;

        private void SetupObstacleMonitor()
        {
            var needsObstacle = PluginConfig.Instance.RecordWallHits || energySimulator != null;
            if (!needsObstacle || interaction == null) return;

            obstacleMonitor = container.InstantiateComponentOnNewGameObject<ObstacleMonitor>(
                "HazardTimer_ObstacleMonitor");
            obstacleMonitor.Initialize(interaction, OnHeadEnteredObstacle, OnInsideObstacle);
        }

        private void OnNoteWasCut(NoteController noteController, in NoteCutInfo cutInfo)
        {
            if (energySimulator == null) return;

            var gameplayType = noteController.noteData.gameplayType;
            var reachedZero = gameplayType switch
            {
                NoteData.GameplayType.Bomb => energySimulator.ApplyBombHit(),
                NoteData.GameplayType.BurstSliderElement =>
                    energySimulator.ApplyNoteCut(isChainLink: true, cutInfo.allIsOK),
                _ => energySimulator.ApplyNoteCut(isChainLink: false, cutInfo.allIsOK),
            };

            if (reachedZero) RecordFail();
        }

        private void OnNoteWasMissed(NoteController noteController)
        {
            if (energySimulator == null) return;

            var gameplayType = noteController.noteData.gameplayType;
            // 爆弾は見逃しても減らない
            if (gameplayType == NoteData.GameplayType.Bomb) return;

            var isChainLink = gameplayType == NoteData.GameplayType.BurstSliderElement;
            if (energySimulator.ApplyNoteMiss(isChainLink)) RecordFail();
        }

        private void OnInsideObstacle(float deltaSeconds)
        {
            if (energySimulator == null) return;
            if (energySimulator.ApplyObstacleTime(deltaSeconds)) RecordFail();
        }

        private void OnHeadEnteredObstacle()
        {
            // エネルギー模擬のために監視が動いている場合があるので、ここでも設定を見る
            if (!PluginConfig.Instance.RecordWallHits) return;

            var set = session.Markers;
            if (set == null) return;

            var songTime = audioTimeSyncController.songTime;

            // 同じ壁の中で毎フレーム記録しないよう、ごく短い間隔だけ弾く。
            // 近接した記録をどう扱うか（どれを警告に使うか）は集合側が決める
            if (songTime - lastObstacleEventTime < MinimumEventGapSeconds) return;
            lastObstacleEventTime = songTime;

            if (set.AddWall(songTime))
            {
                session.NotifyChanged();
            }
        }

        private void OnEnergyDidReach0() => RecordFail();

        private void RecordFail()
        {
            var set = session.Markers;
            if (set == null) return;

            if (set.AddFail(audioTimeSyncController.songTime))
            {
                session.NotifyChanged();
            }
        }

        public void Dispose()
        {
            if (energyCounter != null)
            {
                energyCounter.gameEnergyDidReach0Event -= OnEnergyDidReach0;
            }
            if (beatmapObjectManager != null)
            {
                beatmapObjectManager.noteWasCutEvent -= OnNoteWasCut;
                beatmapObjectManager.noteWasMissedEvent -= OnNoteWasMissed;
            }
            if (obstacleMonitor != null)
            {
                UnityEngine.Object.Destroy(obstacleMonitor.gameObject);
                obstacleMonitor = null;
            }
        }
    }
}

