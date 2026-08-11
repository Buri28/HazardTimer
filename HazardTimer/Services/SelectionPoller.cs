using System;
using UnityEngine;

namespace HazardTimer.Services
{
    /// <summary>
    /// 一定間隔でコールバックを呼ぶだけの MonoBehaviour。
    /// 選択中の譜面を定期的に見に行くために使う。
    /// </summary>
    public class SelectionPoller : MonoBehaviour
    {
        private Action? onPoll;
        private float intervalSeconds = 0.25f;
        private float elapsed;

        public void Initialize(Action callback, float interval)
        {
            onPoll = callback;
            intervalSeconds = interval;
            elapsed = 0f;
        }

        private void Update()
        {
            if (onPoll == null) return;

            elapsed += Time.unscaledDeltaTime;
            if (elapsed < intervalSeconds) return;

            elapsed = 0f;
            onPoll();
        }
    }
}
