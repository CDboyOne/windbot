using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using YGOSharp.Network;
using YGOSharp.Network.Enums;
using YGOSharp.Network.Utils;

namespace WindBot.Game
{
    public class GameClient
    {
        private static long _nextInstanceId;

        public YGOClient Connection { get; private set; }
        public long InstanceId { get; private set; }
        public string ExecutorName { get; private set; }
        public string CurrentSTOCMessage { get; private set; }
        public string Username;
        public string Deck;
        public string DeckFile;
        public string Dialog;
        public int Hand;
        public bool Debug;
        public bool _chat;
        public string RecordFile;
        public MatchRecordWriter Recorder { get; private set; }
        private string _serverHost;
        private int _serverPort;
        private short _proVersion;

        private string _roomInfo;

        private GameBehavior _behavior;

        public GameClient(WindBotInfo Info)
        {
            InstanceId = Interlocked.Increment(ref _nextInstanceId);
            Username = Info.Name;
            Deck = Info.Deck;
            DeckFile = Info.DeckFile;
            Dialog = Info.Dialog;
            Hand = Info.Hand;
            Debug = Info.Debug;
            _chat = Info.Chat;
            _serverHost = Info.Host;
            _serverPort = Info.Port;
            _roomInfo = Info.HostInfo;
            _proVersion = (short)Info.Version;
            RecordFile = Info.RecordFile;
        }

        internal void SetDeckContext(string executorName)
        {
            ExecutorName = executorName;
        }

        internal void SetCurrentSTOCMessage(string message)
        {
            CurrentSTOCMessage = message;
        }

        public string GetLogContext()
        {
            return "Instance=" + InstanceId
                + ", Bot=" + (Username ?? "<null>")
                + ", Executor=" + (ExecutorName ?? "<not initialized>")
                + ", STOCMessage=" + (CurrentSTOCMessage ?? "<none>");
        }

        public void Start()
        {
            Connection = new YGOClient();
            _behavior = new GameBehavior(this);

            Connection.Connected += OnConnected;
            Connection.PacketReceived += OnPacketReceived;
            Connection.PacketSent += OnPacketSent;
            OpenRecorder();

            IPAddress target_address;
            try
            {
                target_address = IPAddress.Parse(_serverHost);
            }
            catch (System.Exception)
            {
                IPHostEntry _hostEntry = Dns.GetHostEntry(_serverHost);
                target_address = _hostEntry.AddressList.FirstOrDefault(findIPv4 => findIPv4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            }

            Connection.Connect(target_address, _serverPort);
        }

        private void OnConnected()
        {
            BinaryWriter packet = GamePacketFactory.Create(CtosMessage.ExternalAddress);
            packet.Write((UInt32)0); // real_ip, is always 0 in normal client
            packet.WriteUnicodeAutoLength(_serverHost, 255);
            Connection.Send(packet);

            packet = GamePacketFactory.Create(CtosMessage.PlayerInfo);
            packet.WriteUnicode(Username, 20);
            Connection.Send(packet);

            byte[] junk = { 0xCC, 0xCC, 0x00, 0x00, 0x00, 0x00 };
            packet = GamePacketFactory.Create(CtosMessage.JoinGame);
            packet.Write(_proVersion);
            packet.Write(junk);
            packet.WriteUnicode(_roomInfo, 20);
            Connection.Send(packet);
        }

        public void Tick()
        {
            Connection.Update();
        }

        public void Chat(string message)
        {
            BinaryWriter chat = GamePacketFactory.Create(CtosMessage.Chat);
            chat.WriteUnicodeAutoLength(message, 255);
            Connection.Send(chat);
        }

        public void Surrender()
        {
            Connection.Send(CtosMessage.Surrender);
        }

        public string SaveReplay(byte[] replayPayload)
        {
            if (Recorder == null)
                return null;
            string yrpPath = Recorder.SaveReplay(replayPayload);
            Logger.WriteLine("YRP saved: " + yrpPath);
            return yrpPath;
        }

        public void CloseRecorder()
        {
            if (Recorder == null)
                return;
            if (Recorder.Truncated)
                Logger.WriteLine("YMR reached 8MB limit; later packets were not written.");
            Logger.WriteLine("YMR saved: " + Recorder.YmrPath + " (" + Recorder.RecordCount + " records)");
            Recorder.Dispose();
            Recorder = null;
        }

        private void OpenRecorder()
        {
            if (string.IsNullOrWhiteSpace(RecordFile))
                return;

            RecordFile = Path.GetFullPath(RecordFile);
            Recorder = MatchRecordWriter.Create(RecordFile, (ushort)_proVersion);
            Logger.WriteLine("Recording: " + Recorder.YmrPath);
        }

        private void OnPacketSent(byte[] packet)
        {
            RecordPacket(MatchRecordWriter.DirectionCtos, packet);
        }

        private void OnPacketReceived(BinaryReader reader)
        {
            MemoryStream stream = reader.BaseStream as MemoryStream;
            if (stream != null)
                RecordPacket(MatchRecordWriter.DirectionStoc, stream.ToArray());
            _behavior.OnPacket(reader);
        }

        private void RecordPacket(byte direction, byte[] packet)
        {
            if (Recorder == null || packet == null || packet.Length == 0)
                return;
            Recorder.AppendPacket(direction, packet);
        }
    }
}