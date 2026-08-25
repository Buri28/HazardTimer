using System.Runtime.CompilerServices;
using HazardTimer.Installers;
using IPA;
using IPA.Config.Stores;
using SiraUtil.Zenject;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace HazardTimer
{
    /// <summary>
    /// エントリポイント。設定の読み込みと Zenject インストーラー、UI の登録を行う。
    /// </summary>
    [Plugin(RuntimeOptions.DynamicInit)]
    public class Plugin
    {
        internal static Plugin? Instance { get; private set; }
        public static IPA.Logging.Logger? Log { get; private set; }

        /// <summary>
        /// 詳細ログを出すか。既定は false で、警告とエラーだけをログに残す。
        /// </summary>
        /// <remarks>
        /// 動作の記録は不具合を追うときにしか読まないのに、毎回の曲選択で何行も出る。
        /// 他プラグインのログに混ざって邪魔になるので、既定では止めておき、
        /// 調べたいときだけここを true にしてビルドし直す。
        /// </remarks>
        internal static bool DebugLogging = false;

        /// <summary>
        /// 詳細ログ。<see cref="DebugLogging"/> が有効なときだけ出力する。
        /// 警告とエラーはこれを通さず、直接 <see cref="Log"/> へ出す。
        /// </summary>
        internal static void LogDebug(string message)
        {
            if (DebugLogging) Log?.Info(message);
        }

        [Init]
        public void Init(IPA.Logging.Logger logger, IPA.Config.Config config, Zenjector zenjector)
        {
            Instance = this;
            Log = logger;
            PluginConfig.Instance = config.Generated<PluginConfig>();

            // 設定画面と曲選択タブの登録は MenuUiRegistrar が行う。
            // BSML のシングルトンはここではまだ取得できない
            zenjector.Install<HazardTimerMenuInstaller>(Location.Menu);
            zenjector.Install<HazardTimerGameInstaller>(Location.Player);
        }

        [OnEnable]
        public void OnEnable() => LogDebug("HazardTimer enabled");

        [OnDisable]
        public void OnDisable()
        {
            // 未保存の変更があれば取りこぼさない
            Markers.MarkerStore.Instance.Save();
        }
    }
}
