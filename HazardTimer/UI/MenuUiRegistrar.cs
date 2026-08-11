using System;
using BeatSaberMarkupLanguage.GameplaySetup;
using Zenject;

namespace HazardTimer.UI
{
    /// <summary>
    /// 曲選択画面の Mods タブを BSML に登録する。
    /// </summary>
    /// <remarks>
    /// <c>GameplaySetup.Instance</c> は
    /// BSML のメニュー用 DiContainer から解決される。プラグインの
    /// <c>[OnEnable]</c> はそれより前に走るため、そこで触ると
    /// 「Tried getting BSMLSettings too early!」で落ちる。
    /// メニュー・スコープの <see cref="IInitializable"/> なら、
    /// インストール完了後に呼ばれるので安全。
    /// </remarks>
    internal class MenuUiRegistrar : IInitializable, IDisposable
    {
        private readonly ManualMarkerController manualMarkerController = new ManualMarkerController();

        private bool tabRegistered;

        public void Initialize()
        {
            tabRegistered = TryRun("AddTab", () =>
                GameplaySetup.Instance.AddTab(ManualMarkerController.TabName,
                                              ManualMarkerController.Resource,
                                              manualMarkerController,
                                              // Solo だけにするとキャンペーンや Custom
                                              // （AccSaber など）でタブが出ない
                                              MenuType.All));

            if (tabRegistered) Plugin.Log?.Info("Menu UI registered");
        }

        public void Dispose()
        {
            // 後始末で例外が出ても、購読解除だけは必ず通す。
            // 通らないと静的イベントに死んだコントローラーが残り続ける
            if (tabRegistered && TryRun("RemoveTab", () =>
                    GameplaySetup.Instance.RemoveTab(ManualMarkerController.TabName)))
            {
                tabRegistered = false;
            }

            manualMarkerController.Dispose();
        }

        private static bool TryRun(string what, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log?.Error($"Menu UI {what} failed: {e}");
                return false;
            }
        }
    }
}
