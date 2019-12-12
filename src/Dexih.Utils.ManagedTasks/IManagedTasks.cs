using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
public interface IManagedTasks
	{
		event ManagedTasks.Status OnStatus;
		event ManagedTasks.Progress OnProgress;

		long CreatedCount { get; }
		long ScheduledCount { get; }
		long FileWatchCount { get; }
		long QueuedCount { get; }
		long RunningCount { get; }
		long CompletedCount { get; }
		long ErrorCount { get; }
		long CancelCount { get; }
		ManagedTask Add(ManagedTask managedTask);
		ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers);
		ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers);
		ManagedTask Add(string originatorId, string name, string category, IManagedObject managedObject);
		ManagedTask Add(string originatorId, string name, string category, long hubKey, string remoteAgentId, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences);
		ManagedTask Add(string originatorId, string name, string category, long hubKey, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers);
		ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher, string[] dependentReferences);
		ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatcher);
		ManagedTask Add(string reference, string originatorId, string name, string category, IManagedObject managedObject);

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
		ManagedTask Add(string reference, string originatorId, string name, string category, long referenceKey, string referenceId, long categoryKey, IManagedObject managedObject, IEnumerable<ManagedTaskTrigger> triggers, IEnumerable<ManagedTaskFileWatcher> fileWatchers, string[] dependentReferences);

		/// <summary>
		/// Waits for all tasks to complete.
		/// </summary>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		Task WhenAll(CancellationToken cancellationToken = default);
		IEnumerator<ManagedTask> GetEnumerator();
		ManagedTask GetTask(string reference);
		ManagedTask GetTask(string category, long categoryKey);
		IEnumerable<ManagedTask> GetActiveTasks(string category = "");
		IEnumerable<ManagedTask> GetRunningTasks(string category = "");
		IEnumerable<ManagedTask> GetScheduledTasks(string category = "");
		IEnumerable<ManagedTask> GetCompletedTasks(string category = "");
		Task CancelAsync(IEnumerable<string> references);
		IEnumerable<ManagedTask> GetTaskChanges(bool resetTaskChanges = false);
		int TaskChangesCount();
		void Dispose();
	}
}