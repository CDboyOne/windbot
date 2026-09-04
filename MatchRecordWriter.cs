using System;
using System.IO;
using System.Linq;

namespace WindBot
{
    /// <summary>
    /// Streaming YMR1 writer. Format matches flu-fork / csharp-src MatchRecordWriter:
    /// 20-byte header then direction + proto + u16 payload_len + payload (no TCP length prefix).
    /// </summary>
    public sealed class MatchRecordWriter : IDisposable
    {
        public const uint Magic = 0x31726D79;
        public const int FormatVersion = 1;
        public const int HeaderSize = 20;
        public const int MaxDataSize = 0x800000;

        public const byte DirectionCtos = 0;
        public const byte DirectionStoc = 1;

        private readonly FileStream _stream;
        private readonly object _sync = new object();
        private readonly ushort _proVersion;
        private int _recordCount;
        private int _dataSize;
        private bool _truncated;
        private bool _disposed;

        private MatchRecordWriter(FileStream stream, ushort proVersion, string recordFile, string ymrPath)
        {
            _stream = stream;
            _proVersion = proVersion;
            RecordFile = recordFile;
            YmrPath = ymrPath;
        }

        /// <summary>Path without extension; .ymr / .yrp are appended to this.</summary>
        public string RecordFile { get; private set; }
        public string YmrPath { get; private set; }
        public bool Truncated { get { return _truncated; } }
        public int RecordCount { get { return _recordCount; } }

        /// <summary>
        /// Create a writer for <paramref name="recordFile"/> + ".ymr".
        /// <paramref name="recordFile"/> is the path without extension.
        /// </summary>
        public static MatchRecordWriter Create(string recordFile, ushort proVersion)
        {
            string path = recordFile + ".ymr";
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.Read,
                4096,
                FileOptions.None);
            MatchRecordWriter writer = new MatchRecordWriter(stream, proVersion, recordFile, path);
            writer.WriteHeaderUnlocked();
            stream.Flush(true);
            return writer;
        }

        public void Append(byte direction, byte proto, byte[] payload)
        {
            if (payload == null)
                payload = new byte[0];

            lock (_sync)
            {
                if (_truncated || _disposed)
                    return;

                int payloadLen = payload.Length;
                if (payloadLen > ushort.MaxValue)
                {
                    byte[] truncated = new byte[ushort.MaxValue];
                    Array.Copy(payload, truncated, ushort.MaxValue);
                    payload = truncated;
                    payloadLen = ushort.MaxValue;
                }

                int entrySize = 4 + payloadLen;
                if (_dataSize + entrySize > MaxDataSize)
                {
                    _truncated = true;
                    return;
                }

                _stream.Seek(HeaderSize + _dataSize, SeekOrigin.Begin);
                _stream.WriteByte(direction);
                _stream.WriteByte(proto);
                byte[] lenBuf = BitConverter.GetBytes((ushort)payloadLen);
                _stream.Write(lenBuf, 0, 2);
                if (payloadLen > 0)
                    _stream.Write(payload, 0, payloadLen);
                _recordCount++;
                _dataSize += entrySize;
                WriteHeaderUnlocked();
                _stream.Flush(true);
            }
        }

        public void AppendPacket(byte direction, byte[] packet)
        {
            if (packet == null)
                throw new ArgumentNullException("packet");
            if (packet.Length == 0)
                throw new ArgumentException("Empty packet", "packet");

            byte[] payload = new byte[packet.Length - 1];
            if (payload.Length > 0)
                Array.Copy(packet, 1, payload, 0, payload.Length);
            Append(direction, packet[0], payload);
        }

        public string SaveReplay(byte[] replayPayload)
        {
            if (replayPayload == null)
                replayPayload = new byte[0];

            string yrpPath = RecordFile + ".yrp";
            File.WriteAllBytes(yrpPath, replayPayload);
            return yrpPath;
        }

        private void WriteHeaderUnlocked()
        {
            byte[] header = new byte[HeaderSize];
            WriteUInt32(header, 0, Magic);
            WriteInt32(header, 4, FormatVersion);
            WriteUInt32(header, 8, _proVersion);
            WriteInt32(header, 12, _recordCount);
            WriteInt32(header, 16, _dataSize);
            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Write(header, 0, header.Length);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, 4);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, buffer, offset, 4);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _stream.Dispose();
            }
        }
    }
}
