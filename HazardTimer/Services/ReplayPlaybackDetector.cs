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
    /// 状態フラグだけをリフレクションで読む。
    ///
    /// 読めなかったときの倒し方は 2 通りある。そもそも見る手段が無い（MOD 未導入、
    /// 候補がどれも当たらない）なら通常のプレイとして扱う。一度読めた口が読めなくなった場合は、
    /// リプレイかもしれない側に倒して記録を止める。
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

        /// <summary>解決済みの読み取り口。MOD 名で引く。</summary>
        private static readonly Dictionary<string, Func<bool?>> readers =
            new Dictionary<string, Func<bool?>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 入っていないと分かった MOD。あとから入れるには再起動が要るので探し直さない。
        /// </summary>
        private static readonly HashSet<string> absent =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>読めないと警告済みの MOD。同じ警告を毎プレイ出さないために覚える。</summary>
        private static readonly HashSet<string> warned =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>いずれかの MOD がリプレイを再生中か。</summary>
        public static bool IsPlayingReplay
        {
            get
            {
                Resolve();

                var unreadable = false;
                foreach (var read in readers.Values)
                {
                    var playing = read();
                    if (playing == true) return true;
                    if (playing == null) unreadable = true;
                }

                // 一度は読めた口が読めなくなったのは異常なので、リプレイの側に倒す。
                // 記録を止めそこねると、当たってもいない壁が実測マーカーとして残り、
                // 取り込みより優先されるので手で消すしかない。
                // 逆に誤って止めても、そのプレイはあとでリプレイから取り込み直せる
                return unreadable;
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

        /// <summary>まだ解決できていない MOD だけを探す。</summary>
        /// <remarks>
        /// 覚えるのは命中した口と、入っていないと分かった MOD だけ。
        /// 「入っているのに読めなかった」は覚えず、次に呼ばれたときもう一度探す。
        /// これを覚えてしまうと、状態が遅延生成に変わった版で最初の 1 回が空振りしただけで、
        /// そのセッション中ずっと検出できないまま黙って記録し続けることになる。
        ///
        /// 探し直すのは異常なときだけで、正常時は辞書の照会で終わる。
        /// アセンブリの全走査も、未解決の MOD が残っているときにしか行わない。
        /// プレイ開始直後は FPS 半減の判定が入るため、そこで走査を繰り返したくない。
        /// </remarks>
        private static void Resolve()
        {
            Dictionary<string, Assembly>? assemblies = null;

            foreach (var group in ProbeGroups)
            {
                // 同じグループの候補はすべて同じ MOD のもの
                var modName = group[0].Assembly;
                if (readers.ContainsKey(modName) || absent.Contains(modName)) continue;

                assemblies ??= FindAssemblies();

                if (!assemblies.ContainsKey(modName))
                {
                    absent.Add(modName);
                    Plugin.LogDebug($"{modName} is not installed; its replay playback cannot be detected.");
                    continue;
                }

                var reader = ResolveGroup(group, assemblies);
                if (reader != null)
                {
                    readers[modName] = reader;
                    continue;
                }

                // 入っているのに 1 つも読めない。版が変わって候補が全滅した合図で、
                // 黙って通すとその MOD のリプレイ再生がプレイとして記録され続ける。
                // 記録が止まる側（HazardRecorder）と違って、
                // 「増えてはいけないものが増えた」ことには誰も気づけない
                if (warned.Add(modName))
                {
                    Plugin.Log?.Warn(
                        $"{modName} is installed but no known replay state could be read; " +
                        "its replays will be recorded as if they were your own play.");
                }
            }
        }

        /// <summary>同じ MOD の候補を順に試し、先に読めた 1 つを返す。読めなければ null。</summary>
        private static Func<bool?>? ResolveGroup(Probe[] group, Dictionary<string, Assembly> assemblies)
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
                return reader;
            }
            return null;
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
                Plugin.Log?.Warn(
                    $"Failed to read replay state from {type.FullName}.{string.Join(".", path)}: {e.Message}");
                return null;
            }
        }

        private static object? ReadStatic(Type type, string name)
            => ReadMember(type, target: null, name, StaticFlags);

        private static object? ReadInstance(object target, string name)
            => ReadMember(target.GetType(), target, name, InstanceFlags);

        /// <summary>プロパティを先に、無ければフィールドを読む。基底型まで遡る。</summary>
        /// <remarks>
        /// 両方を見るのは、同じ名前でも版によってどちらでもありうるため。
        /// ScoreSaber の IsPlaybackEnabled は、短縮プロパティ経由なら静的プロパティ、
        /// 状態オブジェクト経由なら非公開フィールドで、どちらも読めないと候補が減る。
        ///
        /// 基底型まで遡るのは、GetProperty / GetField が非公開メンバーを宣言型でしか探さないため。
        /// 状態が基底クラスへ移された版でも読めるようにしておく。
        /// 1 段ずつ宣言型だけを見るので、派生側で名前を隠している場合も曖昧にならない。
        /// </remarks>
        private static object? ReadMember(Type? type, object? target, string name, BindingFlags flags)
        {
            for (; type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead) return property.GetValue(target);

                var field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }
            return null;
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
