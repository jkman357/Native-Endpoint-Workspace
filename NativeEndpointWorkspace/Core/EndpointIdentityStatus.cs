namespace NativeEndpointWorkspace.Core
{
    public enum EndpointIdentityStatus
    {
        Current,
        WindowMissing,
        ProcessOrThreadChanged,
        ProcessStartTimeChanged,
        ProcessStartTimeUnavailable,
        WindowClassChanged
    }
}
