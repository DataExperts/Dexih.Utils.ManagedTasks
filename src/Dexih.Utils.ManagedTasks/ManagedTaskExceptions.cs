using System;
using System.Collections.Generic;
using System.Text;

namespace dexih.utils.ManagedTasks
{
    public class ManagedTaskException : Exception
    {
        public ManagedTask ManagedTask { get; protected set; }

        public ManagedTaskException(ManagedTask managedTask)
        {
        }
        public ManagedTaskException(ManagedTask managedTask, string message) : base(message)
        {
        }
        public ManagedTaskException(ManagedTask managedTask, string message, Exception innerException) : base(message, innerException)
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
