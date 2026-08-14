using HazardTimer.Markers;

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

        /// <summary>フェイルマーカーを他のマーカーと併記するか。</summary>
        public virtual bool ShowFailMarker { get; set; } = true;

        /// <summary>ミス地点をタイマーに表示するか。</summary>
        /// <remarks>
        /// 種別ごとの表示可否は譜面ごとではなく全譜面に効かせる。譜面ごとの
        /// On / Off は既にマーカー単位の指定でできるので、そちらと同じ粒度で
        /// 二重に持つと、どちらで消したのか分からなくなる。
        /// 記録そのものは残すため、切っても取り込みや一覧には影響しない。
        /// </remarks>
        public virtual bool ShowMissMarkers { get; set; } = true;

        /// <summary>ボムの被弾地点をタイマーに表示するか。</summary>
        public virtual bool ShowBombMarkers { get; set; } = true;

        /// <summary>壁への接触地点をタイマーに表示するか。</summary>
        public virtual bool ShowWallMarkers { get; set; } = true;

        /// <summary>その種別をタイマーに表示する設定になっているか。</summary>
        public bool IsShown(MarkerSource source) => source switch
        {
            MarkerSource.Miss => ShowMissMarkers,
            MarkerSource.Bomb => ShowBombMarkers,
            MarkerSource.Wall => ShowWallMarkers,
            MarkerSource.Fail => ShowFailMarker,
            // 手動マーカーは利用者が 1 つずつ置いたものなので、種別でまとめて消さない
            _ => true,
        };

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

        /// <summary>
        /// 1 譜面あたり何個までミス地点を取り込むか。0 なら取り込まない。
        /// </summary>
        /// <remarks>
        /// ミスは壁と比べて数が多いので、全部入れると一覧が埋まる。
        /// 複数のプレイで重なっている箇所を優先し、残りは曲の先頭から埋める。
        /// </remarks>
        public virtual int MaxMissMarkers { get; set; } = 10;

        /// <summary>
        /// 1 譜面あたり何個まで爆弾の被弾地点を取り込むか。0 なら取り込まない。
        /// </summary>
        /// <remarks>
        /// 爆弾はミスほど頻繁には当たらないので、取り込んだものは既定で使う指定にする。
        /// ボムリセットのような、当たると立て直せない配置を見落とさないため。
        /// </remarks>
        public virtual int MaxBombMarkers { get; set; } = 5;

        public virtual float CounterXOffset { get; set; } = 0.0f;
        public virtual float CounterYOffset { get; set; } = 0.0f;
    }
}
