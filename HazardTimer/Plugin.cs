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
        public void OnEnable() => Log?.Info("HazardTimer enabled");

        [OnDisable]
        public void OnDisable()
        {
            // 未保存の変更があれば取りこぼさない
            Markers.MarkerStore.Instance.Save();
        }
    }
}
