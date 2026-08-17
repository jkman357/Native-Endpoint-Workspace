using System;
using System.Collections.Generic;

namespace NativeEndpointWorkspace.Core
{
    [Serializable]
    public class GridLayoutState
    {
        // rc06 adaptive layout: each row owns its own independent column proportions.
        public List<AdaptiveRowLayoutState> RowLayouts { get; set; }

        // rc03 compatibility fields. rc06 reads these when RowLayouts is absent.
        public int Rows { get; set; }
        public int Columns { get; set; }
        public List<double> RowWeights { get; set; }
        public List<double> ColumnWeights { get; set; }

        public GridLayoutState()
        {
            RowLayouts = new List<AdaptiveRowLayoutState>();
            RowWeights = new List<double>();
            ColumnWeights = new List<double>();
        }
    }
}
