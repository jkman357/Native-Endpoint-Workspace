using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NativeEndpointWorkspace.Core;
using NativeEndpointWorkspace.Services;

namespace NativeEndpointWorkspace.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("Strong identity fails closed when bind-time start time is missing", TestStrongIdentityFailsClosed);
            Run("Runtime identity policy requires strong health and mutation checks", TestStrongRuntimeIdentityPolicy);
            Run("Toolbar restore tracks only endpoints minimized by toolbar", TestToolbarRestoreTrackingPolicy);
            Run("Raw layout validation rejects malformed shortcuts before merge", TestRawLayoutValidationOrdering);
            Run("Workspace bounds clamp converges inside monitor work area", TestWorkspaceBoundsClamp);
            Run("Layout save replaces only after durable temp serialization", TestFailureSafeLayoutSave);
            Run("Maintenance regression policies remain consolidated", TestMaintenanceRegressionConsolidation);
            Run("Legacy layout version matching is exact", TestLayoutVersionBoundaries);
            Run("Invalid Cell topology fails fast", TestTopologyFailFast);
            Run("Cell and shortcut surface is bounded to 1-8", TestCellAndShortcutBounds);
            Run("Destroyed handle tombstones the bound endpoint instance", TestDestroyTombstone);
            Run("Hotkey partial registration failure rolls back previous set", TestShortcutTransactionRollback);
            Run("Unsupported layout schema is rejected", TestUnsupportedSchemaRejected);
            Run("Removing an arbitrary Cell shifts later endpoint bindings down", TestCellRemovalShift);

            Console.WriteLine();
            Console.WriteLine("Tests: " + _passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void TestStrongIdentityFailsClosed()
        {
            AssertEqual(EndpointIdentityStatus.ProcessStartTimeUnavailable,
                EndpointIdentityPolicy.EvaluateStrongProcessStart(0, true, 1234));
            AssertEqual(EndpointIdentityStatus.ProcessStartTimeUnavailable,
                EndpointIdentityPolicy.EvaluateStrongProcessStart(1234, false, 0));
            AssertEqual(EndpointIdentityStatus.Current,
                EndpointIdentityPolicy.EvaluateStrongProcessStart(1234, true, 1234));
            AssertEqual(EndpointIdentityStatus.ProcessStartTimeChanged,
                EndpointIdentityPolicy.EvaluateStrongProcessStart(1234, true, 5678));
            AssertFalse(EndpointIdentityPolicy.CanEstablishStrongIdentity(0));
            AssertTrue(EndpointIdentityPolicy.CanEstablishStrongIdentity(1234));
        }

        private static void TestStrongRuntimeIdentityPolicy()
        {
            AssertTrue(EndpointIdentityPolicy.RequireStrongCheckForHealthRevalidation);
            AssertTrue(EndpointIdentityPolicy.RequireStrongCheckForNativeMutation);
        }

        private static void TestToolbarRestoreTrackingPolicy()
        {
            AssertFalse(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(true, true));
            AssertFalse(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(true, false));
            AssertFalse(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(false, false));
            AssertTrue(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(false, true));
        }

        private static void TestRawLayoutValidationOrdering()
        {
            var invalidCount = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 99 };
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.ValidateRawState(invalidCount));

            var invalidCell = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 4 };
            invalidCell.Shortcuts.Add(Binding(1, true, true, false, 0x70));
            invalidCell.Shortcuts.Add(Binding(9, true, true, false, 0x71));
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.ValidateRawState(invalidCell));

            var nullEntry = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 4 };
            nullEntry.Shortcuts.Add(null);
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.ValidateRawState(nullEntry));

            var duplicateCell = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 4 };
            duplicateCell.Shortcuts.Add(Binding(1, true, true, false, 0x70));
            duplicateCell.Shortcuts.Add(Binding(1, true, false, true, 0x71));
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.ValidateRawState(duplicateCell));

            var missingEntriesAreCompatible = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 4 };
            missingEntriesAreCompatible.Shortcuts.Add(Binding(1, true, true, false, 0x70));
            WorkspaceStateValidator.ValidateRawState(missingEntriesAreCompatible);
        }

        private static void TestWorkspaceBoundsClamp()
        {
            WorkspaceBounds bounded = WorkspaceBoundsPolicy.ClampToWorkArea(
                1500, 800, 900, 700, 400, 300, 0, 0, 1920, 1080);
            AssertEqual(1020.0, bounded.Left);
            AssertEqual(380.0, bounded.Top);
            AssertEqual(900.0, bounded.Width);
            AssertEqual(700.0, bounded.Height);

            WorkspaceBounds oversized = WorkspaceBoundsPolicy.ClampToWorkArea(
                -500, -300, 3000, 2000, 900, 600, -1920, 0, 0, 1080);
            AssertEqual(-1920.0, oversized.Left);
            AssertEqual(0.0, oversized.Top);
            AssertEqual(1920.0, oversized.Width);
            AssertEqual(1080.0, oversized.Height);

            WorkspaceBounds normalized = WorkspaceBoundsPolicy.ClampToWorkArea(
                double.NaN, double.PositiveInfinity, 800, 600, 400, 300, 100, 50, 1700, 950);
            AssertEqual(100.0, normalized.Left);
            AssertEqual(50.0, normalized.Top);
        }


        private static void TestFailureSafeLayoutSave()
        {
            string directory = Path.Combine(Path.GetTempPath(), "NativeEndpointWorkspace.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "workspace.newlayout.xml");
            try
            {
                File.WriteAllText(path, "known-good-layout");
                var service = new LayoutService();

                var replacement = new WorkspaceState
                {
                    Version = "0.0.2rc03",
                    LayoutSchemaVersion = 1,
                    CellCount = 4
                };
                service.Save(path, replacement);
                string saved = File.ReadAllText(path);
                AssertTrue(saved.IndexOf("0.0.2rc03", StringComparison.Ordinal) >= 0);
                AssertFalse(saved.IndexOf("known-good-layout", StringComparison.Ordinal) >= 0);
                AssertEqual(0, Directory.GetFiles(directory, ".workspace.newlayout.xml.tmp-*").Length);

                File.WriteAllText(path, "known-good-layout-2");
                AssertThrows<InvalidOperationException>(() => service.Save(path, new UnsupportedWorkspaceState()));
                AssertEqual("known-good-layout-2", File.ReadAllText(path));
                AssertEqual(0, Directory.GetFiles(directory, ".workspace.newlayout.xml.tmp-*").Length);
            }
            finally
            {
                try { Directory.Delete(directory, true); }
                catch { }
            }
        }

        private static void TestMaintenanceRegressionConsolidation()
        {
            AssertTrue(EndpointIdentityPolicy.RequireStrongCheckForHealthRevalidation);
            AssertTrue(EndpointIdentityPolicy.RequireStrongCheckForNativeMutation);
            AssertTrue(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(false, true));
            AssertFalse(GroupVisibilityPolicy.ShouldTrackForToolbarRestore(true, true));

            var raw = new WorkspaceState { Version = "0.0.2rc03", LayoutSchemaVersion = 1, CellCount = 4 };
            raw.Shortcuts.Add(Binding(1, true, true, false, 0x70));
            WorkspaceStateValidator.ValidateRawState(raw);

            WorkspaceBounds bounded = WorkspaceBoundsPolicy.ClampToWorkArea(
                1800, 900, 700, 500, 400, 300, 0, 0, 1920, 1080);
            AssertEqual(1220.0, bounded.Left);
            AssertEqual(580.0, bounded.Top);
            AssertEqual(700.0, bounded.Width);
            AssertEqual(500.0, bounded.Height);
        }

        private static void TestLayoutVersionBoundaries()
        {
            AssertTrue(LayoutVersionPolicy.IsSupported(0, "0.0.1rc11"));
            AssertTrue(LayoutVersionPolicy.IsSupported(0, "0.0.1"));
            AssertFalse(LayoutVersionPolicy.IsSupported(0, "0.0.10"));
            AssertFalse(LayoutVersionPolicy.IsSupported(0, "0.0.100"));
            AssertFalse(LayoutVersionPolicy.IsSupported(0, "0.0.1-not-supported"));
            AssertTrue(LayoutVersionPolicy.IsSupported(1, "future-app-version-does-not-drive-schema"));
        }

        private static void TestTopologyFailFast()
        {
            AssertEqual(1, LayoutTopologyPolicy.GetRowCellCounts(1).Length);
            AssertEqual(1, LayoutTopologyPolicy.GetRowCellCounts(3).Length);
            AssertEqual(2, LayoutTopologyPolicy.GetRowCellCounts(4).Length);
            AssertEqual(2, LayoutTopologyPolicy.GetRowCellCounts(8).Length);
            AssertThrows<ArgumentOutOfRangeException>(() => LayoutTopologyPolicy.GetRowCellCounts(0));
            AssertThrows<ArgumentOutOfRangeException>(() => LayoutTopologyPolicy.GetRowCellCounts(9));
        }

        private static void TestCellAndShortcutBounds()
        {
            var fake = new FakeHotKeyRegistrar();
            var service = new ShortcutService(new IntPtr(1), fake);
            try
            {
                IList<ShortcutBinding> defaults = service.CreateDefaultBindings();
                AssertEqual(8, defaults.Count);
                AssertEqual(1, defaults.First().CellId);
                AssertEqual(8, defaults.Last().CellId);
            }
            finally { service.Dispose(); }
        }

        private static void TestDestroyTombstone()
        {
            var registry = new EndpointRegistry();
            var endpoint = new NativeEndpoint(1, new IntPtr(0x1234), "ignored", "test", 10, 20, 30, "TestWindow");
            registry.Bind(1, endpoint);
            NativeEndpoint marked = registry.MarkDestroyed(new IntPtr(0x1234));
            AssertTrue(object.ReferenceEquals(endpoint, marked));
            AssertTrue(endpoint.DestroyObserved);
        }

        private static void TestShortcutTransactionRollback()
        {
            var fake = new FakeHotKeyRegistrar();
            var service = new ShortcutService(new IntPtr(1), fake);
            try
            {
                var oldSet = new List<ShortcutBinding>
                {
                    Binding(1, true, true, false, 0x70),
                    Binding(2, true, true, false, 0x71)
                };
                string summary;
                AssertTrue(service.ApplyBindings(oldSet, out summary));

                var requested = new List<ShortcutBinding>
                {
                    Binding(1, true, false, true, 0x70),
                    Binding(2, true, false, true, 0x71)
                };
                fake.FailNextRegistrationId = ShortcutService.HotKeyIdForCell(2);
                AssertFalse(service.ApplyBindings(requested, out summary));

                IList<ShortcutBinding> active = service.ActiveBindings;
                AssertEqual(2, active.Count);
                AssertTrue(active.All(x => x.Control && x.Shift && !x.Alt));
                AssertTrue(summary.IndexOf("rolled back", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally { service.Dispose(); }
        }

        private static void TestCellRemovalShift()
        {
            var registry = new EndpointRegistry();
            var endpoint5 = new NativeEndpoint(5, new IntPtr(0x5000), "ignored", "five", 10, 20, 30, "TestWindow");
            var endpoint6 = new NativeEndpoint(6, new IntPtr(0x6000), "ignored", "six", 11, 21, 31, "TestWindow");
            var endpoint8 = new NativeEndpoint(8, new IntPtr(0x8000), "ignored", "eight", 12, 22, 32, "TestWindow");
            registry.Bind(5, endpoint5);
            registry.Bind(6, endpoint6);
            registry.Bind(8, endpoint8);

            NativeEndpoint removed = registry.RemoveCellAndShiftDown(5);
            AssertTrue(object.ReferenceEquals(endpoint5, removed));
            AssertTrue(object.ReferenceEquals(endpoint6, registry.GetByCell(5)));
            AssertTrue(object.ReferenceEquals(endpoint8, registry.GetByCell(7)));
            AssertEqual(5, endpoint6.CellId);
            AssertEqual(7, endpoint8.CellId);
            AssertTrue(registry.GetByCell(8) == null);
        }

        private static void TestUnsupportedSchemaRejected()
        {
            var state = new WorkspaceState { Version = "0.0.1", LayoutSchemaVersion = 99, CellCount = 4 };
            var shortcuts = new List<ShortcutBinding>
            {
                Binding(1, true, true, false, 0x70), Binding(2, true, true, false, 0x71),
                Binding(3, true, true, false, 0x72), Binding(4, true, true, false, 0x73)
            };
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.Validate(state, 4, shortcuts));
        }

        private static ShortcutBinding Binding(int cell, bool ctrl, bool shift, bool alt, int key)
        {
            return new ShortcutBinding { CellId = cell, Control = ctrl, Shift = shift, Alt = alt, KeyCode = key };
        }

        private static void Run(string name, Action test)
        {
            try { test(); _passed++; Console.WriteLine("PASS  " + name); }
            catch (Exception ex) { _failed++; Console.WriteLine("FAIL  " + name + " :: " + ex.Message); }
        }

        private static void AssertTrue(bool value) { if (!value) throw new Exception("Expected true."); }
        private static void AssertFalse(bool value) { if (value) throw new Exception("Expected false."); }
        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Expected " + expected + ", actual " + actual + ".");
        }
        private static void AssertThrows<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected exception " + typeof(T).Name + ".");
        }

        private sealed class UnsupportedWorkspaceState : WorkspaceState
        {
            public string UnsupportedPayload { get; set; }
        }

        private sealed class FakeHotKeyRegistrar : IHotKeyRegistrar
        {
            private readonly HashSet<int> _registered = new HashSet<int>();
            public int FailNextRegistrationId { get; set; }

            public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int nativeErrorCode)
            {
                if (id == FailNextRegistrationId)
                {
                    FailNextRegistrationId = 0;
                    nativeErrorCode = 1409;
                    return false;
                }
                _registered.Add(id);
                nativeErrorCode = 0;
                return true;
            }

            public bool Unregister(IntPtr windowHandle, int id)
            {
                _registered.Remove(id);
                return true;
            }
        }
    }
}
