using System;
using System.IO;
using System.Text;

namespace RevitConnector.Utils
{
    /// <summary>Minimal file logger. Must never throw — logging failures are swallowed.</summary>
    public static class Logger
    {
        private static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex != null)
                    line += Environment.NewLine + ex;

                var file = Path.Combine(
                    PathManager.GetLogsDirectoryPath(),
                    $"revit-connector-{DateTime.Now:yyyyMMdd}.log");

                lock (Gate)
                {
                    File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
