using System;
using System.Collections.Generic;
using System.Linq;
using NativeEndpointWorkspace.Native;

namespace NativeEndpointWorkspace.Services
{
    // Observes external top-level windows without injection. rc10 filters managed HWNDs
    // inside the WinEvent callback before WPF Dispatcher work is created.
    public sealed class EndpointLayoutLockService : IDisposable
    {
        private readonly object _managedHandleLock = new object();
        private readonly HashSet<IntPtr> _managedHandles = new HashSet<IntPtr>();
        private readonly NativeMethods.WinEventDelegate _callback;
        private IntPtr _locationHook;
        private IntPtr _foregroundHook;
        private IntPtr _destroyHook;
        private IntPtr _workspaceHwnd;
        private bool _disposed;

        public event Action<IntPtr> WindowLocationChanged;
        public event Action<IntPtr> ForegroundChanged;
        public event Action<IntPtr> WindowDestroyed;

        public EndpointLayoutLockService()
        {
            _callback = WinEventCallback;
        }

        public void SetWorkspaceHandle(IntPtr workspaceHwnd)
        {
            lock (_managedHandleLock)
                _workspaceHwnd = workspaceHwnd;
        }

        public void UpdateManagedHandles(IEnumerable<IntPtr> handles)
        {
            lock (_managedHandleLock)
            {
                _managedHandles.Clear();
                if (handles == null)
                    return;

                foreach (IntPtr handle in handles.Where(x => x != IntPtr.Zero))
                    _managedHandles.Add(handle);
            }
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

            if (_destroyHook == IntPtr.Zero)
            {
                _destroyHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    NativeMethods.EVENT_OBJECT_DESTROY,
                    IntPtr.Zero,
                    _callback,
                    0,
                    0,
                    NativeMethods.WINEVENT_OUTOFCONTEXT);
            }

            return _locationHook != IntPtr.Zero && _foregroundHook != IntPtr.Zero && _destroyHook != IntPtr.Zero;
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
        {
            if (_disposed || hwnd == IntPtr.Zero)
                return;

            bool isManaged;
            bool isWorkspace;
            lock (_managedHandleLock)
            {
                isManaged = _managedHandles.Contains(hwnd);
                isWorkspace = hwnd == _workspaceHwnd;
            }

            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                if (!isWorkspace && !isManaged)
                    return;

                Action<IntPtr> foregroundHandler = ForegroundChanged;
                if (foregroundHandler != null)
                    foregroundHandler(hwnd);
                return;
            }

            if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
                return;
            if (!isManaged)
                return;

            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                Action<IntPtr> destroyedHandler = WindowDestroyed;
                if (destroyedHandler != null)
                    destroyedHandler(hwnd);
                return;
            }

            if (eventType != NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
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

            if (_destroyHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_destroyHook);
                _destroyHook = IntPtr.Zero;
            }

            lock (_managedHandleLock)
                _managedHandles.Clear();
        }
    }
}
