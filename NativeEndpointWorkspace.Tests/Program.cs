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
            Run("Legacy layout version matching is exact", TestLayoutVersionBoundaries);
            Run("Invalid Cell topology fails fast", TestTopologyFailFast);
            Run("Destroyed handle tombstones the bound endpoint instance", TestDestroyTombstone);
            Run("Hotkey partial registration failure rolls back previous set", TestShortcutTransactionRollback);
            Run("Unsupported layout schema is rejected", TestUnsupportedSchemaRejected);

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
            AssertEqual(2, LayoutTopologyPolicy.GetRowCellCounts(4).Length);
            AssertEqual(3, LayoutTopologyPolicy.GetRowCellCounts(12).Length);
            AssertThrows<ArgumentOutOfRangeException>(() => LayoutTopologyPolicy.GetRowCellCounts(3));
            AssertThrows<ArgumentOutOfRangeException>(() => LayoutTopologyPolicy.GetRowCellCounts(13));
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
            finally
            {
                service.Dispose();
            }
        }

        private static void TestUnsupportedSchemaRejected()
        {
            var state = new WorkspaceState
            {
                Version = "0.0.1rc12",
                LayoutSchemaVersion = 99,
                CellCount = 4
            };
            var shortcuts = new List<ShortcutBinding>
            {
                Binding(1, true, true, false, 0x70),
                Binding(2, true, true, false, 0x71),
                Binding(3, true, true, false, 0x72),
                Binding(4, true, true, false, 0x73)
            };
            AssertThrows<InvalidDataException>(() => WorkspaceStateValidator.Validate(state, 4, shortcuts));
        }

        private static ShortcutBinding Binding(int cell, bool ctrl, bool shift, bool alt, int key)
        {
            return new ShortcutBinding { CellId = cell, Control = ctrl, Shift = shift, Alt = alt, KeyCode = key };
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name + " :: " + ex.Message);
            }
        }

        private static void AssertTrue(bool value)
        {
            if (!value) throw new Exception("Expected true.");
        }

        private static void AssertFalse(bool value)
        {
            if (value) throw new Exception("Expected false.");
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Expected " + expected + ", actual " + actual + ".");
        }

        private static void AssertThrows<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new Exception("Expected exception " + typeof(T).Name + ".");
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
