using System;
using System.Threading;
using System.Threading.Tasks;
using Dexih.Utils.ManagedTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ManagedTaskDemo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    // add a singleton to host the ManagedTasks service
                    services.AddSingleton<IManagedTasks, ManagedTasks>();
                    
                    // add a hosted service, which will kick off some scheduled tasks.
                    services.AddHostedService<TaskService>();
                });

            var host = hostBuilder.Build();
            await host.RunAsync();
        }
    }

    public class TaskService : IHostedService
    {
        private readonly IManagedTasks _managedTasks;

        public TaskService(IManagedTasks managedTasks)
        {
            _managedTasks = managedTasks;
            
            // track status changes
            managedTasks.OnStatus += TaskStatusChange;
            
            // track progress changes
            managedTasks.OnProgress += TaskProgressChange;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // add a new count task that reruns every 10 seconds.
            _managedTasks.Add(new ManagedTask()
            {
                TaskId = Guid.NewGuid().ToString(),
                Name = "count to 5",
                ManagedObject = new CountTask(),
                Triggers = new [] { new ManagedTaskTrigger(TimeSpan.FromSeconds(10), 1000)}
            });
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        private void TaskStatusChange(ManagedTask value, EManagedTaskStatus managedTaskStatus)
        {
            Console.WriteLine($"Task Status: {value.Name}, Status: {managedTaskStatus}");
        }

        private void TaskProgressChange(ManagedTask value, ManagedTaskProgressItem progressItem)
        {
            Console.WriteLine($"Task Progress: {value.Name}, Step: {progressItem.StepName}");
        }
    }
    
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
}