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
    
    /// <summary>
    /// Day of the week
    /// </summary>
    // [JsonConverter(typeof(StringEnumConverter))]
    public enum EDayOfWeek
    {
        Sunday = 1,
        Monday = 2,
        Tuesday = 3,
        Wednesday = 4,
        Thursday = 5,
        Friday = 6,
        Saturday = 7
    }

    // [JsonConverter(typeof(StringEnumConverter))]
    public enum EIntervalType
    {
        None,
        Once,
        Interval,
        Daily,
        Monthly
    }

    // [JsonConverter(typeof(StringEnumConverter))]
    public enum EWeekOfMonth
    {
        First = 1,
        Second,
        Third,
        Fourth,
        Last
    }
}