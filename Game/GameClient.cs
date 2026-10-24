using System;
using System.IO;
using System.Linq;
using System.Net;
using YGOSharp.Network;
using YGOSharp.Network.Enums;
using YGOSharp.Network.Utils;

namespace WindBot.Game
{
    public class GameClient
    {
        public YGOClient Connection { get; private set; }
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

        public void Start()
        {
            Logger.WriteLine(Lobby("connecting host=" + _serverHost + ":" + _serverPort
                + " version=0x" + ((ushort)_proVersion).ToString("X4")
                + " room=\"" + (_roomInfo ?? "") + "\""
                + " deck=" + (Deck ?? "<default>")
                + " deckFile=" + (DeckFile ?? "<executor>")
                + " hand=" + Hand
                + " chat=" + _chat));

            Connection = new YGOClient();
            _behavior = new GameBehavior(this);

            Connection.Connected += OnConnected;
            Connection.Disconnected += OnDisconnected;
            Connection.PacketReceived += OnPacketReceived;
            Connection.PacketSent += OnPacketSent;
            OpenRecorder();

            IPAddress targetAddress;
            if (!IPAddress.TryParse(_serverHost, out targetAddress))
            {
                Logger.WriteLine(Lobby("resolving host " + _serverHost));
                IPHostEntry hostEntry = Dns.GetHostEntry(_serverHost);
                targetAddress = hostEntry.AddressList.FirstOrDefault(
                    address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (targetAddress == null)
                    throw new InvalidOperationException("No IPv4 address for host " + _serverHost);
            }

            Logger.WriteLine(Lobby("TCP connect requested " + targetAddress + ":" + _serverPort));
            Connection.Connect(targetAddress, _serverPort);
        }

        private void OnConnected()
        {
            Logger.WriteLine(Lobby("TCP connected, sending ExternalAddress / PlayerInfo / JoinGame"));

            BinaryWriter packet = GamePacketFactory.Create(CtosMessage.ExternalAddress);
            packet.Write((UInt32)0); // real_ip, is always 0 in normal client
            packet.WriteUnicodeAutoLength(_serverHost, 255);
            Connection.Send(packet);
            Logger.WriteLine(Lobby("sent CTOS_EXTERNAL_ADDRESS hostname=" + _serverHost));

            packet = GamePacketFactory.Create(CtosMessage.PlayerInfo);
            packet.WriteUnicode(Username, 20);
            Connection.Send(packet);
            Logger.WriteLine(Lobby("sent CTOS_PLAYER_INFO name=" + Username));

            byte[] junk = { 0xCC, 0xCC, 0x00, 0x00, 0x00, 0x00 };
            packet = GamePacketFactory.Create(CtosMessage.JoinGame);
            packet.Write(_proVersion);
            packet.Write(junk);
            packet.WriteUnicode(_roomInfo, 20);
            Connection.Send(packet);
            Logger.WriteLine(Lobby("sent CTOS_JOIN_GAME version=0x" + ((ushort)_proVersion).ToString("X4")
                + " room=\"" + (_roomInfo ?? "") + "\""));
        }

        private void OnDisconnected(Exception error)
        {
            if (error == null)
            {
                Logger.WriteLine(Lobby("disconnected (local close)."));
                return;
            }

            bool remoteClose = error is IOException && error.Message == "remote host closed the connection";
            Logger.WriteLine(Lobby("disconnected: " + error.GetType().Name + ": " + error.Message));
            if (!remoteClose)
                Logger.WriteErrorLine(Lobby("disconnect error"), error);
        }

        private string Lobby(string message)
        {
            return (Username ?? "<null>") + " " + message;
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