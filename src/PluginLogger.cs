using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace CESDK
{
    /// <summary>
    /// Process-local CESDK file logger backed by NLog.
    /// </summary>
    public static class PluginLogger
    {
        private static readonly string logFilePath = CreateLogFilePath();
        private static readonly LogFactory logFactory = CreateLogFactory();
        private static readonly Logger logger = logFactory.GetLogger("CESDK");

        /// <summary>Gets the active CESDK log file path.</summary>
        public static string LogFilePath => logFilePath;

        internal static LogFactory Factory => logFactory;

        public static void Log(string message) =>
            logger.Info(message);

        public static void LogException(Exception ex) =>
            logger.Error(ex, "Unhandled CESDK plugin exception");

        /// <summary>Flushes buffered log events to their targets.</summary>
        public static void Flush() =>
            logFactory.Flush(TimeSpan.FromSeconds(5));

        private static string CreateLogFilePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CeMCP",
                "ce-mcp.log");


        private static LogFactory CreateLogFactory()
        {
            var factory = new LogFactory();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                var configuration = new LoggingConfiguration(factory);
                var fileTarget = new FileTarget("ce-mcp-file")
                {
                    FileName = logFilePath,
                    KeepFileOpen = false,
                    ArchiveAboveSize = 10 * 1024 * 1024,
                    MaxArchiveFiles = 5,
                    Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner=|${exception:format=tostring}}",
                };

                configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
                factory.Configuration = configuration;
            }
            catch (Exception)
            {
                // Logging must never prevent a Cheat Engine plugin from loading.
            }

            return factory;
        }
    }
}