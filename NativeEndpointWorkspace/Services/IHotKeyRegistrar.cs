using System;
using System.ComponentModel;
using NativeEndpointWorkspace.Native;

namespace NativeEndpointWorkspace.Services
{
    public interface IHotKeyRegistrar
    {
        bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int nativeErrorCode);
        bool Unregister(IntPtr windowHandle, int id);
    }

    internal sealed class NativeHotKeyRegistrar : IHotKeyRegistrar
    {
        public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int nativeErrorCode)
        {
            bool ok = NativeMethods.RegisterHotKey(windowHandle, id, modifiers, virtualKey);
            nativeErrorCode = ok ? 0 : new Win32Exception().NativeErrorCode;
            return ok;
        }

        public bool Unregister(IntPtr windowHandle, int id)
        {
            return NativeMethods.UnregisterHotKey(windowHandle, id);
        }
    }
}
