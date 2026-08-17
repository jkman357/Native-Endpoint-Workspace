namespace NativeEndpointWorkspace.Core
{
    public static class EndpointIdentityPolicy
    {
        public static EndpointIdentityStatus EvaluateStrongProcessStart(
            long storedProcessStartTimeUtcTicks,
            bool currentStartTimeAvailable,
            long currentProcessStartTimeUtcTicks)
        {
            if (storedProcessStartTimeUtcTicks <= 0 || !currentStartTimeAvailable || currentProcessStartTimeUtcTicks <= 0)
                return EndpointIdentityStatus.ProcessStartTimeUnavailable;

            return storedProcessStartTimeUtcTicks == currentProcessStartTimeUtcTicks
                ? EndpointIdentityStatus.Current
                : EndpointIdentityStatus.ProcessStartTimeChanged;
        }
    }
}
