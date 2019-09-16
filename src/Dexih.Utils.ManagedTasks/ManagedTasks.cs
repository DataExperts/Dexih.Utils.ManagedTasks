using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
	
	/// <summary>
	/// Collection of managed tasks.
	/// </summary>
	[DataContract]
	public class ManagedTasks : IEnumerable<ManagedTask>, IDisposable, IManagedTasks
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
		
		[DataMember(Order = 1)]
        public long CreatedCount => _createdCount;
		
        [DataMember(Order = 2)]
        public long ScheduledCount => _scheduledCount;
		
        [DataMember(Order = 3)]
        public long FileWatchCount => _fileWatchCount;
        
        [DataMember(Order = 4)]
        public long QueuedCount => _queuedCount;
        
        [DataMember(Order = 5)]
        public long RunningCount => _runningCount;
        
        [DataMember(Order = 6)]
        public long CompletedCount => _completedCount;
        
        [DataMember(Order = 7)]
        public long ErrorCount => _errorCount;
        [DataMember(Order = 8)]
        public long CancelCount  => _cancelCount;
        
        private readonly ConcurrentDictionary<string , ManagedTask> _activeTasks;
		private readonly ConcurrentDictionary<string, ManagedTask> _runningTasks;
		private readonly ConcurrentQueue<ManagedTask> _queuedTasks;
		private readonly ConcurrentDictionary<string, ManagedTask> _scheduledTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _completedTasks;

		private readonly ConcurrentDictionary<string, ManagedTask> _taskChangeHistory;
		
		private Exception _exitException; //used to push exceptions to the WhenAny function.
		
		private readonly ConcurrentQueue<TaskCompletionSource<bool>> _awaitTasks; //event handler that triggers when all tasks completed.

		// dedicated thread used to process status changes.
		private readonly Thread _statusChangeThread;
		private readonly BlockingCollection<(EManagedTaskStatus status, ManagedTask managedTask)> _statusChangeQueue = new BlockingCollection<(EManagedTaskStatus, ManagedTask)>(1024);
		
        private int _resetRunningCount;

		private readonly object _taskAddLock = 1;
		
		public ManagedTasks(int maxConcurrent = 100)
		{
			_maxConcurrent = maxConcurrent;

			_activeTasks = new ConcurrentDictionary<string, ManagedTask>();
			_completedTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
			_runningTasks = new ConcurrentDictionary<string, ManagedTask>();
			_queuedTasks = new ConcurrentQueue<ManagedTask>();
			_scheduledTasks = new ConcurrentDictionary<string, ManagedTask>();
			_taskChangeHistory = new ConcurrentDictionary<string, ManagedTask>();

			_awaitTasks = new ConcurrentQueue<TaskCompletionSource<bool>>();
			
			_statusChangeThread = new Thread(ProcessStatusChanges);
			_statusChangeThread.Start();
		}

		public ManagedTask Add(ManagedTask managedTask)
		{
			if(!string.IsNullOrEmpty(managedTask.Category) && managedTask.CategoryKey > 0 && ContainsKey(managedTask.Category, managedTask.CategoryKey))
			{
				switch (managedTask.ConcurrentTaskAction)
				{
					case EConcurrentTaskAction.Parallel:
						break;
					case EConcurrentTaskAction.Abend:
						throw new ManagedTaskException($"The {managedTask.Category} - {managedTask.Name} (key {managedTask.CategoryKey}) is already active and cannot be run at the same time.");
					case EConcurrentTaskAction.Sequence:
						var depTasks = GetTasks(managedTask.Category, managedTask.CategoryKey);
						var depReferences = depTasks.Select(c => c.Reference);
						if (managedTask.DependentReferences == null)
						{
							managedTask.DependentReferences = depReferences.ToArray();
						}
						else
						{
							managedTask.DependentReferences =
								managedTask.DependentReferences.Concat(depReferences).ToArray();
						}
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			managedTask.OnStatus += StatusChange;
			managedTask.OnProgress += ProgressChanged;

			if (!_activeTasks.TryAdd(managedTask.Reference, managedTask))
			{
				throw new ManagedTaskException("Failed to add the task to the active tasks list.");
			}

			// if there are no dependencies, put the task immediately on the queue.
			if ((managedTask.Triggers == null || !managedTask.Triggers.Any()) &&
			    (managedTask.FileWatchers == null || !managedTask.FileWatchers.Any()) &&
			    (managedTask.DependentReferences ==null || !managedTask.DependentReferences.Any()))
			{
				Start(managedTask.Reference);
			}
			else
			{
				if (!(managedTask.Schedule()))
				{
					if (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0)
					{
						throw new ManagedTaskException("The task could not be started as none of the triggers returned a future schedule time.");
					}
				}
				
				Schedule(managedTask.Reference);
			}

			return managedTask;
		}

        public ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(originatorId, name, category, 0, "", 0, managedObject, triggers, dependentReferences);
        }

		public ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
		{
			return Add(originatorId, name, category, 0, 0, managedObject, triggers, fileWatchers, dependentReferences);
		}

        public ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(originatorId, name, category, 0, "", 0, managedObject, triggers, null);
        }

		public ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers)
		{
			return Add(originatorId, name, category, 0, 0, managedObject, triggers, fileWatchers, null);
		}
		
        public ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject)
        {
            return Add(originatorId, name, category, 0, "", 0, managedObject, null, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, long hubKey, string remoteAgentId, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, remoteAgentId, categoryKey, managedObject, triggers, null, dependentReferences);
		}
		
		public ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, "", categoryKey, managedObject, triggers, fileWatchers, dependentReferences);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, managedObject, triggers, null, dependentReferences);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, managedObject, triggers, null, null);
        }
		
		public ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher, string[] dependentReferences)
		{
			return Add(reference, originatorId, name, category, 0, "", 0, managedObject, triggers, fileWatcher, dependentReferences);
		}

		public ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher)
		{
			return Add(reference, originatorId, name, category, 0, "", 0, managedObject, triggers, fileWatcher, null);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject)
        {
            return Add(reference, originatorId, name, category, 0, "", 0, managedObject, null, null, null);
        }

        /// <summary>
        /// Creates & starts a new managed task.
        /// </summary>
        /// <param name="reference"></param>
        /// <param name="originatorId">Id that can be used to reference where the task was started from.</param>
        /// <param name="name"></param>
        /// <param name="category"></param>
        /// <param name="referenceId"></param>
        /// <param name="categoryKey"></param>
        /// <param name="managedObject"></param>
        /// <param name="triggers"></param>
        /// <param name="fileWatchers"></param>
        /// <param name="dependentReferences"></param>
        /// <param name="referenceKey"></param>
        /// <returns></returns>
        public ManagedTask Add(string reference, string originatorId, string name, string category, long referenceKey, string referenceId, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences)
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
				ManagedObject = managedObject,
				Triggers = triggers,
				FileWatchers = fileWatchers,
				DependentReferences = dependentReferences
			};

			return Add(managedTask);
		}

        private void StatusChange(object sender, EManagedTaskStatus newStatus)
        {
	        _statusChangeQueue.Add((newStatus, (ManagedTask) sender));
        }

        private void ProcessStatusChanges()
		{
			try
			{
				foreach (var statusChange in _statusChangeQueue.GetConsumingEnumerable())
				{
					var managedTask = statusChange.managedTask;
					var newStatus = statusChange.status;

					// if current status called multiple times, do not resend the event.
					if (newStatus == managedTask.Status) continue;

					var oldStatus = managedTask.Status;
					managedTask.Status = newStatus;

					//store most recent update
					_taskChangeHistory.AddOrUpdate(managedTask.ChangeId, managedTask,
						(oldKey, oldValue) => managedTask);

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
					if (newStatus == EManagedTaskStatus.Completed || newStatus == EManagedTaskStatus.Cancelled ||
					    newStatus == EManagedTaskStatus.Error)
					{
						if (oldStatus == EManagedTaskStatus.Running)
						{
							if (!_runningTasks.TryRemove(managedTask.Reference, out var _))
							{
								_exitException =
									new ManagedTaskException(
										"Failed to remove the task from the running tasks list.");
								SetException(_exitException);
								return;
							}
						}

						if (oldStatus == EManagedTaskStatus.Scheduled ||
						    oldStatus == EManagedTaskStatus.FileWatching)
						{
							if (!_scheduledTasks.TryRemove(managedTask.Reference, out var _))
							{
								_exitException =
									new ManagedTaskException(
										"Failed to remove the task from the scheduled tasks list.");
								SetException(_exitException);
								return;
							}
						}

						UpdateRunningQueue();

						ReStartTask(managedTask);
						OnStatus?.Invoke(managedTask, newStatus);
					}
					else
					{
						OnStatus?.Invoke(managedTask, newStatus);
					}
				}

			}
			catch (Exception ex)
			{
				_exitException = ex;
				_statusChangeQueue.CompleteAdding();
				SetException(_exitException);
			}

		}

		private void Schedule(string reference)
		{
			var taskFound = _activeTasks.TryGetValue(reference, out var managedTask);
			if (!taskFound)
			{
				throw new ManagedTaskException("Failed to schedule the task as it could not be found in the active task list.");
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

		private void SetException(Exception ex)
		{
			if (_awaitTasks.TryDequeue(out var task))
			{
				task.SetException(ex);
			}
		}

		private void SetResult(bool value)
		{
			if (_awaitTasks.TryDequeue(out var task))
			{
				task.SetResult(value);
			}
		}
		
		private void Start(string reference)
		{
			var startTasks = new List<ManagedTask>();
			
			lock (_taskAddLock) // lock to ensure _runningTask.Count is consistent when adding the task
			{
				var taskFound = _activeTasks.TryGetValue(reference, out var managedTask);
				if (!taskFound)
				{
					throw new ManagedTaskException($"Failed to start the task with reference {reference} as it could not be found in the active task list.");
				}
                
				if (_runningTasks.Count < _maxConcurrent)
				{
					var tryaddTask = _runningTasks.TryAdd(managedTask.Reference, managedTask);
					if (!tryaddTask)
					{
						throw new ManagedTaskException("Failed to add the managed task to the running tasks queue.");
					}

					startTasks.Add(managedTask);
				}
				else
				{
					_queuedTasks.Enqueue(managedTask);
				}
			}
			
			foreach (var task in startTasks)
			{
				task.Start();
			}
		}

		
		private void UpdateRunningQueue()
		{
			var startTasks = new List<ManagedTask>();
	        
            lock (_taskAddLock)
            {
                // update the running queue
                while (_runningTasks.Count < _maxConcurrent && _queuedTasks.Count > 0)
                {
                    if (!_queuedTasks.TryDequeue(out var queuedTask))
                    {
                        // something wrong with concurrency if this is hit.
                        _exitException = new ManagedTaskException("Failed to remove the task from the queued tasks list.");
                        SetException(_exitException);
                        return;
                    }

                    // if the task is marked as cancelled just ignore it
                    if (queuedTask.Status == EManagedTaskStatus.Cancelled)
                    {
                        // OnStatus?.Invoke(queuedTask, EManagedTaskStatus.Cancelled);
                        continue;
                    }

                    if (!_runningTasks.TryAdd(queuedTask.Reference, queuedTask))
                    {
                        // something wrong with concurrency if this is hit.
                        _exitException = new ManagedTaskException("Failed to add the task from the running tasks list.");
                        SetException(_exitException);
                        return;
                    }

                    startTasks.Add(queuedTask);
                }
            }

            foreach (var task in startTasks)
            {
	            task.Start();
            }
        }

        private void ReStartTask(ManagedTask managedTask)
        {
          	Interlocked.Increment(ref _resetRunningCount);

            // copy the activeTask so it is preserved when job is rerun to to schedule.
            var completedTask = managedTask.Copy();

			if (managedTask.Schedule())
			{
				_taskChangeHistory.AddOrUpdate(completedTask.ChangeId, completedTask, (oldKey, oldValue) => completedTask);
				managedTask.ResetChangeId();

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
						new ManagedTaskException("Failed to remove the task to the active tasks list.");
					SetException(_exitException);
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
				SetResult(true);
			}
		}

		private void ProgressChanged(object sender, ManagedTaskProgressItem progress)
		{
			var managedTask = (ManagedTask)sender;
			_taskChangeHistory.AddOrUpdate(managedTask.ChangeId, managedTask, (oldKey, oldValue) => managedTask);
			OnProgress?.Invoke(sender, progress);
		}
		
		
	   private void Trigger(object sender, EventArgs e)
        {
//            lock (_triggerLock)
//            {
                var managedTask = (ManagedTask) sender;
                var success = _scheduledTasks.TryRemove(managedTask.Reference, out ManagedTask _);
                if (success) // if the schedule task could not be removed, it is due to two simultaneous triggers occurring, so ignore.
                {
                    managedTask.DisposeTrigger(); //stop the trigger whilst the task is running.
                    Start(managedTask.Reference);
                }
//            }
        }

		public async Task WhenAll(CancellationToken cancellationToken = default)
		{
			
			var awaitTask = new TaskCompletionSource<bool>(false);
			_awaitTasks.Enqueue(awaitTask);

			while (_activeTasks.Count > 0 || _runningTasks.Count > 0 || _resetRunningCount > 0)
			{
				await Task.WhenAny(awaitTask.Task, Task.Delay(-1, cancellationToken));

				if (_exitException != null)
				{
					throw _exitException;
				}

                if (cancellationToken.IsCancellationRequested)
                {
	                throw new TaskCanceledException();
                }

				if (await awaitTask.Task)
				{
					break;
				}
				
                awaitTask = new TaskCompletionSource<bool>(false);
                _awaitTasks.Enqueue(awaitTask);
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

		public IEnumerable<ManagedTask> GetRunningTasks(string category = null)
		{
			if(string.IsNullOrEmpty(category))
			{
				return _runningTasks.Values;
			}
			return _runningTasks.Values.Where(c => c.Category == category);
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
				if(_activeTasks.TryGetValue(reference, out var task))
				{
					task.CancelAsync();
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
	        _statusChangeThread.Join(1500);
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

		private IEnumerable<ManagedTask> GetTasks(string category, long categoryKey)
		{
			var tasks = _activeTasks.Values.Where(c => c.Category == category && c.CategoryKey == categoryKey)
				.Concat(_scheduledTasks.Values.Where(c => c.Category == category && c.CategoryKey == categoryKey))
				.Concat(_queuedTasks.Where(c => c.Category == category && c.CategoryKey == categoryKey));

			return tasks;
		}
    }
}