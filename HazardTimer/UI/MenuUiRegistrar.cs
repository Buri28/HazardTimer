using System;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.Settings;
using Zenject;

namespace HazardTimer.UI
{
    /// <summary>
    /// 設定画面と曲選択タブを BSML に登録する。
    /// </summary>
    /// <remarks>
    /// <c>BSMLSettings.Instance</c> / <c>GameplaySetup.Instance</c> は
    /// BSML のメニュー用 DiContainer から解決される。プラグインの
    /// <c>[OnEnable]</c> はそれより前に走るため、そこで触ると
    /// 「Tried getting BSMLSettings too early!」で落ちる。
    /// メニュー・スコープの <see cref="IInitializable"/> なら、
    /// インストール完了後に呼ばれるので安全。
    /// </remarks>
    internal class MenuUiRegistrar : IInitializable, IDisposable
    {
        private const string SettingsMenuName = "HazardTimer";
        private const string SettingsResource = "HazardTimer.Resources.SettingsUI.bsml";

        private readonly SettingController settingController = new SettingController();
        private readonly ManualMarkerController manualMarkerController = new ManualMarkerController();

        // 登録は 2 つ独立して行う。片方が失敗したときに、成功した方の後始末が
        // 漏れると、メニューに入り直すたびに設定画面が重複して増えていく
        private bool settingsRegistered;
        private bool tabRegistered;

        public void Initialize()
        {
            settingsRegistered = TryRun("AddSettingsMenu", () =>
                BSMLSettings.Instance.AddSettingsMenu(SettingsMenuName, SettingsResource, settingController));

            tabRegistered = TryRun("AddTab", () =>
                GameplaySetup.Instance.AddTab(ManualMarkerController.TabName,
                                              ManualMarkerController.Resource,
                                              manualMarkerController,
                                              MenuType.Solo));

            if (settingsRegistered || tabRegistered) Plugin.Log?.Info("Menu UI registered");
        }

        public void Dispose()
        {
            // 後始末で例外が出ても、購読解除だけは必ず通す。
            // 通らないと静的イベントに死んだコントローラーが残り続ける
            if (settingsRegistered && TryRun("RemoveSettingsMenu", () =>
                    BSMLSettings.Instance.RemoveSettingsMenu(settingController)))
            {
                settingsRegistered = false;
            }

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
