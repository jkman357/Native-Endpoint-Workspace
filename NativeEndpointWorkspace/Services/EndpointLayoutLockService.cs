using System;
using NativeEndpointWorkspace.Native;

namespace NativeEndpointWorkspace.Services
{
    // Observes, but never injects into, external top-level windows.
    // Two narrow out-of-context hooks are used instead of one broad event range so the
    // workspace does not receive unrelated accessibility events.
    public sealed class EndpointLayoutLockService : IDisposable
    {
        private readonly NativeMethods.WinEventDelegate _callback;
        private IntPtr _locationHook;
        private IntPtr _foregroundHook;
        private bool _disposed;

        public event Action<IntPtr> WindowLocationChanged;
        public event Action<IntPtr> ForegroundChanged;

        public EndpointLayoutLockService()
        {
            _callback = WinEventCallback;
        }

        public bool Start()
        {
            if (_locationHook == IntPtr.Zero)
            {
                _locationHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    _callback,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }

            if (_foregroundHook == IntPtr.Zero)
            {
                _foregroundHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _callback,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }

            return _locationHook != IntPtr.Zero && _foregroundHook != IntPtr.Zero;
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
        {
            if (_disposed || hwnd == IntPtr.Zero)
                return;

            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                Action<IntPtr> foregroundHandler = ForegroundChanged;
                if (foregroundHandler != null)
                    foregroundHandler(hwnd);
                return;
            }

            if (eventType != NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
                return;
            if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
                return;

            Action<IntPtr> locationHandler = WindowLocationChanged;
            if (locationHandler != null)
                locationHandler(hwnd);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_locationHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_locationHook);
                _locationHook = IntPtr.Zero;
            }

            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
        }
    }
}
