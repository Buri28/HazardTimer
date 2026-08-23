# HazardTimer

**試作段階**

譜面中の**危険地点までの残り秒数**をカウントダウン表示する Beat Saber 用MODです。

しゃがみ壁は、気付いてから反応しても間に合わないことがあります。
到達までの秒数が事前に分かれば、しゃがむ準備ができます。

> **開発中です。** まだ動作するリリースはありません。
<img width="40%" height="40%" alt="image" src="https://github.com/user-attachments/assets/b0e2db18-1a2d-4f6e-b47b-1a650226741b" />

## 何を危険地点とするか

譜面を解析して「しゃがみ壁かどうか」を判定するのではなく、
**実際に当たった場所を記録して、次回そこを警告します。**

| 種別 | 記録のされ方 |
|---|---|
| **壁** | 壁に接触した地点 |
| **フェイル** | フェイルした地点（1譜面につき1箇所） |
| **手動** | 分と秒を指定して自分で追加 |

デフォルト設定では到達の **10秒前** からカウントダウンが始まります。
連続した壁は1つと判定します。

## 使い方

カウントダウンは **Counters+ のカスタムカウンター** として表示されます。
`Mod Settings → Counters+` の一覧から **Hazard Timer** を有効にしてください。

<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/41b815e5-437f-4956-b056-55a2c16d2a8b" />

壁への接触とフェイルは、プレイするだけで自動的に記録されます。

マーカーの編集と設定は、曲選択画面の左パネル **Mods タブ → HazardTimer** で行います。

<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/72c2a06a-cffa-4feb-a91c-f9e64677e1c2" />

### 過去のリプレイから取り込む

実測方式なので、そのままだと初めて遊ぶ譜面では何も表示されません。
BeatLeader MOD を使っていれば、**曲を選んだ時点で過去のプレイ記録から自動で取り込みます。**

**通信は行いません。** `UserData/BeatLeader/Replays` にあるファイルを読むだけです。

NoFail を付けていると、ゲームはどこで落ちていたかを記録しません。
HazardTimer はゲーム本体と同じ規則で体力を数え直し、その地点をフェイルマーカーにします。

## 設定

Mods タブの `Settings` から変更できます。

<img width="50%" height="50%" alt="image" src="https://github.com/user-attachments/assets/12854469-54a3-4e06-b50a-26388ee4de58" />


| 項目 | 内容 | 既定 |
|---|---|---|
| Lead Time | 何秒前からカウントダウンを表示するか | 10秒 |
| Cluster Threshold | この秒数未満で続く壁への接触を1つの危険地点にまとめる | 5秒 |
| Record Wall Hits | 壁に当たった地点を記録する | ON |
| Record Fails | フェイルした地点を記録する | ON |
| Show Fail Marker | フェイル地点のカウントダウンを他と併記する | ON |
| Auto Import Replays | 曲を選んだときリプレイから自動で取り込む | ON |

表示位置は `Mod Settings → Counters+ → Hazard Timer` の `Counter X Offset` /
`Counter Y Offset` で調整します。

## 必要なMOD

- BSIPA
- BeatSaberMarkupLanguage
- SiraUtil
- Counters+

BeatLeader は必須ではありません。入っている場合のみ、リプレイの取り込みが使えます。

## 大会での使用について

本MODは **「プレイ中にマップの情報を表示するMOD」に該当します。**
BSWC をはじめとする多くの大会では、この種のMODは禁止されています。
大会に参加する際は必ず外してください。

## ライセンス

MIT License. [LICENSE](LICENSE) を参照してください。
