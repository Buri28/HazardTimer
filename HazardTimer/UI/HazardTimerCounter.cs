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

        // 行ごとに色を変えるので、文字列に埋め込む形で持つ。
        // 1 つの TMP_Text に 2 行出しており、色を分けるにはこの方法しかない
        private const string CalmColor = "#FFFFFF";
        private const string CautionColor = "#FFD940";
        private const string WarningColor = "#FF5A4D";

        // フェイルは別の系統の色にして、近づいたら同じように赤へ寄せる。
        // 1 色のままだと、あと何秒かが色から読み取れない
        private const string FailCalmColor = "#B48CFF";
        private const string FailCautionColor = "#FF9E40";

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
            if (fail.HasValue) AppendLine(builder, fail.Value, isFail: true);
            if (primary.HasValue)
            {
                if (builder.Length > 0) builder.Append('\n');
                AppendLine(builder, primary.Value, isFail: false);
            }

            counterText.text = builder.ToString();
        }

        private static void AppendLine(StringBuilder sb, CountdownEntry entry, bool isFail)
        {
            sb.Append("<color=");
            sb.Append(ColorFor(entry.RemainingSeconds, isFail));
            sb.Append('>');
            sb.Append(entry.Label);
            sb.Append(' ');
            sb.Append(Mathf.Max(entry.RemainingSeconds, 0f).ToString("F1"));
            sb.Append("</color>");
        }

        private static string ColorFor(float remainingSeconds, bool isFail)
        {
            if (remainingSeconds <= WarningSeconds) return WarningColor;
            if (remainingSeconds <= CautionSeconds) return isFail ? FailCautionColor : CautionColor;
            return isFail ? FailCalmColor : CalmColor;
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
            // 色は行ごとにテキスト側で指定する
            counterText.richText = true;
            counterText.color = Color.white;
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
