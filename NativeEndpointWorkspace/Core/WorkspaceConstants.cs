using System;

namespace NativeEndpointWorkspace.Core
{
    internal static class WorkspaceConstants
    {
        public const string Version = "0.0.2rc02";
        public const int LayoutSchemaVersion = 1;

        public const int MinimumCellCount = 1;
        public const int MaximumCellCount = 8;
        public const int DefaultCellCount = 8;

        public const int HotKeyIdBase = 5000;
        public const int FunctionKeyFirstVirtualKey = 0x70;
        public const int FunctionKeyLastVirtualKey = 0x77;
        public const int FunctionKeyDisplayOffset = 0x6F;
        public const int FunctionKeyCount = 8;

        public const double SplitterSize = 6.0;
        public const double DefaultCellMinimumWidth = 155.0;
        public const double DefaultRowMinimumHeight = 115.0;
        public const double SizeVerificationPositionTolerance = 20.0;
        public const double SizeVerificationGrowthTolerance = 8.0;
        public const double MaximumLayoutWeight = 1000000.0;

        public const int WindowRectangleTolerance = 3;
        public const int WindowTextCapacity = 512;
        public const int WindowClassCapacity = 256;
        public const int MaximumZOrderEnumerationWindows = 4096;

        public const int MaximumCorrectionsPerBurst = 4;
        public static readonly TimeSpan CorrectionBurstWindow = TimeSpan.FromMilliseconds(1500);
        public static readonly TimeSpan CorrectionBackoffDuration = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan LocationCorrectionSuppression = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan EndpointHealthInterval = TimeSpan.FromMilliseconds(1250);
        public static readonly TimeSpan InteractiveLayoutCommitInterval = TimeSpan.FromMilliseconds(45);
        public static readonly TimeSpan EndpointSizeVerificationDelay = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan IdentifyOverlayDuration = TimeSpan.FromMilliseconds(1400);

        public const long RuntimeLogMaxBytes = 5L * 1024L * 1024L;
        public const int RuntimeLogRetentionFiles = 5;
        public const int SlowLayoutCommitWarningMilliseconds = 50;
    }
}
