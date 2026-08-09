using System;
using UnityEngine;

namespace HazardTimer.Services
{
    /// <summary>
    /// 頭部が障害物に入った「立ち上がり」を検出して通知する MonoBehaviour。
    /// 退出時刻ではなく進入時刻を直接得られるのがこの方式の利点。
    /// </summary>
    public class ObstacleMonitor : MonoBehaviour
    {
        private PlayerHeadAndObstacleInteraction? interaction;
        private Action? onEnter;
        private Action<float>? onInsideForSeconds;
        private bool previousFrameInObstacle;

        /// <param name="enterCallback">接触が始まったフレームで呼ばれる。</param>
        /// <param name="insideCallback">
        /// 壁の中にいる間、毎フレームその経過時間で呼ばれる。
        /// 本体の <c>GameEnergyCounter.LateUpdate</c> と同じ位置・同じ値を渡す。
        /// </param>
        public void Initialize(PlayerHeadAndObstacleInteraction interactionRef,
                               Action enterCallback,
                               Action<float>? insideCallback = null)
        {
            interaction = interactionRef;
            onEnter = enterCallback;
            onInsideForSeconds = insideCallback;
            previousFrameInObstacle = false;
        }

        private void LateUpdate()
        {
            if (interaction == null || onEnter == null) return;

            var current = interaction.playerHeadIsInObstacle;
            if (!previousFrameInObstacle && current)
            {
                onEnter();
            }
            if (current)
            {
                onInsideForSeconds?.Invoke(Time.deltaTime);
            }
            previousFrameInObstacle = current;
        }
    }
}
