using System;
using System.Collections.Generic;

namespace NativeEndpointWorkspace.Core
{
    public sealed class NativeLayoutCommitResult
    {
        public int AppliedGeometryCount { get; set; }
        public int AlreadyCorrectCount { get; set; }
        public int SkippedMinimizedCount { get; set; }
        public int HungEndpointCount { get; set; }
        public int StaleEndpointCount { get; set; }
        public int GeometryFailureCount { get; set; }
        public int ZOrderFailureCount { get; set; }
        public int SizeAccommodationCount { get; set; }
        public List<string> Failures { get; private set; }

        public NativeLayoutCommitResult()
        {
            Failures = new List<string>();
        }

        public bool HasFailures
        {
            get { return GeometryFailureCount > 0 || ZOrderFailureCount > 0 || StaleEndpointCount > 0; }
        }

        public string ToStatusText()
        {
            string text = "Native layout committed: " +
                          (AppliedGeometryCount + AlreadyCorrectCount) + " geometry OK, " +
                          GeometryFailureCount + " geometry failure(s), " +
                          ZOrderFailureCount + " Z-order failure(s), " +
                          StaleEndpointCount + " stale endpoint(s)";
            if (HungEndpointCount > 0)
                text += ", " + HungEndpointCount + " hung endpoint(s) skipped";
            if (SkippedMinimizedCount > 0)
                text += ", " + SkippedMinimizedCount + " minimized endpoint(s) skipped";
            if (SizeAccommodationCount > 0)
                text += ", " + SizeAccommodationCount + " size accommodation(s) applied";
            return text + ".";
        }
    }
}
