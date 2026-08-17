using System;
using System.Collections.Generic;
using System.Linq;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Services
{
    public class EndpointRegistry
    {
        private readonly Dictionary<int, NativeEndpoint> _byCell = new Dictionary<int, NativeEndpoint>();

        public NativeEndpoint GetByCell(int cellId)
        {
            NativeEndpoint endpoint;
            return _byCell.TryGetValue(cellId, out endpoint) ? endpoint : null;
        }

        public NativeEndpoint GetByHandle(IntPtr hwnd)
        {
            return _byCell.Values.FirstOrDefault(x => x.Handle == hwnd);
        }

        public bool ContainsHandle(IntPtr hwnd)
        {
            return GetByHandle(hwnd) != null;
        }

        public NativeEndpoint Bind(int cellId, NativeEndpoint endpoint)
        {
            NativeEndpoint old = GetByCell(cellId);
            _byCell[cellId] = endpoint;
            return old;
        }

        public NativeEndpoint UnbindCell(int cellId)
        {
            NativeEndpoint old = GetByCell(cellId);
            if (old != null)
                _byCell.Remove(cellId);
            return old;
        }

        public IEnumerable<NativeEndpoint> All()
        {
            return _byCell.Values.ToArray();
        }

        public void Clear()
        {
            _byCell.Clear();
        }
    }
}
