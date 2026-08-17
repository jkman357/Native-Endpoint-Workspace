using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Services
{
    public sealed class RuntimeLogService : IDisposable
    {
        public static RuntimeLogService Shared { get; } = new RuntimeLogService();

        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly string _filePath;
        private readonly bool _debugEnabled;
        private bool _disposed;

        public RuntimeLogService()
        {
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _filePath = Path.Combine(_directory, "NativeEndpointWorkspace.log");
            _debugEnabled = string.Equals(
                Environment.GetEnvironmentVariable("NATIVE_ENDPOINT_WORKSPACE_LOG_LEVEL"),
                "DEBUG", StringComparison.OrdinalIgnoreCase);
        }

        public string LogFilePath { get { return _filePath; } }

        public void StartSession()
        {
            Info("SESSION_START", "version=" + WorkspaceConstants.Version +
                 " pid=" + Process.GetCurrentProcess().Id +
                 " os=" + Environment.OSVersion.VersionString +
                 " clr=" + Environment.Version);
        }

        public void Info(string eventName, string details) { Write("INFO", eventName, details); }
        public void Warn(string eventName, string details) { Write("WARN", eventName, details); }
        public void Error(string eventName, Exception ex)
        {
            string safe = ex == null ? "unknown" : "type=" + ex.GetType().FullName + " hresult=0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture);
            Write("ERROR", eventName, safe);
        }
        public void Debug(string eventName, string details)
        {
            if (_debugEnabled) Write("DEBUG", eventName, details);
        }

        public static string EndpointMetadata(NativeEndpoint endpoint)
        {
            if (endpoint == null) return "endpoint=null";
            return "cell=" + endpoint.CellId +
                   " hwnd=0x" + endpoint.Handle.ToInt64().ToString("X") +
                   " pid=" + endpoint.ProcessId +
                   " tid=" + endpoint.ThreadId +
                   " process=" + SafeToken(endpoint.ProcessName);
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private void Write(string level, string eventName, string details)
        {
            if (_disposed) return;
            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    RotateIfNeeded();
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                                  " [" + level + "] " + eventName +
                                  (string.IsNullOrWhiteSpace(details) ? string.Empty : " " + details) + Environment.NewLine;
                    File.AppendAllText(_filePath, line);
                }
                catch
                {
                    // Diagnostics must never become a new failure path for the Workspace.
                }
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(_filePath)) return;
            var info = new FileInfo(_filePath);
            if (info.Length < WorkspaceConstants.RuntimeLogMaxBytes) return;

            int backupCount = Math.Max(0, WorkspaceConstants.RuntimeLogRetentionFiles - 1);
            if (backupCount == 0)
            {
                File.Delete(_filePath);
                return;
            }

            string oldest = _filePath + "." + backupCount;
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = backupCount - 1; i >= 1; i--)
            {
                string source = _filePath + "." + i;
                string destination = _filePath + "." + (i + 1);
                if (File.Exists(source)) File.Move(source, destination);
            }
            File.Move(_filePath, _filePath + ".1");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Info("SESSION_END", "version=" + WorkspaceConstants.Version);
            _disposed = true;
        }
    }
}
