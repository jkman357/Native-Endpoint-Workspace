using System;
using System.Diagnostics;
using System.Text;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Native
{
    public class NativeWindowCoordinator
    {

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
            var title = new StringBuilder(WorkspaceConstants.WindowTextCapacity);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);

            var className = new StringBuilder(WorkspaceConstants.WindowClassCapacity);
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
                var className = new StringBuilder(WorkspaceConstants.WindowClassCapacity);
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
            int ignoredError;
            return SyncToRectangle(endpoint, x, y, width, height, out ignoredError);
        }

        public GeometrySyncResult SyncToRectangle(NativeEndpoint endpoint, int x, int y, int width, int height, out int nativeErrorCode)
        {
            nativeErrorCode = 0;
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

            if (!ok)
                nativeErrorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
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
            int ignoredError;
            return PlaceWorkspaceBehindEndpoint(workspaceHwnd, endpointAbove, out ignoredError);
        }

        public bool PlaceWorkspaceBehindEndpoint(IntPtr workspaceHwnd, NativeEndpoint endpointAbove, out int nativeErrorCode)
        {
            nativeErrorCode = 0;
            if (!IsValidWindow(workspaceHwnd) || !IsEndpointIdentityCurrent(endpointAbove, false) || workspaceHwnd == endpointAbove.Handle)
                return false;

            bool ok = NativeMethods.SetWindowPos(
                workspaceHwnd,
                endpointAbove.Handle,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            if (!ok)
                nativeErrorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            return ok;
        }

        // Group operations deliberately avoid activation. User clicks are the only path that
        // should naturally activate an endpoint.
        public bool MinimizeWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;
            return NativeMethods.ShowWindowAsync(endpoint.Handle, NativeMethods.SW_SHOWMINNOACTIVE);
        }

        public bool RestoreWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;
            return NativeMethods.ShowWindowAsync(endpoint.Handle, NativeMethods.SW_SHOWNOACTIVATE);
        }

        public bool TryGetWindowRectangle(NativeEndpoint endpoint, out int x, out int y, out int width, out int height)
        {
            x = y = width = height = 0;
            if (!IsEndpointIdentityCurrent(endpoint, false))
                return false;

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(endpoint.Handle, out rect))
                return false;

            x = rect.Left;
            y = rect.Top;
            width = Math.Max(1, rect.Right - rect.Left);
            height = Math.Max(1, rect.Bottom - rect.Top);
            return true;
        }

        public bool TryGetMonitorWorkArea(IntPtr hwnd, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            if (!IsValidWindow(hwnd))
                return false;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            var info = new NativeMethods.MONITORINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                return false;

            left = info.rcWork.Left;
            top = info.rcWork.Top;
            right = info.rcWork.Right;
            bottom = info.rcWork.Bottom;
            return true;
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
            return Math.Abs(rect.Left - x) <= WorkspaceConstants.WindowRectangleTolerance &&
                   Math.Abs(rect.Top - y) <= WorkspaceConstants.WindowRectangleTolerance &&
                   Math.Abs(currentWidth - width) <= WorkspaceConstants.WindowRectangleTolerance &&
                   Math.Abs(currentHeight - height) <= WorkspaceConstants.WindowRectangleTolerance;
        }
    }
}
