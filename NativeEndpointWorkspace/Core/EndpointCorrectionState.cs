using System;

namespace NativeEndpointWorkspace.Core
{
    public sealed class EndpointCorrectionState
    {
        private static readonly TimeSpan BurstWindow = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan BackoffDuration = TimeSpan.FromSeconds(3);
        private const int MaximumCorrectionsPerBurst = 4;

        public int ConsecutiveCorrections { get; private set; }
        public DateTime BurstStartedUtc { get; private set; }
        public DateTime BackoffUntilUtc { get; private set; }

        public bool IsBackedOff(DateTime nowUtc)
        {
            return BackoffUntilUtc > nowUtc;
        }

        public bool RecordCorrectionAttempt(DateTime nowUtc)
        {
            if (BurstStartedUtc == default(DateTime) || nowUtc - BurstStartedUtc > BurstWindow)
            {
                BurstStartedUtc = nowUtc;
                ConsecutiveCorrections = 0;
            }

            ConsecutiveCorrections++;
            if (ConsecutiveCorrections < MaximumCorrectionsPerBurst)
                return false;

            BackoffUntilUtc = nowUtc.Add(BackoffDuration);
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
