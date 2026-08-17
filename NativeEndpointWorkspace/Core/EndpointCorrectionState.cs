using System;

namespace NativeEndpointWorkspace.Core
{
    public sealed class EndpointCorrectionState
    {

        public int ConsecutiveCorrections { get; private set; }
        public DateTime BurstStartedUtc { get; private set; }
        public DateTime BackoffUntilUtc { get; private set; }

        public bool IsBackedOff(DateTime nowUtc)
        {
            return BackoffUntilUtc > nowUtc;
        }

        public bool RecordCorrectionAttempt(DateTime nowUtc)
        {
            if (BurstStartedUtc == default(DateTime) || nowUtc - BurstStartedUtc > WorkspaceConstants.CorrectionBurstWindow)
            {
                BurstStartedUtc = nowUtc;
                ConsecutiveCorrections = 0;
            }

            ConsecutiveCorrections++;
            if (ConsecutiveCorrections < WorkspaceConstants.MaximumCorrectionsPerBurst)
                return false;

            BackoffUntilUtc = nowUtc.Add(WorkspaceConstants.CorrectionBackoffDuration);
            ConsecutiveCorrections = 0;
            BurstStartedUtc = nowUtc;
            return true;
        }

        public void Reset()
        {
            ConsecutiveCorrections = 0;
            BurstStartedUtc = default(DateTime);
            BackoffUntilUtc = default(DateTime);
        }
    }
}
