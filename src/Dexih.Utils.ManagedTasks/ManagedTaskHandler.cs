using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dexih.utils.ManagedTasks
{
    /// <summary>
    /// Creates a queue of tasks, and sequentially executes those tasks using the maxConcurrent 
    /// as the number of tasks to execute in parallel.
    /// </summary>
    public class ManagedTaskHandler : IDisposable
    {
        private readonly int _maxConcurrent;

        public event EventHandler<EManagedTaskStatus> OnStatus;
        public event EventHandler<ManagedTaskProgressItem> OnProgress;
        public event EventHandler OnTasksCompleted;

        private long _createdCount;
        private long _scheduledCount;
        private long _queuedCount;
        private long _runningCount;
        private long _completedCount;
        private long _errorCount;
        private long _cancelCount;
		
        public long CreatedCount => _createdCount;
        public long ScheduledCount => _scheduledCount;
        public long QueuedCount => _queuedCount;
        public long RunningCount => _runningCount;
        public long CompletedCount => _completedCount;
        public long ErrorCount => _errorCount;
        public long CancelCount  => _cancelCount;

        private readonly ConcurrentDictionary<string, ManagedTask> _runningTasks;
        private readonly ConcurrentQueue<ManagedTask> _queuedTasks;

        private readonly ConcurrentDictionary<string, ManagedTask> _taskChangeHistory;

        private TaskCompletionSource<bool> _noMoreTasks; //event handler that triggers when all tasks completed.
        
        private Exception _exitException; //used to push exceptions to the WhenAny function.

        public ManagedTaskHandler(int maxConcurrent = 100)
        {
            _maxConcurrent = maxConcurrent;

            _runningTasks = new ConcurrentDictionary<string, ManagedTask>();
            _queuedTasks = new ConcurrentQueue<ManagedTask>();
            _noMoreTasks = new TaskCompletionSource<bool>(false);
            _taskChangeHistory = new ConcurrentDictionary<string, ManagedTask>();
        }

        public ManagedTask Add(ManagedTask managedTask)
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

            return managedTask;
        }

        private void StatusChange(object sender, EManagedTaskStatus newStatus)
        {
            try
            {
                var managedTask = (ManagedTask)sender;

                //store most recent update
                _taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask );

                switch (newStatus)
                {
                    case EManagedTaskStatus.Created:
                        Interlocked.Increment(ref _createdCount);
                        break;
                    case EManagedTaskStatus.Scheduled:
                        Interlocked.Increment(ref _scheduledCount);
                        break;
                    case EManagedTaskStatus.Queued:
                        Interlocked.Increment(ref _queuedCount);
                        break;
                    case EManagedTaskStatus.Running:
                        Interlocked.Increment(ref _runningCount);
                        break;
                    case EManagedTaskStatus.Completed:
                        Interlocked.Increment(ref _completedCount);
                        break;
                    case EManagedTaskStatus.Error:
                        Interlocked.Increment(ref _errorCount);
                        break;
                    case EManagedTaskStatus.Cancelled:
                        Interlocked.Increment(ref _cancelCount);
                        break;
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
                _noMoreTasks.TrySetException(ex);
            }
        }

        private void ProgressChanged(object sender, ManagedTaskProgressItem progress)
        {
            var managedTask = (ManagedTask)sender;

            //store most recent update
            _taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask);

            OnProgress?.Invoke(sender, progress);
        }

        private void ResetCompletedTask(ManagedTask managedTask)
        {
            if (!_runningTasks.TryRemove(managedTask.Reference, out var finishedTask))
            {
                _exitException = new ManagedTaskException(managedTask, "Failed to remove the task from the running tasks list.");
                _noMoreTasks.TrySetException(_exitException);
                return;
            }
            finishedTask.Dispose();

            UpdateRunningQueue();

            // if there are no remainning tasks, set the trigger to allow WhenAll to run.
            if (_runningTasks.Count == 0 && _queuedTasks.Count == 0)
            {
                OnTasksCompleted?.Invoke(this, EventArgs.Empty);
                _noMoreTasks.TrySetResult(true);
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
                    _noMoreTasks.TrySetException(_exitException);
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
                    _noMoreTasks.TrySetException(_exitException);
                    return;
                }

                queuedTask.OnStatus += StatusChange;
                queuedTask.OnProgress += ProgressChanged;
                queuedTask.Start();
            }
        }

        public async Task WhenAll(CancellationToken cancellationToken)
        {
            var resetValue = false;
            
            while (!resetValue || (_runningTasks.Count > 0 && _queuedTasks.Count > 0))
            {
                await Task.WhenAny(_noMoreTasks.Task, Task.Delay(-1, cancellationToken));
                if (_exitException != null)
                {
                    throw _exitException;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }

                resetValue = _noMoreTasks.Task.Result;
                _noMoreTasks = new TaskCompletionSource<bool>(false);
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

        private void ResetTaskChanges()
        {
            _taskChangeHistory.Clear();
        }

        public void Dispose()
        {
            foreach (var task in _queuedTasks)
            {
                task.Cancel();
                task.Dispose();
            }
            foreach (var task in _runningTasks)
            {
                task.Value.Cancel();
                task.Value.Dispose();
            }
            
            OnStatus = null;
            OnProgress = null;
            OnTasksCompleted = null;
        }
    }
}
