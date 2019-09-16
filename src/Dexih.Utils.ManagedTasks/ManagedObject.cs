using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dexih.Utils.ManagedTasks
{
    
    /// <summary>
    /// Basic managed object that only requires the start to be implemented
    /// </summary>
    public abstract class ManagedObject: IManagedObject
    {
        public virtual void Dispose()
        {
        }

        public abstract Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default);

        public void Cancel()
        {
        }

        public void Schedule(DateTime startsAt, CancellationToken cancellationToken = default)
        {
        }

        public abstract object Data { get; set; }
    }
}