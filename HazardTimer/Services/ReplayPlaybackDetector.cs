using System;
using System.Collections.Generic;
using System.Reflection;

namespace HazardTimer.Services
{
    /// <summary>
    /// BeatLeader / ScoreSaber がリプレイを再生中かどうかを見る。
    /// </summary>
    /// <remarks>
    /// リプレイ再生は通常のプレイと同じゲームシーンで動くので、放っておくと
    /// 再生された壁接触やフェイルを、その場のプレイとして記録してしまう。
    /// 他人のリプレイなら、一度も当たっていない壁にマーカーが立つことになる。
    ///
    /// どちらの MOD にも参照は持たない。入っていない環境でも動かしたいので、
    /// 状態フラグだけをリフレクションで読む。読めなければリプレイではないものとして扱う。
    ///
    /// どちらの MOD も、フラグを立てるのはゲームシーンへ移る前なので、
    /// プレイ開始時に 1 度読めば足りる（BeatLeader は StartReplay、
    /// ScoreSaber は LoadReplay のあとにシーン遷移を始める）。
    ///
    /// 誰のリプレイかは見ない。自分のリプレイでも、そのプレイは実測で記録済みか
    /// リプレイから取り込む対象なので、再生中に足すと同じ接触を二度数えるだけになる。
    /// </remarks>
    internal static class ReplayPlaybackDetector
    {
        /// <summary>
        /// MOD ごとの候補。版によって置き場所が変わるので、先に読めた 1 つだけを使う。
        /// </summary>
        /// <remarks>
        /// 同じ MOD の候補を全部見ないのは、古い版向けの候補が別の意味で残っていたときに、
        /// そちらが立っているだけで記録が止まってしまうため。
        ///
        /// <see cref="Probe.Path"/> は先頭が静的メンバー、以降はインスタンスメンバー。
        /// 状態が別のオブジェクトへ移されることがあるので、1 段だけでは追えない。
        /// </remarks>
        private static readonly Probe[][] ProbeGroups =
        {
            new[]
            {
                // BeatLeader 0.5 以降。public static bool
                new Probe("BeatLeader", "BeatLeader.Replayer.ReplayerLauncher", "IsStartedAsReplay"),
            },
            new[]
            {
                // ScoreSaber 現行。新旧どちらの形式の再生でも立つ
                new Probe("ScoreSaber", "ScoreSaber.Features.Replays.ReplayStateRegistry", "IsPlaybackEnabled"),

                // 同じ値を持つ実体。上の短縮プロパティが無くなっても、こちらなら残る見込み
                new Probe("ScoreSaber", "ScoreSaber.Features.Replays.ReplayStateRegistry", "Current", "IsPlaybackEnabled"),

                // Beat Saber 1.39 世代の ScoreSaber（3.3 系）。
                // 状態は Plugin の静的プロパティが直接持っていて、型の置き場所も別だった
                new Probe("ScoreSaber", "ScoreSaber.Plugin", "ReplayState", "IsPlaybackEnabled"),
            },
        };

        private const BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// 解決済みの読み取り口。見つからなかった場合も含めて 1 度で確定させる。
        /// </summary>
        /// <remarks>
        /// MOD のアセンブリはゲーム起動時に読み込まれ、ここを最初に通るのはプレイ開始時なので、
        /// 探し直しても結果は変わらない。プレイ開始直後は FPS 半減の判定が入るため、
        /// 毎プレイ アセンブリを全走査するのは避ける。
        /// </remarks>
        private static List<Func<bool?>>? readers;

        /// <summary>いずれかの MOD がリプレイを再生中か。</summary>
        public static bool IsPlayingReplay
        {
            get
            {
                foreach (var read in readers ??= Resolve())
                {
                    if (read() == true) return true;
                }
                return false;
            }
        }

        private readonly struct Probe
        {
            public readonly string Assembly;
            public readonly string Type;

            /// <summary>先頭が静的メンバー、以降はそこから辿るインスタンスメンバー。</summary>
            public readonly string[] Path;

            public Probe(string assembly, string type, params string[] path)
            {
                Assembly = assembly;
                Type = type;
                Path = path;
            }
        }

        private static List<Func<bool?>> Resolve()
        {
            var assemblies = FindAssemblies();
            var found = new List<Func<bool?>>();

            foreach (var group in ProbeGroups)
            {
                foreach (var probe in group)
                {
                    if (!assemblies.TryGetValue(probe.Assembly, out var assembly)) continue;

                    var type = assembly.GetType(probe.Type, throwOnError: false);
                    if (type == null) continue;

                    // 実際に読めるところまで確かめてから採る。途中で辿れなくなる候補を
                    // 採ってしまうと、同じ MOD の次の候補が試されなくなる
                    Func<bool?> reader = () => ReadChain(type, probe.Path);
                    if (reader() == null) continue;

                    Plugin.LogDebug($"Replay state found: {probe.Type}.{string.Join(".", probe.Path)}");
                    found.Add(reader);
                    break;
                }
            }

            if (found.Count == 0)
            {
                Plugin.LogDebug("No replay mod state found; replay playback cannot be detected.");
            }
            return found;
        }

        /// <summary>候補の経路を辿って bool を読む。辿れなければ null。</summary>
        private static bool? ReadChain(Type type, string[] path)
        {
            try
            {
                var value = ReadStatic(type, path[0]);
                for (var i = 1; i < path.Length && value != null; i++)
                {
                    value = ReadInstance(value, path[i]);
                }
                return value as bool?;
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn($"Failed to read replay state from {type.FullName}: {e.Message}");
                return null;
            }
        }

        private static object? ReadStatic(Type type, string name)
        {
            var property = type.GetProperty(name, StaticFlags);
            if (property != null && property.CanRead) return property.GetValue(null);

            return type.GetField(name, StaticFlags)?.GetValue(null);
        }

        private static object? ReadInstance(object target, string name)
        {
            var type = target.GetType();

            var property = type.GetProperty(name, InstanceFlags);
            if (property != null && property.CanRead) return property.GetValue(target);

            return type.GetField(name, InstanceFlags)?.GetValue(target);
        }

        /// <summary>候補に出てくるアセンブリを 1 度の走査で拾う。</summary>
        private static Dictionary<string, Assembly> FindAssemblies()
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in ProbeGroups)
            {
                foreach (var probe in group) wanted.Add(probe.Assembly);
            }

            var found = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // MOD を入れた環境ではアセンブリが数百ある。AssemblyName を作ると
                // その数だけ無駄が出るので、FullName の先頭（名前の部分）だけを見る
                var fullName = assembly.FullName;
                var comma = fullName.IndexOf(',');
                var name = comma < 0 ? fullName : fullName.Substring(0, comma);

                if (wanted.Contains(name) && !found.ContainsKey(name)) found[name] = assembly;
            }
            return found;
        }
    }
}
