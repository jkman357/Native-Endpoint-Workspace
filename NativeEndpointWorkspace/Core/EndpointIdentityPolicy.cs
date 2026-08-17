namespace NativeEndpointWorkspace.Core
{
    public static class EndpointIdentityPolicy
    {
        // v0.0.2rc01 closes the gap between the strong identity policy and runtime behavior.
        // Read-only probes may use the cheaper HWND/PID/TID/class check, but health
        // revalidation and every external native mutation must verify process start time.
        public const bool RequireStrongCheckForHealthRevalidation = true;
        public const bool RequireStrongCheckForNativeMutation = true;

        public static bool CanEstablishStrongIdentity(long processStartTimeUtcTicks)
        {
            return processStartTimeUtcTicks > 0;
        }

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
