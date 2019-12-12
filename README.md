# Dexih.Utils.ManagedTasks

[build]:    https://ci.appveyor.com/project/dataexperts/dexih-utils-managedtasks
[build-img]: https://ci.appveyor.com/api/projects/status/txclrfnvgcgvomj7?svg=true
[nuget]:     https://www.nuget.org/packages/Dexih.Utils.ManagedTasks
[nuget-img]: https://badge.fury.io/nu/Dexih.Utils.ManagedTasks.svg
[nuget-name]: Dexih.Utils.ManagedTasks
[dex-img]: http://dataexpertsgroup.com/assets/img/dex_web_logo.png
[dex]: https://dataexpertsgroup.com

[![][dex-img]][dex]
[![Build status][build-img]][build] [![Nuget][nuget-img]][nuget]

The `Managed` library is for scheduling and executing multiple tasks within a .net core application.

The primary benefits:

 * Can run large number of simultaneous tasks.
 * Allows tasks to provide progress.
 * Allows tasks to be cancelled.
 * Allows tasks to be schedule once off, and on a recurring schedule.
 * Allows tasks to be started based on a file watcher event.
 * Start a task when one or more tasks complete.
 * Get a tasks status snapshot

---

## Installation

Add the [latest version][nuget] of the package "Dexih.Utils.ManagedTasks" to a .net core/.net project.  This requires .net standard framework 2.0 or newer, or the .net framework 4.6 or newer.

---

## Getting started

To initialize the scheduler simply create an instance of the `ManagedTasks` class, and connect the `OnProgress` and `OnStatus` events.

```csharp
var managedTasks = new ManagedTasks();
managedTasks.OnProgress += Progress;
managedTasks.OnStatus += Status;

void Progress(ManagedTask managedTask, ManagedTaskProgressItem progressItem)
{
    // progress tasks
}

void Status(ManagedTask managedTask, EManagedTaskStatus status)
{
    // status
}
```

The `OnProgress` event will be called whenever a task reports a progress event.
The `OnStatus` event will be called whenever a task reports a progress event.  The events which trigger this are Created, FileWatching, Scheduled, Queued, Running, Cancelled, Error, Completed.

Tasks need to be wrapped in a task derived from the `ManagedObject` class or `IManagedObject` interface.  

```csharp
// task counts to 5 in 5 seconds.
public class CountTask : ManagedObject
{
    public override async Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(1000, cancellationToken);
            var percent = (i+1) * 5;

            // report progress every second
            progress.Report( $"step: {i+1}");
        }
    }
}
```

To run the task, use the `Add` function to added it to the `ManagedTasks` instance.

```csharp
var countTask = new CountTask();

Console.WriteLine("Run one task.");
managedTasks.Add(new ManagedTask()
{
    Reference = Guid.NewGuid().ToString(),
    Name = "count to 5",
    ManagedObject = countTask
});
```

To wait for all the tasks to complete, use the `WhenAll()` function.

```csharp
await managedTasks.WhenAll();
```

## Scheduling Tasks

Tasks can be scheduled by adding triggers to the `ManagedTask`.

A simple trigger to schedule once off can be created.

```csharp
// trigger will be started 5 seconds from now.
var trigger = new ManagedTaskTrigger(DateTime.Now.AddSeconds(5);
```

Another simple trigger can be created to run a job multiple times based on an interval.

```csharp
// trigger will execute every 5 seconds, a maximum of 10 times.
var trigger = new ManagedTaskTrigger(TimeSpan.FromSeconds(5), 10)
```

To use a trigger, add it to the `Triggers` property of the `ManagedTask`.

```csharp
var managedTask = new ManagedTask()
{
    Reference = Guid.NewGuid().ToString(),
    Name = "count to 5",
    ManagedObject = countTask,
    Triggers = new [] { new ManagedTaskTrigger(TimeSpan.FromSeconds(6), 2)}
};
```

When multiple triggers are added to the `Triggers` property, the job will run on the next trigger of any of the triggers.

Advanced triggers can be created by setting the following properties within the `ManagedTaskTrigger`.

|Property|Description|
|-|-|
|StartDate|The earliest date to start the task.  Note, the time component is ignored, use the StartTime.|
|EndDate|The last date to start the task.  Note, the time component is ignored, use the StartTime.|
|StartTime|The earliest time of the day the job will start.  If null, the starttime will be default to the next second.
|EndTime|The latest time of the day the job will start.
|MaxRecurs|The maximum number of time to recur in an interval.
|IntervalType|The type of interval.  Once = no interval, Interval = Run every `IntervalTime`, Daily = Run every day, Monthly = Run every month.
|IntervalTime|If the `IntervalType` is set to `Interval`, the time between intervals.  Note, the interval time is based on the starting time of the previous job.  For example if the interval time is 60 seconds, the job will run every 60 seconds, irregardless of the time the job takes to execute.
|DaysOfWeek|Array containing the days of the week to run the job.  If empty/null the job will run on any day of week.
|DaysOfMonth|Array containing the days of the month to run the job.  If empty/null the job will run on any day of month.
|WeeksOfMonth|Array containing the weeks of the month to run the job.  If empty/null the job will run on any week of the month
|SkipDates|Array containing specific dates (such as public holidays) to skip.

## File Watching

Tasks can be triggered based on the creation of a file.

Use the `ManagedTaskFileWatcher` class to create file watching trigger based on a path and with a file pattern.

```csharp
var fileWatch = new ManagedTaskFileWatcher('/data/', "trigger_file_*");

var managedTask = new ManagedTask()
{
    Reference = Guid.NewGuid().ToString(),
    Name = "start on file",
    ManagedObject = countTask,
    FileWatchers = new [] { fileWatch }
};
```
## Task Concurrency

Tasks that have the same `Category` and `CategoryKey` are grouped for the purposes of concurrency.

If a task if part of the same category is added `ConcurrentTaskAction` determines the concurrency:
* Parallel - Runs the tasks in parallel based on their schedule.
* Sequence - Runs the tasks of the same category in sequence.
* Abend - Throws an exception when tasks of the same category are run simultaneously.

The following sample will run the two tasks in sequence:

```csharp
var managedTasks = new ManagedTasks();

var task1 = new ManagedTask
{
    Reference = Guid.NewGuid().ToString(),
    Name = "test",
    Category = "category",
    CategoryKey = 1,
    ManagedObject =  new ProgressTask(20, 1),
    ConcurrentTaskAction = EConcurrentTaskAction.Sequence
};

var task2 = new ManagedTask
{
    Reference = Guid.NewGuid().ToString(),
    Name = "test",
    Category = "category",
    CategoryKey = 1,
    ManagedObject =  new ProgressTask(0, 1),
    ConcurrentTaskAction = EConcurrentTaskAction.Sequence
};

managedTasks.Add(task1);
managedTasks.Add(task2);

await managedTasks.WhenAll();
```

## Task Dependencies

Tasks can be executed based on the outcome of another task or tasks.

In order to set dependent tasks on a task use the `DependentReferences` property to specify the `Reference` of the dependent tasks.

The following example will run task1 until completion, and then task2.

```csharp
var managedTasks = new ManagedTasks();

managedTasks.Add(new ManagedTask()
{
    Reference = "ref1",
    Name = "task1",
    ManagedObject = countTask
});

managedTasks.Add(new ManagedTask()
{
    Reference = "ref2",
    Name = "task2",
    ManagedObject = countTask,
    DependentReferences = new []{"ref1"}
});

await managedTasks.WhenAll();
```

## Task / Scheduler Communication

The `ManagedObject` class is used to wrap a task.  The following method can be overrided in this class to communicate between the task and the scheduler:

|Method|Description|
|-|-|
|StartAsync|(Mandatory) Executed when the task is started|
|Schedule|Executed when a task is scheduled|
|Cancel|Called when a cancel request is made for the class through the ManagedTask.Cancel() or ManagedTasks.Cancel() functions.|
|Dispose|Called when a task is finished|
|Data|Any object data that is automatically mapped to the `ManagedTask` object.|

The following skeleton shows how to construct a managed object.
```csharp
// task counts to 5 in 5 seconds.
public class CustomTask : ManagedObject
{
    public override async Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
    {
        // run task actions

        // use progress to send progress updates
        progress.Report("progress details");
    }

    public override void Cancel()
    {
        // cancel actions
    }

    public override void Dispose()
    {
        // dispose actions
    }

    public override void Schedule(DateTime startsAt, CancellationToken cancellationToken = default)
    {
        // schedule actions
    }
}
```

## Task Management

The 