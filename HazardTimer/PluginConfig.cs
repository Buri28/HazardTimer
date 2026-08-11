namespace HazardTimer
{
    /// <summary>
    /// プラグイン設定。BSIPA の生成ストアで永続化されるため、
    /// 公開プロパティはすべて virtual にする必要がある。
    /// </summary>
    public class PluginConfig
    {
        public static PluginConfig Instance { get; internal set; } = new PluginConfig();

        /// <summary>カウントダウンを開始する残り秒数（譜面時間ではなく実時間）。</summary>
        public virtual float LeadTimeSeconds { get; set; } = 10.0f;

        /// <summary>
        /// 壁マーカーのクラスタ統合閾値（秒）。
        /// 前のイベントからこの秒数未満なら同一クラスタとみなし、先頭の時刻だけを残す。
        /// </summary>
        public virtual float ClusterThresholdSeconds { get; set; } = 5.0f;

        /// <summary>壁への接触地点を記録するか。</summary>
        public virtual bool RecordWallHits { get; set; } = true;

        /// <summary>フェイル地点を記録するか。</summary>
        public virtual bool RecordFails { get; set; } = true;

        /// <summary>フェイルマーカーを他のマーカーと併記するか。</summary>
        public virtual bool ShowFailMarker { get; set; } = true;

        /// <summary>
        /// 曲を選んだときに、その譜面のリプレイを自動で取り込むか。
        /// 通信は行わず、ローカルに保存済みのファイルだけを読む。
        /// </summary>
        public virtual bool AutoImportReplays { get; set; } = true;

        /// <summary>
        /// 1 譜面あたり何件のリプレイまで読むか。新しいものから数える。
        /// </summary>
        /// <remarks>
        /// 何百回も遊んだ譜面では全部読むと重く、マーカーも増えすぎる。
        /// 中身を読まないとフェイル時刻が分からないので、絞り込みに使えるのは
        /// ファイル名の時刻だけ。新しいものほど今の実力を表すので、その順で採る。
        /// </remarks>
        public virtual int MaxImportReplays { get; set; } = 10;

        public virtual float CounterXOffset { get; set; } = 0.0f;
        public virtual float CounterYOffset { get; set; } = 0.0f;
    }
}
