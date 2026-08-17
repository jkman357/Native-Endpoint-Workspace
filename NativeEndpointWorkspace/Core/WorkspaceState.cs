using System;
using System.Collections.Generic;

namespace NativeEndpointWorkspace.Core
{
    [Serializable]
    public class WorkspaceState
    {
        public string Version { get; set; }
        public int CellCount { get; set; }
        public GridLayoutState Grid { get; set; }

        // Kept for rc01/rc02 layout-file compatibility. rc08 continues to avoid free-form
        // Cell geometry because Cells are constrained to the tiled Workspace grid.
        public List<CellLayoutState> Cells { get; set; }
        public List<ShortcutBinding> Shortcuts { get; set; }

        public WorkspaceState()
        {
            Version = "0.0.1rc08";
            CellCount = 8;
            Grid = new GridLayoutState();
            Cells = new List<CellLayoutState>();
            Shortcuts = new List<ShortcutBinding>();
        }
    }
}
