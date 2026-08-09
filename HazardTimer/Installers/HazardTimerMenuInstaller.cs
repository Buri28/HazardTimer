using HazardTimer.Services;
using HazardTimer.UI;
using Zenject;

namespace HazardTimer.Installers
{
    /// <summary>
    /// メニュー・スコープのインストーラー。選択中の譜面の追跡と、
    /// BSML への UI 登録を行う。
    /// </summary>
    internal class HazardTimerMenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<SelectedBeatmapTracker>().AsSingle();
            Container.BindInterfacesAndSelfTo<AutoImportService>().AsSingle();
            Container.BindInterfacesAndSelfTo<MenuUiRegistrar>().AsSingle();
        }
    }
}
