using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NativeEndpointWorkspace.Core
{
    public static class WorkspaceStateValidator
    {
        public static void Validate(WorkspaceState state, int proposedCellCount, IList<ShortcutBinding> proposedShortcuts)
        {
            if (state == null)
                throw new InvalidDataException("Layout file did not contain a WorkspaceState.");
            if (!LayoutVersionPolicy.IsSupported(state.LayoutSchemaVersion, state.Version))
                throw new InvalidDataException("Layout schema/version is not supported by this application.");
            if (state.CellCount != 0 && (state.CellCount < WorkspaceConstants.MinimumCellCount || state.CellCount > WorkspaceConstants.MaximumCellCount))
                throw new InvalidDataException("Layout CellCount is outside the supported 1-8 range.");
            if (state.CellCount == 0 && (state.Cells == null || state.Cells.Count < WorkspaceConstants.MinimumCellCount || state.Cells.Count > WorkspaceConstants.MaximumCellCount))
                throw new InvalidDataException("Legacy layout does not provide a valid 1-8 Cell count.");
            if (proposedCellCount < WorkspaceConstants.MinimumCellCount || proposedCellCount > WorkspaceConstants.MaximumCellCount)
                throw new InvalidDataException("Resolved layout CellCount is outside the supported 1-8 range.");

            ValidateGridLayoutState(state.Grid, proposedCellCount);

            if (proposedShortcuts == null || proposedShortcuts.Count < proposedCellCount)
                throw new InvalidDataException("Layout shortcut configuration is incomplete.");

            foreach (ShortcutBinding binding in proposedShortcuts)
            {
                if (binding == null)
                    throw new InvalidDataException("Layout contains a null shortcut binding.");
                if (binding.CellId < 1 || binding.CellId > WorkspaceConstants.MaximumCellCount)
                    throw new InvalidDataException("Layout contains an invalid shortcut CellId.");
                if (binding.KeyCode < WorkspaceConstants.FunctionKeyFirstVirtualKey ||
                    binding.KeyCode > WorkspaceConstants.FunctionKeyLastVirtualKey)
                    throw new InvalidDataException("Layout contains a shortcut key outside the supported F1-F8 range.");
                if (binding.Win)
                    throw new InvalidDataException("Layout contains a Win-key global shortcut, which is not supported.");
                if (!binding.HasSupportedModifier)
                    throw new InvalidDataException("Layout contains a bare global function-key shortcut. Ctrl, Alt, or Shift is required.");
            }

            string duplicate = proposedShortcuts.Where(x => x.CellId <= proposedCellCount)
                .GroupBy(x => x.ConflictKey)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(duplicate))
                throw new InvalidDataException("Layout contains duplicate active shortcut gestures: " + duplicate + ".");
        }

        public static void ValidateGridLayoutState(GridLayoutState grid, int cellCount)
        {
            if (grid == null)
                return;

            int[] rowCounts = LayoutTopologyPolicy.GetRowCellCounts(cellCount);
            if (grid.RowLayouts != null && grid.RowLayouts.Count > 0)
            {
                if (grid.RowLayouts.Count != rowCounts.Length)
                    throw new InvalidDataException("Layout row count is incompatible with CellCount.");

                int expectedCellId = 1;
                for (int row = 0; row < rowCounts.Length; row++)
                {
                    AdaptiveRowLayoutState rowState = grid.RowLayouts[row];
                    if (rowState == null || rowState.CellIds == null || rowState.ColumnWeights == null ||
                        rowState.CellIds.Count != rowCounts[row] || rowState.ColumnWeights.Count != rowCounts[row])
                        throw new InvalidDataException("Layout row geometry is incomplete or incompatible.");
                    ValidateFinitePositiveWeight(rowState.HeightWeight, "row height");
                    for (int column = 0; column < rowCounts[row]; column++)
                    {
                        if (rowState.CellIds[column] != expectedCellId++)
                            throw new InvalidDataException("Layout Cell ordering is invalid.");
                        ValidateFinitePositiveWeight(rowState.ColumnWeights[column], "column width");
                    }
                }
            }

            if (grid.RowWeights != null)
                foreach (double weight in grid.RowWeights) ValidateFinitePositiveWeight(weight, "legacy row weight");
            if (grid.ColumnWeights != null)
                foreach (double weight in grid.ColumnWeights) ValidateFinitePositiveWeight(weight, "legacy column weight");
        }

        public static void ValidateFinitePositiveWeight(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > WorkspaceConstants.MaximumLayoutWeight)
                throw new InvalidDataException("Layout contains an invalid " + name + ".");
        }
    }
}
