namespace NativeEndpointWorkspace.Core
{
    public static class GroupVisibilityPolicy
    {
        public static bool ShouldTrackForToolbarRestore(bool wasAlreadyMinimized, bool minimizeRequestAccepted)
        {
            return !wasAlreadyMinimized && minimizeRequestAccepted;
        }
    }
}
