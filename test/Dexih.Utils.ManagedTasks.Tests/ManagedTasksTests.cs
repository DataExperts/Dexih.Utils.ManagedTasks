﻿using dexih.utils.ManagedTasks;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace dexih.functions.tests
{
    public class DexihFunctionsManagedTasks
    {
        private readonly ITestOutputHelper _output;

        int _progressCounter = 0;
        int _completedCounter = 0;

        public DexihFunctionsManagedTasks(ITestOutputHelper output)
        {
            this._output = output;
        }

        private void PrintManagedTasksCounters(ManagedTasks managedTasks)
        {
            _output.WriteLine($"Create tasks count: {managedTasks.CreatedCount }");
            _output.WriteLine($"Queued tasks count: {managedTasks.QueuedCount }");
            _output.WriteLine($"Running tasks count: {managedTasks.RunningCount }");
            _output.WriteLine($"Scheduled tasks count: {managedTasks.ScheduledCount }");
            _output.WriteLine($"Completed tasks count: {managedTasks.CompletedCount }");
            _output.WriteLine($"Cancel tasks count: {managedTasks.CancelCount }");
            _output.WriteLine($"Error tasks count: {managedTasks.ErrorCount }");

            _output.WriteLine($"Create handler tasks count: {managedTasks.TaskHandler.CreatedCount }");
            _output.WriteLine($"Queued handler tasks count: {managedTasks.TaskHandler.QueuedCount }");
            _output.WriteLine($"Running handler tasks count: {managedTasks.TaskHandler.RunningCount }");
            _output.WriteLine($"Scheduled handler tasks count: {managedTasks.TaskHandler.ScheduledCount }");
            _output.WriteLine($"Completed handler tasks count: {managedTasks.TaskHandler.CompletedCount }");
            _output.WriteLine($"Cancel handler tasks count: {managedTasks.TaskHandler.CancelCount }");
            _output.WriteLine($"Error handler tasks count: {managedTasks.TaskHandler.ErrorCount }");

        }

        [Theory]
        [InlineData(2000)]
        public async Task ParalleManagedTaskHandlerConcurrent(int taskCount)
        {
            var handler = new ManagedTaskHandler();

            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                await Task.Delay(1);
            }

            for (int i = 0; i < taskCount; i++)
            {
                var task = new ManagedTask()
                {
                    Reference = Guid.NewGuid().ToString(),
                    CatagoryKey = 1,
                    Name = "task",
                    Category = "123",
                    Action = Action
                };
                var task1 = handler.Add(task);
            }

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await handler.WhenAll();

            Assert.Equal(taskCount, handler.CompletedCount);
        }

        [Fact]
        public async Task Test_1ManagedTask()
        {
            var managedTasks = new ManagedTasks();

            // add a series of tasks with various delays to ensure the task manager is running.
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                for (var i = 0; i <= 5; i++)
                {
                    await Task.Delay(20, cancellationToken);
                    progress.Report(i * 20, "step:" + (i*20));
                }
            }

            _progressCounter = 0;
            managedTasks.OnProgress += Progress;
            var task1 = managedTasks.Add("123", "task", "test", "object", Action, null, null);

            //check properties are set correctly.
            Assert.Equal("123", task1.OriginatorId);
            Assert.Equal("task", task1.Name);
            Assert.Equal("test", task1.Category);
            Assert.Equal("object", task1.Data);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            Assert.Equal(1, managedTasks.GetCompletedTasks().Count());

            // ensure the progress was called at least once. 
            // This doesn't get called for every progress event as when they stack up they get dropped
            // which is expected bahaviour.
            Assert.True(_progressCounter > 0);
        }

        void Progress(Object sender, ManagedTaskProgressItem progressItem)
        {
            Assert.True(progressItem.Percentage > _progressCounter);
            Assert.Equal(progressItem.StepName, "step:" + progressItem.Percentage);
            _progressCounter = progressItem.Percentage;
        }

        [Fact]
        public async Task Test_ManagedTasks_WithKeys()
        {
            var managedTasks = new ManagedTasks();

            // add a series of tasks with various delays to ensure the task manager is running.
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                for (var i = 0; i <= 5; i++)
                {
                    await Task.Delay(20, cancellationToken);
                    progress.Report(i * 20);
                }
            }

            var task1 = managedTasks.Add("123", "task", "test","category", 1, 1, "object", Action, null, null);

            //adding the same task when runnning should result in error.
            Assert.Throws(typeof(ManagedTaskException), () =>
            {
                var task2 = managedTasks.Add("123", "task", "test", "category", 1, 1, "object", Action, null, null);
            });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            // add the same task again now the previous one has finished.
            var task3 = managedTasks.Add("123", "task", "test", "category", 1, 1, "object", Action, null, null);

            Assert.Equal(1, managedTasks.GetCompletedTasks().Count());
        }



        [Theory]
        [InlineData(50)]
        [InlineData(500)]
        public async Task Test_MultipleManagedTasks(int taskCount)
        {
            var handler = new ManagedTaskHandler();
            var managedTasks = new ManagedTasks(handler);
            managedTasks.OnStatus += CompletedCounter;

            // simple task reports progress 10 times.
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(20, cancellationToken);
                    progress.Report(i * 10);
                }
            }

            _completedCounter = 0;

            // add the simple task 100 times.
            for (int i = 0; i < taskCount; i++)
            {
                var task1 = managedTasks.Add("123", "task3", "test", 0 , i, null, Action, null, null);
            }

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);


            Assert.Equal(taskCount, managedTasks.GetCompletedTasks().Count());

            // counter should eqaul the number of tasks
            Assert.Equal(taskCount, _completedCounter);
            Assert.Equal(0, managedTasks.Count());

            // check the changes history
            var changes = handler.GetTaskChanges();
            Assert.Equal(taskCount, changes.Count());
            foreach(var change in changes)
            {
                Assert.Equal(EManagedTaskStatus.Completed, change.Status);
            }
        }

        object _completedLock = 1;

        void CompletedCounter(Object sender, EManagedTaskStatus status)
        {
            var task = (ManagedTask)sender;
            //if (task.Status == EManagedTaskStatus.Success)
            lock (_completedLock)
            {
                if (status == EManagedTaskStatus.Completed)
                {
                    _completedCounter++;
                }
                if(status == EManagedTaskStatus.Error)
                {
                    ManagedTask t = (ManagedTask)sender;
                    _output.WriteLine("Error status: " + status.ToString() +". Error: " + t?.Exception?.Message);
                }
            }

        }

        int _errorCount = 0;
        [Theory]
        [InlineData(2)]
        [InlineData(500)]
        public async Task Test_ManagedTask_Error(int taskCount)
        {
            using (var managedTasks = new ManagedTasks())
            {
                var startedTaskCount = 0;

                // task throws an error.
                async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
                {
                    startedTaskCount++;
                    await Task.Run(() => throw new Exception("An error"));
                }

                // add the error task multiple times.
                _errorCount = 0;
                managedTasks.OnStatus += ErrorResult;

                for (int i = 0; i < taskCount; i++)
                {
                    managedTasks.Add("123", "task3", "test", null, Action, null, null);
                }

                var cts = new CancellationTokenSource();
                cts.CancelAfter(30000);
                await managedTasks.WhenAll(cts.Token);

                PrintManagedTasksCounters(managedTasks);

                Assert.Equal(taskCount, startedTaskCount);

                // all error counters should eqaul the number of tasks
                Assert.Equal(taskCount, managedTasks.TaskHandler.ErrorCount);
                Assert.Equal(taskCount, managedTasks.ErrorCount);
                Assert.Equal(taskCount, _errorCount);
                Assert.Equal(0, managedTasks.Count());
            }

        }

        void ErrorResult(Object sender, EManagedTaskStatus status)
        {
            if (status == EManagedTaskStatus.Error)
            {
                Assert.True(((ManagedTask)sender).Exception != null);
                Interlocked.Increment(ref _errorCount);
            }
        }


        int _cancelCounter = 0;
        [Fact]
        public async Task Test_ManagedTask_Cancel()
        {
            var managedTasks = new ManagedTasks();

            // simple task that can be cancelled
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                try
                {
                    await Task.Delay(10000, cancellationToken);
                } catch(Exception ex)
                {
                    _output.WriteLine(ex.Message);
                }
                _output.WriteLine("cancelled");
            }

            // add the simple task 500 times.
            _cancelCounter = 0;
            managedTasks.OnStatus += CancelResult;

            var tasks = new ManagedTask[100];
            for (int i = 0; i < 100; i++)
            {
                tasks[i] = managedTasks.Add("123", "task3", "test", null, Action, null, null);
            }

            for (int i = 0; i < 100; i++)
            {
                tasks[i].Cancel();
            }

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);


            // counter should eqaul the number of tasks
            Assert.Equal(100, _cancelCounter);
            Assert.Equal(0, managedTasks.Count());
        }

       void CancelResult(Object sender, EManagedTaskStatus status)
        {
            if (status == EManagedTaskStatus.Cancelled)
            {
                Interlocked.Increment(ref _cancelCounter);
            }
            if (status == EManagedTaskStatus.Error)
            {
                ManagedTask t = (ManagedTask)sender;
                _output.WriteLine("Error status: " + status.ToString() + ". Error: " + t?.Exception?.Message);
            }
        }

        [Fact]
        public async Task Test_ManagedTask_Dependencies_Chain()
        {
            var managedTasks = new ManagedTasks();

            // simple task that takes 5 seconds
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                await Task.Delay(5000, cancellationToken);
            }

            var startDate = DateTime.Now;

            // run task1, then task2, then task 3 
            var task1 = managedTasks.Add("123", "task1", "test", null, Action, null, null);
            var task2 = managedTasks.Add("123", "task2", "test", null, Action, null, new string[] { task1.Reference });
            var task3 = managedTasks.Add("123", "task3", "test", null, Action, null, new string[] { task2.Reference });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);
            
            // job should take 15 seconds.
            Assert.True(startDate.AddSeconds(15) < DateTime.Now && startDate.AddSeconds(16) > DateTime.Now);
        }

        [Fact]
        public async Task Test_ManagedTask_Dependencies_Parallel()
        {
            var managedTasks = new ManagedTasks();

            // simple task that takes 5 seconds
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                await Task.Delay(5000, cancellationToken);
            }

            var startDate = DateTime.Now;

            // run task1 & task2 parallel, then task 3 when both finish
            var task1 = managedTasks.Add("123", "task1", "test", null, Action, null, null);
            var task2 = managedTasks.Add("123", "task2", "test", null, Action, null, null);
            var task3 = managedTasks.Add("123", "task3", "test", null, Action, null, new string[] { task1.Reference, task2.Reference });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);


            // job should take about 10 seconds
            Assert.True(startDate.AddSeconds(10) < DateTime.Now && startDate.AddSeconds(11) > DateTime.Now);
        }

        [Fact]
        public async Task Test_ManagedTask_Schedule()
        {
            var managedTasks = new ManagedTasks();

            // simple task that takes 5 seconds to run
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                await Task.Delay(5000, cancellationToken);
            }

            // set a trigger 5 seconds in the future
            var trigger = new ManagedTaskSchedule()
            {
                StartDate = DateTime.Now.AddSeconds(5)
            };

            var task1 = managedTasks.Add("123", "task3", "test", null, Action, new ManagedTaskSchedule[] { trigger }, null);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            // time should be startdate + 5 second for the job to run.
            Assert.True(trigger.StartDate.Value.AddSeconds(5) < DateTime.Now);
        }

        [Fact]
        public async Task Test_ManagedTask_Schedule_Recurring()
        {
            var managedTasks = new ManagedTasks();

            var startTime = DateTime.Now.TimeOfDay;

            // simple task that takes 1 second to run
            async Task Action(ManagedTaskProgress progress, CancellationToken cancellationToken)
            {
                _output.WriteLine("task started - " + DateTime.Now.TimeOfDay.Subtract(startTime).ToString());
                await Task.Delay(1000, cancellationToken);
            }

            // starts in 1 second, then runs 1 second job
            var trigger = new ManagedTaskSchedule()
            {
                StartDate = DateTime.Now,
                StartTime = DateTime.Now.AddSeconds(1).TimeOfDay,
                IntervalTime = TimeSpan.FromSeconds(2),
                MaxRecurrs = 5
            };

            var task1 = managedTasks.Add("123", "task3", "test", null, Action, new ManagedTaskSchedule[] { trigger }, null);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            Assert.Equal(5, managedTasks.CompletedCount);

            // 10 seconds = Initial 1 + 2 *(5-1) recurrs + 1 final job
            Assert.True(trigger.StartDate.Value.AddSeconds(10) < DateTime.Now);
        }
    }
}