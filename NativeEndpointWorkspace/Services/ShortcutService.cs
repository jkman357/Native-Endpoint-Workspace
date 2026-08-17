using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NativeEndpointWorkspace.Core;
using NativeEndpointWorkspace.Native;

namespace NativeEndpointWorkspace.Services
{
    public class ShortcutService : IDisposable
    {
        private readonly IntPtr _windowHandle;
        private readonly Dictionary<int, ShortcutBinding> _active = new Dictionary<int, ShortcutBinding>();

        public ShortcutService(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
        }

        public IList<ShortcutBinding> CreateDefaultBindings()
        {
            var list = new List<ShortcutBinding>();
            for (int i = 1; i <= WorkspaceConstants.FunctionKeyCount; i++)
            {
                list.Add(new ShortcutBinding
                {
                    CellId = i,
                    Control = true,
                    Shift = true,
                    Alt = false,
                    Win = false,
                    KeyCode = WorkspaceConstants.FunctionKeyDisplayOffset + i,
                    Status = "Not registered"
                });
            }
            return list;
        }

        public IList<ShortcutBinding> ActiveBindings
        {
            get { return _active.Values.OrderBy(x => x.CellId).Select(x => x.Clone()).ToList(); }
        }

        public bool ApplyBindings(IList<ShortcutBinding> requested, out string summary)
        {
            summary = string.Empty;
            if (requested == null || requested.Count == 0)
            {
                summary = "No shortcuts were supplied.";
                return false;
            }

            UnregisterAll();

            var duplicateKeys = new HashSet<string>(
                requested.GroupBy(x => x.ConflictKey)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key));

            int failureCount = 0;
            foreach (var binding in requested.OrderBy(x => x.CellId))
            {
                // rc11 shortcut policy: use Ctrl / Alt / Shift combinations only.
                // Bare F1-F12 and Win-key global combinations are rejected to reduce
                // collisions with normal application and Windows shell shortcuts.
                if (binding.Win)
                {
                    binding.Status = "Rejected: Win modifier is not supported";
                    failureCount++;
                    continue;
                }

                if (!binding.HasSupportedModifier)
                {
                    binding.Status = "Rejected: Ctrl, Alt, or Shift is required";
                    failureCount++;
                    continue;
                }

                if (binding.KeyCode < WorkspaceConstants.FunctionKeyFirstVirtualKey ||
                    binding.KeyCode > WorkspaceConstants.FunctionKeyLastVirtualKey)
                {
                    binding.Status = "Rejected: only F1-F12 are supported";
                    failureCount++;
                    continue;
                }

                if (duplicateKeys.Contains(binding.ConflictKey))
                {
                    binding.Status = "Conflict: duplicate inside workspace";
                    failureCount++;
                    continue;
                }

                int id = HotKeyIdForCell(binding.CellId);
                uint modifiers = BuildModifiers(binding) | NativeMethods.MOD_NOREPEAT;
                bool ok = NativeMethods.RegisterHotKey(_windowHandle, id, modifiers, (uint)binding.KeyCode);
                if (!ok)
                {
                    int nativeError = new Win32Exception().NativeErrorCode;
                    binding.Status = "Conflict/unavailable (Win32 " + nativeError + ")";
                    failureCount++;
                    continue;
                }

                binding.Status = "Registered";
                _active[binding.CellId] = binding.Clone();
            }

            if (failureCount == 0)
            {
                summary = "All " + requested.Count + " shortcuts registered.";
                return true;
            }

            summary = (requested.Count - failureCount) + " shortcut(s) registered; " + failureCount + " conflict/validation issue(s) detected.";
            return false;
        }

        public int CellIdFromHotKeyId(int hotKeyId)
        {
            int cellId = hotKeyId - WorkspaceConstants.HotKeyIdBase;
            return cellId >= 1 &&
                   cellId <= WorkspaceConstants.MaximumCellCount ? cellId : 0;
        }

        public static int HotKeyIdForCell(int cellId)
        {
            if (cellId < 1 || cellId > WorkspaceConstants.MaximumCellCount)
                throw new ArgumentOutOfRangeException(nameof(cellId));
            return WorkspaceConstants.HotKeyIdBase + cellId;
        }

        private static uint BuildModifiers(ShortcutBinding binding)
        {
            uint modifiers = 0;
            if (binding.Control) modifiers |= NativeMethods.MOD_CONTROL;
            if (binding.Shift) modifiers |= NativeMethods.MOD_SHIFT;
            if (binding.Alt) modifiers |= NativeMethods.MOD_ALT;
            return modifiers;
        }

        public void UnregisterAll()
        {
            foreach (int cellId in _active.Keys.ToArray())
                NativeMethods.UnregisterHotKey(_windowHandle, HotKeyIdForCell(cellId));
            _active.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
        }
    }
}
