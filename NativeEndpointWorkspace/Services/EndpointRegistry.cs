using System;
using System.Collections.Generic;
using System.Linq;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Services
{
    public class EndpointRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, NativeEndpoint> _byCell = new Dictionary<int, NativeEndpoint>();

        public NativeEndpoint GetByCell(int cellId)
        {
            lock (_gate)
            {
                NativeEndpoint endpoint;
                return _byCell.TryGetValue(cellId, out endpoint) ? endpoint : null;
            }
        }

        public NativeEndpoint GetByHandle(IntPtr hwnd)
        {
            lock (_gate)
                return _byCell.Values.FirstOrDefault(x => x.Handle == hwnd);
        }

        public bool ContainsHandle(IntPtr hwnd)
        {
            return GetByHandle(hwnd) != null;
        }

        public NativeEndpoint Bind(int cellId, NativeEndpoint endpoint)
        {
            lock (_gate)
            {
                NativeEndpoint old;
                _byCell.TryGetValue(cellId, out old);
                _byCell[cellId] = endpoint;
                return old;
            }
        }

        public NativeEndpoint UnbindCell(int cellId)
        {
            lock (_gate)
            {
                NativeEndpoint old;
                if (!_byCell.TryGetValue(cellId, out old))
                    return null;
                _byCell.Remove(cellId);
                return old;
            }
        }

        public NativeEndpoint RemoveCellAndShiftDown(int removedCellId)
        {
            if (removedCellId < 1 || removedCellId > WorkspaceConstants.MaximumCellCount)
                throw new ArgumentOutOfRangeException(nameof(removedCellId));

            lock (_gate)
            {
                NativeEndpoint removed = null;
                _byCell.TryGetValue(removedCellId, out removed);
                _byCell.Remove(removedCellId);

                KeyValuePair<int, NativeEndpoint>[] shifted = _byCell
                    .Where(x => x.Key > removedCellId)
                    .OrderBy(x => x.Key)
                    .ToArray();

                foreach (KeyValuePair<int, NativeEndpoint> pair in shifted)
                    _byCell.Remove(pair.Key);

                foreach (KeyValuePair<int, NativeEndpoint> pair in shifted)
                {
                    int newCellId = pair.Key - 1;
                    pair.Value.ReassignCellId(newCellId);
                    _byCell[newCellId] = pair.Value;
                }

                return removed;
            }
        }

        public NativeEndpoint MarkDestroyed(IntPtr hwnd)
        {
            lock (_gate)
            {
                NativeEndpoint endpoint = _byCell.Values.FirstOrDefault(x => x.Handle == hwnd);
                if (endpoint != null)
                    endpoint.MarkDestroyObserved();
                return endpoint;
            }
        }

        public IEnumerable<NativeEndpoint> All()
        {
            lock (_gate)
                return _byCell.Values.ToArray();
        }

        public void Clear()
        {
            lock (_gate)
                _byCell.Clear();
        }
    }
}
