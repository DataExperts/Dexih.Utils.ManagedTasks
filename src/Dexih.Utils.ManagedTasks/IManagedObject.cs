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
        Task Start(ManagedTaskProgress progress, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Action to take when task is cancelled.
        /// </summary>
        void Cancel();
        
        /// <summary>
        /// Action to take when task is scheduled.
        /// </summary>
        /// <returns></returns>
        Task Schedule(DateTime startsAt, CancellationToken cancellationToken = default);
    }
}