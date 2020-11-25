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

        public virtual void Cancel()
        {
        }

        public virtual void Schedule(DateTimeOffset startsAt, CancellationToken cancellationToken = default)
        {
        }

        public virtual object Data { get; set; } = null;
    }
}