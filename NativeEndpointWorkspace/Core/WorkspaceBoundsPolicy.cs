using System;

namespace NativeEndpointWorkspace.Core
{
    public struct WorkspaceBounds
    {
        public double Left { get; private set; }
        public double Top { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }

        public WorkspaceBounds(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    public static class WorkspaceBoundsPolicy
    {
        public static WorkspaceBounds ClampToWorkArea(
            double left, double top, double width, double height,
            double minimumWidth, double minimumHeight,
            double workLeft, double workTop, double workRight, double workBottom)
        {
            double workWidth = Math.Max(1.0, workRight - workLeft);
            double workHeight = Math.Max(1.0, workBottom - workTop);

            double effectiveMinimumWidth = Math.Min(Math.Max(1.0, minimumWidth), workWidth);
            double effectiveMinimumHeight = Math.Min(Math.Max(1.0, minimumHeight), workHeight);
            double boundedWidth = Math.Max(effectiveMinimumWidth, Math.Min(NormalizeSize(width, effectiveMinimumWidth), workWidth));
            double boundedHeight = Math.Max(effectiveMinimumHeight, Math.Min(NormalizeSize(height, effectiveMinimumHeight), workHeight));

            double normalizedLeft = NormalizePosition(left, workLeft);
            double normalizedTop = NormalizePosition(top, workTop);
            double maximumLeft = workRight - boundedWidth;
            double maximumTop = workBottom - boundedHeight;

            double boundedLeft = Math.Max(workLeft, Math.Min(normalizedLeft, maximumLeft));
            double boundedTop = Math.Max(workTop, Math.Min(normalizedTop, maximumTop));
            return new WorkspaceBounds(boundedLeft, boundedTop, boundedWidth, boundedHeight);
        }

        private static double NormalizeSize(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;
        }

        private static double NormalizePosition(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        }
    }
}
