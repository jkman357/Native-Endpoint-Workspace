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

        // Read-only probes deliberately use the lightweight identity check. Health
        // revalidation and every external native mutation use the strong policy below.
        public bool IsMinimized(NativeEndpoint endpoint)
        {
            return IsEndpointIdentityCurrent(endpoint, false) && NativeMethods.IsIconic(endpoint.Handle);
        }

        public bool IsHung(NativeEndpoint endpoint)
        {
            return IsEndpointIdentityCurrent(endpoint, false) && NativeMethods.IsHungAppWindow(endpoint.Handle);
        }

        public bool IsVisible(NativeEndpoint endpoint)
        {
            return IsEndpointIdentityCurrent(endpoint, false) && NativeMethods.IsWindowVisible(endpoint.Handle);
        }

        public bool RequestClientRepaint(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation) ||
                NativeMethods.IsIconic(endpoint.Handle) ||
                NativeMethods.IsHungAppWindow(endpoint.Handle) ||
                !NativeMethods.IsWindowVisible(endpoint.Handle))
                return false;

            return NativeMethods.RedrawWindow(
                endpoint.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN);
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
            if (endpoint == null || endpoint.Handle == IntPtr.Zero)
                return EndpointIdentityStatus.WindowMissing;
            if (endpoint.DestroyObserved)
                return EndpointIdentityStatus.DestroyObserved;
            if (!NativeMethods.IsWindow(endpoint.Handle))
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

            if (requireStrongProcessCheck)
            {
                long currentStartTimeUtcTicks;
                bool available = TryGetProcessStartTimeUtcTicks(currentProcessId, out currentStartTimeUtcTicks);
                EndpointIdentityStatus strongStatus = EndpointIdentityPolicy.EvaluateStrongProcessStart(
                    endpoint.ProcessStartTimeUtcTicks, available, currentStartTimeUtcTicks);
                if (strongStatus != EndpointIdentityStatus.Current)
                    return strongStatus;
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
            return SyncToRectangle(endpoint, x, y, width, height, false, out ignoredError);
        }

        public GeometrySyncResult SyncToRectangle(NativeEndpoint endpoint, int x, int y, int width, int height, out int nativeErrorCode)
        {
            return SyncToRectangle(endpoint, x, y, width, height, false, out nativeErrorCode);
        }

        public GeometrySyncResult SyncToRectangle(NativeEndpoint endpoint, int x, int y, int width, int height, bool discardClientBits, out int nativeErrorCode)
        {
            nativeErrorCode = 0;
            if (!IsEndpointIdentityCurrent(endpoint, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation))
                return GeometrySyncResult.StaleEndpoint;

            if (NativeMethods.IsIconic(endpoint.Handle))
                return GeometrySyncResult.SkippedMinimized;

            if (NativeMethods.IsHungAppWindow(endpoint.Handle))
                return GeometrySyncResult.HungEndpoint;

            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (WindowRectMatches(endpoint.Handle, x, y, width, height))
                return GeometrySyncResult.AlreadyCorrect;

            uint flags = NativeMethods.SWP_NOACTIVATE |
                         NativeMethods.SWP_NOZORDER |
                         NativeMethods.SWP_ASYNCWINDOWPOS;
            if (discardClientBits)
                flags |= NativeMethods.SWP_NOCOPYBITS;

            bool ok = NativeMethods.SetWindowPos(
                endpoint.Handle,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                flags);

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

        public bool RaiseWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation) ||
                NativeMethods.IsIconic(endpoint.Handle) || NativeMethods.IsHungAppWindow(endpoint.Handle))
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

        public bool TryAnalyzeEndpointGroupZOrder(
            IntPtr workspaceHwnd,
            NativeEndpoint[] endpoints,
            out bool workspaceBelowAllEndpoints,
            out NativeEndpoint bottomMostEndpoint)
        {
            workspaceBelowAllEndpoints = false;
            bottomMostEndpoint = null;

            if (!IsValidWindow(workspaceHwnd) || endpoints == null || endpoints.Length == 0)
                return false;

            var managedByHandle = new System.Collections.Generic.Dictionary<IntPtr, NativeEndpoint>();
            foreach (NativeEndpoint endpoint in endpoints)
            {
                if (endpoint == null || !IsEndpointIdentityCurrent(endpoint, false))
                    continue;
                managedByHandle[endpoint.Handle] = endpoint;
            }

            if (managedByHandle.Count == 0)
                return false;

            int workspaceOrder = -1;
            int bottomMostEndpointOrder = -1;
            int foundEndpointCount = 0;
            int order = 0;
            int guard = 0;
            IntPtr current = NativeMethods.GetTopWindow(IntPtr.Zero);

            while (current != IntPtr.Zero && guard++ < WorkspaceConstants.MaximumZOrderEnumerationWindows)
            {
                if (current == workspaceHwnd)
                    workspaceOrder = order;

                NativeEndpoint endpoint;
                if (managedByHandle.TryGetValue(current, out endpoint))
                {
                    foundEndpointCount++;
                    if (order > bottomMostEndpointOrder)
                    {
                        bottomMostEndpointOrder = order;
                        bottomMostEndpoint = endpoint;
                    }
                }

                if (workspaceOrder >= 0 && foundEndpointCount == managedByHandle.Count)
                    break;

                current = NativeMethods.GetWindow(current, NativeMethods.GW_HWNDNEXT);
                order++;
            }

            if (workspaceOrder < 0 || foundEndpointCount != managedByHandle.Count || bottomMostEndpoint == null)
                return false;

            workspaceBelowAllEndpoints = bottomMostEndpointOrder < workspaceOrder;
            return true;
        }

        public bool PlaceWorkspaceBehindEndpoint(IntPtr workspaceHwnd, NativeEndpoint endpointAbove)
        {
            int ignoredError;
            return PlaceWorkspaceBehindEndpoint(workspaceHwnd, endpointAbove, out ignoredError);
        }

        public bool PlaceWorkspaceBehindEndpoint(IntPtr workspaceHwnd, NativeEndpoint endpointAbove, out int nativeErrorCode)
        {
            nativeErrorCode = 0;
            if (!IsValidWindow(workspaceHwnd) ||
                !IsEndpointIdentityCurrent(endpointAbove, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation) ||
                workspaceHwnd == endpointAbove.Handle)
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

        public bool MinimizeWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation))
                return false;
            return NativeMethods.ShowWindowAsync(endpoint.Handle, NativeMethods.SW_SHOWMINNOACTIVE);
        }

        public bool RestoreWithoutActivate(NativeEndpoint endpoint)
        {
            if (!IsEndpointIdentityCurrent(endpoint, EndpointIdentityPolicy.RequireStrongCheckForNativeMutation))
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
