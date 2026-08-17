using System;

namespace NativeEndpointWorkspace.Core
{
    [Serializable]
    public class CellLayoutState
    {
        public int CellId { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
