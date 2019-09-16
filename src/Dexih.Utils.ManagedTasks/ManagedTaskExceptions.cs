using System;

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
        public ManagedTaskException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class ManagedTaskTriggerException: Exception
    {
        public ManagedTaskSchedule ManagedTaskTrigger { get; protected set; }

        public ManagedTaskTriggerException(ManagedTaskSchedule managedTaskTrigger)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
        public ManagedTaskTriggerException(ManagedTaskSchedule managedTaskTrigger, string message) : base(message)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
        public ManagedTaskTriggerException(ManagedTaskSchedule managedTaskTrigger, string message, Exception innerException) : base(message, innerException)
        {
            ManagedTaskTrigger = managedTaskTrigger;
        }
    }

}
