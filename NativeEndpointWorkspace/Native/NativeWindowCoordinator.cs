using System;
using System.Diagnostics;
using System.Text;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Native
{
    public class NativeWindowCoordinator
    {
        private const int RectTolerance = 3;

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

        public bool IsMinimized(IntPtr hwnd)
        {
            return IsValidWindow(hwnd) && NativeMethods.IsIconic(hwnd);
        }

        public NativeEndpoint DescribeWindow(int cellId, IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);

            string processName = string.Empty;
            try
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid != 0)
                    processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                processName = string.Empty;
            }

            return new NativeEndpoint(cellId, hwnd, sb.ToString(), processName);
        }

        // Geometry synchronization deliberately leaves Z-order untouched. rc06 applies geometry
        // and then reconstructs the endpoint/workspace Z-order in the same native layout commit.
        public bool SyncToRectangle(IntPtr hwnd, int x, int y, int width, int height)
        {
            if (!IsValidWindow(hwnd))
                return false;

            if (NativeMethods.IsIconic(hwnd))
                return true;

            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (WindowRectMatches(hwnd, x, y, width, height))
                return true;

            return NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        // Raises a normal (non-topmost) top-level window without activating it.
        // This is only used while the Workspace or one of its bound endpoints is foreground.
        public bool RaiseWithoutActivate(IntPtr hwnd)
        {
            if (!IsValidWindow(hwnd) || NativeMethods.IsIconic(hwnd))
                return false;

            return NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOP,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        // Place a window immediately behind a known top-level window without activation.
        // rc06 uses this to keep the opaque WPF Workspace below the entire bound-endpoint
        // group while preserving all windows as independent non-topmost top-level windows.
        public bool PlaceBehindWithoutActivate(IntPtr hwnd, IntPtr hwndAbove)
        {
            if (!IsValidWindow(hwnd) || !IsValidWindow(hwndAbove) || hwnd == hwndAbove)
                return false;

            return NativeMethods.SetWindowPos(
                hwnd,
                hwndAbove,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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

        public void Minimize(IntPtr hwnd)
        {
            if (IsValidWindow(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
        }

        public void Restore(IntPtr hwnd)
        {
            if (IsValidWindow(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        public void RequestClose(IntPtr hwnd)
        {
            if (IsValidWindow(hwnd))
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
