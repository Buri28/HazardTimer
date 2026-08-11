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
    /// <remarks>
    /// 近接したマーカーは捨てずに全部持ち、その中の 1 つだけを
    /// カウントダウンの対象（<see cref="HazardMarker.IsActive"/>）にする。
    /// 捨ててしまうと、どれが選ばれたのか利用者が確認できず、選び直すこともできない。
    /// </remarks>
    public class BeatmapMarkerSet
    {
        /// <summary>
        /// これ以内は同じ地点の記録とみなして 1 つにまとめる。
        /// </summary>
        /// <remarks>
        /// プレイ中に記録した時刻と、同じプレイのリプレイから取り込んだ時刻は
        /// 0.05 秒ほどずれる。これより狭くすると同じ接触が 2 つ並んでしまう。
        /// </remarks>
        private const float SameSpotSeconds = 0.2f;

        [JsonProperty("markers")]
        private readonly List<HazardMarker> markers = new List<HazardMarker>();

        [JsonIgnore]
        public IReadOnlyList<HazardMarker> Markers => markers;

        /// <summary>
        /// 取り込み済みのリプレイ（ファイル名の UNIX 秒）。
        /// </summary>
        /// <remarks>
        /// BeatLeader は同じ譜面の古いリプレイを消すことがある。実際に、3 回続けて
        /// 遊んだ譜面で毎回ファイルが 1 件しか残っていなかった。
        /// 取り込むたびにマーカーを作り直す方式だと、ファイルが消えた時点で
        /// それまでの記録も一緒に消える。読んだファイルを覚えておき、
        /// まだ読んでいないものだけを足していくことで、記録を積み上げる。
        /// </remarks>
        [JsonProperty("importedFiles")]
        private readonly List<long> importedTimestamps = new List<long>();

        /// <summary>取り込み済みのリプレイ件数。0 なら未取り込み。</summary>
        [JsonIgnore]
        public int ImportedReplayCount => importedTimestamps.Count;

        /// <summary>このリプレイを既に読んでいるか。</summary>
        public bool HasImported(long timestamp) => importedTimestamps.Contains(timestamp);

        /// <summary>このリプレイを読んだ印を付ける。</summary>
        public void MarkImported(long timestamp)
        {
            if (importedTimestamps.Contains(timestamp)) return;
            importedTimestamps.Add(timestamp);
        }

        /// <summary>
        /// 自動取り込みの対象外にするか。手動で記録を全消しした譜面に立てる。
        /// </summary>
        [JsonProperty("autoImportSuppressed")]
        public bool AutoImportSuppressed { get; set; }

        /// <summary>カウントダウンの対象になっているフェイルマーカー。無ければ null。</summary>
        [JsonIgnore]
        public HazardMarker? ActiveFail =>
            markers.FirstOrDefault(m => m.Source == MarkerSource.Fail && m.IsActive);

        [JsonIgnore]
        public int Count => markers.Count;

        /// <summary>
        /// 内容が変わるたびに増える番号。
        /// 表示側が「作り直す必要があるか」を安く判定するために使う。
        /// </summary>
        [JsonIgnore]
        public int Version { get; private set; }

        /// <summary>
        /// 実測した壁マーカーを追加する。
        /// ほぼ同じ時刻の記録があればまとめ、取り込みマーカーなら実測で置き換える。
        /// </summary>
        public bool AddWall(float songTime)
        {
            var existing = NearestSameSpot(songTime, MarkerSource.Wall);
            if (existing == null)
            {
                Insert(new HazardMarker(songTime, MarkerSource.Wall));
                return true;
            }

            // 取り込みは実測に譲る。時刻は早い方を残す
            if (existing.Imported)
            {
                existing.SongTime = Math.Min(songTime, existing.SongTime);
                existing.Imported = false;
                existing.HitCount = 1;
            }
            else
            {
                existing.HitCount++;
                if (songTime < existing.SongTime) existing.SongTime = songTime;
            }
            Normalize();
            return true;
        }

        /// <summary>
        /// リプレイから取り込んだミス地点や被弾地点を追加する。
        /// </summary>
        /// <remarks>
        /// 使う・使わないは取り込み側が決める。ミスは数が多く、
        /// 全部を警告の対象にすると画面がふさがるため。
        /// </remarks>
        /// <param name="playCount">その箇所を落としたプレイの数。多いほど本当の難所。</param>
        public bool AddImportedHit(float songTime, MarkerSource source, MarkerState state, int playCount)
        {
            if (NearestSameSpot(songTime, source) != null) return false;

            Insert(new HazardMarker(songTime, source, imported: true)
            {
                State = state,
                HitCount = playCount,
            });
            return true;
        }

        /// <summary>
        /// リプレイから取り込んだ壁マーカーを追加する。
        /// 同じ地点に実測マーカーがあれば、そちらを信用して何もしない。
        /// </summary>
        public bool AddImportedWall(float songTime)
        {
            var existing = NearestSameSpot(songTime, MarkerSource.Wall);
            if (existing == null)
            {
                Insert(new HazardMarker(songTime, MarkerSource.Wall, imported: true));
                return true;
            }

            if (!existing.Imported) return false;

            existing.HitCount++;
            if (songTime < existing.SongTime) existing.SongTime = songTime;
            Normalize();
            return true;
        }

        /// <summary>
        /// フェイル地点を候補として追加する。
        /// </summary>
        /// <remarks>
        /// 1 点に絞らず全部残す。どこまで到達できたかは試行ごとに違い、
        /// どれを警告に使うかは利用者が選べた方がよい。
        /// </remarks>
        public bool AddFail(float songTime, bool imported = false)
        {
            var existing = NearestSameSpot(songTime, MarkerSource.Fail);
            if (existing != null)
            {
                if (existing.Imported && !imported)
                {
                    // 実測が勝つ。時刻も実測のものに置き換える
                    existing.SongTime = songTime;
                    existing.Imported = false;
                    Normalize();
                    return true;
                }
                return false;
            }

            Insert(new HazardMarker(songTime, MarkerSource.Fail, imported));
            return true;
        }

        /// <summary>
        /// 手動マーカーを追加する。ほぼ同じ時刻に既にあれば何もしない。
        /// </summary>
        public bool AddManual(float songTime, string? label = null)
        {
            if (songTime < 0f) return false;
            if (NearestSameSpot(songTime, MarkerSource.Manual) != null) return false;

            Insert(new HazardMarker(songTime, MarkerSource.Manual) { Label = CleanLabel(label) });
            return true;
        }

        /// <summary>
        /// そのマーカーを指定時刻へ動かしてよいか。
        /// 同じ地点に別のマーカーが重なるのは防ぐ。
        /// </summary>
        public bool CanMoveTo(HazardMarker marker, float songTime)
        {
            if (songTime < 0f || !markers.Contains(marker)) return false;

            return !markers.Any(other => !ReferenceEquals(other, marker)
                                         && other.Source == marker.Source
                                         && Math.Abs(other.SongTime - songTime) < SameSpotSeconds);
        }

        /// <summary>
        /// 既存のマーカーの時刻と名前を書き換える。
        /// 手を入れた時点で利用者の意思を表すものになるので、取り込み印を落とす。
        /// </summary>
        public bool Update(HazardMarker marker, float songTime, string? label)
        {
            if (songTime < 0f || !markers.Contains(marker)) return false;

            marker.SongTime = songTime;
            marker.Label = CleanLabel(label);
            marker.Imported = false;
            marker.UserTouched = true;
            Normalize();
            return true;
        }

        /// <summary>
        /// そのマーカーを必ず使う指定にする。
        /// 時刻が重なる他のマーカーは、押した時点で使わない指定に変える。
        /// </summary>
        /// <remarks>
        /// 重なりの解消をこの操作の中でやってしまうのは、押した結果が
        /// そのまま一覧に出るようにするため。判定の中で暗黙に潰すと、
        /// なぜその表示になったのかが利用者から見えない。
        /// </remarks>
        public bool TurnOn(HazardMarker marker)
        {
            if (!markers.Contains(marker)) return false;

            var threshold = PluginConfig.Instance.ClusterThresholdSeconds;
            foreach (var other in markers)
            {
                if (ReferenceEquals(other, marker)) continue;
                if (!SharesSlot(marker, other)) continue;

                // フェイルは距離に関係なく 1 つしか使わない。壁と手動は時刻で判断する
                var competes = marker.Source == MarkerSource.Fail
                    ? other.Source == MarkerSource.Fail
                    : Math.Abs(other.SongTime - marker.SongTime) < threshold;

                if (!competes) continue;

                other.State = MarkerState.Off;
                other.UserTouched = true;
            }

            marker.State = MarkerState.On;
            marker.UserTouched = true;
            Normalize();
            return true;
        }

        /// <summary>
        /// そのマーカーを使わない指定にする。消さずに残す。
        /// </summary>
        /// <remarks>
        /// カウントダウンに使われていたものを外した場合は、同じ危険地点の残りも
        /// まとめて使わない指定にする。そうしないと自動選択が次の候補を拾い上げ、
        /// 「使わないようにしたのに別のものが点いた」という結果になる。
        /// </remarks>
        public bool TurnOff(HazardMarker marker)
        {
            if (!markers.Contains(marker)) return false;

            var wasActive = marker.IsActive;
            marker.State = MarkerState.Off;
            marker.UserTouched = true;

            if (wasActive)
            {
                foreach (var member in Competitors(marker))
                {
                    if (member.State != MarkerState.Auto) continue;

                    member.State = MarkerState.Off;
                    member.UserTouched = true;
                }
            }

            Normalize();
            return true;
        }

        /// <summary>すべてのマーカーを使わない指定にする。</summary>
        public bool AllOff()
        {
            if (markers.Count == 0) return false;

            foreach (var marker in markers)
            {
                marker.State = MarkerState.Off;
                marker.UserTouched = true;
            }
            Normalize();
            return true;
        }

        /// <summary>
        /// 同じ表示枠を奪い合う相手（自分は含まない）。
        /// </summary>
        /// <remarks>
        /// <see cref="TurnOn"/> が押した時点で黙らせる相手と同じ範囲にする。
        /// ここを種別で絞ると、壁を外したときに近くの手動マーカーが繰り上がって点く。
        /// </remarks>
        private IEnumerable<HazardMarker> Competitors(HazardMarker marker)
        {
            if (marker.Source == MarkerSource.Fail)
            {
                return markers.Where(m => m.Source == MarkerSource.Fail
                                          && !ReferenceEquals(m, marker));
            }

            var threshold = PluginConfig.Instance.ClusterThresholdSeconds;
            return markers.Where(m => !ReferenceEquals(m, marker)
                                      && SharesSlot(marker, m)
                                      && Math.Abs(m.SongTime - marker.SongTime) < threshold);
        }

        public bool Remove(HazardMarker marker)
        {
            if (!markers.Remove(marker)) return false;
            Normalize();
            return true;
        }

        public bool RemoveAll(MarkerSource source)
        {
            if (markers.RemoveAll(m => m.Source == source) == 0) return false;
            Normalize();
            return true;
        }

        public bool Clear()
        {
            if (markers.Count == 0 && importedTimestamps.Count == 0) return false;
            markers.Clear();
            importedTimestamps.Clear();
            Normalize();
            return true;
        }

        /// <summary>同じ危険地点とみなせる既存マーカー。無ければ null。</summary>
        public HazardMarker? FindNear(float songTime, MarkerSource source, float withinSeconds)
            => markers.FirstOrDefault(m => m.Source == source
                                           && Math.Abs(m.SongTime - songTime) < withinSeconds);

        public int CountOf(MarkerSource source) => markers.Count(m => m.Source == source);

        /// <summary>その種別に、使う指定のマーカーがあるか。</summary>
        public bool AnyOn(MarkerSource source)
            => markers.Any(m => m.Source == source && m.State == MarkerState.On);

        /// <summary>
        /// その種別で最もよく落としている箇所を 1 つだけ使う指定にする。
        /// </summary>
        /// <remarks>
        /// 判断には積み上げた回数を使う。1 回の取り込みで読むリプレイは
        /// 遊んだ直後なら 1 件しかないため、その回の重なりだけで決めると
        /// いつまでも候補が現れない。
        /// 利用者が指定を触ったものは動かさない。
        /// </remarks>
        public bool PromoteMostHit(MarkerSource source)
        {
            if (AnyOn(source)) return false;

            var best = markers
                .Where(m => m.Source == source && !m.UserTouched && m.HitCount > 1)
                .OrderByDescending(m => m.HitCount)
                .ThenBy(m => m.SongTime)
                .FirstOrDefault();

            if (best == null) return false;

            best.State = MarkerState.On;
            Normalize();
            return true;
        }

        /// <summary>
        /// カウントダウンの対象を決め直す。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>壁 … 近接したものを 1 つの危険地点とみなし、その<b>先頭</b>を採る。
        /// 警告は最初の接触より前に出す必要があるため。</item>
        /// <item>フェイル … <b>最も遅い</b>ものを採る。到達点が一番奥のところが今の壁であり、
        /// 手前で落ちた記録は既に通過できているため。</item>
        /// <item>手動 … 実際に記録された地点と重なるならそちらへ譲る。重ならなければ対象。</item>
        /// </list>
        /// <see cref="MarkerState.On"/> の指定があればそれを使い、同じ枠の他は選ばない。
        /// </remarks>
        public void RecomputeActive()
        {
            var threshold = PluginConfig.Instance.ClusterThresholdSeconds;

            foreach (var marker in markers) marker.IsActive = false;

            // フェイルは距離に関係なく全部で 1 グループ
            ActivateOne(markers.Where(m => m.Source == MarkerSource.Fail).ToList(), byLatest: true);

            // 壁とミスは同じ表示枠だが、危険地点のまとめ方は種別ごとに独立させる
            foreach (var source in new[] { MarkerSource.Wall, MarkerSource.Miss, MarkerSource.Bomb })
            {
                foreach (var group in GroupsOf(source)) ActivateOne(group, byLatest: false);
            }

            // 手動は最後に決める。実測や取り込みで同じ地点が記録されているなら、
            // 手で置いた見当より実際の記録の方が確かなので譲る。
            // 記録が消えていた頃に手当てとして置いたものが、記録の復活後も
            // 二重に残り続けるのを防ぐ
            foreach (var manual in markers.Where(m => m.Source == MarkerSource.Manual))
            {
                if (manual.State == MarkerState.Off) continue;
                if (manual.State == MarkerState.On)
                {
                    manual.IsActive = true;
                    continue;
                }

                manual.IsActive = !markers.Any(other => other.Source != MarkerSource.Manual
                                                        && other.Source != MarkerSource.Fail
                                                        && other.IsActive
                                                        && Math.Abs(other.SongTime - manual.SongTime) < threshold);
            }
        }

        /// <summary>
        /// グループの中から 1 つだけを対象にする。
        /// </summary>
        /// <remarks>
        /// 使う指定が複数あっても 1 つに絞る。取り込みと利用者の指定が同じ危険地点で
        /// 重なることがあり、そのまま両方を対象にすると 1 つの地点に警告が 2 回出る。
        /// </remarks>
        private static void ActivateOne(List<HazardMarker> group, bool byLatest)
        {
            if (group.Count == 0) return;

            var forced = group.FirstOrDefault(m => m.State == MarkerState.On);
            if (forced != null)
            {
                forced.IsActive = true;
                return;
            }

            var candidates = group.Where(m => m.State == MarkerState.Auto).ToList();
            if (candidates.Count == 0) return;

            (byLatest ? candidates[candidates.Count - 1] : candidates[0]).IsActive = true;
        }

        /// <summary>
        /// 同じ表示枠を奪い合う関係か。
        /// </summary>
        /// <remarks>
        /// フェイルは専用の行に出すので、壁や手動とは競合しない（設計 2.3）。
        /// ここを区別しないと、壁を指定しただけで近くのフェイルが消えてしまう。
        /// </remarks>
        private static bool SharesSlot(HazardMarker a, HazardMarker b)
            => (a.Source == MarkerSource.Fail) == (b.Source == MarkerSource.Fail);

        /// <summary>読み込み直後や編集後に、順序と対象を整える。</summary>
        internal void Normalize()
        {
            markers.Sort((a, b) => a.SongTime.CompareTo(b.SongTime));
            RecomputeActive();
            Version++;
        }

        /// <summary>
        /// 同じ種別のマーカーを危険地点ごとにまとめる（壁・ミス・爆弾）。
        /// 判定は「直前のマーカーからの間隔」で連鎖させる（設計 2.2）。
        /// </summary>
        /// <remarks>
        /// 指定の有無に関係なく全てのマーカーで連鎖を組む。使わない指定のものを先に外すと、
        /// 鎖の途中が抜けてグループが分裂し、1 つの危険地点に 2 つの警告が立つ。
        /// </remarks>
        private List<List<HazardMarker>> GroupsOf(MarkerSource source)
        {
            var threshold = PluginConfig.Instance.ClusterThresholdSeconds;
            var target = markers.Where(m => m.Source == source).ToList();
            var groups = new List<List<HazardMarker>>();

            var index = 0;
            while (index < target.Count)
            {
                var end = index + 1;
                while (end < target.Count && target[end].SongTime - target[end - 1].SongTime < threshold) end++;
                groups.Add(target.GetRange(index, end - index));
                index = end;
            }
            return groups;
        }

        /// <summary>
        /// 表示名を整える。空白だけなら既定に戻し、山括弧は落とす。
        /// </summary>
        /// <remarks>
        /// カウンターは 1 行ごとに色を付けるためリッチテキストで描いている。
        /// 名前に山括弧が混ざるとタグとして解釈され、行の途中から色が崩れる。
        /// </remarks>
        private static string? CleanLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;

            var cleaned = label!.Replace("<", string.Empty).Replace(">", string.Empty).Trim();
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>同じ地点の記録とみなせる既存マーカー。無ければ null。</summary>
        private HazardMarker? NearestSameSpot(float songTime, MarkerSource source)
            => markers.FirstOrDefault(m => m.Source == source
                                           && Math.Abs(m.SongTime - songTime) < SameSpotSeconds);

        private void Insert(HazardMarker marker)
        {
            markers.Add(marker);
            Normalize();
        }
    }
}
