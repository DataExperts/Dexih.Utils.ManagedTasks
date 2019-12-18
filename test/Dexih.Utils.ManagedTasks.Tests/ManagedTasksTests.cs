using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dexih.Utils.ManagedTasks;
using Xunit;
using Xunit.Abstractions;

namespace Dexih.Utils.Managed.Tasks.Tests
{
    public class DexihFunctionsManagedTasks
    {
        private readonly ITestOutputHelper _output;

        int _progressCounter = 0;
        

        public DexihFunctionsManagedTasks(ITestOutputHelper output)
        {
            this._output = output;
        }

        private void PrintManagedTasksCounters(ManagedTasks.ManagedTasks managedTasks)
        {
            _output.WriteLine($"Create tasks count: {managedTasks.CreatedCount }");
            _output.WriteLine($"Queued tasks count: {managedTasks.QueuedCount }");
            _output.WriteLine($"Running tasks count: {managedTasks.RunningCount }");
            _output.WriteLine($"Scheduled tasks count: {managedTasks.ScheduledCount }");
            _output.WriteLine($"Completed tasks count: {managedTasks.CompletedCount }");
            _output.WriteLine($"Cancel tasks count: {managedTasks.CancelCount }");
            _output.WriteLine($"Error tasks count: {managedTasks.ErrorCount }");
        }


        [Theory]
        [InlineData(2000)]
        public async Task ParallelManagedTaskHandlerConcurrentAsync(int taskCount)
        {
            var managedTasks = new ManagedTasks.ManagedTasks();


            var oneSecondTask = new ProgressTask(1, 1);

            for (var i = 0; i < taskCount; i++)
            {
                var task = new ManagedTask()
                {
                    TaskId = Guid.NewGuid().ToString(),
                    CategoryKey = i,
                    Name = "task",
                    Category = "123",
                    ManagedObject = oneSecondTask
                };
                managedTasks.Add(task);
            }

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            PrintManagedTasksCounters(managedTasks);
            
            Assert.Equal(taskCount, managedTasks.CompletedCount);
        }
        


        [Fact]
        public async Task Test_1ManagedTaskAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();


            _progressCounter = 0;
            managedTasks.OnProgress += Progress;
            
            var progressTask = new ProgressTask(20, 5);
            
            var task1 = managedTasks.Add("123", "task", "test", progressTask, null);

            //check properties are set correctly.
            Assert.Equal("123", task1.OriginatorId);
            Assert.Equal("task", task1.Name);
            Assert.Equal("test", task1.Category);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            Assert.Single(managedTasks.GetCompletedTasks());

            // ensure the progress was called at least once. 
            // This doesn't get called for every progress event as when they stack up they get dropped
            // which is expected bahaviour.
            Assert.True(_progressCounter > 0);
        }

        void Progress(object sender, ManagedTaskProgressItem progressItem)
        {
            Assert.True(progressItem.Percentage > _progressCounter);
            Assert.Equal(progressItem.StepName, "step: " + progressItem.Percentage);
            _progressCounter = progressItem.Percentage;
        }

        [Fact]
        public async Task Test_ManagedTask_BadTrigger()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();
            
            var trigger = new ManagedTaskTrigger(TimeSpan.Zero, 5);
            var triggers = new[] {trigger};
            var progressTask = new ProgressTask(200 ,5);
            Assert.Throws<ManagedTaskTriggerException>(() =>
            {
                var managedTask = managedTasks.Add("123", "task", "test", "category", 1, "id", 1, progressTask,
                    triggers, null, null);
            });

            Assert.Empty(managedTasks.GetActiveTasks());
        }

        [Fact]
        public async Task Test_ManagedTasks_WithKeysAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();

            var progressTask = new ProgressTask(200 ,5);

            managedTasks.Add("123", "task", "test","category", 1, "id", 1, progressTask, null, null, null);

            //adding the same task when running should result in error.
            Assert.Throws<ManagedTaskException>(() =>
            {
                managedTasks.Add("123", "task", "test", "category", 1, "id", 1, progressTask, null, null, null);
            });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            // add the same task again now the previous one has finished.
            managedTasks.Add("123", "task", "test", "category", 1, "id", 1, progressTask, null, null, null);

            Assert.Single(managedTasks.GetCompletedTasks());
        }
        

        /// <summary>
        /// Adds two tasks using the sequence action, and checks parallel, and sequential running.
        /// </summary>
        /// <returns></returns>
        [Theory]
        [InlineData(EConcurrentTaskAction.Sequence)]
        [InlineData(EConcurrentTaskAction.Parallel)]
        public async Task Test_Add_SameTask_ActionsAsync(EConcurrentTaskAction concurrentTaskAction)
        {
            var managedTasks = new ManagedTasks.ManagedTasks();
            
            var task1 = new ManagedTask
            {
                TaskId = Guid.NewGuid().ToString(),
                OriginatorId = "task",
                Name = "test",
                Category = "category",
                CategoryKey = 1,
                ReferenceKey = 1,
                ReferenceId = "id",
                ManagedObject =  new ProgressTask(20, 1),
                Triggers = null,
                FileWatchers = null,
                DependentTaskIds = null,
                ConcurrentTaskAction = EConcurrentTaskAction.Sequence
            };
            
            var task2 = new ManagedTask
            {
                TaskId = Guid.NewGuid().ToString(),
                OriginatorId = "task",
                Name = "test",
                Category = "category",
                CategoryKey = 1,
                ReferenceKey = 2,
                ReferenceId = "id",
                ManagedObject =  new ProgressTask(0, 1),
                Triggers = null,
                FileWatchers = null,
                DependentTaskIds = null,
                ConcurrentTaskAction = concurrentTaskAction
            };

            managedTasks.Add(task1);
            managedTasks.Add(task2);

            await managedTasks.WhenAll();

            if (concurrentTaskAction == EConcurrentTaskAction.Parallel)
            {
                Assert.True(task1.EndTime > task2.EndTime);                
            }
            else
            {
                Assert.True(task1.EndTime < task2.EndTime);
            }
            
            Assert.Equal(2, managedTasks.ErrorCount + managedTasks.CompletedCount);

        }



        [Theory]
        [InlineData(50)]
        [InlineData(500)]
        public async Task Test_MultipleManagedTasksAsync(int taskCount)
        {
            var completedCounter = 0;
            var runningCounter = 0;

            void CompletedCounter(object sender, EManagedTaskStatus status)
            {
                switch (status)
                {
                    case EManagedTaskStatus.Running:
                        Interlocked.Increment(ref runningCounter);
                        break;
                    case EManagedTaskStatus.Error:
                        var t = (ManagedTask)sender;
                        _output.WriteLine("Error status: " + status +". Error: " + t?.Exception?.Message);
                        break;
                    case EManagedTaskStatus.Completed:
                        Interlocked.Increment(ref completedCounter);
                        if (completedCounter == taskCount)
                        {
                            _output.WriteLine("complete success");
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(status), status, null);
                }
            }
            
            var managedTasks = new ManagedTasks.ManagedTasks();
            managedTasks.OnStatus += CompletedCounter;

            var managedObject = new ProgressTask(20, 10); 

            completedCounter = 0;

            // add the simple task 100 times.
            for (var i = 0; i < taskCount; i++)
            {
                managedTasks.Add("123", "task3", "test", 0 , "id", i, managedObject, null, null);
            }

            // use cancellation token to ensure test doesn't get stuck forever.
            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            Assert.Equal(taskCount, managedTasks.GetCompletedTasks().Count());
            Assert.Equal(taskCount, managedTasks.CompletedCount);

            // counter should equal the number of tasks8
            _output.WriteLine($"runningCounter: {runningCounter}, completedCounter: {completedCounter}");
            
            Assert.Equal(taskCount, runningCounter);
            Assert.Equal(taskCount, completedCounter);
            Assert.Empty(managedTasks);
            Assert.Empty(managedTasks.GetActiveTasks());
            Assert.Empty(managedTasks.GetRunningTasks());

            // check the changes history
            var changes = managedTasks.GetTaskChanges().ToArray();
            Assert.Equal(taskCount, changes.Count());
            foreach(var change in changes)
            {
                Assert.Equal(EManagedTaskStatus.Completed, change.Status);
            }
        }

        private class ErrorTask : ManagedObject
        {
            public override Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
            {
                throw new Exception("An error");
            }

            public override object Data { get; set; }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(500)]
        public async Task Test_ManagedTask_ErrorAsync(int taskCount)
        {
            using (var managedTasks = new ManagedTasks.ManagedTasks())
            {
                var errorCount = 0;

                var managedTask = new ErrorTask();
                
                void ErrorResult(ManagedTask task, EManagedTaskStatus status)
                {
                    if (status == EManagedTaskStatus.Error)
                    {
                        Assert.True(task.Exception != null);
                        Interlocked.Increment(ref errorCount);
                    }
                }

                // add the error task multiple times.
                errorCount = 0;
                managedTasks.OnStatus += ErrorResult;

                for (var i = 0; i < taskCount; i++)
                {
                    managedTasks.Add("123", "task3", "test", managedTask, null);
                }

                var cts = new CancellationTokenSource();
                cts.CancelAfter(30000);
                await managedTasks.WhenAll(cts.Token);

                PrintManagedTasksCounters(managedTasks);
                
                _output.WriteLine($"Error count {errorCount}, error count in tasks {managedTasks.ErrorCount}");
                // all error counters should equal the number of tasks
                Assert.Equal(taskCount, managedTasks.ErrorCount);
                Assert.Equal(taskCount, errorCount);
                Assert.Empty(managedTasks);
            }
        }

        
        int _cancelCounter = 0;
        [Fact]
        public async Task Test_ManagedTask_CancelAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();

            // simple task that can be cancelled
            var managedObject = new ProgressTask(10000, 1);

            // add the simple task 500 times.
            _cancelCounter = 0;
            managedTasks.OnStatus += CancelResult;

            var tasks = new ManagedTask[100];
            for (var i = 0; i < 100; i++)
            {
                tasks[i] = managedTasks.Add("123", "task3", "test", managedObject, null);
            }

            for (var i = 0; i < 100; i++)
            {
                await tasks[i].CancelAsync();
            }

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);


            // counter should equal the number of tasks
            Assert.Equal(100, _cancelCounter);
            Assert.Empty(managedTasks);
        }

       void CancelResult(object sender, EManagedTaskStatus status)
        {
            if (status == EManagedTaskStatus.Cancelled)
            {
                Interlocked.Increment(ref _cancelCounter);
            }
            if (status == EManagedTaskStatus.Error)
            {
                var t = (ManagedTask)sender;
                _output.WriteLine("Error status: " + status + ". Error: " + t?.Exception?.Message);
            }
        }

        [Fact]
        public async Task Test_ManagedTask_Dependencies_ChainAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();

            // simple task that takes 5 seconds
            var managedObject = new ProgressTask(5000, 1);

            var timer = Stopwatch.StartNew();

            // run task1, then task2, then task 3 
            var task1 = managedTasks.Add("123", "task1", "test", managedObject, null);
            var task2 = managedTasks.Add("123", "task2", "test", managedObject, null, new[] { task1.TaskId });
            var task3 = managedTasks.Add("123", "task3", "test", managedObject, null, new[] { task2.TaskId });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);
            
            // job should take 15 seconds.
            Assert.True(timer.Elapsed.Seconds >= 15 && timer.Elapsed.Seconds <= 16, $"Took {timer.Elapsed.Seconds} should take 15 seconds.");
        }

        [Fact]
        public async Task Test_ManagedTask_Dependencies_ParallelAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();

            // simple task that takes 5 seconds
            var managedObject = new ProgressTask(5000, 1);

            var timer = Stopwatch.StartNew();

            // run task1 & task2 parallel, then task 3 when both finish
            var task1 = managedTasks.Add("123", "task1", "test", managedObject, null);
            var task2 = managedTasks.Add("123", "task2", "test", managedObject, null);
            var task3 = managedTasks.Add("123", "task3", "test", managedObject, null, new[] { task1.TaskId, task2.TaskId });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);


            // job should take 10 seconds.
            Assert.True(timer.Elapsed.Seconds >= 10 && timer.Elapsed.Seconds <= 11, $"Took {timer.Elapsed.Seconds} should take 10 seconds.");
        }

        [Fact]
        public async Task Test_ManagedTask_ScheduleAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();

            // simple task that takes 5 seconds
            var managedObject = new ProgressTask(5000, 1);
            
            var currentDate = DateTime.Now;

            // set a trigger 5 seconds in the future
            var trigger = new ManagedTaskTrigger()
            {
                StartDate = currentDate,
                StartTime = currentDate.AddSeconds(5).TimeOfDay,
                IntervalType = EIntervalType.Once
            };
            
            _output.WriteLine($"Time to Start: {trigger.NextOccurrence(DateTime.Now)-DateTime.Now}");

            var task1 = managedTasks.Add("123", "task3", "test", managedObject, new[] { trigger });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            // time should be startDate + 5 second for the job to run.
            Assert.True(trigger.StartDate.Value.AddSeconds(5) < DateTime.Now);
        }

        [Fact]
        public async Task Test_ManagedTask_Schedule_Error_No_IntervalTime()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();
            
            var startTime = DateTime.Now.TimeOfDay;

            // simple task that takes 1 second to run
            var managedObject = new ProgressTask(1000, 1);
            
            var scheduleCount = 0;
            
            void OnSchedule(object sender, EManagedTaskStatus status)
            {
                if (status == EManagedTaskStatus.Scheduled)
                {
                    Interlocked.Increment(ref scheduleCount);
                }
            }

            managedTasks.OnStatus += OnSchedule;

            // starts in 1 second, then runs 1 second job
            var trigger = new ManagedTaskTrigger()
            {
                StartDate = DateTime.Now,
                StartTime = DateTime.Now.AddSeconds(1).TimeOfDay,
                IntervalTime = TimeSpan.FromSeconds(0),
                MaxRecurs = null,
                IntervalType = EIntervalType.Interval
            };

            managedTasks.Add("123", "task3", "test", managedObject, new[] { trigger });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);

            await Assert.ThrowsAsync<ManagedTaskTriggerException>(async () => await managedTasks.WhenAll(cts.Token));
        }
        
        [Fact]
        public async Task Test_ManagedTask_Schedule_RecurringAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();
            
            var startTime = DateTime.Now.TimeOfDay;

            // simple task that takes 1 second to run
            var managedObject = new ProgressTask(1000, 1);
            
            var scheduleCount = 0;
            
            void OnSchedule(object sender, EManagedTaskStatus status)
            {
                if (status == EManagedTaskStatus.Scheduled)
                {
                    Interlocked.Increment(ref scheduleCount);
                }
            }

            managedTasks.OnStatus += OnSchedule;

            // starts in 1 second, then runs 1 second job
            var trigger = new ManagedTaskTrigger()
            {
                StartDate = DateTime.Now,
                StartTime = DateTime.Now.AddSeconds(1).TimeOfDay,
                IntervalTime = TimeSpan.FromSeconds(2),
                MaxRecurs = 5
            };

            managedTasks.Add("123", "task3", "test", managedObject, new[] { trigger });

            var cts = new CancellationTokenSource();
            cts.CancelAfter(30000);
            await managedTasks.WhenAll(cts.Token);

            Assert.Equal(5, scheduleCount);

            Assert.Equal(5, managedTasks.ScheduledCount);
            Assert.Equal(5, managedTasks.CompletedCount);

            // 10 seconds = Initial 1 + 2 *(5-1) recurs + 1 final job
            Assert.True(trigger.StartDate.Value.AddSeconds(10) <= DateTime.Now);
        }

        
        [Fact]
        // test a scheduled task is added to the queue, and removed when cancelled.
        public async Task Test_ManagedTask_Schedule_Recurring_CancelAsync()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();
            
            // simple task that takes 1 second to run
            var managedObject = new ProgressTask(1000, 1);
            
            var scheduleCount = 0;
            
            void OnSchedule(object sender, EManagedTaskStatus status)
            {
                if (status == EManagedTaskStatus.Scheduled)
                {
                    Interlocked.Increment(ref scheduleCount);
                }
            }

            managedTasks.OnStatus += OnSchedule;

            // starts in 1 second, then runs 1 second job
            var trigger = new ManagedTaskTrigger()
            {
                StartDate = DateTime.Now,
                StartTime = DateTime.Now.AddSeconds(100).TimeOfDay,
                IntervalTime = TimeSpan.FromSeconds(2),
                MaxRecurs = 5
            };

            var task = managedTasks.Add("123", "task3", "test", managedObject, new[] { trigger });

            Assert.Single(managedTasks.GetScheduledTasks());

            await Task.Delay(2000, CancellationToken.None);
            task.Cancel();
            await Task.Delay(100, CancellationToken.None); //small delay to give spooler a chance to remove task from schedule.
            Assert.Empty(managedTasks.GetScheduledTasks());
            
        }

        private class FileDeleteTask : ManagedObject
        {
            public FileDeleteTask(string path)
            {
                _path = path;
            }

            private readonly string _path;
            public override Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
            {
                var files = Directory.GetFiles(_path);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                return Task.CompletedTask;
            }

            public override object Data { get; set; }
        }
        
        [Fact]
        public async Task Test_ManagedTask_FileWatcherAsync()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "TestFiles");

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            
            var managedTasks = new ManagedTasks.ManagedTasks();
            var startTime = DateTime.Now;
           
            _output.WriteLine($"Started at {DateTime.Now}.");

            var fileDelete = new FileDeleteTask(path);
            
            var fileWatchCount = 0;
            
            void OnFileWatch(object sender, EManagedTaskStatus status)
            {
                if (status == EManagedTaskStatus.FileWatching)
                {
                    Interlocked.Increment(ref fileWatchCount);
                }
            }
            
            managedTasks.OnStatus += OnFileWatch;
            
            _output.WriteLine($"Step 1 {DateTime.Now}.");

            var fileWatch = new ManagedTaskFileWatcher(path, "*");

            _output.WriteLine($"Step 1a {DateTime.Now}.");

            var fileTask = managedTasks.Add("123", "task3", "test", fileDelete, null,  new[] { fileWatch });

            var startTime2 = DateTime.Now;

            _output.WriteLine($"Step 2 {startTime2}.");
            
            for (var i = 0; i < 5; i++)
            {
                File.Create(Path.Combine(path, "test-" + Guid.NewGuid())).Close();
                await Task.Delay(1000);
            }

            
            await fileTask.CancelAsync();
            
            var cts = new CancellationTokenSource();
            cts.CancelAfter(10000);
            await managedTasks.WhenAll(cts.Token);

            // file watch count will be 6.  One for each file (5), and then another for the next watcher that was started before the cancel.
            Assert.Equal(6, fileWatchCount);
            Assert.Equal(6, managedTasks.FileWatchCount);

            Assert.Equal(5, managedTasks.CompletedCount);

            // should 5 seconds (with tolerance)
            
            _output.WriteLine($"Finished {DateTime.Now}.");
            Assert.True(startTime2.AddSeconds(5) < DateTime.Now && startTime2.AddSeconds(5.5) > DateTime.Now);
        }

    }
}