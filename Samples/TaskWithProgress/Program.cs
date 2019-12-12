using System;
using System.Threading;
using System.Threading.Tasks;
using Dexih.Utils.ManagedTasks;

namespace TaskWithProgress
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            var managedTasks = new ManagedTasks();
            managedTasks.OnProgress += Progress;
            
            var countTask = new CountTask();

            Console.WriteLine("Run one task.");
            managedTasks.Add(new ManagedTask()
            {
                Reference = Guid.NewGuid().ToString(),
                Name = "count to 5",
                ManagedObject = countTask
            });

            await managedTasks.WhenAll();
            
            Console.WriteLine("Finished.");
            
            Console.WriteLine("Run task in 5 seconds.");
            managedTasks.Add(new ManagedTask()
            {
                Reference = Guid.NewGuid().ToString(),
                Name = "count to 5",
                ManagedObject = countTask,
                Triggers = new []{new ManagedTaskTrigger(DateTime.Now.AddSeconds(5))}
            });

            await managedTasks.WhenAll();
            
            Console.WriteLine("Finished.");
            
            Console.WriteLine("Run task 2 times.");
            managedTasks.Add(new ManagedTask()
            {
                Reference = Guid.NewGuid().ToString(),
                Name = "count to 5",
                ManagedObject = countTask,
                Triggers = new [] { new ManagedTaskTrigger(TimeSpan.FromSeconds(6), 2)}
            });

            await managedTasks.WhenAll();
            Console.WriteLine("Finished.");

            
            Console.WriteLine("Run two tasks in parallel.");
            managedTasks.Add(new ManagedTask()
            {
                Reference = Guid.NewGuid().ToString(),
                Name = "task1",
                ManagedObject = countTask
            });

            managedTasks.Add(new ManagedTask()
            {
                Reference = Guid.NewGuid().ToString(),
                Name = "task2",
                ManagedObject = countTask
            });

            await managedTasks.WhenAll();
            
            Console.WriteLine("Run two tasks in sequence.");
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
            
            Console.WriteLine("Finished.");
        }

        static void Progress(ManagedTask managedTask, ManagedTaskProgressItem progressItem)
        {
            Console.WriteLine($"Task:{managedTask.Name}, Progress: {progressItem.StepName} ");
        }
        
    }
    
    public class CountTask : ManagedObject
    {
        public override async Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(1000, cancellationToken);
                var percent = (i+1) * 5;
                progress.Report( $"step: {i+1}");
            }
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
}