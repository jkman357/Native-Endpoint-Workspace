using System;
using System.Diagnostics;
using System.Text;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Native
{
    public class NativeWindowCoordinator
    {
        private const int RectTolerance = 3;
        private const int WindowTextCapacity = 512;
        private const int WindowClassCapacity = 256;

        public IntPtr GetForegroundWindow()
        {
            return NativeMethods.GetForegroundWindow();
        }

        public bool IsValidWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);
        }

        public bool IsCurrentProcessWindow(IntPtr hwnd)
        {
            if (!IsValidWindow(hwnd)) return false;
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }

        public bool IsMinimized(NativeEndpoint endpoint)
        {
            return IsEndpointIdentityCurrent(endpoint, false) && NativeMethods.IsIconic(endpoint.Handle);
        }

        public bool IsHung(NativeEndpoint endpoint)
        {
            return IsEndpointIdentityCurrent(endpoint, false) && NativeMethods.IsHungAppWindow(endpoint.Handle);
        }

        public NativeEndpoint DescribeWindow(int cellId, IntPtr hwnd)
        {
            var title = new StringBuilder(WindowTextCapacity);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);

            var className = new StringBuilder(WindowClassCapacity);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);

            uint processId;
            uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            string processName = string.Empty;
            long processStartTimeUtcTicks = 0;

            try
            {
                if (processId != 0 && processId <= int.MaxValue)
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        processName = process.ProcessName;
                        try
                        {
                            processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                        }
                        catch
                        {
                            processStartTimeUtcTicks = 0;
                        }
                    }
                }
            }
            catch
            {
                processName = string.Empty;
                processStartTimeUtcTicks = 0;
            }

            return new NativeEndpoint(
                cellId,
                hwnd,
                title.ToString(),
                processName,
                processId,
                threadId,
                processStartTimeUtcTicks,
                className.ToString());
        }

        public EndpointIdentityStatus ValidateEndpointIdentity(NativeEndpoint endpoint, bool requireStrongProcessCheck)
        {
            if (endpoint == null || endpoint.Handle == IntPtr.Zero || !NativeMethods.IsWindow(endpoint.Handle))
                return EndpointIdentityStatus.WindowMissing;

            uint currentProcessId;
            uint currentThreadId = NativeMethods.GetWindowThreadProcessId(endpoint.Handle, out currentProcessId);
            if (currentProcessId != endpoint.ProcessId || currentThreadId != endpoint.ThreadId)
                return EndpointIdentityStatus.ProcessOrThreadChanged;

            if (!string.IsNullOrEmpty(endpoint.WindowClassName))
            {
                var className = new StringBuilder(WindowClassCapacity);
                if (NativeMethods.GetClassName(endpoint.Handle, className, className.Capacity) > 0 &&
                    !string.Equals(className.ToString(), endpoint.WindowClassName, StringComparison.Ordinal))
                    return EndpointIdentityStatus.WindowClassChanged;
            }

            if (requireStrongProcessCheck && endpoint.ProcessStartTimeUtcTicks > 0)
            {
                long currentStartTimeUtcTicks;
                if (!TryGetProcessStartTimeUtcTicks(currentProcessId, out currentStartTimeUtcTicks))
                    return EndpointIdentityStatus.ProcessStartTimeUnavailable;

                if (currentStartTimeUtcTicks != endpoint.ProcessStartTimeUtcTicks)
                    return EndpointIdentityStatus.ProcessStartTimeChanged;
            }

            return EndpointIdentityStatus.Current;
        }

        public bool IsEndpointIdentityCurrent(NativeEndpoint endpoint, bool requireStrongProcessCheck)
        {
            return ValidateEndpointIdentity(endpoint, requireStrongProcessCheck) == EndpointIdentityStatus.Current;
        }

        public GeometrySyncResult SyncToRectangle(NativeEndpoint endpoint, int x, int y, int width, int height)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return GeometrySyncResult.StaleEndpoint;

            if (NativeMethods.IsIconic(endpoint.Handle))
                return GeometrySyncResult.SkippedMinimized;

            if (NativeMethods.IsHungAppWindow(endpoint.Handle))
                return GeometrySyncResult.HungEndpoint;

            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (WindowRectMatches(endpoint.Handle, x, y, width, height))
                return GeometrySyncResult.AlreadyCorrect;

            bool ok = NativeMethods.SetWindowPos(
                endpoint.Handle,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_ASYNCWINDOWPOS);

            return ok ? GeometrySyncResult.Applied : GeometrySyncResult.Failed;
        }

        public bool IsAtRectangle(NativeEndpoint endpoint, int x, int y, int width, int height)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;
            return WindowRectMatches(endpoint.Handle, x, y, Math.Max(1, width), Math.Max(1, height));
        }

        // Endpoint Z-order requests are asynchronous across process/thread boundaries so an
        // unresponsive external UI thread cannot block the WPF Dispatcher. Bound Cells do
        // not overlap, so relative endpoint order is not relied on for layout correctness.
        public bool RaiseWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false) || NativeMethods.IsIconic(endpoint.Handle))
                return false;
            if (NativeMethods.IsHungAppWindow(endpoint.Handle))
                return false;

            return NativeMethods.SetWindowPos(
                endpoint.Handle,
                NativeMethods.HWND_TOP,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_ASYNCWINDOWPOS);
        }

        public bool PlaceWorkspaceBehindEndpoint(IntPtr workspaceHwnd, NativeEndpoint endpointAbove)
        {
            if (!IsValidWindow(workspaceHwnd) || !IsEndpointIdentityCurrent(endpointAbove, false) || workspaceHwnd == endpointAbove.Handle)
                return false;

            return NativeMethods.SetWindowPos(
                workspaceHwnd,
                endpointAbove.Handle,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        public bool Minimize(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;
            return NativeMethods.ShowWindowAsync(endpoint.Handle, NativeMethods.SW_MINIMIZE);
        }

        public bool Restore(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;
            return NativeMethods.ShowWindowAsync(endpoint.Handle, NativeMethods.SW_RESTORE);
        }

        public bool RequestClose(NativeEndpoint endpoint)
        {
            // WM_CLOSE is destructive. Revalidate the strong endpoint identity immediately
            // before posting so a stale/reused HWND is not treated as the original endpoint.
            if (!IsEndpointIdentityCurrent(endpoint, true))
                return false;

            return NativeMethods.PostMessage(endpoint.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private static bool TryGetProcessStartTimeUtcTicks(uint processId, out long ticks)
        {
            ticks = 0;
            try
            {
                if (processId == 0 || processId > int.MaxValue)
                    return false;

                using (Process process = Process.GetProcessById((int)processId))
                {
                    ticks = process.StartTime.ToUniversalTime().Ticks;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool WindowRectMatches(IntPtr hwnd, int x, int y, int width, int height)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
                return false;

            int currentWidth = rect.Right - rect.Left;
            int currentHeight = rect.Bottom - rect.Top;
            return Math.Abs(rect.Left - x) <= RectTolerance &&
                   Math.Abs(rect.Top - y) <= RectTolerance &&
                   Math.Abs(currentWidth - width) <= RectTolerance &&
                   Math.Abs(currentHeight - height) <= RectTolerance;
        }
    }
}
