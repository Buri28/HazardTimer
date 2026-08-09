using System;
using System.Collections.Generic;
using System.Linq;
using HazardTimer.Services;

namespace HazardTimer.Replay
{
    /// <summary>
    /// リプレイのイベント列を時系列に流し込み、エネルギーがゼロになった時刻を求める。
    /// </summary>
    /// <remarks>
    /// NoFail のリプレイにはフェイル時刻が入っていない（ゲーム本体が計算していない）ため、
    /// 「NoFail が無ければどこで落ちていたか」はここで再現するしかない。
    /// </remarks>
    public static class ReplayEnergyEstimator
    {
        private const int NoteEventBad = 1;
        private const int NoteEventMiss = 2;
        private const int NoteEventBomb = 3;

        /// <summary>この模擬が扱えないモディファイア。規則が別物になる。</summary>
        private static readonly string[] UnsupportedModifiers = { "BE", "IF", "SC" };

        /// <summary>
        /// エネルギーがゼロになる曲内時刻を求める。落ちなければ null。
        /// </summary>
        public static float? EstimateFailTime(BsorReplay replay)
        {
            if (IsUnsupported(replay.Modifiers)) return null;

            var energy = new EnergySimulator();

            // 壁の区間とノートを 1 本の時間軸にまとめる。
            // 壁は継続的な減少なので、区間の途中でゼロを跨ぐ場合は交差時刻を補間して返す
            var boundaries = BuildBoundaries(replay);

            var noteIndex = 0;
            var notes = replay.NoteEvents;

            for (var i = 0; i < boundaries.Count - 1; i++)
            {
                var segmentStart = boundaries[i];
                var segmentEnd = boundaries[i + 1];

                // 区間の開始時刻ちょうどにあるノートを先に処理する
                while (noteIndex < notes.Count && notes[noteIndex].Time <= segmentStart)
                {
                    if (ApplyNote(energy, notes[noteIndex])) return notes[noteIndex].Time;
                    noteIndex++;
                }

                var drainRate = ObstacleDrainRateAt(replay.ObstacleIntervals, segmentStart, segmentEnd);
                if (drainRate <= 0f) continue;

                var secondsToZero = energy.SecondsUntilZeroInObstacle();
                var segmentSeconds = segmentEnd - segmentStart;

                if (secondsToZero.HasValue && secondsToZero.Value <= segmentSeconds)
                {
                    energy.ApplyObstacleTime(segmentSeconds);
                    return segmentStart + secondsToZero.Value;
                }

                energy.ApplyObstacleTime(segmentSeconds);
            }

            // 残ったノートを処理する
            for (; noteIndex < notes.Count; noteIndex++)
            {
                if (ApplyNote(energy, notes[noteIndex])) return notes[noteIndex].Time;
            }

            return null;
        }

        private static bool ApplyNote(EnergySimulator energy, ReplayNoteEvent note) => note.EventType switch
        {
            NoteEventBomb => energy.ApplyBombHit(),
            NoteEventMiss => energy.ApplyNoteMiss(note.IsChainLink),
            NoteEventBad => energy.ApplyNoteCut(note.IsChainLink, allIsOK: false),
            _ => energy.ApplyNoteCut(note.IsChainLink, allIsOK: true),
        };

        /// <summary>
        /// 区間 [start, end) の間ずっと有効な壁の減少レート。
        /// 重なった壁は本体と同じくフレームごとに 1 回しか引かれないため、重複は数えない。
        /// </summary>
        private static float ObstacleDrainRateAt(List<ReplayObstacleInterval> intervals, float start, float end)
        {
            var middle = (start + end) * 0.5f;
            foreach (var interval in intervals)
            {
                if (interval.Start <= middle && middle < interval.End)
                {
                    return EnergySimulator.ObstacleEnergyDrainPerSecond;
                }
            }
            return 0f;
        }

        /// <summary>ノート時刻と壁の境界を集めた、重複のない昇順の時刻列。</summary>
        private static List<float> BuildBoundaries(BsorReplay replay)
        {
            var boundaries = new List<float>(replay.NoteEvents.Count + replay.ObstacleIntervals.Count * 2);
            foreach (var note in replay.NoteEvents) boundaries.Add(note.Time);
            foreach (var interval in replay.ObstacleIntervals)
            {
                boundaries.Add(interval.Start);
                boundaries.Add(interval.End);
            }
            boundaries.Sort();
            return boundaries.Distinct().ToList();
        }

        private static bool IsUnsupported(string modifiers)
        {
            if (string.IsNullOrEmpty(modifiers)) return false;
            var applied = modifiers.Split(',');
            return applied.Any(m => UnsupportedModifiers.Contains(m.Trim(), StringComparer.OrdinalIgnoreCase));
        }
    }
}
