using HazardTimer.Services;
using Zenject;

namespace HazardTimer.Installers
{
    /// <summary>
    /// プレイヤー・スコープのインストーラー。
    /// 1 プレイ分のマーカー集合・記録・カウントダウン計算を登録する。
    /// </summary>
    internal class HazardTimerGameInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<HazardMarkerSession>().AsSingle();
            Container.BindInterfacesAndSelfTo<HazardRecorder>().AsSingle();
            Container.Bind<CountdownService>().AsSingle();
        }
    }
}
