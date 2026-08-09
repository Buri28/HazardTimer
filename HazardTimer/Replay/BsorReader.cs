using System;
using System.IO;
using System.Text;

namespace HazardTimer.Replay
{
    /// <summary>
    /// BeatLeader のリプレイ（<c>.bsor</c> v1）から、Info と壁イベントだけを読む。
    /// </summary>
    /// <remarks>
    /// フォーマット仕様: https://github.com/BeatLeader/BS-Open-Replay
    /// セクションは 0:Info, 1:Frames, 2:Notes, 3:Walls, ... の順に並ぶ。
    /// フレームとノートは中身に用がないので、サイズ計算で読み飛ばす。
    /// リトルエンディアン。文字列は「長さ(int) + バイト列」。
    /// </remarks>
    public static class BsorReader
    {
        private const int MagicNumber = 0x442d3d69;

        // Frame: time(float) + fps(int) + head/leftHand/rightHand それぞれ Vector3 + Quaternion
        private const int FrameSize = 4 + 4 + 3 * (12 + 16);

        // NoteCutInfo: bool×4 + float + Vector3 + int + float×2 + Vector3×2 + float×4
        private const int NoteCutInfoSize = 4 + 4 + 12 + 4 + 8 + 24 + 16;

        private const int NoteEventGood = 0;
        private const int NoteEventBad = 1;

        /// <summary>
        /// リプレイを読む。壊れていたり想定外の形式なら null を返す（例外は投げない）。
        /// </summary>
        public static BsorReplay? Read(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream, Encoding.UTF8);
                return ReadCore(reader);
            }
            catch (Exception e)
            {
                Plugin.Log?.Warn($"Failed to read replay '{Path.GetFileName(path)}': {e.Message}");
                return null;
            }
        }

        private static BsorReplay? ReadCore(BinaryReader reader)
        {
            if (reader.ReadInt32() != MagicNumber) return null;

            var version = reader.ReadByte();
            if (version != 1) return null;

            var replay = new BsorReplay();

            if (reader.ReadByte() != 0) return null;
            ReadInfo(reader, replay);

            if (reader.ReadByte() != 1) return null;
            SkipFrames(reader);

            if (reader.ReadByte() != 2) return null;
            ReadNotes(reader, replay);

            if (reader.ReadByte() != 3) return null;
            ReadWalls(reader, replay);

            return replay;
        }

        private static void ReadInfo(BinaryReader reader, BsorReplay replay)
        {
            SkipString(reader);          // version
            SkipString(reader);          // gameVersion
            SkipString(reader);          // timestamp
            SkipString(reader);          // playerID
            SkipString(reader);          // playerName
            SkipString(reader);          // platform
            SkipString(reader);          // trackingSystem
            SkipString(reader);          // hmd
            SkipString(reader);          // controller
            SkipString(reader);          // hash
            SkipString(reader);          // songName
            SkipString(reader);          // mapper
            SkipString(reader);          // difficulty
            reader.ReadInt32();          // score
            SkipString(reader);          // mode
            SkipString(reader);          // environment
            replay.Modifiers = ReadString(reader);
            reader.ReadSingle();         // jumpDistance
            reader.ReadBoolean();        // leftHanded
            reader.ReadSingle();         // height
            replay.StartTime = reader.ReadSingle();

            var failTime = reader.ReadSingle();
            // フェイルしていない場合は 0 が入る
            replay.FailTime = failTime > 0f ? failTime : (float?)null;

            reader.ReadSingle();         // speed
        }

        private static void SkipFrames(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            Skip(reader, (long)count * FrameSize);
        }

        private static void ReadNotes(BinaryReader reader, BsorReplay replay)
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var noteId = reader.ReadInt32();
                var eventTime = reader.ReadSingle();
                reader.ReadSingle();             // spawnTime
                var eventType = reader.ReadInt32();

                // cutInfo は good / bad のときだけ続く
                if (eventType == NoteEventGood || eventType == NoteEventBad)
                {
                    Skip(reader, NoteCutInfoSize);
                }

                replay.NoteEvents.Add(new ReplayNoteEvent(eventTime, eventType, IsChainLink(noteId)));
            }
            replay.NoteEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        /// <summary>
        /// noteID からチェーンの節かどうかを判定する。
        /// </summary>
        /// <remarks>
        /// noteID は
        /// <c>(scoringType + 2) * 10000 + lineIndex * 1000 + lineLayer * 100 + colorType * 10 + cutDirection</c>。
        /// +2 されているのは <c>ScoringType.Ignore</c> が -1 で負になるのを避けるため。
        /// 実リプレイでの検証: アークの頭と尾（符号化値 4 と 5）の出現数が完全に一致し、
        /// 通常ノート（符号化値 3）が大多数を占めることで、このオフセットを確認している。
        /// </remarks>
        private static bool IsChainLink(int noteId)
        {
            var scoringType = noteId / 10000 - 2;
            // ChainLink = 5, ChainLinkArcHead = 8
            return scoringType == 5 || scoringType == 8;
        }

        private static void ReadWalls(BinaryReader reader, BsorReplay replay)
        {
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                reader.ReadInt32();              // wallID
                reader.ReadSingle();             // energy
                var end = reader.ReadSingle();
                var start = reader.ReadSingle();

                // 仕様書では 4 番目を "spawn time of wall" と書いているが、実データでは
                // end との差が中央値 0.055 秒しかなく、壁の出現時刻ではありえない
                // （出現ならジャンプ時間ぶん 2 秒前後になる）。接触の開始時刻として扱う。
                // 全 870 イベントで start < end、負の差はゼロ。
                replay.ObstacleIntervals.Add(new ReplayObstacleInterval(start, end));
            }
            replay.ObstacleIntervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > 1_000_000) throw new InvalidDataException($"Bad string length {length}");
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        private static void SkipString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > 1_000_000) throw new InvalidDataException($"Bad string length {length}");
            Skip(reader, length);
        }

        private static void Skip(BinaryReader reader, long bytes)
        {
            if (bytes < 0) throw new InvalidDataException($"Bad skip length {bytes}");
            // 実ファイルは常にシーク可能だが、念のため保険を残す
            if (reader.BaseStream.CanSeek) reader.BaseStream.Seek(bytes, SeekOrigin.Current);
            else reader.ReadBytes((int)bytes);
        }
    }
}
