using System;

namespace dexih.utils.ManagedTasks
{
    public class ManagedTaskProgress : Progress<ManagedTaskProgressItem>
    {
        private ManagedTaskProgressItem _previousProgressItem;
        
        public ManagedTaskProgress(Action<ManagedTaskProgressItem> progress) : base(progress)
        {
        }

        public void Report(int percentage)
        {
            var progress = new ManagedTaskProgressItem()
            {
                Percentage = percentage,
                StepName = _previousProgressItem?.StepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
        
        public void Report(string stepName)
        {
            var progress = new ManagedTaskProgressItem()
            {
                Percentage = _previousProgressItem?.Percentage ?? 0,
                StepName = stepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
        
        public void Report(int percentage, string stepName)
        {
            var progress = new ManagedTaskProgressItem()
            {
                Percentage = percentage,
                StepName = stepName
            };

            _previousProgressItem = progress;

            OnReport(progress);
        }
    }

    public class ManagedTaskProgressItem
    {
        public string StepName { get; set; }
        public int Percentage { get; set; }
    }
    
}