using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dexih.utils.ManagedTasks
{
    public class ManagedTaskHandler : IDisposable
    {
        private readonly int _maxConcurrent;

        public event EventHandler<EManagedTaskStatus> OnStatus;
        public event EventHandler<ManagedTaskProgressItem> OnProgress;
        public event EventHandler OnTasksCompleted;

        public long CreatedCount { get; set; }
        public long ScheduledCount { get; set; }
        public long QueuedCount { get; set; }
        public long RunningCount { get; set; }
        public long CompletedCount { get; set; }
        public long ErrorCount { get; set; }
        public long CancelCount { get; set; }

        public object _incrementLock = 1; //used to lock the increment counters, to avoid race conditions.

        private readonly ConcurrentDictionary<string, ManagedTask> _runningTasks;
        private readonly ConcurrentQueue<ManagedTask> _queuedTasks;

        private readonly ConcurrentDictionary<string, ManagedTask> _taskChangeHistory;

        private AutoResetEvent _resetWhenNoTasks; //event handler that triggers when all tasks completed.
        private object _updateTasksLock = 1; // used to lock when updaging task queues.
        private Exception _exitException; //used to push exceptions to the WhenAny function.

        public ManagedTaskHandler(int maxConcurrent = 100)
        {
            _maxConcurrent = maxConcurrent;

            _runningTasks = new ConcurrentDictionary<string, ManagedTask>();
            _queuedTasks = new ConcurrentQueue<ManagedTask>();
            _resetWhenNoTasks = new AutoResetEvent(false);
            _taskChangeHistory = new ConcurrentDictionary<string, ManagedTask>();
        }

        public ManagedTask Add(ManagedTask managedTask)
        {
            lock (_updateTasksLock)
            {
                if (_runningTasks.Count < _maxConcurrent)
                {
                    var tryaddTask = _runningTasks.TryAdd(managedTask.Reference, managedTask);
                    if(!tryaddTask)
                    {
                        throw new ManagedTaskException(managedTask, "Failed to add the managed task to the running tasks queue.");
                    }

                    managedTask.OnStatus += StatusChange;
                    managedTask.OnProgress += ProgressChanged;
                    managedTask.Start();
                }
                else
                {
                    _queuedTasks.Enqueue(managedTask);
                }
            }

            return managedTask;
        }

        public void StatusChange(object sender, EManagedTaskStatus newStatus)
        {
            try
            {
                var managedTask = (ManagedTask)sender;

                //store most recent update
                _taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask );

                lock (_incrementLock)
                {
                    switch (newStatus)
                    {
                        case EManagedTaskStatus.Created:
                            CreatedCount++;
                            break;
                        case EManagedTaskStatus.Scheduled:
                            ScheduledCount++;
                            break;
                        case EManagedTaskStatus.Queued:
                            QueuedCount++;
                            break;
                        case EManagedTaskStatus.Running:
                            RunningCount++;
                            break;
                        case EManagedTaskStatus.Completed:
                            CompletedCount++;
                            break;
                        case EManagedTaskStatus.Error:
                            ErrorCount++;
                            break;
                        case EManagedTaskStatus.Cancelled:
                            CancelCount++;
                            break;
                    }
                }

                // if the status is finished (eg completed, cancelled, error) when remove the task and look for new tasks.
                if(newStatus == EManagedTaskStatus.Completed || newStatus == EManagedTaskStatus.Cancelled || newStatus == EManagedTaskStatus.Error)
                {
                    ResetCompletedTask(managedTask);
                }

                OnStatus?.Invoke(sender, newStatus);
            }
            catch (Exception ex)
            {
                _exitException = ex;
                _resetWhenNoTasks.Set();
            }
        }

        public void ProgressChanged(object sender, ManagedTaskProgressItem progress)
        {
            var managedTask = (ManagedTask)sender;

            //store most recent update
            _taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask);

            OnProgress?.Invoke(sender, progress);
        }

        private void ResetCompletedTask(ManagedTask managedTask)
        {
            lock (_updateTasksLock)
            {
                if (!_runningTasks.TryRemove(managedTask.Reference, out var finishedTask))
                {
                    _exitException = new ManagedTaskException(managedTask, "Failed to remove the task from the running tasks list.");
                    _resetWhenNoTasks.Set();
                    return;
                }
                finishedTask.Dispose();

                UpdateRunningQueue();

                // if there are no remainning tasks, set the trigger to allow WhenAll to run.
                if (_runningTasks.Count == 0 && _queuedTasks.Count == 0)
                {
                    OnTasksCompleted?.Invoke(this, EventArgs.Empty);
                    _resetWhenNoTasks.Set();
                }
            }
        }

        private void UpdateRunningQueue()
        {
            // update the running queue
            while (_runningTasks.Count < _maxConcurrent && _queuedTasks.Count > 0)
            {
                if (!_queuedTasks.TryDequeue(out var queuedTask))
                {
                    // something wrong with concurrency if this is hit.
                    _exitException = new ManagedTaskException(queuedTask, "Failed to remove the task from the queued tasks list.");
                    _resetWhenNoTasks.Set();
                    return;
                }

                // if the task is marked as cancelled just ignore it
                if (queuedTask.Status == EManagedTaskStatus.Cancelled)
                {
                    OnStatus?.Invoke(queuedTask, EManagedTaskStatus.Cancelled);
                    continue;
                }

                if (!_runningTasks.TryAdd(queuedTask.Reference, queuedTask))
                {
                    // something wrong with concurrency if this is hit.
                    _exitException = new ManagedTaskException(queuedTask, "Failed to add the task from the running tasks list.");
                    _resetWhenNoTasks.Set();
                    return;
                }

                queuedTask.OnStatus += StatusChange;
                queuedTask.OnProgress += ProgressChanged;
                queuedTask.Start();
            }
        }

        public async Task WhenAll()
        {
            if (_runningTasks.Count > 0 || _queuedTasks.Count > 0)
            {
                await Task.Run(() => _resetWhenNoTasks.WaitOne());
                if (_exitException != null)
                {
                    throw _exitException;
                }
            }
        }

        public IEnumerable<ManagedTask> GetTaskChanges(bool resetTaskChanges = false)
        {
            var taskChanges = _taskChangeHistory.Values;
            if (resetTaskChanges) ResetTaskChanges();

            return taskChanges;
        }

        public int TaskChangesCount()
        {
            return _taskChangeHistory.Count;
        }

        public void ResetTaskChanges()
        {
            _taskChangeHistory.Clear();
        }

        public void Dispose()
        {
            OnStatus = null;
            OnProgress = null;
            OnTasksCompleted = null;
        }
    }
}
