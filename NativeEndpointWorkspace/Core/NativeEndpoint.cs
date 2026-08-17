using System;

namespace NativeEndpointWorkspace.Core
{
    public class NativeEndpoint
    {
        public NativeEndpoint(int cellId, IntPtr handle, string title, string processName)
        {
            CellId = cellId;
            Handle = handle;
            Title = title ?? string.Empty;
            ProcessName = processName ?? string.Empty;
        }

        public int CellId { get; private set; }
        public IntPtr Handle { get; private set; }
        public string Title { get; private set; }
        public string ProcessName { get; private set; }

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
    }
}
