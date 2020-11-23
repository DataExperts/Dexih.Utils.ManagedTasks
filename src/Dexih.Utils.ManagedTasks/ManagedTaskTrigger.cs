using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Dexih.Utils.ManagedTasks
{
    /// <summary>
    /// The trigger class allows a schedule to be implemented via the parameters.
    /// This can then be called to provide the NextTrigger, which is the next date/time the execution should occur.
    /// </summary>
    [DataContract]
    public class ManagedTaskTrigger
    {
        public ManagedTaskTrigger() { }

        /// <summary>
        /// Create a trigger that starts at the specified time and executes once.
        /// </summary>
        /// <param name="startAt">Start At Date</param>
        public ManagedTaskTrigger(DateTime startAt)
        {
            IntervalType = EIntervalType.Once;
            StartDate = startAt.Date;
            StartTime = startAt.TimeOfDay;
        }

        /// <summary>
        /// Create a trigger that starts now, and executes every interval for a maximum of recurrences.
        /// </summary>
        /// <param name="intervalTime">Interval time</param>
        /// <param name="maxRecurs">Maximum number of recurrences</param>
        public ManagedTaskTrigger(TimeSpan intervalTime, int maxRecurs)
        {
            StartTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromSeconds(1));
            IntervalTime = intervalTime;
            MaxRecurs = maxRecurs;
        }

        /// <summary>
        /// StartDate (note, the time component is ignored, use the <see cref="StartTime"/>)
        /// </summary>
        [DataMember(Order = 1)]
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// EndDate (note, the time component is ignored, use the <see cref="EndTime"/>)
        /// </summary>
        [DataMember(Order = 2)]
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Type of interval to use.<para/>
        /// Once - One interval at the first valid starting time<para/>
        /// Interval - Repeat at each <see cref="IntervalTime"/><para/>
        /// Daily - Run once on each valid <see cref="DaysOfWeek"/><para/>
        /// Monthly - Run monthly<para/>
        /// </summary>
        [DataMember(Order = 3)]
        public EIntervalType IntervalType { get; set; } = EIntervalType.Interval;

        /// <summary>
        /// Interval time is the duration between the starting times
        /// <para /> Note: this applies to interval schedule only.
        /// </summary>
        [DataMember(Order = 4)]
        public TimeSpan? IntervalTime { get; set; }

        /// <summary>
        /// Days of week is an array of of valid days of the week for the schedule.
        /// </summary>
        [DataMember(Order = 5)]
        public EDayOfWeek[] DaysOfWeek { get; set; }

        /// <summary>
        /// Days of the month is any array of valid days of the month for the schedule. 
        /// </summary>
        [DataMember(Order = 6)]
        public int[] DaysOfMonth { get; set; }

        /// <summary>
        /// Days of the month is any array of valid weeks within a month for the schedule. 
        /// </summary>
        [DataMember(Order = 7)]
        public EWeekOfMonth[] WeeksOfMonth { get; set; }

        /// <summary>
        /// List of specific dates (such as public holidays) that are skipped.
        /// </summary>
        [DataMember(Order = 8)]
        public DateTime[] SkipDates { get; set; }

        /// <summary>
        /// StartTime is the time of the day which the job will start.
        /// </summary>
        [DataMember(Order = 9)]
        public TimeSpan? StartTime { get; set; }

        /// <summary>
        /// EndTime is the last time of the day at job can start.
        /// </summary>
        [DataMember(Order = 10)]
        public TimeSpan? EndTime { get; set; }

        /// <summary>
        /// Maximum number of times the schedule recurs.  If null or -1, this will be infinite.
        /// </summary>
        [DataMember(Order = 11)]
        public int? MaxRecurs { get; set; } = 1;


        private string _details;
        
        /// <summary>
        /// Gets a description of the trigger.
        /// </summary>
        [DataMember(Order = 12)]
        public string Details
        {
            get
            {
                if (_details != null)
                {
                    return _details;
                }
                
                var desc = new StringBuilder();

                switch (IntervalType)
                {
                    case EIntervalType.None:
                        return "";
                    case EIntervalType.Once:
                        desc.AppendLine($"Once " + StartDateTimeDesc());
                        break;
                    case EIntervalType.Interval:
                        if (IntervalTime == null || IntervalTime == TimeSpan.Zero)
                        {
                            desc.AppendLine("Error: Interval specified, however no interval time set.");
                        }
                        else
                        {
                            desc.AppendLine($"Starts at {StartDateTimeDesc()}");
                            desc.AppendLine($"Every {IntervalTime.Value.ToString()}");
                            desc.AppendLine("Between " + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()) + " and " + (EndTime == null ? "23:59:59" : EndTime.Value.ToString()));
                            if (MaxRecurs != null || MaxRecurs < 0) desc.AppendLine("Maximum recurrences of " + MaxRecurs.Value);
                        }
                        break;
                    case EIntervalType.Daily:
                        desc.AppendLine($"Daily {StartDateTimeDesc()} at:" + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()));
                        break;
                    case EIntervalType.Monthly:
                        desc.AppendLine($"Monthly {StartDateTimeDesc()} at:" + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()));
                        break;
                }

                if (DaysOfWeek != null && DaysOfWeek.Length > 0 && DaysOfWeek.Length < 7)
                {
                    desc.AppendLine("Only on day(s):" + string.Join(",", DaysOfWeek.Select(c => c.ToString()).ToArray()));
                }

                if (DaysOfMonth?.Length > 0)
                {
                    desc.AppendLine("Only on these day(s) of month:" + string.Join(",", DaysOfMonth.Select(c => c.ToString()).ToArray()));
                }

                if (WeeksOfMonth?.Length > 0)
                {
                    desc.AppendLine("Only on these week(s) of the month:" + string.Join(",", WeeksOfMonth.Select(c => c.ToString()).ToArray()));
                }

                if (SkipDates?.Length > 0)
                {
                    desc.AppendLine("Excluding the following specific dates:" + string.Join(",", SkipDates.Select(c => c.ToString(CultureInfo.CurrentCulture)).ToArray()));
                }

                return desc.ToString();
            }
            set => _details = value;
        }

        private string StartDateTimeDesc()
        {
            if(StartDate == null && StartTime == null)
            {
                return "immediately";
            }
            if(StartDate == null)
            {
                if (StartTime != null) return "from time " + StartTime.Value;
                return "";
            }
            var startDateTime = StartDate.Value.Date;
            if(StartTime != null)
            {
                startDateTime = startDateTime.Add(StartTime.Value);
            }
            return "from date " + startDateTime;
        }


        /// <summary>
        /// Retrieves the next time this schedule will occur from the specified date.
        /// </summary>
        /// <returns>DateTime of schedule, or null if no date is available</returns>
        public DateTime? NextOccurrence(DateTime fromDate)
        {
            DateTime? nextDate = IntervalType switch
            {
                EIntervalType.None => null,
                EIntervalType.Daily => NextOccurrenceDaily(fromDate),
                EIntervalType.Interval => NextOccurrenceInterval(fromDate),
                EIntervalType.Monthly => NextOccurrenceMonthly(fromDate),
                EIntervalType.Once => NextOccurrenceOnce(fromDate),
                _ => null
            };

            return nextDate > EndDate ? null : nextDate;
        }

        private DateTime? NextOccurrenceOnce(DateTime fromDate)
        {
            // for once of, return the start date if it in the future
            if (StartDate == null)
            {
                return null;
            }
            var startDateTime = StartDate.Value.Date;
            if (StartTime != null)
            {
                startDateTime = startDateTime.Add(StartTime.Value);
            }
            if (startDateTime > fromDate)
            {
                return startDateTime;
            }
            return null;
        }

        private DateTime? NextOccurrenceMonthly(DateTime fromDate)
        {
            // check if a valid day has already occurred, this means we should jump to next month
            var priorDate = new DateTime(fromDate.Year, fromDate.Month, 1);
            if(StartTime != null)
            {
                priorDate = priorDate.Add(StartTime.Value);
            }

            var priorValidDate = false;
            while (priorDate < fromDate && !priorValidDate)
            {
                priorValidDate = IsValidDate(priorDate);
                priorDate = priorDate.AddDays(1);
            }

            var nextDate = fromDate;

            // if there was a valid date earlier in the month, then we should start looking for the next valid date from the 1st of next month.
            if (priorValidDate)
            {
                nextDate = nextDate.AddMonths(1);
                nextDate = new DateTime(nextDate.Year, nextDate.Month, 1);
            }

            // if the start time has passed, move to the next day.
            if(StartTime != null && nextDate.TimeOfDay > StartTime)
            {
                nextDate = nextDate.AddDays(1);
            }

            // loop through and test each date until we find the first valid one.
            var isValidDate = false;
            var infiniteBreakCounter = 1;
            var currentMonth = nextDate.Month;
            while ((!isValidDate || currentMonth != nextDate.Month) && infiniteBreakCounter < 10000)
            {
                isValidDate = IsValidDate(nextDate);
                if(!isValidDate)
                {
                    nextDate = nextDate.AddDays(1);
                }

                infiniteBreakCounter++;
            }

            if (isValidDate)
            {
                var startDateTime = new DateTime(nextDate.Year, nextDate.Month, nextDate.Day);
                if (StartTime != null)
                {
                    startDateTime = startDateTime.Add(StartTime.Value);
                }
                return startDateTime;
            }
            return null;
        }

        private DateTime? NextOccurrenceDaily(DateTime fromDate)
        {
            var nextDate = fromDate;

            // if the start time has passed, move to the next day.
            if (StartTime != null && nextDate.TimeOfDay > StartTime)
            {
                nextDate = nextDate.AddDays(1);
            }

            // loop through and test each date until we find the first valid one.
            var isValidDate = false;
            var infiniteBreakCounter = 1;
            while (!isValidDate && infiniteBreakCounter < 10000)
            {
                isValidDate = IsValidDate(nextDate);
                if (!isValidDate)
                {
                    nextDate = nextDate.AddDays(1);
                }

                infiniteBreakCounter++;
            }

            if (isValidDate)
            {
                var startDateTime = new DateTime(nextDate.Year, nextDate.Month, nextDate.Day);
                if (StartTime != null)
                {
                    startDateTime = startDateTime.Add(StartTime.Value);
                }
                return startDateTime;
            }
            return null;
        }

        private DateTime? NextOccurrenceInterval(DateTime fromDate)
        {
            var dailyStart = StartTime ?? new TimeSpan(0, 0, 0);
            var dailyEnd = EndTime ?? new TimeSpan(23, 59, 59);

            //set the initial start date
            var startAt = StartDate == null || StartDate < fromDate ? fromDate.Date : StartDate.Value.Date;

            Console.WriteLine($"startAt: {startAt}");
            ValidateTrigger();

            if (dailyStart > dailyEnd)
            {
                throw new ManagedTaskTriggerException(this, "The daily end time is after the daily start time.");
            }
            
            if (MaxRecurs > 1 && (IntervalTime == null || IntervalTime == TimeSpan.Zero))
            {
                throw new ManagedTaskTriggerException(this, "The interval time must be set to a non-null/non-zero value.");
            }

            //loop through until we find a valid date.
            var validDateCounter = 0;
            while (!IsValidDate(startAt))
            {
                startAt = startAt.AddDays(1);

                // scan for 100 years of intervals before giving up on finding a valid date.
                validDateCounter++;
                if (validDateCounter > 36500)
                {
                    return null;
                }
            }

            //Combine that start date and time to get a final start date/time
            startAt = startAt.Add(dailyStart);
            var passDate = true;
            var recurs = 1;

            Console.WriteLine($"startAt: {startAt}, fromDate: {fromDate}");

            //loop through the intervals until we find one that is greater than the current time.
            while (startAt < fromDate && passDate)
            {
                Console.WriteLine($"startAt: {startAt}, fromDate: {fromDate}, IntervalTime: {IntervalTime}");

                if (IntervalTime != null && IntervalTime != TimeSpan.Zero)
                {
                    startAt = startAt.Add(IntervalTime.Value);
                    Console.WriteLine($"2 . startAt: {startAt}, fromDate: {fromDate}, IntervalTime: {IntervalTime}");
                }

                if (startAt > EndDate + dailyEnd)
                {
                    return null;
                }

                //if this is an invalid day, move to next day/starttime.
                if (IsValidDate(startAt) == false)
                {
                    passDate = false;
                }

                if (startAt.TimeOfDay < dailyStart || startAt.TimeOfDay > dailyEnd)
                {
                    Console.WriteLine($"2.startAt.TimeOfDay: {startAt.TimeOfDay} dailyStart: {dailyStart}, dailyEnd: {dailyEnd}");
                    passDate = false;
                }

                // if MaxRecurs == null, assume this is unlimited.
                if (passDate && (MaxRecurs == null || MaxRecurs > 0))
                {
                    recurs++;
                    if (recurs > MaxRecurs)
                    {
                        // The trigger has exceeded the maximum recurrences
                        return null;
                    }
                }
                else
                {
                    //if the day of the week is invalid, move the the start of the next valid one.
                    for (var i = 0; i < 31; i++)
                    {
                        startAt = startAt.AddDays(1);

                        if (IsValidDate(startAt))
                        {
                            break;
                        }
                    }

                    startAt = startAt.Date.Add(dailyStart);
                }

                if ((IntervalTime == null || IntervalTime == TimeSpan.Zero) && startAt < fromDate)
                {
                    return null;
                }

            }
            
            Console.WriteLine($"2 . startAt: {startAt}, fromDate: {fromDate}, IntervalTime: {IntervalTime}");
            return startAt;
        }

        private void ValidateTrigger()
        {
            if (DaysOfWeek != null && DaysOfWeek.Length == 0)
            {
                throw new ManagedTaskTriggerException(this, "No days of the week have been selected.");
            }
            if (DaysOfMonth != null && DaysOfMonth.Length == 0)
            {
                throw new ManagedTaskTriggerException(this, "No days of the month have been selected.");
            }
            if (WeeksOfMonth != null && WeeksOfMonth.Length == 0)
            {
                throw new ManagedTaskTriggerException(this, "No weeks of the month have been selected.");
            }
        }

        private bool CheckDaysOfWeek(DateTime date)
        {
            return DaysOfWeek == null || DaysOfWeek.Contains(DayOfWeek(date));
        }

        private bool CheckDaysOfMonth(DateTime date)
        {
            return DaysOfMonth == null || DaysOfMonth.Contains(date.Day);
        }

        private bool CheckWeekOfMonth(DateTime date)
        {
            if (WeeksOfMonth == null)
            {
                return true;
                
            }
            
            var validWeekOfMonth = false;
            var dayOfMonth = date.Day;
            foreach(var weekOfMonth in WeeksOfMonth)
            {
                switch(weekOfMonth)
                {
                    case EWeekOfMonth.First:
                        validWeekOfMonth = dayOfMonth <= 7;
                        break;
                    case EWeekOfMonth.Second:
                        validWeekOfMonth = dayOfMonth > 7 && dayOfMonth <= 14;
                        break;
                    case EWeekOfMonth.Third:
                        validWeekOfMonth = dayOfMonth > 14 && dayOfMonth <= 21;
                        break;
                    case EWeekOfMonth.Fourth:
                        validWeekOfMonth = dayOfMonth > 22 && dayOfMonth <= 29;
                        break;
                    case EWeekOfMonth.Last:
                        validWeekOfMonth = dayOfMonth > DateTime.DaysInMonth(date.Year, date.Month) -7;
                        break;
                }
                if(validWeekOfMonth)
                {
                    break;
                }
            }

            return validWeekOfMonth;
        }
        
        /// <summary>
        /// Confirms if the day is a valid date
        /// </summary>
        /// <param name="checkDate"></param>
        public bool IsValidDate(DateTime checkDate)
        {
            if (IntervalType == EIntervalType.None)
            {
                return false;
            }
            
            if(IntervalType == EIntervalType.Once)
            {
                return true;
            }

            var checkDates = CheckDaysOfMonth(checkDate) && CheckDaysOfWeek(checkDate) && CheckWeekOfMonth(checkDate);

            if (!checkDates)
            {
                return false;
            }

            if(SkipDates != null)
            {
                if (SkipDates.Select(skipDate => skipDate.Date).Contains(checkDate.Date))
                {
                    return false;
                }
            }

            return true;
        }

        private EDayOfWeek DayOfWeek(DateTime date)
        {
            return (EDayOfWeek)Enum.Parse(typeof(EDayOfWeek), date.DayOfWeek.ToString());
        }
    }
}
