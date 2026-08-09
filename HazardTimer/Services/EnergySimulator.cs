namespace HazardTimer.Services
{
    /// <summary>
    /// ゲーム本体の <c>GameEnergyCounter</c> と同じ規則でエネルギーを再現する。
    /// </summary>
    /// <remarks>
    /// NoFail 中、本体は <c>ProcessEnergyChange</c> の先頭で
    /// <c>if (noFail) return;</c> と即座に抜けるため、エネルギーが一切動かない。
    /// 体力ゼロのイベントも飛ばない。フェイル地点を知るには自前で数えるしかない。
    /// <para>
    /// 定数と分岐は 1.40.8 の <c>GameEnergyCounter</c> から取っている。
    /// バッテリー／インスタフェイルは規則が別なので、この模擬では扱わない。
    /// </para>
    /// </remarks>
    public class EnergySimulator
    {
        public const float BadNoteEnergyDrain = 0.1f;
        public const float BadChainLinkEnergyDrain = 0.025f;
        public const float MissNoteEnergyDrain = 0.15f;
        public const float MissChainLinkEnergyDrain = 0.03f;
        public const float HitBombEnergyDrain = 0.15f;
        public const float GoodNoteEnergyCharge = 0.01f;
        public const float GoodChainLinkEnergyCharge = 0.002f;
        public const float ObstacleEnergyDrainPerSecond = 1.3f;

        private const float StartEnergy = 0.5f;
        private const float MaxEnergy = 1.0f;

        /// <summary>本体が 0 とみなす閾値。</summary>
        private const float ZeroEpsilon = 1e-5f;

        public float Energy { get; private set; } = StartEnergy;

        /// <summary>一度でも 0 に達したか。本体と同じく一度きりで固定する。</summary>
        public bool ReachedZero { get; private set; }

        /// <summary>
        /// エネルギーを増減する。この呼び出しで初めて 0 に達したら true。
        /// </summary>
        public bool ApplyDelta(float delta)
        {
            if (ReachedZero) return false;

            if (delta < 0f)
            {
                if (Energy <= 0f) return false;
                Energy += delta;
                if (Energy <= ZeroEpsilon)
                {
                    Energy = 0f;
                    ReachedZero = true;
                    return true;
                }
                return false;
            }

            if (Energy >= MaxEnergy) return false;
            Energy += delta;
            if (Energy > MaxEnergy) Energy = MaxEnergy;
            return false;
        }

        /// <summary>ノートを切ったときの増減。</summary>
        public bool ApplyNoteCut(bool isChainLink, bool allIsOK) => ApplyDelta(
            isChainLink
                ? (allIsOK ? GoodChainLinkEnergyCharge : -BadChainLinkEnergyDrain)
                : (allIsOK ? GoodNoteEnergyCharge : -BadNoteEnergyDrain));

        /// <summary>ノートを見逃したときの減少。</summary>
        public bool ApplyNoteMiss(bool isChainLink) => ApplyDelta(
            -(isChainLink ? MissChainLinkEnergyDrain : MissNoteEnergyDrain));

        /// <summary>爆弾に当たったときの減少。爆弾は見逃しても減らない。</summary>
        public bool ApplyBombHit() => ApplyDelta(-HitBombEnergyDrain);

        /// <summary>壁の中にいた時間ぶんの減少。</summary>
        public bool ApplyObstacleTime(float seconds) => ApplyDelta(-ObstacleEnergyDrainPerSecond * seconds);

        /// <summary>
        /// 壁の中にいる間に 0 へ到達するまでの秒数。到達しないなら null。
        /// リプレイの区間から、ゼロ交差の時刻を割り出すために使う。
        /// </summary>
        public float? SecondsUntilZeroInObstacle()
        {
            if (ReachedZero || Energy <= 0f) return null;
            return Energy / ObstacleEnergyDrainPerSecond;
        }
    }
}
