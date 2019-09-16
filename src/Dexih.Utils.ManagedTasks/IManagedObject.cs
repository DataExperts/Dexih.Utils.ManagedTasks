using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
    public interface IManagedObject: IDisposable
    {
        /// <summary>
        /// Action to take when task is started.
        /// </summary>
        /// <returns></returns>
        Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Action to take when task is cancelled.
        /// </summary>
        void Cancel();
        
        /// <summary>
        /// Action to take when task is scheduled.
        /// </summary>
        /// <returns></returns>
        void Schedule(DateTime startsAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Data object which can be used to expose any data the task needs to provide to other processes.
        /// </summary>
        object Data { get; set; }
    }
}