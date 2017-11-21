using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dexih.utils.ManagedTasks
{
	/// <summary>
	/// Collection of managed tasks.
	/// </summary>
	public class ManagedTasks : IEnumerable<ManagedTask>, IDisposable
	{
		public event EventHandler<EManagedTaskStatus> OnStatus;
		public event EventHandler<ManagedTaskProgressItem> OnProgress;

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


        private readonly ConcurrentDictionary<string , ManagedTask> _activeTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _activeCategoryTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _completedTasks;

		private Exception _exitException; //used to push exceptions to the WhenAny function.
		private TaskCompletionSource<bool> _noMoreTasks; //event handler that triggers when all tasks completed.
        private int _resetRunningCount;

		public ManagedTaskHandler TaskHandler { get; private set; }

		public ManagedTasks(ManagedTaskHandler taskHandler = null)
		{
			TaskHandler = taskHandler ?? new ManagedTaskHandler();
			TaskHandler.OnStatus += StatusChange;
			TaskHandler.OnProgress += ProgressChange;

			_activeTasks = new ConcurrentDictionary<string, ManagedTask>();
			_activeCategoryTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
			_completedTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
            _noMoreTasks = new TaskCompletionSource<bool>(false);
		}

		public ManagedTask Add(ManagedTask managedTask)
		{
			if(!string.IsNullOrEmpty(managedTask.Category) && managedTask.CatagoryKey >= 0 && _activeCategoryTasks.ContainsKey((managedTask.Category, managedTask.CatagoryKey)))
			{
				throw new ManagedTaskException(managedTask, $"The {managedTask.Category} - {managedTask.Name} with key {managedTask.CatagoryKey} is alredy active and cannot be run at the same time.");
			}

			if (!_activeTasks.TryAdd(managedTask.Reference, managedTask))
			{
				throw new ManagedTaskException(managedTask, "Failed to add the task to the active tasks list.");
			}

			// if there are no depdencies, put the task immediately on the queue.
			if ((managedTask.Triggers == null || !managedTask.Triggers.Any()) && (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0))
			{
				TaskHandler.Add(managedTask);
			}
			else
			{
				managedTask.OnSchedule += Schedule;
				managedTask.OnTrigger += Trigger;

				if (!managedTask.Schedule())
				{
					if (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0)
					{
						throw new ManagedTaskException(managedTask, "The task could not be started as none of the triggers returned a future schedule time.");
					}
				}
			}

			return managedTask;
		}

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(originatorId, name, category, 0, 0, data, action, triggers, dependentReferences);
        }

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(originatorId, name, category, 0, 0, data, action, triggers, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action)
        {
            return Add(originatorId, name, category, 0, 0, data, action, null, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, categoryKey, data, action, triggers, dependentReferences);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, triggers, dependentReferences);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, triggers, null);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTaskProgress, CancellationToken, Task> action)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, null, null);
        }

		/// <summary>
		/// Creates & starts a new managed task.
		/// </summary>
		/// <param name="reference"></param>
		/// <param name="originatorId">Id that can be used to referernce where the task was started from.</param>
		/// <param name="data"></param>
		/// <param name="action">The action </param>
		/// <param name="name"></param>
		/// <param name="category"></param>
		/// <param name="hubKey"></param>
		/// <param name="categoryKey"></param>
		/// <param name="triggers"></param>
		/// <param name="dependentReferences"></param>
		/// <returns></returns>
		public ManagedTask Add(string reference, string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences)
		{
			var managedTask = new ManagedTask
			{
				Reference = reference,
				OriginatorId = originatorId,
				Name = name,
				Category = category,
				CatagoryKey = categoryKey,
				HubKey = hubKey,
				Data = data,
				Action = action,
				Triggers = triggers,
				DependentReferences = dependentReferences
			};

			return Add(managedTask);
		}

		private void Trigger(object sender, EventArgs e)
		{
			var managedTask = (ManagedTask)sender;
			TaskHandler.Add(managedTask);
		}
		
		private void Schedule(object sender, EventArgs e)
		{
			StatusChange(sender, EManagedTaskStatus.Scheduled);
		}

		private void StatusChange(object sender, EManagedTaskStatus newStatus)
		{
			try
			{
				var managedTask = (ManagedTask)sender;

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

				OnStatus?.Invoke(sender, newStatus);

                // if the status is finished (eg completed, cancelled, error) when remove the task and look for new tasks.
                if (newStatus == EManagedTaskStatus.Completed || newStatus == EManagedTaskStatus.Cancelled || newStatus == EManagedTaskStatus.Error)
                {
                    ResetCompletedTask(managedTask);
                }

			}
			catch (Exception ex)
			{
				_exitException = ex;
				_noMoreTasks.TrySetException(_exitException);
			}
		}

        private void ResetCompletedTask(ManagedTask managedTask)
        {
          	Interlocked.Increment(ref _resetRunningCount);

	        managedTask.OnSchedule += Schedule;
            if (managedTask.Schedule())
            {
                managedTask.Reset();
                managedTask.OnTrigger += Trigger;
            }
            else
            {
                if (!_activeTasks.ContainsKey(managedTask.Reference))
                {
                    return;
                }

                if (!_activeTasks.TryRemove(managedTask.Reference, out var activeTask))
                {
                    _exitException = new ManagedTaskException(managedTask, "Failed to add the task to the active tasks list.");
	                _noMoreTasks.TrySetException(_exitException);
                }

                _completedTasks.AddOrUpdate((activeTask.Category, activeTask.CatagoryKey), activeTask, (oldKey, oldValue) => activeTask);
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

                    // if no depdent tasks are found, then the current task is ready to go.
                    if (!depFound)
                    {
                        // check dependencies are not already met is not already set, which can happen when two depdent tasks finish at the same time.
                        if (!activeTask.DepedenciesMet)
                        {
                            activeTask.DepedenciesMet = true;
                            if (activeTask.Schedule())
                            {
                                TaskHandler.Add(activeTask);
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

		private void ProgressChange(object sender, ManagedTaskProgressItem progress)
		{
			OnProgress?.Invoke(sender, progress);
		}

		public Task WhenAll()
		{
			var cancellationToken = CancellationToken.None;
			return WhenAll(cancellationToken);
		}

		public async Task WhenAll(CancellationToken cancellationToken)
		{
			var resetValue = false;
			
			while (!resetValue || (_activeTasks.Count > 0 && _resetRunningCount == 0))
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

		public IEnumerable<ManagedTask> GetActiveTasks(string category = null)
		{
			if(string.IsNullOrEmpty(category))
            {
                return _activeTasks.Values;
            }
			return _activeTasks.Values.Where(c => c.Category == category);
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

        public void Dispose()
        {
            TaskHandler.Dispose();
            OnProgress = null;
            OnStatus = null;
        }
    }
}