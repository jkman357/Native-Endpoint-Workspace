namespace NativeEndpointWorkspace.Core
{
    public enum GeometrySyncResult
    {
        Applied,
        AlreadyCorrect,
        SkippedMinimized,
        StaleEndpoint,
        HungEndpoint,
        Failed
    }
}
