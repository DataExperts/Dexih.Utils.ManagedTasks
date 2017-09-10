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
	/// Simple collection of managed tasks
	/// </summary>
	public class ManagedTasks : IEnumerable<ManagedTask>, IDisposable
	{
		public event EventHandler<EManagedTaskStatus> OnStatus;
		public event EventHandler<int> OnProgress;

        public long CreatedCount { get; set; } = 0;
        public long ScheduledCount { get; set; } = 0;
        public long QueuedCount { get; set; } = 0;
        public long RunningCount { get; set; } = 0;
        public long CompletedCount { get; set; } = 0;
        public long ErrorCount { get; set; } = 0;
        public long CancelCount { get; set; } = 0;

        public object _incrementLock = 1; //used to lock the increment counters, to avoid race conditions.

        private readonly ConcurrentDictionary<string , ManagedTask> _activeTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _activeCategoryTasks;
		private readonly ConcurrentDictionary<(string category, long categoryKey), ManagedTask> _completedTasks;

        private object _updateTasksLock = 1; // used to lock when updaging task queues.
		private Exception _exitException; //used to push exceptions to the WhenAny function.
		private AutoResetEvent _resetWhenNoTasks; //event handler that triggers when all tasks completed.

        private int _resetRunningCount = 0;

		public ManagedTaskHandler TaskHandler { get; protected set; }

		public ManagedTasks(ManagedTaskHandler taskHandler = null)
		{
			if (taskHandler == null)
			{
				TaskHandler = new ManagedTaskHandler();
			}
			else
			{
				TaskHandler = taskHandler;
			}

            TaskHandler.OnStatus += StatusChange;
            TaskHandler.OnProgress += ProgressChange;

            _activeTasks = new ConcurrentDictionary<string, ManagedTask>();
			_activeCategoryTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
			_completedTasks = new ConcurrentDictionary<(string, long), ManagedTask>();
			_resetWhenNoTasks = new AutoResetEvent(false);
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
			if ((managedTask.Triggers == null || managedTask.Triggers.Count() == 0) && (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0))
			{
				TaskHandler.Add(managedTask);
			}
			else
			{
				if (managedTask.Schedule())
				{
					managedTask.OnTrigger += Trigger;
				}
				else
				{
					if (managedTask.DependentReferences == null || managedTask.DependentReferences.Length == 0)
					{
						managedTask.Error("None of the triggers returned a future schedule time.", null);
					}
				}
			}

			return managedTask;
		}

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences)
        {
            return Add(originatorId, name, category, 0, 0, data, action, triggers, dependentReferences);
        }

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers)
        {
            return Add(originatorId, name, category, 0, 0, data, action, triggers, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action)
        {
            return Add(originatorId, name, category, 0, 0, data, action, null, null);
        }

        public ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences)
		{
			var reference = Guid.NewGuid().ToString();
			return Add(reference, originatorId, name, category, hubKey, categoryKey, data, action, triggers, dependentReferences);
		}

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, triggers, dependentReferences);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, triggers, null);
        }

        public ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<IProgress<int>, CancellationToken, Task> action)
        {
            return Add(reference, originatorId, name, category, 0, 0, data, action, null, null);
        }

        /// <summary>
        /// Creates & starts a new managed task.
        /// </summary>
        /// <param name="originatorId">Id that can be used to referernce where the task was started from.</param>
        /// <param name="title">Short description of the task.</param>
        /// <param name="action">The action </param>
        /// <returns></returns>
        public ManagedTask Add(string reference, string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<IProgress<int>, CancellationToken, Task> action, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences)
		{
			var managedTask = new ManagedTask()
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

		private void StatusChange(object sender, EManagedTaskStatus newStatus)
		{
			try
			{
				var managedTask = (ManagedTask)sender;

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
                if (newStatus == EManagedTaskStatus.Completed || newStatus == EManagedTaskStatus.Cancelled || newStatus == EManagedTaskStatus.Error)
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

        private void ResetCompletedTask(ManagedTask managedTask)
        {
            _resetRunningCount++;

            if (managedTask.Schedule())
            {
                managedTask.Reset();
                //_taskHandler.Add(managedTask);
            }
            else
            {
                if (!_activeTasks.ContainsKey(managedTask.Reference))
                {
                    return;
                }

                if (!_activeTasks.TryRemove(managedTask.Reference, out ManagedTask activeTask))
                {
                    _exitException = new ManagedTaskException(managedTask, "Failed to add the task to the active tasks list.");
                    _resetWhenNoTasks.Set();
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

            _resetRunningCount--;

			if (_activeTasks.Count == 0 && _resetRunningCount == 0)
			{
				_resetWhenNoTasks.Set();
			}
		}

		public void ProgressChange(object sender, int percentage)
		{
			OnProgress?.Invoke(sender, percentage);
		}

		public Task WhenAll()
		{
			CancellationToken cancellationToken = CancellationToken.None;
			return WhenAll(cancellationToken);
		}


		public async Task WhenAll(CancellationToken cancellationToken)
		{
			while (_activeTasks.Count > 0 && _resetRunningCount == 0)
			{
				var noTasks = Task.Run(() => _resetWhenNoTasks.WaitOne());
				await Task.WhenAny(noTasks, Task.Delay(-1, cancellationToken));

				if (_exitException != null)
				{
					throw _exitException;
				}

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
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
            else
            {
                return _activeTasks.Values.Where(c => c.Category == category);
            }
        }

		public IEnumerable<ManagedTask> GetCompletedTasks(string category = null)
		{
            if (string.IsNullOrEmpty(category))
            {
                return _completedTasks.Values;
            }
            else
            {
                return _completedTasks.Values.Where(c => c.Category == category);
            }
        }

		public void Cancel(string[] references)
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
            TaskHandler.WhenAll().Wait();
            TaskHandler.Dispose();
            OnProgress = null;
            OnStatus = null;
        }
    }
}