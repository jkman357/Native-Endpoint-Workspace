using System;
using System.Collections.Generic;

namespace NativeEndpointWorkspace.Core
{
    [Serializable]
    public class AdaptiveRowLayoutState
    {
        public double HeightWeight { get; set; }
        public List<int> CellIds { get; set; }
        public List<double> ColumnWeights { get; set; }

        public AdaptiveRowLayoutState()
        {
            HeightWeight = 1.0;
            CellIds = new List<int>();
            ColumnWeights = new List<double>();
        }
    }
}
