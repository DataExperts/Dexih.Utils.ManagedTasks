using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
	/// <summary>
	/// Collection of managed tasks.
	/// </summary>
	public class ManagedTasks : IEnumerable<ManagedTask>, IDisposable
	{
		public event EventHandler<EManagedTaskStatus> OnStatus;
		public event EventHandler<ManagedTaskProgressItem> OnProgress;

		private readonly int _maxConcurrent;
		
		private long _createdCount;
		private long _scheduledCount;
		private long _fileWatchCount;
		private long _queuedCount;
		private long _runningCount;
		private long _completedCount;
		private long _errorCount;
		private long _cancelCount;
		
        public long CreatedCount => _createdCount;
		public long ScheduledCount => _scheduledCount;
		public long FileWatchCount => _fileWatchCount;
        public long QueuedCount => _queuedCount;
        public long RunningCount => _runningCount;
        public long CompletedCount => _completedCount;
        public long ErrorCount => _errorCount;
        public long CancelCount  => _cancelCount;


        private readonly ConcurrentDictionary<string , ManagedTask> _activeTasks;
		private readonly ConcurrentDictionary<string, ManagedTask> _runningTasks;
		private readonly ConcurrentQueue<ManagedTask> _queuedTasks;
		private readonly ConcurrentDictionary<string, ManagedTask> _scheduledTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _completedTasks;

		private readonly ConcurrentDictionary<string, ManagedTask> _taskChangeHistory;
		
		private Exception _exitException; //used to push exceptions to the WhenAny function.
		private TaskCompletionSource<bool> _noMoreTasks; //event handler that triggers when all tasks completed.
        private int _resetRunningCount;

		private readonly object _taskAddLock = 1;
		private readonly object _triggerLock = 1;
		
		public ManagedTasks(int maxConcurrent = 100)
		{
			_maxConcurrent = maxConcurrent;

			_activeTasks = new ConcurrentDictionary<string, ManagedTask>();
			_completedTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
			_runningTasks = new ConcurrentDictionary<string, ManagedTask>();
			_queuedTasks = new ConcurrentQueue<ManagedTask>();
			_scheduledTasks = new ConcurrentDictionary<string, ManagedTask>();
			_taskChangeHistory = new ConcurrentDictionary<string, ManagedTask>();

            _noMoreTasks = new TaskCompletionSource<bool>(false);
		}

		public ManagedTask Add(ManagedTask managedTask)
		{
			if(!string.IsNullOrEmpty(managedTask.Category) && managedTask.CategoryKey > 0 && ContainsKey(managedTask.Category, managedTask.CategoryKey))
			{
				throw new ManagedTaskException(managedTask, $"The {managedTask.Category} - {managedTask.Name} with key {managedTask.CategoryKey} is already active and cannot be run at the same time.");
			}

			if (!_activeTasks.TryAdd(managedTask.Reference, managedTask))
			{
				throw new ManagedTaskException(managedTask, "Failed to add the task to the active tasks list.");
			}
			
			managedTask.OnStatus += StatusChange;
            managedTask.OnProgress += ProgressChanged;

			// if there are no dependencies, put the task immediately on the queue.
			if ((managedTask.Triggers == null || !managedTask.Triggers.Any()) &&
			    (managedTask.FileWatchers == null || !managedTask.FileWatchers.Any()) &&
			    (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0))
			{
				Start(managedTask.Reference);
			}
			else
			{
				if (!managedTask.Schedule())
				{
					if (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0)
					{
						throw new ManagedTaskException(managedTask, "The task could not be started as none of the triggers returned a future schedule time.");
					}
				}
				
				Schedule(managedTask.Reference);
			}

			return managedTask;
		}

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(originatorId, name, category, 0, "", 0, data, action, triggers, dependentReferences);
        }

		public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
		{
			return Add(originatorId, name, category, 0, 0, data, action, triggers, fileWatchers, dependentReferences);
		}

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(originatorId, name, category, 0, "", 0, data, action, triggers, null);
        }

		public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers)
		{
			return Add(originatorId, name, category, 0, 0, data, action, triggers, fileWatchers, null);
		}
		
        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action)
        {
            return Add(originatorId, name, category, 0, "", 0, data, action, null, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, long hubKey, string remoteAgentId, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, remoteAgentId, categoryKey, data, action, triggers, null, dependentReferences);
		}
		
		public ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, "", categoryKey, data, action, triggers, fileWatchers, dependentReferences);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, data, action, triggers, null, dependentReferences);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, data, action, triggers, null, null);
        }
		
		public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher, string[] dependentReferences)
		{
			return Add(reference, originatorId, name, category, 0, "", 0, data, action, triggers, fileWatcher, dependentReferences);
		}

		public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher)
		{
			return Add(reference, originatorId, name, category, 0, "", 0, data, action, triggers, fileWatcher, null);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, data, action, null, null, null);
        }

		/// <summary>
		/// Creates & starts a new managed task.
		/// </summary>
		/// <param name="reference"></param>
		/// <param name="originatorId">Id that can be used to reference where the task was started from.</param>
		/// <param name="data"></param>
		/// <param name="action">The action </param>
		/// <param name="name"></param>
		/// <param name="category"></param>
		/// <param name="hubKey"></param>
		/// <param name="categoryKey"></param>
		/// <param name="triggers"></param>
		/// <param name="fileWatchers"></param>
		/// <param name="dependentReferences"></param>
		/// <returns></returns>
		public ManagedTask Add(string reference, string originatorId, string name, string category, long referenceKey, string referenceId, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
		{
			var managedTask = new ManagedTask
			{
				Reference = reference,
				OriginatorId = originatorId,
				Name = name,
				Category = category,
				CategoryKey = categoryKey,
				ReferenceKey = referenceKey,
				ReferenceId = referenceId,
				Data = data,
				Action = action,
				Triggers = triggers,
				FileWatchers = fileWatchers,
				DependentReferences = dependentReferences,
			};

			return Add(managedTask);
		}

		private void StatusChange(object sender, EManagedTaskStatus newStatus)
		{
			try
			{
				var managedTask = (ManagedTask)sender;
				
				if (newStatus == managedTask.Status) return;

				var oldStatus = managedTask.Status;
				managedTask.Status = newStatus;

				//store most recent update
                _taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask );

				switch (newStatus)
				{
					case EManagedTaskStatus.Created:
						Interlocked.Increment(ref _createdCount);
						break;
					case EManagedTaskStatus.FileWatching:
						Interlocked.Increment(ref _fileWatchCount);
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

				// if the status is finished update the queues
				if (newStatus == EManagedTaskStatus.Completed || newStatus == EManagedTaskStatus.Cancelled || newStatus == EManagedTaskStatus.Error)
				{
					if (oldStatus == EManagedTaskStatus.Running)
					{
						if (!_runningTasks.TryRemove(managedTask.Reference, out var _))
						{
							_exitException = new ManagedTaskException(managedTask, "Failed to remove the task from the running tasks list.");
							_noMoreTasks.TrySetException(_exitException);
							return;
						}
					}
					
					if (oldStatus == EManagedTaskStatus.Scheduled)
					{
						if (!_scheduledTasks.TryRemove(managedTask.Reference, out var _))
						{
							_exitException = new ManagedTaskException(managedTask, "Failed to remove the task from the scheduled tasks list.");
							_noMoreTasks.TrySetException(_exitException);
							return;
						}
					}
					
					UpdateRunningQueue();

					if (newStatus == EManagedTaskStatus.Cancelled)
					{
						if (!_activeTasks.TryRemove(managedTask.Reference, out var _))
						{
							_exitException = new ManagedTaskException(managedTask, "Failed to remove the cancelled from the active tasks list.");
							_noMoreTasks.TrySetException(_exitException);
							return;
						}
					}
					else
					{
						ReStartTask(managedTask);
					}
					OnStatus?.Invoke(sender, newStatus);

				}
				else
				{
					OnStatus?.Invoke(sender, newStatus);
				}

			}
			catch (Exception ex)
			{
				_exitException = ex;
				_noMoreTasks.TrySetException(_exitException);
			}
		}

		private void Schedule(string reference)
		{
			var taskFound = _activeTasks.TryGetValue(reference, out var managedTask);
			if (!taskFound)
			{
				throw new ManagedTaskException(managedTask,
					"Failed to schedule the task as it could not be found in the active task list.");
			}

			// StatusChange(managedTask, managedTask.Status);

			// if the task was triggered previously, then start it.
			if (managedTask.CheckPreviousTrigger())
			{
				Start(managedTask.Reference);
			}
			else
			{
				managedTask.OnTrigger += Trigger;
				_scheduledTasks.TryAdd(managedTask.Reference, managedTask);
			}
		}
		
		
		private void Start(string reference)
		{
			lock (_taskAddLock) // lock to ensure _runningTask.Count is consistent when adding the task
			{
				var taskFound = _activeTasks.TryGetValue(reference, out var managedTask);
				if (!taskFound)
				{
					throw new ManagedTaskException(managedTask,
						"Failed to start the task as it could not be found in the active task list.");
				}
                
				if (_runningTasks.Count < _maxConcurrent)
				{
					var tryaddTask = _runningTasks.TryAdd(managedTask.Reference, managedTask);
					if (!tryaddTask)
					{
						throw new ManagedTaskException(managedTask,
							"Failed to add the managed task to the running tasks queue.");
					}

					managedTask.Start();
				}
				else
				{
					_queuedTasks.Enqueue(managedTask);
				}
			}
		}

		
		private void UpdateRunningQueue()
        {

            lock (_taskAddLock)
            {
                // update the running queue
                while (_runningTasks.Count < _maxConcurrent && _queuedTasks.Count > 0)
                {
                    if (!_queuedTasks.TryDequeue(out var queuedTask))
                    {
                        // something wrong with concurrency if this is hit.
                        _exitException = new ManagedTaskException(queuedTask,
                            "Failed to remove the task from the queued tasks list.");
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
                        _exitException = new ManagedTaskException(queuedTask,
                            "Failed to add the task from the running tasks list.");
                        _noMoreTasks.TrySetException(_exitException);
                        return;
                    }

                    queuedTask.Start();
                }

                // if there are no remaining tasks, set the trigger to allow WhenAll to run.
//                if (_runningTasks.Count == 0 && _queuedTasks.Count == 0 && _scheduledTasks.Count == 0)
//                {
//                    _noMoreTasks.TrySetResult(true);
//                }
            }
        }

        private void ReStartTask(ManagedTask managedTask)
        {
          	Interlocked.Increment(ref _resetRunningCount);

			if (managedTask.Schedule())
			{
				// managedTask.Reset();
				Schedule(managedTask.Reference);
			}
			else
			{
				if (!_activeTasks.ContainsKey(managedTask.Reference))
				{
					return;
				}

				managedTask.Dispose();

				if (!_activeTasks.TryRemove(managedTask.Reference, out var activeTask))
				{
					_exitException =
						new ManagedTaskException(managedTask, "Failed to remove the task to the active tasks list.");
					_noMoreTasks.TrySetException(_exitException);
				}

				_completedTasks.AddOrUpdate((activeTask.Category, activeTask.CategoryKey), activeTask,
					(oldKey, oldValue) => activeTask);
			}

			// check all active tasks, to see if the dependency conditions have been met.
			foreach (var activeTask in _activeTasks.Values)
			{
				if (activeTask.DependentReferences != null && activeTask.DependentReferences.Length > 0)
				{
					var depFound = false;
					foreach (var dep in activeTask.DependentReferences)
					{
						if (_activeTasks.ContainsKey(dep))
						{
							depFound = true;
							break;
						}
					}

					// if no dependent tasks are found, then the current task is ready to go.
					if (!depFound)
					{
						// check dependencies are not already met is not already set, which can happen when two dependent tasks finish at the same time.
						if (!activeTask.DependenciesMet)
						{
							activeTask.DependenciesMet = true;
							if (activeTask.Schedule())
							{
								Start(activeTask.Reference);
							}
						}
					}
				}
			}

	        Interlocked.Decrement(ref _resetRunningCount);

			if (_activeTasks.Count == 0 && _resetRunningCount == 0)
			{
				_noMoreTasks.TrySetResult(true);
			}
		}

		private void ProgressChanged(object sender, ManagedTaskProgressItem progress)
		{
			var managedTask = (ManagedTask)sender;
			_taskChangeHistory.AddOrUpdate(managedTask.Reference, managedTask, (oldKey, oldValue) => managedTask);
			OnProgress?.Invoke(sender, progress);
		}
		
	   private void Trigger(object sender, EventArgs e)
        {
            lock (_triggerLock)
            {
                var managedTask = (ManagedTask) sender;
                var success = _scheduledTasks.TryRemove(managedTask.Reference, out ManagedTask _);
                if (success) // if the schedule task could not be removed, it is due to two simultaneous triggers occurring, so ignore.
                {
                    managedTask.DisposeTrigger(); //stop the trigger whilst the task is running.
                    Start(managedTask.Reference);
                }
            }
        }

		public Task WhenAll()
		{
			var cancellationToken = CancellationToken.None;
			return WhenAll(cancellationToken);
		}

		public async Task WhenAll(CancellationToken cancellationToken)
		{
			_noMoreTasks = new TaskCompletionSource<bool>(false);

			while (_activeTasks.Count > 0 || _resetRunningCount > 0)
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

				if (_noMoreTasks.Task.Result)
				{
					break;
				}
                _noMoreTasks = new TaskCompletionSource<bool>(false);
			}

		}

		public IEnumerator<ManagedTask> GetEnumerator()
		{
			return _activeTasks.Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public ManagedTask GetTask(string reference)
		{
			return _activeTasks.ContainsKey(reference) ? _activeTasks[reference] : null;
		}

		public ManagedTask GetTask(string category, long categoryKey)
		{
			return _activeTasks.Values.SingleOrDefault(c => c.Category == category && c.CategoryKey == categoryKey);
		}

		public IEnumerable<ManagedTask> GetActiveTasks(string category = null)
		{
			if(string.IsNullOrEmpty(category))
            {
                return _activeTasks.Values;
            }
			return _activeTasks.Values.Where(c => c.Category == category);
		}
		
		public IEnumerable<ManagedTask> GetScheduledTasks(string category = null)
		{
			if(string.IsNullOrEmpty(category))
			{
				return _scheduledTasks.Values;
			}
			return _scheduledTasks.Values.Where(c => c.Category == category);
		}

		public IEnumerable<ManagedTask> GetCompletedTasks(string category = null)
		{
			if (string.IsNullOrEmpty(category))
            {
                return _completedTasks.Values;
            }
			return _completedTasks.Values.Where(c => c.Category == category);
		}

		public void Cancel(IEnumerable<string> references)
		{
			foreach (var reference in references)
			{
				if(_activeTasks.ContainsKey(reference))
				{
					_activeTasks[reference].Cancel();
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

        private void ResetTaskChanges()
        {
            _taskChangeHistory.Clear();
        }


        public void Dispose()
        {
            OnProgress = null;
            OnStatus = null;
        }

		private bool ContainsKey(string category, long categoryKey)
		{
			if (_activeTasks.Values.Any(c => c.Category == category && c.CategoryKey == categoryKey))
			{
				return true;
			}
			
			if (_scheduledTasks.Values.Any(c => c.Category == category && c.CategoryKey == categoryKey))
			{
				return true;
			}

			if (_queuedTasks.Any(c => c.Category == category && c.CategoryKey == categoryKey))
			{
				return true;
			}

			
			return false;
		}
    }
}