using System;
using System.Collections.Generic;
using System.IO;

namespace HazardTimer.Replay
{
    /// <summary>
    /// BeatLeader がローカルに残しているリプレイを、ファイル名だけで譜面に索引する。
    /// </summary>
    /// <remarks>
    /// ファイル名は
    /// <c>{playerId}-[exit-]{songName}-{difficulty}-{characteristic}-{hash}-{unixTime}.bsor</c>。
    /// 曲名にハイフンが入りうるので、必ず<b>末尾から</b>数えて取り出す。
    /// 中身を開かずに該当ファイルを選べるので、数千件あっても走査が軽い。
    /// <para>
    /// 曲を選び直すたびにディレクトリを走査すると無駄なので、一度作った索引は
    /// 使い回す。プレイ後は新しいリプレイが増えるため、そのタイミングで
    /// <see cref="Invalidate"/> して作り直す。
    /// </para>
    /// </remarks>
    public static class ReplayFileIndex
    {
        private const string CustomLevelPrefix = "custom_level_";

        /// <summary>途中で抜けたプレイに付く接頭辞。</summary>
        private const string ExitToken = "exit";

        private static Dictionary<string, List<ReplayFileInfo>>? index;

        /// <summary>リプレイの保存先。BeatLeader MOD が書き出す場所。</summary>
        public static string ReplaysDirectory =>
            Path.Combine(UnityEngine.Application.dataPath, "..", "UserData", "BeatLeader", "Replays");

        public static bool DirectoryExists => Directory.Exists(ReplaysDirectory);

        /// <summary>索引を捨てる。次回の参照で作り直される。</summary>
        public static void Invalidate() => index = null;

        /// <summary>指定した譜面に対応するリプレイを、古い順に返す。</summary>
        public static IReadOnlyList<ReplayFileInfo> Find(BeatmapKey key)
        {
            var lookupKey = LookupKeyOf(key);
            if (lookupKey == null) return Array.Empty<ReplayFileInfo>();

            var map = EnsureIndex();
            return map.TryGetValue(lookupKey, out var found)
                ? found
                : (IReadOnlyList<ReplayFileInfo>)Array.Empty<ReplayFileInfo>();
        }

        private static Dictionary<string, List<ReplayFileInfo>> EnsureIndex()
        {
            if (index != null) return index;

            index = new Dictionary<string, List<ReplayFileInfo>>(StringComparer.OrdinalIgnoreCase);
            if (!DirectoryExists) return index;

            try
            {
                foreach (var path in Directory.GetFiles(ReplaysDirectory, "*.bsor"))
                {
                    if (!TryDescribe(path, out var lookupKey, out var info)) continue;

                    if (!index.TryGetValue(lookupKey, out var list))
                    {
                        index[lookupKey] = list = new List<ReplayFileInfo>();
                    }
                    list.Add(info);
                }

                foreach (var list in index.Values)
                {
                    list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Failed to scan replays: {e}");
            }

            return index;
        }

        private static bool TryDescribe(string path, out string lookupKey, out ReplayFileInfo info)
        {
            lookupKey = string.Empty;
            info = default;

            var name = Path.GetFileNameWithoutExtension(path);
            var parts = name.Split('-');
            if (parts.Length < 5) return false;

            // 末尾から: [-1]=unixTime, [-2]=hash, [-3]=characteristic, [-4]=difficulty
            var hash = parts[parts.Length - 2];
            var characteristic = parts[parts.Length - 3];
            var difficulty = parts[parts.Length - 4];
            lookupKey = MakeLookupKey(hash, characteristic, difficulty);

            // [0]=playerId。途中で抜けたプレイはその次に "exit" が入る
            var isCompleted = !string.Equals(parts[1], ExitToken, StringComparison.OrdinalIgnoreCase);

            // ファイルの更新時刻ではなくファイル名の時刻を使う。コピーしても狂わない
            if (!long.TryParse(parts[parts.Length - 1], out var timestamp)) timestamp = 0;

            info = new ReplayFileInfo(path, isCompleted, timestamp);
            return true;
        }

        private static string? LookupKeyOf(BeatmapKey key)
        {
            var hash = HashOf(key.levelId);
            if (string.IsNullOrEmpty(hash)) return null;
            if (key.beatmapCharacteristic == null) return null;

            return MakeLookupKey(hash, key.beatmapCharacteristic.serializedName, key.difficulty.ToString());
        }

        private static string MakeLookupKey(string hash, string characteristic, string difficulty)
            => $"{hash}|{characteristic}|{difficulty}";

        /// <summary>
        /// levelId から譜面ハッシュを取り出す。
        /// カスタム譜面は <c>custom_level_&lt;HASH&gt;</c>、公式曲は levelId がそのまま入る。
        /// </summary>
        private static string HashOf(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return string.Empty;
            return levelId.StartsWith(CustomLevelPrefix, StringComparison.OrdinalIgnoreCase)
                ? levelId.Substring(CustomLevelPrefix.Length)
                : levelId;
        }
    }
}
