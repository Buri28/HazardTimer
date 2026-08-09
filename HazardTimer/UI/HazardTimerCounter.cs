using System.Text;
using CountersPlus.Counters.Custom;
using CountersPlus.Utils;
using HazardTimer.Markers;
using HazardTimer.Services;
using TMPro;
using UnityEngine;
using Zenject;

namespace HazardTimer.UI
{
    /// <summary>
    /// Counters+ のカスタムカウンターとして、危険地点までの残り秒数を表示する。
    /// 位置調整 UI は Counters+ 側が用意してくれる。
    /// </summary>
    public class HazardTimerCounter : BasicCustomCounter, ITickable
    {
        /// <summary>この残り秒数を切ったら注意色にする。</summary>
        private const float CautionSeconds = 5.0f;

        /// <summary>この残り秒数を切ったら警告色にする。</summary>
        private const float WarningSeconds = 2.0f;

        private static readonly Color CalmColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color CautionColor = new Color(1f, 0.85f, 0.25f, 1f);
        private static readonly Color WarningColor = new Color(1f, 0.35f, 0.3f, 1f);
        private static readonly Color FailColor = new Color(0.75f, 0.55f, 1f, 1f);

        private static HazardTimerCounter? activeInstance;

        [Inject] private readonly CountdownService countdownService = null!;
        [Inject] private readonly CanvasUtility canvasUtility = null!;

        private TMP_Text? counterText;
        private readonly StringBuilder builder = new StringBuilder(48);

        public override void CounterInit()
        {
            if (countdownService == null || canvasUtility == null || Settings == null) return;

            CreateOrUpdateText();
            activeInstance = this;
            Render();
        }

        public override void CounterDestroy()
        {
            if (counterText != null)
            {
                Object.Destroy(counterText.gameObject);
                counterText = null;
            }
            if (activeInstance == this) activeInstance = null;
        }

        public void Tick() => Render();

        private void Render()
        {
            if (counterText == null) return;

            var fail = countdownService.GetFail();
            var primary = countdownService.GetPrimary();

            if (fail == null && primary == null)
            {
                if (counterText.text.Length != 0) counterText.text = string.Empty;
                return;
            }

            builder.Length = 0;

            // フェイル枠は他と競合させず、常に上の行に置く
            if (fail.HasValue) AppendLine(builder, fail.Value);
            if (primary.HasValue)
            {
                if (builder.Length > 0) builder.Append('\n');
                AppendLine(builder, primary.Value);
            }

            counterText.text = builder.ToString();
            // 色は「一番切迫している方」に合わせる
            var nearest = primary.HasValue && fail.HasValue
                ? Mathf.Min(primary.Value.RemainingSeconds, fail.Value.RemainingSeconds)
                : (primary ?? fail!.Value).RemainingSeconds;
            counterText.color = ColorFor(nearest, primary == null && fail.HasValue);
        }

        private static void AppendLine(StringBuilder sb, CountdownEntry entry)
        {
            sb.Append(entry.Label);
            sb.Append(' ');
            sb.Append(Mathf.Max(entry.RemainingSeconds, 0f).ToString("F1"));
        }

        private static Color ColorFor(float remainingSeconds, bool failOnly)
        {
            if (failOnly) return FailColor;
            if (remainingSeconds <= WarningSeconds) return WarningColor;
            if (remainingSeconds <= CautionSeconds) return CautionColor;
            return CalmColor;
        }

        private void CreateOrUpdateText()
        {
            if (counterText != null)
            {
                Object.Destroy(counterText.gameObject);
                counterText = null;
            }

            counterText = canvasUtility.CreateTextFromSettings(Settings, Vector3.zero);
            counterText.fontSize = 4f;
            counterText.color = CalmColor;
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.enableWordWrapping = false;
            counterText.lineSpacing = -30;
            counterText.text = string.Empty;

            var rect = counterText.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(160f, 20f);
                rect.anchoredPosition += new Vector2(PluginConfig.Instance.CounterXOffset,
                                                     PluginConfig.Instance.CounterYOffset);
            }
        }

        /// <summary>設定画面からのオフセット変更を、表示中のカウンターへ即座に反映する。</summary>
        public static void ApplyOffsets() => activeInstance?.CreateOrUpdateText();
    }
}
