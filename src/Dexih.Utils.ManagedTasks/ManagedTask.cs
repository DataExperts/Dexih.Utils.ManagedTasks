using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dexih.Utils.ManagedTasks
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EManagedTaskStatus
    {
        Created, FileWatching, Scheduled, Queued, Running, Cancelled, Error, Completed
    }
    
    public class ManagedTask: IDisposable
    {
        public event EventHandler<EManagedTaskStatus> OnStatus;
        public event EventHandler<ManagedTaskProgressItem> OnProgress;
        public event EventHandler OnTrigger;
        public event EventHandler OnSchedule;
        public event EventHandler OnFileWatch;

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
        
        public bool IsCompleted => Status == EManagedTaskStatus.Cancelled || Status == EManagedTaskStatus.Completed || Status == EManagedTaskStatus.Error;

        public IEnumerable<ManagedTaskSchedule> Triggers { get; set; }
        
        public IEnumerable<ManagedTaskFileWatcher> FileWatchers { get; set; }

        public DateTime? NextTriggerTime { get; set; }

        public int RunCount { get; protected set; }

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
        public Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> Action { get; set; }

        /// <summary>
        /// Action that will be started and executed when the schedule starts.
        /// </summary>
        [JsonIgnore]
        public Func<ManagedTask, DateTime, CancellationToken, Task> ScheduleAction { get; set; }

        /// <summary>
        /// Action that will be started when a cancel is requested.
        /// </summary>
        [JsonIgnore]
        public Func<ManagedTask, CancellationToken, Task> CancelScheduleAction { get; set; }

        private readonly CancellationTokenSource _cancellationTokenSource;
        
        private Task _task;
        private readonly ManagedTaskProgress _progress;
        private Task _progressInvoke;
        private bool _anotherProgressInvoke;
        private bool _previousTrigger;
        
        private HashSet<string> _filesProcessed;


        private Timer _timer;
        private readonly object _triggerLock = 1;

        // private Task _eventManager;

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

        private void SetStatus(EManagedTaskStatus newStatus)
        {
            if (newStatus != Status)
            {
                OnStatus?.Invoke(this, newStatus);
            }
        }
        
        /// <summary>
        /// Start task schedule based on the "Triggers".
        /// </summary>
        public bool Schedule()
        {
           
            if(Status == EManagedTaskStatus.Queued || Status == EManagedTaskStatus.Running || Status == EManagedTaskStatus.Scheduled || Status == EManagedTaskStatus.FileWatching)
            {
                throw new ManagedTaskException(this, "The task cannot be scheduled as the status is already set to " + Status);
            }

            var allowSchedule = DependentReferences != null && DependentReferences.Length > 0 && DepedenciesMet && RunCount == 0;

            // if the filewatchers are not set, then set them.
            if (FileWatchers != null && FileWatchers.Any())
            {
                foreach (var fileWatcher in FileWatchers)
                {
                    if (!fileWatcher.IsStarted)
                    {
                        fileWatcher.OnFileWatch += FileReady;
                        fileWatcher.Start();
                    }
                }
                
                SetStatus(EManagedTaskStatus.FileWatching);
                OnFileWatch?.Invoke(this, EventArgs.Empty);
                allowSchedule = true;
            }
            
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
                    allowSchedule = true;

                    SetStatus(EManagedTaskStatus.Scheduled);
                    
                    StepName = "Scheduled...";
                    Percentage = 0;
                    
                    OnSchedule?.Invoke(this, EventArgs.Empty);

                    var timeToGo = startAt.Value - DateTime.Now;

                    if (timeToGo > TimeSpan.Zero)
                    {
                        NextTriggerTime = startAt;
                        ScheduleAction?.Invoke(this, startAt.Value, _cancellationTokenSource.Token);
                        
                        //add a schedule.
                        _timer = new Timer(x => TriggerReady(startTrigger), null, timeToGo, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        TriggerReady(startTrigger);
                    }
                }
            }

            return allowSchedule;
        }


        /// <summary>
        /// Stops the schedule, and file watchers.
        /// </summary>
        public void DisposeSchedules()
        {
            // close all the existing triggers.
            _timer?.Dispose();
            if (FileWatchers != null && FileWatchers.Any())
            {
                foreach (var fileWatcher in FileWatchers)
                {
                    fileWatcher.Stop();
                }
            }

            _timer = null;
        }

        /// <summary>
        /// Removes OnTrigger events.
        /// </summary>
        public void DisposeTrigger()
        {
            OnTrigger = null;
        }

        private void FileReady(object source, EventArgs args)
        {
            lock (_triggerLock) // trigger lock is to avoid double trigger
            {
                // if there is no trigger set, the previousTrigger is flagged to record the trigger.
                if (OnTrigger == null)
                {
                    _previousTrigger = true;
                }
                else
                {
                    OnTrigger?.Invoke(this, EventArgs.Empty);
                }
            } 
        }
        
        private void TriggerReady(ManagedTaskSchedule trigger)
        {
            lock (_triggerLock) // trigger lock is to avoid double trigger
            {
                // if there is no trigger set, the previousTrigger is flagged to record the trigger.
                if (OnTrigger == null)
                {
                    _previousTrigger = true;
                }
                else
                {
                    OnTrigger?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
      public bool CheckPreviousTrigger()
        {
            var value = _previousTrigger;
            _previousTrigger = false;
            return value;
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
                        await Action(this, _progress, _cancellationTokenSource.Token);
                    }
                    catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
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
                }
                catch (Exception ex)
                {
                    Message = ex.Message;
                    Exception = ex;
                    Success = false;
                    SetStatus(EManagedTaskStatus.Error);
                    Percentage = 100;
                }

            }); //.ContinueWith((o) => Dispose());
        }

        public  void Cancel()
        {
            _cancellationTokenSource.Cancel();
            
            if (Status == EManagedTaskStatus.Scheduled)
            {
                CancelScheduleAction?.Invoke(this, CancellationToken.None);
            }

            Success = false;
            Message = "The task was cancelled.";
            SetStatus(EManagedTaskStatus.Cancelled);
            DisposeSchedules();
            DisposeTrigger();
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
            DisposeSchedules();
            DisposeTrigger();
            OnProgress = null;
            OnStatus = null;
            OnTrigger = null;
            OnSchedule = null;
            OnFileWatch = null;
        }

        private string _exceptionDetails;

        /// <summary>
        /// Full trace of the exception.  This can either be set to a value, or 
        /// will be constructed from the exception.
        /// </summary>
        public virtual string ExceptionDetails
        {
            get
            {
                if (!string.IsNullOrEmpty(_exceptionDetails))
                {
                    return _exceptionDetails;
                }
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
            set => _exceptionDetails = value;
        }
    }
}
