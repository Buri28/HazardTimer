using BeatSaberMarkupLanguage.Attributes;

namespace HazardTimer.UI
{
    /// <summary>
    /// Mod Settings に出す設定画面のホスト。
    /// カウントダウンの挙動に関わる値は、この 1 箇所からしか触らせない。
    /// </summary>
    public class SettingController
    {
        [UIValue("lead-time")]
        public float LeadTimeSeconds
        {
            get => PluginConfig.Instance.LeadTimeSeconds;
            set => PluginConfig.Instance.LeadTimeSeconds = value;
        }

        [UIValue("cluster-threshold")]
        public float ClusterThresholdSeconds
        {
            get => PluginConfig.Instance.ClusterThresholdSeconds;
            set => PluginConfig.Instance.ClusterThresholdSeconds = value;
        }

        [UIValue("record-wall-hits")]
        public bool RecordWallHits
        {
            get => PluginConfig.Instance.RecordWallHits;
            set => PluginConfig.Instance.RecordWallHits = value;
        }

        [UIValue("record-fails")]
        public bool RecordFails
        {
            get => PluginConfig.Instance.RecordFails;
            set => PluginConfig.Instance.RecordFails = value;
        }

        [UIValue("show-fail-marker")]
        public bool ShowFailMarker
        {
            get => PluginConfig.Instance.ShowFailMarker;
            set => PluginConfig.Instance.ShowFailMarker = value;
        }

        [UIValue("auto-import-replays")]
        public bool AutoImportReplays
        {
            get => PluginConfig.Instance.AutoImportReplays;
            set => PluginConfig.Instance.AutoImportReplays = value;
        }

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

        [UIValue("FormatSeconds")]
        public string FormatSeconds(float value) => $"{value:F1} s";

        [UIValue("FormatOneDecimal")]
        public string FormatOneDecimal(float value) => value.ToString("F1");
    }
}
