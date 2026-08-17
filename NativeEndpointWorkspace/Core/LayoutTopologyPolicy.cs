using System;

namespace NativeEndpointWorkspace.Core
{
    public static class LayoutTopologyPolicy
    {
        public static int[] GetRowCellCounts(int cellCount)
        {
            switch (cellCount)
            {
                case 4: return new[] { 2, 2 };
                case 5: return new[] { 3, 2 };
                case 6: return new[] { 3, 3 };
                case 7: return new[] { 4, 3 };
                case 8: return new[] { 4, 4 };
                case 9: return new[] { 3, 3, 3 };
                case 10: return new[] { 4, 3, 3 };
                case 11: return new[] { 4, 4, 3 };
                case 12: return new[] { 4, 4, 4 };
                default: throw new ArgumentOutOfRangeException(nameof(cellCount), "Supported Cell count is 4 through 12.");
            }
        }
    }
}
