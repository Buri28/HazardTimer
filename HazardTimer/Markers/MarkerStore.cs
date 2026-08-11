using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HazardTimer.Markers
{
    /// <summary>
    /// マーカーの永続化。<c>UserData/HazardTimer/markers.json</c> に
    /// 「譜面キー → マーカー集合」の辞書として保存する。
    /// 配列インデックスでは持たない（譜面が変わると全部ずれるため）。
    /// </summary>
    public class MarkerStore
    {
        private const string DirectoryName = "HazardTimer";
        private const string FileName = "markers.json";

        private static readonly Lazy<MarkerStore> LazyInstance = new Lazy<MarkerStore>(() => new MarkerStore());
        public static MarkerStore Instance => LazyInstance.Value;

        private readonly Dictionary<string, BeatmapMarkerSet> sets =
            new Dictionary<string, BeatmapMarkerSet>(StringComparer.Ordinal);

        private readonly object gate = new object();
        private bool loaded;
        private bool dirty;

        private static string DirectoryPath =>
            Path.Combine(UnityEngine.Application.dataPath, "..", "UserData", DirectoryName);

        private static string FilePath => Path.Combine(DirectoryPath, FileName);

        /// <summary>
        /// 譜面に対応するマーカー集合を返す。無ければ空の集合を作って登録する。
        /// </summary>
        public BeatmapMarkerSet GetOrCreate(BeatmapKey key) => GetOrCreate(BeatmapMarkerKey.From(key));

        /// <summary>
        /// 譜面に対応するマーカー集合を返す。無ければ null。作りはしない。
        /// </summary>
        /// <remarks>
        /// 表示の更新のように読むだけの用途で <see cref="GetOrCreate"/> を使うと、
        /// 曲を眺めただけの譜面まで辞書に残り続ける。
        /// </remarks>
        public BeatmapMarkerSet? Find(BeatmapKey key)
        {
            lock (gate)
            {
                EnsureLoaded();
                return sets.TryGetValue(BeatmapMarkerKey.From(key), out var set) ? set : null;
            }
        }

        /// <summary>
        /// 読み込み済みの全集合で、カウントダウンの対象を決め直す。
        /// 統合閾値を変えたときのように、判定の前提が変わったときに呼ぶ。
        /// </summary>
        public void RecomputeAll()
        {
            lock (gate)
            {
                EnsureLoaded();
                foreach (var set in sets.Values) set.Normalize();
            }
        }

        public BeatmapMarkerSet GetOrCreate(string key)
        {
            lock (gate)
            {
                EnsureLoaded();
                if (!sets.TryGetValue(key, out var set))
                {
                    set = new BeatmapMarkerSet();
                    sets[key] = set;
                }
                return set;
            }
        }

        /// <summary>次回の <see cref="Save"/> で書き出すよう印を付ける。</summary>
        public void MarkDirty()
        {
            lock (gate) { dirty = true; }
        }

        /// <summary>変更があればファイルへ書き出す。</summary>
        public void Save(bool force = false)
        {
            lock (gate)
            {
                if (!dirty && !force) return;
                try
                {
                    Directory.CreateDirectory(DirectoryPath);

                    // 曲を眺めただけの譜面は GetOrCreate で空の集合ができる。
                    // それを書き出すとファイルが中身のないエントリで膨らむので落とす。
                    // ただし「自動取り込みしない」という指定だけは、中身が空でも残す
                    var persisted = sets
                        .Where(pair => pair.Value.Count > 0
                                       || pair.Value.ImportedReplayCount > 0
                                       || pair.Value.AutoImportSuppressed)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

                    var json = JsonConvert.SerializeObject(persisted, Formatting.Indented);
                    // 書き込み途中でクラッシュしても既存ファイルを壊さないよう、一時ファイル経由で置き換える。
                    // Delete してから Move だと、その隙間で落ちると全譜面の記録が消える。
                    // File.Replace なら差し替えが不可分になる
                    var tempPath = FilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(FilePath))
                    {
                        File.Replace(tempPath, FilePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, FilePath);
                    }
                    dirty = false;
                }
                catch (Exception e)
                {
                    Plugin.Log?.Error($"Failed to save markers: {e}");
                }
            }
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, BeatmapMarkerSet>>(json);
                if (parsed == null) return;
                foreach (var pair in parsed)
                {
                    if (pair.Value == null) continue;
                    pair.Value.Normalize();
                    sets[pair.Key] = pair.Value;
                }
            }
            catch (Exception e)
            {
                // 壊れたファイルで起動を止めない。空から始める
                Plugin.Log?.Error($"Failed to load markers, starting empty: {e}");
            }
        }
    }
}
