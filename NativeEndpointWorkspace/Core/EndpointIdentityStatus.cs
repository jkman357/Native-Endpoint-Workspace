namespace NativeEndpointWorkspace.Core
{
    public enum EndpointIdentityStatus
    {
        Current,
        DestroyObserved,
        WindowMissing,
        ProcessOrThreadChanged,
        ProcessStartTimeChanged,
        ProcessStartTimeUnavailable,
        WindowClassChanged
    }
}
