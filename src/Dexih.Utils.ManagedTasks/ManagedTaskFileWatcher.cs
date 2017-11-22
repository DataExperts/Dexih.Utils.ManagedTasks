using System.IO;

namespace dexih.utils.ManagedTasks
{
    public class ManagedTaskFileWatcher
    {
        public string Path { get; set; }
        public string Filter { get; set; }

        public ManagedTaskFileWatcher(string path, string filter)
        {
            Path = path;
            Filter = filter;
        }
    }
}