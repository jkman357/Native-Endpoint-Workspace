using System;

namespace NativeEndpointWorkspace.Core
{
    internal static class WorkspaceConstants
    {
        public const string Version = "0.0.1rc09";
        public const string LayoutVersionCompatibilityPrefix = "0.0.1";

        public const int MinimumCellCount = 4;
        public const int MaximumCellCount = 12;
        public const int DefaultCellCount = 8;

        public const int HotKeyIdBase = 5000;
        public const int FunctionKeyFirstVirtualKey = 0x70;
        public const int FunctionKeyLastVirtualKey = 0x7B;
        public const int FunctionKeyDisplayOffset = 0x6F;
        public const int FunctionKeyCount = 12;

        public const double SplitterSize = 6.0;
        public const double DefaultCellMinimumWidth = 155.0;
        public const double DefaultRowMinimumHeight = 115.0;
        public const double SizeVerificationPositionTolerance = 20.0;
        public const double SizeVerificationGrowthTolerance = 8.0;
        public const double MaximumLayoutWeight = 1000000.0;

        public const int WindowRectangleTolerance = 3;
        public const int WindowTextCapacity = 512;
        public const int WindowClassCapacity = 256;

        public const int MaximumCorrectionsPerBurst = 4;
        public static readonly TimeSpan CorrectionBurstWindow = TimeSpan.FromMilliseconds(1500);
        public static readonly TimeSpan CorrectionBackoffDuration = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan LocationCorrectionSuppression = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan EndpointHealthInterval = TimeSpan.FromMilliseconds(1250);
        public static readonly TimeSpan EndpointSizeVerificationDelay = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan IdentifyOverlayDuration = TimeSpan.FromMilliseconds(1400);
    }
}
