using BeatSaberMarkupLanguage.Attributes;

namespace HazardTimer.UI
{
    /// <summary>
    /// Counters+ の設定画面に出すホスト。
    /// </summary>
    /// <remarks>
    /// ここに置くのは<b>見た目の調整だけ</b>。カウントダウンの挙動に関わる設定は
    /// 曲選択画面の Mods タブ（<see cref="ManualMarkerController"/>）に集約している。
    /// Counters+ の画面はカウンターの配置を決める場所なので、
    /// 記録の取り方のような設定が混ざると置き場所として分かりにくい。
    /// </remarks>
    public class SettingController
    {
        [UIValue("counter-x-offset")]
        public float CounterXOffset
        {
            get => PluginConfig.Instance.CounterXOffset;
            set
            {
                PluginConfig.Instance.CounterXOffset = value;
                HazardTimerCounter.ApplyOffsets();
            }
        }

        [UIValue("counter-y-offset")]
        public float CounterYOffset
        {
            get => PluginConfig.Instance.CounterYOffset;
            set
            {
                PluginConfig.Instance.CounterYOffset = value;
                HazardTimerCounter.ApplyOffsets();
            }
        }

        [UIValue("FormatOneDecimal")]
        public string FormatOneDecimal(float value) => value.ToString("F1");
    }
}
