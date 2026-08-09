using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace HazardTimer.Markers
{
    /// <summary>
    /// 1 譜面（ハッシュ + characteristic + 難易度）分のマーカー集合。
    /// 常に譜面時間の昇順で保持する。
    /// </summary>
    public class BeatmapMarkerSet
    {
        [JsonProperty("markers")]
        private readonly List<HazardMarker> markers = new List<HazardMarker>();

        [JsonIgnore]
        public IReadOnlyList<HazardMarker> Markers => markers;

        /// <summary>フェイルマーカー（1 譜面に最大 1 点）。無ければ null。</summary>
        [JsonIgnore]
        public HazardMarker? FailMarker => markers.FirstOrDefault(m => m.Source == MarkerSource.Fail);

        /// <summary>取り込み元になったリプレイの件数。0 なら未取り込み。</summary>
        [JsonProperty("importedReplays")]
        public int ImportedReplayCount { get; set; }

        /// <summary>
        /// 自動取り込みの対象外にするか。手動で記録を全消しした譜面に立てる。
        /// </summary>
        /// <remarks>
        /// これを永続化しないと、消してもゲームを再起動した時点で自動取り込みが
        /// 何事もなかったように戻してしまう。消したという意思のほうを残す。
        /// </remarks>
        [JsonProperty("autoImportSuppressed")]
        public bool AutoImportSuppressed { get; set; }

        /// <summary>
        /// 実測した壁マーカーを追加する。閾値以内に既存の壁マーカーがあれば統合し、
        /// より早い方の時刻を先頭として採用する。
        /// 取り込みマーカーと重なった場合は、進入時刻が確かな実測で置き換える。
        /// </summary>
        /// <returns>集合の内容が変化したら true。</returns>
        public bool AddWall(float songTime, float thresholdSeconds)
        {
            var existing = NearestWall(songTime, thresholdSeconds);
            if (existing == null)
            {
                Insert(new HazardMarker(songTime, MarkerSource.Wall));
                return true;
            }

            if (existing.Imported)
            {
                // 取り込みマーカーは実測に譲る。件数は引き継がない。
                // ただし時刻は早い方を残す。後ろへずらすと、その分だけ警告が遅れる
                existing.SongTime = Math.Min(songTime, existing.SongTime);
                existing.Imported = false;
                existing.HitCount = 1;
                Sort();
                return true;
            }

            existing.HitCount++;
            if (songTime < existing.SongTime)
            {
                existing.SongTime = songTime;
                Sort();
            }
            return true;
        }

        /// <summary>
        /// リプレイから取り込んだ壁マーカーを追加する。
        /// 同じ地点に実測マーカーが既にあれば、そちらを信用して何もしない。
        /// </summary>
        public bool AddImportedWall(float songTime, float thresholdSeconds)
        {
            var existing = NearestWall(songTime, thresholdSeconds);
            if (existing == null)
            {
                Insert(new HazardMarker(songTime, MarkerSource.Wall, imported: true));
                return true;
            }

            if (!existing.Imported) return false;

            existing.HitCount++;
            if (songTime < existing.SongTime)
            {
                existing.SongTime = songTime;
                Sort();
            }
            return true;
        }

        /// <summary>
        /// フェイル地点を設定する。1 譜面に 1 点しか持たないので既存があれば置き換える。
        /// 取り込みは既存の実測を上書きしない。
        /// </summary>
        public bool SetFail(float songTime, bool imported = false)
        {
            var existing = FailMarker;
            if (existing != null)
            {
                if (imported && !existing.Imported) return false;
                if (Math.Abs(existing.SongTime - songTime) < 0.001f && existing.Imported == imported) return false;
                markers.Remove(existing);
            }
            Insert(new HazardMarker(songTime, MarkerSource.Fail, imported));
            return true;
        }

        /// <summary>取り込みで作られたマーカーだけを消す。再取り込みの前処理。</summary>
        public bool RemoveImported()
        {
            var removed = markers.RemoveAll(m => m.Imported) > 0;
            if (removed || ImportedReplayCount != 0)
            {
                ImportedReplayCount = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 手動マーカーを追加する。ほぼ同じ時刻に既にあれば何もしない。
        /// 手動指定は意図的な操作なので、壁のようなクラスタ統合は行わない。
        /// </summary>
        public bool AddManual(float songTime)
        {
            if (songTime < 0f) return false;
            if (markers.Any(m => m.Source == MarkerSource.Manual
                                 && Math.Abs(m.SongTime - songTime) < 0.05f))
            {
                return false;
            }
            Insert(new HazardMarker(songTime, MarkerSource.Manual));
            return true;
        }

        public bool Remove(HazardMarker marker) => markers.Remove(marker);

        public bool RemoveAll(MarkerSource source) => markers.RemoveAll(m => m.Source == source) > 0;

        public bool Clear()
        {
            if (markers.Count == 0 && ImportedReplayCount == 0) return false;
            markers.Clear();
            // 取り込み済みの印も消す。消した直後に再取り込みできる状態に戻す
            ImportedReplayCount = 0;
            return true;
        }

        [JsonIgnore]
        public int Count => markers.Count;

        /// <summary>閾値以内で最も近い既存の壁マーカー。無ければ null。</summary>
        private HazardMarker? NearestWall(float songTime, float thresholdSeconds)
        {
            HazardMarker? best = null;
            var bestDistance = thresholdSeconds;
            foreach (var m in markers)
            {
                if (m.Source != MarkerSource.Wall) continue;
                var distance = Math.Abs(m.SongTime - songTime);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = m;
                }
            }
            return best;
        }

        private void Insert(HazardMarker marker)
        {
            markers.Add(marker);
            Sort();
        }

        private void Sort() => markers.Sort((a, b) => a.SongTime.CompareTo(b.SongTime));

        /// <summary>読み込み直後に順序を保証する（手書き編集されたファイルへの保険）。</summary>
        internal void Normalize() => Sort();
    }
}
