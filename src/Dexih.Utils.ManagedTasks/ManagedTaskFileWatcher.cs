using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Dexih.Utils.ManagedTasks
{
    /// <summary>
    /// This the base class for watching files.  
    /// This can be overridden, for other file systems.
    /// </summary>
    public class ManagedTaskFileWatcher : IDisposable
    {
        public event EventHandler OnFileWatch;
        
        public string Path { get; set; }
        public string Filter { get; set; }
        
        public bool IsStarted { get; set; } = false;

        private FileSystemWatcher _fileSystemWatcher;
        private readonly HashSet<string> _filesProcessed;
        
        public ManagedTaskFileWatcher(string path, string filter)
        {
            Path = path;
            Filter = filter;
            _filesProcessed = new HashSet<string>();
        }

        /// <summary>
        /// Start the filewatcher
        /// </summary>
        public virtual void Start()
        {
            var filter = string.IsNullOrEmpty(Filter) ? "*" : Filter;

            var existingFiles = Directory.GetFiles(Path, filter);
            if (existingFiles.Any())
            {
                foreach (var file in existingFiles)
                {
                    FileReady(this, new FileSystemEventArgs(WatcherChangeTypes.Created, "", file));
                }
            }
            
            _fileSystemWatcher = new FileSystemWatcher(Path, filter)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            };
            _fileSystemWatcher.Created += FileReady;
            IsStarted = true;
        }

        /// <summary>
        /// Stop the filewatcher.
        /// </summary>
        public virtual void Stop()
        {
            _fileSystemWatcher?.Dispose();
            lock (_filesProcessed)
            {
                _filesProcessed.Clear();
            }
            IsStarted = false;
        }

        public virtual void Dispose()
        {
            _fileSystemWatcher?.Dispose();
            OnFileWatch = null;
            IsStarted = false;
        }

        private void FileReady(object sender, FileSystemEventArgs e)
        {
            // filesystemwatcher triggers multiple times in some scenarios.  So use a dictionary to make sure same file isn't triggered twice.
            lock (_filesProcessed)
            {
                if (_filesProcessed.Contains(e.FullPath))
                {
                    return;
                }
                _filesProcessed.Add(e.FullPath);
                
                // Wait if file is still open
                // ensures files which are copying do not process until complete
                FileInfo fileInfo = new FileInfo(e.FullPath);
                while (IsFileLocked(fileInfo))
                {
                    Thread.Sleep(100);
                }

                OnFileWatch?.Invoke(this, EventArgs.Empty);
            }

            //wait a second, and clean the file from the dictionary.
            var timer = new System.Timers.Timer(1000d) {AutoReset = false};
            timer.Elapsed += (timerElapsedSender, timerElapsedArgs) =>
            {
                lock (_filesProcessed)
                {
                    _filesProcessed.Remove(e.FullPath);
                }
            };
            timer.Start();

        }
        
        private bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }
            finally
            {
                stream?.Close();
            }

            //file is not locked
            return false;
        }
    }
}