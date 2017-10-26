using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Linq;

namespace dexih.utils.ManagedTasks
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EManagedTaskStatus
    {
        Created, Scheduled, Queued, Running, Cancelled, Error, Completed
    }
    
    public class ManagedTask: IDisposable
    {
        public event EventHandler<EManagedTaskStatus> OnStatus;
        public event EventHandler<ManagedTaskProgressItem> OnProgress;
        public event EventHandler OnTrigger;

        public bool Success { get; set; }
        public string Message { get; set; }
        
        [JsonIgnore]
        public Exception Exception { get; set; }

        /// <summary>
        /// Unique key used to reference the task
        /// </summary>
        public string Reference { get; set; }
        
        /// <summary>
        /// Id that reference the originating client of the task.
        /// </summary>
        public string OriginatorId { get; set; }
        
        /// <summary>
        /// Short name for the task.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description for the task
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// When task was last updated.
        /// </summary>
        public DateTime LastUpdate { get; set; }

        public EManagedTaskStatus Status { get; set; }

        public object Data { get; set; }

        public string Category { get; set; }
		public long CatagoryKey { get; set; }
		public long HubKey { get; set; }

        public int Percentage { get; set; }
        public string StepName { get; set; }

        public bool IsCompleted 
        { 
            get
            {
                return Status == EManagedTaskStatus.Cancelled || Status == EManagedTaskStatus.Completed || Status == EManagedTaskStatus.Error;
            }
        }

        public IEnumerable<ManagedTaskSchedule> Triggers { get; set; }

        public DateTime? NextTriggerTime { get; protected set; }

        public int RunCount { get; protected set; } = 0;

        /// <summary>
        /// Array of task reference which must be complete prior to this task.
        /// </summary>
        public string[] DependentReferences { get; set; }


        private bool _dependenciesMet;
        /// <summary>
        /// Flag to indicate dependent tasks have been completed.
        /// </summary>
        public bool DepedenciesMet {
            get => _dependenciesMet || DependentReferences == null || DependentReferences.Length == 0;
            set => _dependenciesMet = value;
        }

        /// <summary>
        /// Action that will be started and executed when the task starts.
        /// </summary>
        [JsonIgnore]
        public Func<ManagedTaskProgress, CancellationToken, Task> Action { get; set; }

        private CancellationTokenSource _cancellationTokenSource;
        
        private Task _task;
        private readonly ManagedTaskProgress _progress;
        private Task _progressInvoke;
        private bool _anotherProgressInvoke = false;

        private Timer _timer;

        // private Task _eventManager;

        private void SetStatus(EManagedTaskStatus newStatus)
        {
            if(newStatus > Status)
            {
                Status = newStatus;
                OnStatus?.Invoke(this, Status);
            }
        }

        public ManagedTask()
        {
            LastUpdate = DateTime.Now;
            Status = EManagedTaskStatus.Created;
            _cancellationTokenSource = new CancellationTokenSource();

            // progress routine which calls the progress event async 
            _progress = new ManagedTaskProgress(value =>
            {
                if (Percentage != value.Percentage || StepName != value.StepName)
                {
                    Percentage = value.Percentage;
                    StepName = value.StepName;
                    
                    // if the previous progress has finished?
                    if (_progressInvoke == null || _progressInvoke.IsCompleted)
                    {
                        _progressInvoke = Task.Run(() =>
                        {
                            // keep creating a new progress event until the flag is not set.
                            // this allows code to keep running whilst a progress event runs in the background.
                            // if also ensures progress events are only sent one at a time.
                            do
                            {
                                _anotherProgressInvoke = false;
                                OnProgress?.Invoke(this, value);
                            } while (_anotherProgressInvoke);
                        });
                    }
                    else
                    {
                        _anotherProgressInvoke = true;
                    }
                }
            });
        }

        /// <summary>
        /// Start task schedule based on the "Triggers".
        /// </summary>
        public bool Schedule()
        {
            if(Status == EManagedTaskStatus.Queued || Status == EManagedTaskStatus.Running || Status == EManagedTaskStatus.Scheduled)
            {
                throw new ManagedTaskException(this, "The task cannot be scheduled as the status is already set to " + Status.ToString());
            }

            var allowSchedule = DependentReferences != null && DependentReferences.Length > 0 && DepedenciesMet && RunCount == 0;

            if (Triggers != null)
            {
                // loop through the triggers to find the one scheduled the soonest.
                DateTime? startAt = null;
                ManagedTaskSchedule startTrigger = null;
                foreach (var trigger in Triggers)
                {
                    var triggerTime = trigger.NextOcurrance(DateTime.Now);
                    if (triggerTime != null && (startAt == null || triggerTime < startAt))
                    {
                        startAt = triggerTime;
                        startTrigger = trigger;
                    }
                }

                if(startAt != null)
                {
                    var timeToGo = startAt.Value - DateTime.Now;

                    if (timeToGo > TimeSpan.Zero)
                    {
                        NextTriggerTime = startAt;
                        //add a schedule.
                        _timer = new Timer(x => TriggerReady(startTrigger), null, timeToGo, Timeout.InfiniteTimeSpan);
                        allowSchedule = true;
                    }
                    else
                    {
                        TriggerReady(startTrigger);
                    }
                }
            }

            return allowSchedule;
        }

        public void TriggerReady(ManagedTaskSchedule trigger)
        {
            OnTrigger?.Invoke(this, EventArgs.Empty);
        }

        public void Queue()
        {
            if (Status == EManagedTaskStatus.Queued || Status == EManagedTaskStatus.Running || Status == EManagedTaskStatus.Scheduled)
            {
                throw new ManagedTaskException(this, "The task cannot be queued for execution as the status is already set to " + Status.ToString());
            }
            SetStatus(EManagedTaskStatus.Queued);
        }

        /// <summary>
        /// Immediately start the task.
        /// </summary>
        public void Start()
        {
            RunCount++;

            if(Status == EManagedTaskStatus.Running)
            {
                throw new ManagedTaskException(this, "Task cannot be started as it is already running.");
            }

            // kill any active timers.
            if (_timer != null)
            {
                _timer.Dispose();
                NextTriggerTime = null;
            }

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                Success = false;
                Message = "The task was cancelled.";
                Percentage = 100;
                SetStatus(EManagedTaskStatus.Cancelled);
                return;
            }

            _task = Task.Run(async () =>
            {
                try
                {
                    SetStatus(EManagedTaskStatus.Running);

                    try
                    {
                        await Action(_progress, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        Success = false;
                        Message = "The task was cancelled.";
                        SetStatus(EManagedTaskStatus.Cancelled);
                        Percentage = 100;
                        return;
                    }

                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        Success = false;
                        Message = "The task was cancelled.";
                        SetStatus(EManagedTaskStatus.Cancelled);
                    }
                    else
                    {
                        Success = true;
                        Message = "The task completed.";
                        SetStatus(EManagedTaskStatus.Completed);
                    }

                    Percentage = 100;
                    return;

                }
                catch (Exception ex)
                {
                    Message = ex.Message;
                    Exception = ex;
                    Success = false;
                    SetStatus(EManagedTaskStatus.Error);
                    Percentage = 100;
                    return;
                }

            }); //.ContinueWith((o) => Dispose());
        }

        public  void Cancel()
        {
            _cancellationTokenSource.Cancel();
            Success = false;
            Message = "The task was cancelled.";
            SetStatus(EManagedTaskStatus.Cancelled);
            _timer?.Dispose();
        }

        public void Error(string message, Exception ex)
        {
            Success = false;
            Message = message;
            Exception = ex;
            SetStatus(EManagedTaskStatus.Error);
        }

        public void Reset()
        {
            //if (_timer != null) _timer.Dispose();
            Status = EManagedTaskStatus.Created;
            // SetStatus(EManagedTaskStatus.Created);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            OnProgress = null;
            OnStatus = null;
            OnTrigger = null;
           // _cancellationTokenSource.Dispose();
        }
        

        /// <summary>
        /// Full trace of the exception.  This can either be set to a value, or 
        /// will be constructed from the exception.
        /// </summary>
        public virtual string ExceptionDetails
        {
            get
            {
                if (Exception != null)
                {
                    var properties = Exception.GetType().GetProperties();
                    var fields = properties
                        .Select(property => new
                        {
                            property.Name,
                            Value = property.GetValue(Exception, null)
                        })
                        .Select(x => string.Format(
                            "{0} = {1}",
                            x.Name,
                            x.Value != null ? x.Value.ToString() : string.Empty
                        ));
                    return Message + "\n" + string.Join("\n", fields);
                }

                return "";
            }
        }
    }
}
