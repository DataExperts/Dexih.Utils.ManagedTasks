﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Dexih.Utils.ManagedTasks
{
    // [JsonConverter(typeof(StringEnumConverter))]
    public enum EManagedTaskStatus
    {
        Created = 1, FileWatching, Scheduled, Queued, Running, Cancelled, Error, Completed
    }

    public enum EConcurrentTaskAction
    {
        Parallel = 1,
        Abend,
        Sequence
    }
    
    [DataContract]
    public sealed class ManagedTask: IDisposable
    {
        public event EventHandler<EManagedTaskStatus> OnStatus;
        public event EventHandler<ManagedTaskProgressItem> OnProgress;
        public event EventHandler OnTrigger;
        public event EventHandler OnSchedule;
        public event EventHandler OnFileWatch;

        /// <summary>
        /// Used to store changes
        /// </summary>
        [JsonIgnore]
        [IgnoreDataMember]
        public string ChangeId { get; set; }
        
        [DataMember(Order = 1)]
        public bool Success { get; set; }

        [DataMember(Order = 2)]
        public string Message { get; set; }
        
        [JsonIgnore]
        [IgnoreDataMember]
        public Exception Exception { get; set; }

        /// <summary>
        /// Unique key used to reference the task
        /// </summary>
        [DataMember(Order = 3)]
        public string Reference { get; set; }
        
        /// <summary>
        /// Id that reference the originating client of the task.
        /// </summary>
        [DataMember(Order = 4)]
        public string OriginatorId { get; set; }
        
        /// <summary>
        /// Short name for the task.
        /// </summary>
        [DataMember(Order = 5)]
        public string Name { get; set; }

        /// <summary>
        /// A description for the task
        /// </summary>
        [DataMember(Order = 6)]
        public string Description { get; set; }
        
        /// <summary>
        /// When task was last updated.
        /// </summary>
        [DataMember(Order = 7)]
        public DateTime LastUpdate { get; set; }
        
        [DataMember(Order = 8)]
        public EManagedTaskStatus Status { get; set; }

        /// <summary>
        /// Any category that is used to group tasks
        /// </summary>
        [DataMember(Order = 9)]
        public string Category { get; set; }
        
        /// <summary>
        /// A unique key for the item within the category.  If attempts are made to add two items with same
        /// category key, an exception will be raised.
        /// </summary>
        [DataMember(Order = 10)]
		public long CategoryKey { get; set; }
        
        /// <summary>
        /// A long field that can be used as a reference key to the original object.
        /// </summary>
        [DataMember(Order = 11)]
        public long ReferenceKey { get; set; }
        
        /// <summary>
        /// A string field that can be used as a reference key to the original object.
        /// </summary>
        [DataMember(Order = 12)]
        public string ReferenceId { get; set; }

        /// <summary>
        /// The percentage completion of the task
        /// </summary>
        [DataMember(Order = 13)]
        public int Percentage { get; set; }
        
        /// <summary>
        /// A counter used to indicate progress (such as rows processed).
        /// </summary>
        [DataMember(Order = 14)]
        public long Counter { get; set; }

        /// <summary>
        /// Action to take when a task with the same referenceKey is added.
        /// </summary>
        [DataMember(Order = 15)]
        public EConcurrentTaskAction ConcurrentTaskAction { get; set; } = EConcurrentTaskAction.Abend;
        
        /// <summary>
        /// A string use to include the progress step.
        /// </summary>
        [DataMember(Order = 16)]
        public string StepName { get; set; }
        
        public bool IsCompleted => Status == EManagedTaskStatus.Cancelled || Status == EManagedTaskStatus.Completed || Status == EManagedTaskStatus.Error;

        [DataMember(Order = 17)]
        public DateTime StartTime { get; private set; }
        
        [DataMember(Order = 18)]
        public DateTime EndTime { get; private set; }

        [DataMember(Order = 19)]
        public IEnumerable<ManagedTaskSchedule> Triggers { get; set; }
        
        [DataMember(Order = 20)]
        public IEnumerable<ManagedTaskFileWatcher> FileWatchers { get; set; }

        [DataMember(Order = 21)]
        public DateTime? NextTriggerTime { get; set; }

        [DataMember(Order = 22)]
        public int RunCount { get; private set; }

        /// <summary>
        /// Array of task reference which must be complete prior to this task.
        /// </summary>
        [DataMember(Order = 23)]
        public string[] DependentReferences { get; set; }


        private bool _dependenciesMet;
        private Task _startTask;
        
        /// <summary>
        /// Flag to indicate dependent tasks have been completed.
        /// </summary>
        [DataMember(Order = 24)]
        public bool DependenciesMet {
            get => _dependenciesMet || DependentReferences == null || DependentReferences.Length == 0;
            set => _dependenciesMet = value;
        }
        
        /// <summary>
        /// The implementation of the task being run
        /// </summary>
        [JsonIgnore]
        [IgnoreDataMember]
        public IManagedObject ManagedObject { get; set; }

        // The data object is used to pass data when the managedTask is serialized.
        private object _data;

        [DataMember(Order = 25)]
        public object Data
        {
            get => ManagedObject?.Data ?? _data;
            set => _data = value;
        }

//        /// <summary>
//        /// Action that will be started and executed when the task starts.
//        /// </summary>
//        [JsonIgnore]
//        public Func<ManagedTask, ManagedTaskProgress, CancellationToken, Task> Action { get; set; }
//
//        /// <summary>
//        /// Action that will be started and executed when the schedule starts.
//        /// </summary>
//        [JsonIgnore]
//        public Action<ManagedTask, DateTime, CancellationToken> ScheduleAction { get; set; }
//
//        /// <summary>
//        /// Action that will be started when a cancel is requested.
//        /// </summary>
//        [JsonIgnore]
//        public Func<ManagedTask, CancellationToken, Task> CancelScheduleAction { get; set; }
        
        private readonly CancellationTokenSource _cancellationTokenSource;

        private readonly ManagedTaskProgress _progress;
        private Task _progressInvoke;
        private bool _previousTrigger;
       
        private Timer _timer;
        private readonly object _triggerLock = 1;

        public void ResetChangeId()
        {
            ChangeId = Guid.NewGuid().ToString();
        }

        public ManagedTask()
        {
            bool anotherProgressInvoke;
            LastUpdate = DateTime.Now;
            Status = EManagedTaskStatus.Created;
            _cancellationTokenSource = new CancellationTokenSource();
            ResetChangeId();

            // progress routine which calls the progress event async 
            _progress = new ManagedTaskProgress(value =>
            {
                if ( Percentage != value.Percentage || StepName != value.StepName || Counter != value.Counter)
                {
                    Percentage = value.Percentage;
                    StepName = value.StepName;
                    Counter = value.Counter;
                    
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
                                anotherProgressInvoke = false;
                                OnProgress?.Invoke(this, value);
                            } while (anotherProgressInvoke);
                        });
                    }
                    else
                    {
                        anotherProgressInvoke = true;
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
            if (Status == EManagedTaskStatus.Cancelled)
            {
                return false;
            }
           
            if(Status == EManagedTaskStatus.Queued || Status == EManagedTaskStatus.Running || Status == EManagedTaskStatus.Scheduled || Status == EManagedTaskStatus.FileWatching)
            {
                throw new ManagedTaskException("The task cannot be scheduled as the status is already set to " + Status);
            }

            var allowSchedule = DependentReferences != null && DependentReferences.Length > 0 && DependenciesMet && RunCount == 0;

            // if the file watchers are not set, then set them.
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
                foreach (var trigger in Triggers)
                {
                    var triggerTime = trigger.NextOccurrence(DateTime.Now);
                    if (triggerTime != null && (startAt == null || triggerTime < startAt))
                    {
                        startAt = triggerTime;
                    }
                }

                if(startAt != null)
                {
                    allowSchedule = true;

                    StepName = "Scheduled...";
                    Percentage = 0;
                    Counter = 0;
                    
                    var timeToGo = startAt.Value - DateTime.Now;

                    if (timeToGo > TimeSpan.Zero)
                    {
                        NextTriggerTime = startAt;
                        ManagedObject.Schedule(startAt.Value, _cancellationTokenSource.Token);
                        
                        //add a schedule.
                        _timer = new Timer(x => TriggerReady(), null, timeToGo, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        TriggerReady();
                    }
                    
                    SetStatus(EManagedTaskStatus.Scheduled);
                    OnSchedule?.Invoke(this, EventArgs.Empty);
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
        
        private void TriggerReady()
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
                throw new ManagedTaskException("Task cannot be started as it is already running.");
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
            
            SetStatus(EManagedTaskStatus.Running);

            try
            {
                StartTime = DateTime.Now;
                _startTask = ManagedObject.StartAsync(_progress, _cancellationTokenSource.Token)
                    .ContinueWith(o =>
                    {
                        switch (o.Status)
                        {
                            case TaskStatus.RanToCompletion:
                                Success = true;
                                Message = "The task completed.";
                                EndTime = DateTime.Now;
                                SetStatus(EManagedTaskStatus.Completed);
                                break;
                            case TaskStatus.Canceled:
                                Success = false;
                                Message = "The task was cancelled.";
                                EndTime = DateTime.Now;
                                SetStatus(EManagedTaskStatus.Cancelled);
                                break;
                            case TaskStatus.Faulted:
                                Message = o.Exception?.Message ?? "Unknown error occurred";
                                Exception = o.Exception;
                                Success = false;
                                EndTime = DateTime.Now;
                                SetStatus(EManagedTaskStatus.Error);
                                Percentage = 100;
                                break;
                            default:
                                Message = "Task failed with status " + o.Status + ".  Message:" + (o.Exception?.Message??"No Message");
                                Exception = o.Exception;
                                Success = false;
                                EndTime = DateTime.Now;
                                SetStatus(EManagedTaskStatus.Error);
                                Percentage = 100;
                                break;
                        }

                    }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                EndTime = DateTime.Now;
                Message = ex.Message;
                Exception = ex;
                Success = false;
                SetStatus(EManagedTaskStatus.Error);
                Percentage = 100;
            }
        }

        /// <summary>
        /// Sends a cancellation request to the task
        /// </summary>
        /// <returns></returns>
        public void Cancel()
        {
            DisposeSchedules();
            DisposeTrigger();

            if (Status == EManagedTaskStatus.Scheduled || Status == EManagedTaskStatus.FileWatching)
            {
                ManagedObject.Cancel();
                SetStatus(EManagedTaskStatus.Cancelled);
            }
            
            _cancellationTokenSource.Cancel();
            
        }
        
        /// <summary>
        /// Sends a cancellation request to the task and waits for the task to finish.
        /// </summary>
        /// 
        /// <returns></returns>
        public async Task CancelAsync(CancellationToken cancellationToken = default)
        {
            Cancel();

            if (_startTask != null)
            {
                await Task.Run(() => _startTask, cancellationToken);
            }

//            Success = false;
//            Message = "The task was cancelled.";
//            SetStatus(EManagedTaskStatus.Cancelled);
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
            ManagedObject.Dispose();

            if (_startTask != null)
            {
                _startTask.Wait();
                _startTask.Dispose();
            }
        }

        private string _exceptionDetails;

        /// <summary>
        /// Full trace of the exception.  This can either be set to a value, or 
        /// will be constructed from the exception.
        /// </summary>
        public string ExceptionDetails
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

        /// <summary>
        /// Returns a copy of the basic properties
        /// Excludes runtime properties.
        /// </summary>
        /// <returns></returns>
        public ManagedTask Copy()
        {
            return new ManagedTask()
            {
                Category = Category,
                Counter = Counter,
                Data = Data,
                Description = Description,
                Message = Message,
                Name = Name,
                Percentage = Percentage,
                Reference = Reference,
                Status = Status,
                Success = Success,
                CategoryKey = CategoryKey,
                ChangeId = ChangeId,
                DependenciesMet = DependenciesMet,
                EndTime = EndTime,
                LastUpdate = LastUpdate,
                OriginatorId = OriginatorId,
                ReferenceId = ReferenceId,
                ReferenceKey = ReferenceKey,
                RunCount = RunCount,
                StartTime = StartTime,
                StepName = StepName,
                ConcurrentTaskAction = ConcurrentTaskAction
            };
        }
    }
}
