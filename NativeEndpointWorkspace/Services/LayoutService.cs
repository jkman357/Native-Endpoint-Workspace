using System.IO;
using System.Xml.Serialization;
using NativeEndpointWorkspace.Core;

namespace NativeEndpointWorkspace.Services
{
    public class LayoutService
    {
        public void Save(string path, WorkspaceState state)
        {
            var serializer = new XmlSerializer(typeof(WorkspaceState));
            using (var stream = File.Create(path))
                serializer.Serialize(stream, state);
        }

        public WorkspaceState Load(string path)
        {
            var serializer = new XmlSerializer(typeof(WorkspaceState));
            using (var stream = File.OpenRead(path))
                return (WorkspaceState)serializer.Deserialize(stream);
        }
    }
}
