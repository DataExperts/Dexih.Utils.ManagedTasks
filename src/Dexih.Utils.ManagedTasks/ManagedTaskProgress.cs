using System;

namespace Dexih.Utils.ManagedTasks
{
    public class ManagedTaskProgress : Progress<ManagedTaskProgressItem>
    {
        private ManagedTaskProgressItem _previousProgressItem;
        
        public ManagedTaskProgress(Action<ManagedTaskProgressItem> progress) : base(progress)
        {
        }
        
        public void Report(int percentage)
        {
            var progress = new ManagedTaskProgressItem
            {
                Percentage = percentage,
                Counter = _previousProgressItem?.Counter ?? 0,
                StepName = _previousProgressItem?.StepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }

        public void Report(int percentage, string step)
        {
            var progress = new ManagedTaskProgressItem
            {
                Percentage = percentage,
                Counter = _previousProgressItem?.Counter ?? 0,
                StepName = step
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
        
        public void Report(int percentage, long counter)
        {
            var progress = new ManagedTaskProgressItem
            {
                Percentage = percentage,
                Counter = counter,
                StepName = _previousProgressItem?.StepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
        
        public void Report(string stepName)
        {
            var progress = new ManagedTaskProgressItem
            {
                Percentage = _previousProgressItem?.Percentage ?? 0,
                StepName = stepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
        
        public void Report(int percentage, long counter, string stepName)
        {
            var progress = new ManagedTaskProgressItem
            {
                Percentage = percentage,
                StepName = stepName,
                Counter = counter
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
    }

    public class ManagedTaskProgressItem
    {
        public string StepName { get; set; }
        public int Percentage { get; set; }
        public long Counter { get; set; }
    }
    
}