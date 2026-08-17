using System;
using System.Threading;

namespace NativeEndpointWorkspace.Core
{
    public class NativeEndpoint
    {
        private int _destroyObserved;

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
            BindingInstanceId = Guid.NewGuid();
        }

        public int CellId { get; private set; }
        public IntPtr Handle { get; private set; }
        public string Title { get; private set; }
        public string ProcessName { get; private set; }
        public uint ProcessId { get; private set; }
        public uint ThreadId { get; private set; }
        public long ProcessStartTimeUtcTicks { get; private set; }
        public string WindowClassName { get; private set; }
        public Guid BindingInstanceId { get; private set; }

        public bool DestroyObserved
        {
            get { return Volatile.Read(ref _destroyObserved) != 0; }
        }

        internal void MarkDestroyObserved()
        {
            Interlocked.Exchange(ref _destroyObserved, 1);
        }

        internal void ReassignCellId(int cellId)
        {
            if (cellId < 1 || cellId > WorkspaceConstants.MaximumCellCount)
                throw new ArgumentOutOfRangeException(nameof(cellId));
            CellId = cellId;
        }

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
