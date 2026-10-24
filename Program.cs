using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Net;
using System.Diagnostics;
using Serilog.Context;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper;

namespace WindBot
{
    public class Program
    {
        internal static Random Rand;
        internal static bool ServerMode;

        internal static void Main(string[] args)
        {
            Logger.Initialize();
            Logger.WriteLine("WindBot starting...");

            Config.Load(args);

            string databasePath = Config.GetString("DbPath", "cards.cdb");

            InitDatas(databasePath);

            ServerMode = Config.GetBool("ServerMode", false);

            if (ServerMode)
            {
                // Run in server mode, provide a http interface to create bot.
                int serverPort = Config.GetInt("ServerPort", 2399);
                RunAsServer(serverPort);
            }
            else
            {
                // Join the host specified on the command line.
                if (args.Length == 0)
                {
                    Logger.WriteErrorLine("=== WARN ===", null);
                    Logger.WriteLine("No input found, trying to connect to localhost YGOPro host.");
                    Logger.WriteLine("If it fails, the program will quit silently.");
                }
                RunFromArgs();
            }
        }

        public static void InitDatas(string databasePath)
        {
            Rand = new Random();
            DecksManager.Init();
            string absolutePath = Path.GetFullPath(databasePath);
            if (!File.Exists(absolutePath))
                // In case windbot is placed in a folder under ygopro folder
                absolutePath = Path.GetFullPath("../" + databasePath);
            if (!File.Exists(absolutePath))
                // In case windbot is placed in a folder under ygopro2 folder
                absolutePath = Path.GetFullPath("../cdb/" + databasePath);
            if (!File.Exists(absolutePath))
            {
                Logger.WriteErrorLine("Can't find cards database file.", null);
                Logger.WriteErrorLine("Please place cards.cdb next to WindBot.exe or Bot.exe .", null);
                Logger.WriteLine("Press any key to quit...");
                Console.ReadKey();
                System.Environment.Exit(1);
            }
            NamedCardsManager.Init(absolutePath);
        }

        private static void RunFromArgs()
        {
            WindBotInfo Info = new WindBotInfo();
            Info.Name = Config.GetString("Name", Info.Name);
            Info.Deck = Config.GetString("Deck", Info.Deck);
            Info.DeckFile = Config.GetString("DeckFile", Info.DeckFile);
            Info.Dialog = Config.GetString("Dialog", Info.Dialog);
            Info.Host = Config.GetString("Host", Info.Host);
            Info.Port = Config.GetInt("Port", Info.Port);
            Info.HostInfo = Config.GetString("HostInfo", Info.HostInfo);
            Info.Version = Config.GetInt("Version", Info.Version);
            Info.Hand = Config.GetInt("Hand", Info.Hand);
            Info.Debug = Config.GetBool("Debug", Info.Debug);
            Info.Chat = Config.GetBool("Chat", Info.Chat);
            Info.RecordFile = Config.GetString("RecordFile", Info.RecordFile);
            Run(Info);
        }

        private static void RunAsServer(int ServerPort)
        {
            using (HttpListener MainServer = new HttpListener())
            {
                MainServer.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                MainServer.Prefixes.Add("http://+:" + ServerPort + "/");
                try
                {
                    MainServer.TimeoutManager.IdleConnection = TimeSpan.FromHours(6);
                    if (OperatingSystem.IsWindows())
                    {
                        MainServer.TimeoutManager.RequestQueue = TimeSpan.FromHours(6);
                        MainServer.TimeoutManager.HeaderWait = TimeSpan.FromMinutes(5);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine("Set HttpListener timeout failed", ex);
                }
                MainServer.Start();
                Logger.WriteLine("WindBot server start successed.");
                Logger.WriteLine("HTTP GET http://127.0.0.1:" + ServerPort + "/?name=WindBot&host=127.0.0.1&port=7911 to call the bot.");
                Logger.WriteLine("The HTTP response is sent after the duel ends. Each request runs on a dedicated thread (not ThreadPool).");
                while (true)
                {
                    try
                    {
                        HttpListenerContext ctx = MainServer.GetContext();
                        // Dedicated thread per duel: ThreadPool workers stuck in Tick/Sleep would
                        // delay Socket ConnectCallback and miss srvpro's 2s pre-join idle timeout.
                        Thread thread = new Thread(() => HandleHttpRequest(ctx))
                        {
                            IsBackground = true,
                        };
                        thread.Start();
                    }
                    catch (Exception ex)
                    {
                        if (Debugger.IsAttached)
                            throw;
                        Logger.WriteErrorLine("Parse Http Request Error", ex);
                    }
                }
            }
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
                return result;
            string q = query[0] == '?' ? query.Substring(1) : query;
            foreach (string part in q.Split('&'))
            {
                if (part.Length == 0)
                    continue;
                int eq = part.IndexOf('=');
                string key;
                string value;
                if (eq < 0)
                {
                    key = Uri.UnescapeDataString(part.Replace('+', ' '));
                    value = string.Empty;
                }
                else
                {
                    key = Uri.UnescapeDataString(part.Substring(0, eq).Replace('+', ' '));
                    value = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                }
                result[key] = value;
            }
            return result;
        }

        private static string GetQuery(Dictionary<string, string> queryParams, string key)
        {
            string value;
            return queryParams.TryGetValue(key, out value) ? value : null;
        }

        private static void HandleHttpRequest(HttpListenerContext ctx)
        {
            try
            {
                Logger.WriteLine("HTTP bot request " + ctx.Request.RawUrl);
                var queryParams = ParseQueryString(ctx.Request.Url.Query);

                WindBotInfo Info = new WindBotInfo();
                Info.Name = GetQuery(queryParams, "name");
                Info.Deck = GetQuery(queryParams, "deck");
                Info.Host = GetQuery(queryParams, "host");
                string port = GetQuery(queryParams, "port");
                if (port != null)
                    Info.Port = Int32.Parse(port);
                string deckfile = GetQuery(queryParams, "deckfile"); // Obsoleted
                string dialog = GetQuery(queryParams, "dialog");
                if (dialog != null)
                    Info.Dialog = dialog;
                string version = GetQuery(queryParams, "version");
                if (version != null)
                    Info.Version = Int16.Parse(version);
                string password = GetQuery(queryParams, "password");
                if (password != null)
                    Info.HostInfo = password;
                string hand = GetQuery(queryParams, "hand");
                if (hand != null)
                    Info.Hand = Int32.Parse(hand);
                string debug = GetQuery(queryParams, "debug");
                if (debug != null)
                    Info.Debug = bool.Parse(debug);
                string chat = GetQuery(queryParams, "chat");
                if (chat != null)
                    Info.Chat = bool.Parse(chat);
                string recordFile = GetQuery(queryParams, "recordfile");
                if (recordFile != null)
                    Info.RecordFile = recordFile;

                if (Info.Name == null || Info.Host == null || port == null || deckfile != null)
                {
                    Logger.WriteLine("HTTP bot request rejected (400): name, host, or port is missing, or obsolete deckfile is set.");
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    return;
                }
                if (Info.Deck != null && !DecksManager.HasDeck(Info.Deck))
                {
                    Logger.WriteLine("HTTP bot request rejected (404): unknown deck \"" + Info.Deck + "\".");
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                try
                {
                    Thread.CurrentThread.Name = "WindBot-" + Info.Name;
                }
                catch (InvalidOperationException)
                {
                    // Name can only be set once.
                }

                try
                {
                    Run(Info);
                    ctx.Response.StatusCode = 200;
                }
                catch (Exception ex)
                {
                    if (Debugger.IsAttached)
                        throw;
                    Logger.WriteErrorLine("Start Thread Error", ex);
                    ctx.Response.StatusCode = 500;
                }
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                if (Debugger.IsAttached)
                    throw;
                Logger.WriteErrorLine("Parse Http Request Error", ex);
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch
                {
                    // already closed
                }
            }
        }

        private static void Run(object o)
        {
            // All errors should be caught instead of causing the program to crash.
            WindBotInfo Info = (WindBotInfo)o;
            using (LogContext.PushProperty("name", Info.Name ?? "<null>"))
            {
                GameClient client = null;
                try
                {
                    Logger.WriteLine((Info.Name ?? "<null>") + " session starting.");
                    client = new GameClient(Info);
                    client.Start();
                    while (client.Connection.IsConnected)
                    {
                        try
                        {
                            client.Tick();
                            #if DEBUG
                                Thread.Sleep(1);
                            #else
                                Thread.Sleep(30);
                            #endif
                        }
                        catch (Exception ex)
                        {
                            if (Debugger.IsAttached)
                                throw;
                            Logger.WriteErrorLine("Tick Error", ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Debugger.IsAttached)
                        throw;
                    Logger.WriteErrorLine("Run Error", ex);
                }
                finally
                {
                    try
                    {
                        if (client != null)
                            client.CloseRecorder();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine("Close recorder error", ex);
                    }
                    if (client != null)
                        Logger.WriteLine(client.Username + " session ended.");
                }
            }
        }

        public static FileStream ReadFile(string directory, string filename, string extension)
        {
            string tryfilename = filename + "." + extension;
            string fullpath = Path.Combine(directory, tryfilename);
            if (!File.Exists(fullpath))
                fullpath = filename;
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../", filename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../deck/", filename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../", tryfilename);
            if (!File.Exists(fullpath))
                fullpath = Path.Combine("../deck/", tryfilename);
            return new FileStream(fullpath, FileMode.Open, FileAccess.Read);
        }
    }
}
