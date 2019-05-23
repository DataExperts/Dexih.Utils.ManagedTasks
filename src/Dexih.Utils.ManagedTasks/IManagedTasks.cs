using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
public interface IManagedTasks
	{
		event EventHandler<EManagedTaskStatus> OnStatus;
		event EventHandler<ManagedTaskProgressItem> OnProgress;
		long CreatedCount { get; }
		long ScheduledCount { get; }
		long FileWatchCount { get; }
		long QueuedCount { get; }
		long RunningCount { get; }
		long CompletedCount { get; }
		long ErrorCount { get; }
		long CancelCount { get; }
		ManagedTask Add(ManagedTask managedTask);
		ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers);
		ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers);
		ManagedTask Add(string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action);
		ManagedTask Add(string originatorId, string name, string category, long hubKey, string remoteAgentId, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers);
		ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher);
		ManagedTask Add(string reference, string originatorId, string name, string category, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action);

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
		ManagedTask Add(string reference, string originatorId, string name, string category, long referenceKey, string referenceId, long categoryKey, object data, Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> action, IEnumerable<ManagedTaskSchedule> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);

		Task WhenAll(CancellationToken cancellationToken = default);
		IEnumerator<ManagedTask> GetEnumerator();
		ManagedTask GetTask(string reference);
		ManagedTask GetTask(string category, long categoryKey);
		IEnumerable<ManagedTask> GetActiveTasks(string category = null);
		IEnumerable<ManagedTask> GetRunningTasks(string category = null);
		IEnumerable<ManagedTask> GetScheduledTasks(string category = null);
		IEnumerable<ManagedTask> GetCompletedTasks(string category = null);
		void Cancel(IEnumerable<string> references);
		IEnumerable<ManagedTask> GetTaskChanges(bool resetTaskChanges = false);
		int TaskChangesCount();
		void Dispose();
	}
}