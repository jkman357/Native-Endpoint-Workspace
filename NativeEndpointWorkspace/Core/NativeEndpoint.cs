using System;

namespace NativeEndpointWorkspace.Core
{
    public class NativeEndpoint
    {
        public NativeEndpoint(
            int cellId,
            IntPtr handle,
            string title,
            string processName,
            uint processId,
            uint threadId,
            long processStartTimeUtcTicks,
            string windowClassName)
        {
            CellId = cellId;
            Handle = handle;
            Title = title ?? string.Empty;
            ProcessName = processName ?? string.Empty;
            ProcessId = processId;
            ThreadId = threadId;
            ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
            WindowClassName = windowClassName ?? string.Empty;
        }

        public int CellId { get; private set; }
        public IntPtr Handle { get; private set; }
        public string Title { get; private set; }
        public string ProcessName { get; private set; }
        public uint ProcessId { get; private set; }
        public uint ThreadId { get; private set; }
        public long ProcessStartTimeUtcTicks { get; private set; }
        public string WindowClassName { get; private set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title))
                    return Title;
                if (!string.IsNullOrWhiteSpace(ProcessName))
                    return ProcessName;
                return "HWND 0x" + Handle.ToInt64().ToString("X");
            }
        }

        public string IdentitySummary
        {
            get
            {
                return "HWND 0x" + Handle.ToInt64().ToString("X") +
                       ", PID " + ProcessId +
                       ", TID " + ThreadId;
            }
        }
    }
}
