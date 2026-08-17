using System;
using System.Collections.Generic;
using System.Linq;
using NativeEndpointWorkspace.Core;
using NativeEndpointWorkspace.Native;

namespace NativeEndpointWorkspace.Services
{
    public class ShortcutService : IDisposable
    {
        private readonly IntPtr _windowHandle;
        private readonly IHotKeyRegistrar _registrar;
        private readonly Dictionary<int, ShortcutBinding> _active = new Dictionary<int, ShortcutBinding>();

        public ShortcutService(IntPtr windowHandle) : this(windowHandle, new NativeHotKeyRegistrar()) { }

        public ShortcutService(IntPtr windowHandle, IHotKeyRegistrar registrar)
        {
            if (registrar == null) throw new ArgumentNullException(nameof(registrar));
            _windowHandle = windowHandle;
            _registrar = registrar;
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

            string validationSummary;
            if (!ValidateRequested(requested, out validationSummary))
            {
                summary = validationSummary + " Existing global shortcuts were left unchanged.";
                return false;
            }

            IList<ShortcutBinding> previous = ActiveBindings;
            UnregisterAll();

            string applySummary;
            if (TryRegisterSet(requested, out applySummary))
            {
                summary = applySummary;
                return true;
            }

            // All-or-nothing transaction: remove any partially registered requested set and
            // restore the last known working registration set.
            UnregisterAll();
            string rollbackSummary = "No previous shortcut registrations required restoration.";
            bool rollbackOk = previous.Count == 0 || TryRegisterSet(previous, out rollbackSummary);
            summary = applySummary + (rollbackOk
                ? " Registration transaction rolled back to the previous working shortcut set."
                : " WARNING: rollback of the previous shortcut set was not fully successful: " + rollbackSummary);
            return false;
        }

        private bool ValidateRequested(IList<ShortcutBinding> requested, out string summary)
        {
            var duplicateKeys = new HashSet<string>(
                requested.Where(x => x != null)
                         .GroupBy(x => x.ConflictKey)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key));
            int failures = 0;
            foreach (ShortcutBinding binding in requested)
            {
                if (binding == null)
                {
                    failures++;
                    continue;
                }
                if (binding.Win)
                {
                    binding.Status = "Rejected: Win modifier is not supported";
                    failures++;
                    continue;
                }
                if (!binding.HasSupportedModifier)
                {
                    binding.Status = "Rejected: Ctrl, Alt, or Shift is required";
                    failures++;
                    continue;
                }
                if (binding.KeyCode < WorkspaceConstants.FunctionKeyFirstVirtualKey ||
                    binding.KeyCode > WorkspaceConstants.FunctionKeyLastVirtualKey)
                {
                    binding.Status = "Rejected: only F1-F12 are supported";
                    failures++;
                    continue;
                }
                if (binding.CellId < 1 || binding.CellId > WorkspaceConstants.MaximumCellCount)
                {
                    binding.Status = "Rejected: invalid Cell ID";
                    failures++;
                    continue;
                }
                if (duplicateKeys.Contains(binding.ConflictKey))
                {
                    binding.Status = "Conflict: duplicate inside workspace";
                    failures++;
                    continue;
                }
                binding.Status = "Validated";
            }

            summary = failures == 0
                ? "Shortcut request validated."
                : failures + " shortcut validation issue(s) detected.";
            return failures == 0;
        }

        private bool TryRegisterSet(IList<ShortcutBinding> bindings, out string summary)
        {
            int success = 0;
            foreach (ShortcutBinding binding in bindings.OrderBy(x => x.CellId))
            {
                int id = HotKeyIdForCell(binding.CellId);
                uint modifiers = BuildModifiers(binding) | NativeMethods.MOD_NOREPEAT;
                int nativeError;
                if (!_registrar.Register(_windowHandle, id, modifiers, (uint)binding.KeyCode, out nativeError))
                {
                    binding.Status = "Conflict/unavailable (Win32 " + nativeError + ")";
                    summary = success + " shortcut(s) registered before Cell " + binding.CellId + " failed; requested set not committed.";
                    return false;
                }

                binding.Status = "Registered";
                _active[binding.CellId] = binding.Clone();
                success++;
            }

            summary = "All " + bindings.Count + " shortcuts registered transactionally.";
            return true;
        }

        public int CellIdFromHotKeyId(int hotKeyId)
        {
            int cellId = hotKeyId - WorkspaceConstants.HotKeyIdBase;
            return cellId >= 1 && cellId <= WorkspaceConstants.MaximumCellCount ? cellId : 0;
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
                _registrar.Unregister(_windowHandle, HotKeyIdForCell(cellId));
            _active.Clear();
        }

        public void Dispose()
        {
            UnregisterAll();
        }
    }
}
