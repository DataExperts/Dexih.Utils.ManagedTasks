using System;
using Microsoft.Extensions.Logging;

namespace Dexih.Utils.ManagedTasks
{
    public class ManagedTaskException : Exception
    {
        public ManagedTask ManagedTask { get; protected set; }

        public ManagedTaskException()
        {
        }
        public ManagedTaskException(string message) : base(message)
        {
        }

        public ManagedTaskException(ILogger logger, string message) : base(message)
        {
            logger?.LogError(message);
        }

        public ManagedTaskException(string message, Exception innerException) : base(message, innerException)
        {
        }
        
    }

    public class ManagedTaskTriggerException: Exception
    {
        public ManagedTaskTrigger ManagedTaskTrigger { get; protected set; }

        public ManagedTaskTriggerException(ManagedTaskTrigger managedTaskTrigger)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
        public ManagedTaskTriggerException(ManagedTaskTrigger managedTaskTrigger, string message) : base(message)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
        public ManagedTaskTriggerException(ManagedTaskTrigger managedTaskTrigger, string message, Exception innerException) : base(message, innerException)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
    }

}
