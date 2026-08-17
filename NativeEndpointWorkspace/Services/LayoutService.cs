using System;
using System.IO;
using System.Xml.Serialization;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Services
{
    public class LayoutService
    {
        public void Save(string path, WorkspaceState state)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A layout path is required.", nameof(path));
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Could not resolve the layout directory.");

            string fileName = Path.GetFileName(fullPath);
            string tempPath = Path.Combine(
                directory,
                "." + fileName + ".tmp-" + Guid.NewGuid().ToString("N"));
            bool committed = false;

            try
            {
                var serializer = new XmlSerializer(typeof(WorkspaceState));
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    serializer.Serialize(stream, state);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null, true);
                else
                    File.Move(tempPath, fullPath);

                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                        // Preserve the original save exception. A best-effort temp cleanup
                        // failure must not hide the reason the layout commit did not complete.
                    }
                }
            }
        }

        public WorkspaceState Load(string path)
        {
            var serializer = new XmlSerializer(typeof(WorkspaceState));
            using (var stream = File.OpenRead(path))
                return (WorkspaceState)serializer.Deserialize(stream);
        }
    }
}
