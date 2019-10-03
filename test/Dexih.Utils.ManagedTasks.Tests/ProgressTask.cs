using System.Threading;
using System.Threading.Tasks;
using Dexih.Utils.ManagedTasks;

namespace Dexih.Utils.Managed.Tasks.Tests
{
    public class ProgressTask : ManagedObject
    {
        public ProgressTask(int delay, int loops)
        {
            _delay = delay;
            _loops = loops;
        }

        private readonly int _delay;
        private readonly int _loops;
            
        public override async Task StartAsync(ManagedTaskProgress progress, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < _loops; i++)
            {
                await Task.Delay(_delay, cancellationToken);
                var percent = (i+1) *(100 / _loops);
                progress.Report(percent, "step: " + percent);
            }
        }

        public override object Data { get; set; }
    }

}