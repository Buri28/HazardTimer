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

        /// <summary>
        /// <see cref="TurnOn"/> が重なりとみなす幅。
        /// </summary>
        /// <remarks>
        /// 使う指定にしたマーカーは、直前のマーカーの到達時刻からカウントダウンを始める。
        /// これより近いものを両方使うと、数字が 0 まで減った直後に跳ね上がって読めなくなる。
        /// 危険地点のまとめ幅（<c>ClusterThresholdSeconds</c>）を使わないのは、
        /// 押した 1 つのために数秒先まで消えるのが操作として分かりにくいため。
        /// </remarks>
        private const float OverlapSeconds = 0.5f;

        /// <summary>
        /// 手動マーカーが記録と同じ地点を指しているとみなす幅。
        /// </summary>
        /// <remarks>
        /// 手動は分と秒でしか置けないので、実測との差はどうしても 1 秒近くまで開く。
        /// <see cref="OverlapSeconds"/> と同じ幅で見ると、同じ壁を指しているのに
        /// 別々の警告として並んでしまう。
        /// </remarks>
        private const float ManualOverlapSeconds = 1.0f;

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
        /// <param name="hitCount">その箇所で数えた回数。多いほど本当の難所。</param>
        /// <param name="playCount">
        /// <paramref name="hitCount"/> を数えたプレイの数。0 なら 1 プレイあたりに直さない。
        /// </param>
        public bool AddImportedHit(float songTime, MarkerSource source, MarkerState state,
                                   int hitCount, int playCount)
        {
            if (NearestSameSpot(songTime, source) != null) return false;

            Insert(new HazardMarker(songTime, source, imported: true)
            {
                State = state,
                HitCount = hitCount,
                PlayCount = playCount,
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
        /// ほぼ同時刻に重なる他のマーカーだけ、押した時点で使わない指定に変える。
        /// </summary>
        /// <remarks>
        /// 重なりの解消をこの操作の中でやってしまうのは、押した結果が
        /// そのまま一覧に出るようにするため。判定の中で暗黙に潰すと、
        /// なぜその表示になったのかが利用者から見えない。
        /// 黙らせる範囲を <see cref="OverlapSeconds"/> までに留めるのは、
        /// 一覧に見えているものを 1 つ点けただけで数秒先まで消えるのが、
        /// 操作として直感に反するため。近接した記録のまとめ（危険地点のグループ）は
        /// 指定なしのマーカーに対する自動選択の話で、こことは別に働く。
        /// </remarks>
        public bool TurnOn(HazardMarker marker)
        {
            if (!markers.Contains(marker)) return false;

            foreach (var other in markers)
            {
                if (ReferenceEquals(other, marker)) continue;
                if (!SharesSlot(marker, other)) continue;

                // フェイルは距離に関係なく 1 つしか使わない。壁と手動は時刻で判断する
                var competes = marker.Source == MarkerSource.Fail
                    ? other.Source == MarkerSource.Fail
                    : Math.Abs(other.SongTime - marker.SongTime) < OverlapSeconds;

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
        /// 押したものだけを変える。同じ危険地点の残りを巻き添えにすると、
        /// 1 つ外しただけで数秒ぶんの記録がまとめて消え、戻すのに何度も押し直すことになる。
        /// 外した結果として近くの候補が繰り上がって点くのは、
        /// そこがまだ危険地点として残っていることを示すので、そのままにする。
        /// まとめて消したいときは <see cref="AllOff"/> がある。
        /// </remarks>
        public bool TurnOff(HazardMarker marker)
        {
            if (!markers.Contains(marker)) return false;

            marker.State = MarkerState.Off;
            marker.UserTouched = true;
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
        /// 利用者が付けた On / Off の指定を全部落として、自動選択に戻す。
        /// </summary>
        /// <remarks>
        /// 一度 On が付くと、その種別では自動の繰り上げが止まる（<see cref="PromoteMostHit"/> は
        /// 既に On があると何もしない）。後から本当の難所が別の場所に移っても、
        /// 指定を1つずつ外して回らないと追従しない。その戻し口。
        /// 単に <see cref="MarkerState.Auto"/> にせず取り込み時の既定へ戻すのは、
        /// ミスを Auto にすると危険地点ごとに全部が立ち上がってしまうため。
        /// </remarks>
        public bool ResetToAuto()
        {
            // 利用者が触ったものだけを戻すのでは足りない。取り込みが自分で選んだ On は
            // UserTouched が立たないので対象から漏れ、それが残ると下の PromoteMostHit が
            // AnyOn で弾かれて何もしない。戻し口として働かせるには全部を既定へ戻す
            var changed = false;
            foreach (var marker in markers)
            {
                var state = DefaultStateFor(marker.Source);
                if (marker.State == state && !marker.UserTouched) continue;

                marker.State = state;
                marker.UserTouched = false;
                changed = true;
            }

            if (!changed) return false;

            // ミスの既定は Off なので、戻しただけでは1つも残らない。
            // 取り込みと同じ規則で選び直す
            PromoteMostHit(MarkerSource.Miss);
            Normalize();
            return true;
        }

        /// <summary>取り込みが付ける既定の指定。手を入れる前の状態。</summary>
        private static MarkerState DefaultStateFor(MarkerSource source) => source switch
        {
            // 数が多いので、既定では候補として持つだけにする
            MarkerSource.Miss => MarkerState.Off,
            // 数が少なく、当たると立て直しが利かないので既定で使う
            MarkerSource.Bomb => MarkerState.On,
            _ => MarkerState.Auto,
        };

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
        /// 比べるのは 1 プレイあたりに直した値。延べ数のままだと、
        /// 早くから記録している箇所ほど有利になり、後から見つけた難所が上に来ない。
        /// 利用者が指定を触ったものは動かさない。
        /// </remarks>
        public bool PromoteMostHit(MarkerSource source)
        {
            if (AnyOn(source)) return false;

            var best = markers
                .Where(m => m.Source == source && !m.UserTouched && m.HitCount > 1)
                .OrderByDescending(m => m.AverageHits)
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
        /// <item>手動 … 使わない指定でなければ必ず対象。同じ地点を指す記録の方を降ろす。
        /// 記録が無いところに利用者が後から置いたものなので、置いたのに消えるのでは
        /// 何のために設定したのか分からない。ただし明示的に使う指定のある記録には譲る。</item>
        /// </list>
        /// <see cref="MarkerState.On"/> の指定があるものは、同じ危険地点に何個あっても全部使う。
        /// 自動の選択が働くのは、そのグループに指定が 1 つも無いときだけ。
        /// フェイルだけは専用の 1 行しか無いので、指定が複数あっても先頭 1 つに絞る。
        /// 最後に、種別をまたいだ重なりを <see cref="DropOverlaps"/> で落とす。
        /// </remarks>
        public void RecomputeActive()
        {
            foreach (var marker in markers) marker.IsActive = false;

            // フェイルは距離に関係なく全部で 1 グループ。専用の 1 行しかないので 1 つに絞る
            Activate(markers.Where(m => m.Source == MarkerSource.Fail).ToList(),
                     byLatest: true, keepEveryOn: false);

            // 壁とミスは同じ表示枠だが、危険地点のまとめ方は種別ごとに独立させる
            foreach (var source in new[] { MarkerSource.Wall, MarkerSource.Miss, MarkerSource.Bomb })
            {
                foreach (var group in GroupsOf(source))
                {
                    Activate(group, byLatest: false, keepEveryOn: true);
                }
            }

            // 手動は最後に決める。使わない指定でなければ必ず対象。
            // 手動マーカーは記録が無いところに利用者が後から置いたもので、
            // 置いたのに消えるのでは何のために設定したのか分からない
            foreach (var manual in markers.Where(m => m.Source == MarkerSource.Manual))
            {
                if (manual.State == MarkerState.Off) continue;
                manual.IsActive = true;
            }

            // 同じ地点を指している記録を降ろす。手動は分と秒でしか置けないので、
            // 差が 1 秒未満なら同じ地点とみなす。
            // 降ろすのは最も近い 1 つだけ。前後 1 秒にあるものを全部消すと、
            // 手動 1 個が 2 秒幅を黙らせることになり、別々に警告できるものまで巻き込む
            foreach (var manual in markers)
            {
                if (manual.Source != MarkerSource.Manual || !manual.IsActive) continue;

                HazardMarker? nearest = null;
                var shortest = ManualOverlapSeconds;

                foreach (var other in markers)
                {
                    if (other.Source == MarkerSource.Manual) continue;
                    // フェイルは専用の行なので手動とは競合しない
                    if (other.Source == MarkerSource.Fail) continue;
                    if (!other.IsActive) continue;
                    // 利用者が明示的に点けた記録には譲る。手動も明示なら手動を採る。
                    // ここで State を見ないと、指定なしの手動が On のボムを黙らせて
                    // 危険の種類が読み取れなくなる（DropOverlaps の勝敗とも食い違う）
                    if (other.State == MarkerState.On && manual.State != MarkerState.On) continue;

                    var distance = Math.Abs(other.SongTime - manual.SongTime);
                    if (distance >= shortest) continue;

                    shortest = distance;
                    nearest = other;
                }

                if (nearest != null) nearest.IsActive = false;
            }

            DropOverlaps();
        }

        /// <summary>
        /// 表示枠を奪い合う対象のうち、直前と <see cref="OverlapSeconds"/> 未満で並ぶものを落とす。
        /// </summary>
        /// <remarks>
        /// <see cref="TurnOn"/> も重なりを潰すが、それだけでは足りない。
        /// <list type="bullet">
        /// <item>時刻を動かす <see cref="Update"/> は <see cref="CanMoveTo"/>（0.2 秒）しか見ない。</item>
        /// <item>取り込みは重なりを見ずに直接 On を書く（ボムは全部 On）。</item>
        /// <item>危険地点のまとめ方は種別ごとに独立しているので、
        /// ボムとミスのように別グループ同士が並ぶのは止められない。</item>
        /// </list>
        /// ここを通さないと、数字が 0 まで減った直後に跳ね上がって読めなくなる。
        /// 残すのは使う指定のある方。どちらも同じなら早い方を残す。
        /// 警告は最初の接触より前に出す必要があるため。
        /// </remarks>
        private void DropOverlaps()
        {
            HazardMarker? previous = null;
            foreach (var marker in markers)
            {
                // フェイルは専用の行なので、この行の並びとは競合しない
                if (marker.Source == MarkerSource.Fail) continue;
                if (!marker.IsActive) continue;
                // 出さない種別はそもそも並ばないので、隣を降ろす資格も無い。
                // 見ないと「ボムの表示を切ったら近くの壁まで消えた」という結果になる
                if (!PluginConfig.Instance.IsShown(marker.Source)) continue;

                if (previous != null && marker.SongTime - previous.SongTime < OverlapSeconds)
                {
                    // 使う指定が競り勝つ。利用者が点けたものが、指定なしのものに
                    // 押し負けて黙って消えるのでは筋が通らない
                    if (marker.State == MarkerState.On && previous.State != MarkerState.On)
                    {
                        previous.IsActive = false;
                        previous = marker;
                    }
                    else
                    {
                        marker.IsActive = false;
                    }
                    continue;
                }

                previous = marker;
            }
        }

        /// <summary>
        /// グループの中から対象を決める。
        /// </summary>
        /// <remarks>
        /// 指定なしのものは 1 つに絞る。近接した記録は同じ危険地点なので、
        /// 全部を対象にすると 1 つの地点に警告が何度も出る。
        /// 使う指定は <paramref name="keepEveryOn"/> なら全部残す。
        /// 一覧で点けたものが点かないと、なぜそうなったのかが利用者から見えない。
        /// ここで残したものが近すぎて並ぶ場合は、<see cref="DropOverlaps"/> が後から落とす。
        /// </remarks>
        /// <param name="keepEveryOn">
        /// true なら使う指定を全部対象にする。false なら先頭 1 つだけ。
        /// </param>
        private static void Activate(List<HazardMarker> group, bool byLatest, bool keepEveryOn)
        {
            if (group.Count == 0) return;

            var forced = group.Where(m => m.State == MarkerState.On).ToList();
            if (forced.Count > 0)
            {
                if (keepEveryOn)
                {
                    foreach (var marker in forced) marker.IsActive = true;
                }
                else
                {
                    forced[0].IsActive = true;
                }
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
