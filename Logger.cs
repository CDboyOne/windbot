using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WindBot
{
    public static class Logger
    {
        private const string Template =
            "[{Timestamp:yyMMdd-HHmmss.fff}][{ShortLevel}][{SourceContext}]{Scope:l} {Message:l}{NewLine}{Exception}";

        public static void Initialize()
        {
            var config = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.With(new ShortLevelEnricher())
                .Enrich.FromLogContext()
                .Enrich.With(new BotNameScopeEnricher())
                .WriteTo.Console(outputTemplate: Template);
            Log.Logger = config.CreateLogger();
        }

        public static void WriteLine(string message, [CallerFilePath] string file = "")
        {
            ForSource(file).Information("{Text:l}", message);
        }

        public static void DebugWriteLine(string message, bool printStackTrace = false, [CallerFilePath] string file = "")
        {
#if DEBUG
            var log = ForSource(file);
            if (printStackTrace)
                log.Debug("{Text:l}" + Environment.NewLine + "{Stack:l}", message, new StackTrace(1, true).ToString());
            else
                log.Debug("{Text:l}", message);
#endif
        }

        public static void WriteErrorLine(string message, [CallerFilePath] string file = "")
        {
            WriteErrorLine(message, null, new StackTrace(1, true).ToString(), file);
        }

        public static void WriteErrorLine(string message, Exception exception, [CallerFilePath] string file = "")
        {
            WriteErrorLine(message, exception, null, file);
        }

        private static void WriteErrorLine(string message, Exception exception, string callStack, string file)
        {
            var log = ForSource(file);
            if (exception != null)
                log.Error(exception, "{Text:l}", message);
            else if (!string.IsNullOrEmpty(callStack))
                log.Error("{Text:l}" + Environment.NewLine + "Call stack:" + Environment.NewLine + "{Stack:l}", message, callStack);
            else
                log.Error("{Text:l}", message);
        }

        private static Serilog.ILogger ForSource(string file)
        {
            string source = string.IsNullOrEmpty(file)
                ? "WindBot"
                : Path.GetFileNameWithoutExtension(file);
            return Log.ForContext(Constants.SourceContextPropertyName, source);
        }

        private sealed class ShortLevelEnricher : ILogEventEnricher
        {
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
            {
                var level = logEvent.Level switch
                {
                    LogEventLevel.Verbose => "VERBOSE",
                    LogEventLevel.Debug => "DEBUG",
                    LogEventLevel.Information => "INFO",
                    LogEventLevel.Warning => "WARNING",
                    LogEventLevel.Error => "ERROR",
                    LogEventLevel.Fatal => "FATAL",
                    _ => logEvent.Level.ToString().ToUpperInvariant(),
                };
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ShortLevel", level));
            }
        }

        private sealed class BotNameScopeEnricher : ILogEventEnricher
        {
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
            {
                if (logEvent.Properties.TryGetValue("name", out var value)
                    && value is ScalarValue scalar
                    && scalar.Value is string name
                    && name.Length > 0)
                {
                    logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Scope", name));
                }
            }
        }
    }
}
