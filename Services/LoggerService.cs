using System;
using System.IO;
using System.Text;

namespace MediaShow.Services
{
    /// <summary>
    /// 按级别和日期将日志写入程序目录下的 Logs 文件夹。
    /// </summary>
    public static class LoggerService
    {
        private static readonly object _lock = new();
        private static string? _logDirectory;

        static LoggerService()
        {
            try
            {
                // 日志保存在程序所在目录的 Logs 子文件夹
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logDirectory = Path.Combine(baseDir, "Logs");
                Directory.CreateDirectory(_logDirectory);
            }
            catch
            {
                // 若无法创建目录，日志将静默丢失，不影响主程序
                _logDirectory = null;
            }
        }

        private static string GetLogFilePath(string level)
        {
            if (_logDirectory == null) return string.Empty;
            var date = DateTime.Now.ToString("yyyyMMdd");
            return Path.Combine(_logDirectory, $"{date}_{level}.log");
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            var sb = new StringBuilder(message);
            if (ex != null)
            {
                sb.Append(" | Exception: ").Append(ex.Message);
                sb.Append(" | StackTrace: ").Append(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.Append(" | Inner: ").Append(ex.InnerException.Message);
                }
            }
            Write("ERROR", sb.ToString());
        }

        private static void Write(string level, string message)
        {
            if (string.IsNullOrEmpty(_logDirectory)) return;

            try
            {
                var logPath = GetLogFilePath(level);
                if (string.IsNullOrEmpty(logPath)) return;

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [Thread {System.Threading.Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志写入失败不抛出，避免影响主程序
            }
        }
    }
}