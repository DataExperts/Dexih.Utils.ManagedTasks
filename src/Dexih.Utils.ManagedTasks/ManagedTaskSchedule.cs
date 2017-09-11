using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace dexih.utils.ManagedTasks
{
    /// <summary>
    /// The trigger class allows a schedule to be implmeneted via the parameters.
    /// This can then be called to provide the NextTrigger, which is the next date/time the execution should occur.
    /// </summary>
    public class ManagedTaskSchedule
    {
        /// <summary>
        /// Day of the week
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public enum EDayOfWeek
        {
            Sunday = 0,
            Monday = 1,
            Tuesday = 2,
            Wednesday = 3,
            Thursday = 4,
            Friday = 5,
            Saturday = 6
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum EIntervalType
        {
            Once,
            Interval,
            Daily,
            Monthly,
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public enum EWeekOfMonth
        {
            First,
            Second,
            Third,
            Fourth,
            Last
        }

        public ManagedTaskSchedule() { }

        /// <summary>
        /// Create a trigger that starts at the specified time and executes once.
        /// </summary>
        /// <param name="StartAt">Start At Date</param>
        public ManagedTaskSchedule(DateTime StartAt)
        {
            IntervalType = EIntervalType.Once;
            StartDate = StartAt.Date;
            StartTime = StartAt.TimeOfDay;
        }

        /// <summary>
        /// Create a trigger that starts now, and executes every interval for a maximum of recurrances.
        /// </summary>
        /// <param name="intervalTime">Interval time</param>
        /// <param name="maxRecurrs">Maximum number of recurrances</param>
        public ManagedTaskSchedule(TimeSpan intervalTime, int maxRecurrs)
        {
            IntervalTime = intervalTime;
            MaxRecurrs = maxRecurrs;
        }

        /// <summary>
        /// StartDate (note, the time component is ignored, use the <see cref="StartTime"/>)
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// EndDate (note, the time component is ignored, use the <see cref="EndTime"/>)
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Type of interval to use.<para/>
        /// Once - One interval at the first valid starting time<para/>
        /// Interval - Repeat at each <see cref="IntervalTime"/><para/>
        /// Daily - Run once on each valid <see cref="DaysOfWeek"/><para/>
        /// Monthly - Run monthly<para/>
        /// </summary>
        public EIntervalType IntervalType { get; set; } = EIntervalType.Interval;

        /// <summary>
        /// Interval time is the duration between the starting times
        /// <para /> Note: this applies to interval schedule only.
        /// </summary>
        public TimeSpan? IntervalTime { get; set; }

        /// <summary>
        /// Days of week is an array of of valid days of the week for the schedule.
        /// </summary>
        public EDayOfWeek[] DaysOfWeek { get; set; }

        /// <summary>
        /// Days of the month is any array of valid days of the month for the schedule. 
        /// </summary>
        public int[] DaysOfMonth { get; set; }

        /// <summary>
        /// Days of the month is any array of valid weeks within a month for the schedule. 
        /// </summary>
        public EWeekOfMonth[] WeeksOfMonth { get; set; }

        /// <summary>
        /// List of specific dates (such as public holidays) that are skipped.
        /// </summary>
        public DateTime[] SkipDates { get; set; }

        /// <summary>
        /// StartTime is the time of the day which the job will start.
        /// </summary>
        public TimeSpan? StartTime { get; set; }

        /// <summary>
        /// EndTime is the last time of the day at job can start.
        /// </summary>
        public TimeSpan? EndTime { get; set; }

        /// <summary>
        /// Maximum number of times the schedule recurrs.  If null or -1, this will be infinite.
        /// </summary>
        public int? MaxRecurrs { get; set; }

        /// <summary>
        /// Gets a description of the trigger.
        /// </summary>
        public string Details
        {
            get
            {
                StringBuilder desc = new StringBuilder();

                switch (IntervalType)
                {
                    case EIntervalType.Once:
                        desc.AppendLine($"Once " + StartDateTimeDesc());
                        break;
                    case EIntervalType.Interval:
                        if (IntervalTime == null)
                        {
                            desc.AppendLine("Error: Interval specified, however no interval time set.");
                        }
                        else
                        {
                            desc.AppendLine($"Starts at {StartDateTimeDesc()}");
                            desc.AppendLine($"Every {IntervalTime.Value.ToString()}");
                            desc.AppendLine("Between " + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()) + " and " + (EndTime == null ? "23:59:59" : EndTime.Value.ToString()));
                            if (MaxRecurrs != null || MaxRecurrs < 0) desc.AppendLine("Maximum recurrances of " + MaxRecurrs.Value.ToString());
                        }
                        break;
                    case EIntervalType.Daily:
                        desc.AppendLine($"Daily {StartDateTimeDesc()} at:" + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()));
                        break;
                    case EIntervalType.Monthly:
                        desc.AppendLine($"Monthly {StartDateTimeDesc()} at:" + (StartTime == null ? "00:00:00" : StartTime.Value.ToString()));
                        break;
                }

                if (DaysOfWeek.Length > 0 && DaysOfWeek.Length < 7)
                {
                    desc.AppendLine("Only on day(s):" + String.Join(",", DaysOfWeek.Select(c => c.ToString()).ToArray()));
                }

                if (DaysOfMonth?.Length > 0)
                {
                    desc.AppendLine("Only on these day(s) of month:" + String.Join(",", DaysOfMonth.Select(c => c.ToString()).ToArray()));
                }

                if (WeeksOfMonth?.Length > 0)
                {
                    desc.AppendLine("Only on these week(s) of the month:" + String.Join(",", WeeksOfMonth.Select(c => c.ToString()).ToArray()));
                }

                if (SkipDates?.Length > 0)
                {
                    desc.AppendLine("Excluding the following specific dates:" + String.Join(",", SkipDates.Select(c => c.ToString()).ToArray()));
                }

                return desc.ToString();
            }
        }

        private string StartDateTimeDesc()
        {
            if(StartDate == null && StartTime == null)
            {
                return "immediately";
            }
            else if(StartDate == null)
            {
                return "from time " + StartTime.Value.ToString();
            }
            else
            {
                var startDateTime = StartDate.Value.Date;
                if(StartTime != null)
                {
                    startDateTime = startDateTime.Add(StartTime.Value);
                }
                return "from date " + startDateTime.ToString();
            }
        }


        /// <summary>
        /// Retrieves the next time this schedule will occur from the specified date.
        /// </summary>
        /// <returns>DateTime of schedule, or null if no date is available</returns>
        public DateTime? NextOcurrance(DateTime fromDate)
        {
            DateTime? nextDate = null;
            switch(IntervalType)
            {
                case EIntervalType.Daily:
                    nextDate = NextOccurranceDaily(fromDate);
                    break;
                case EIntervalType.Interval:
                    nextDate = NextOccurranceInterval(fromDate);
                    break;
                case EIntervalType.Monthly:
                    nextDate = NextOccurranceMonthly(fromDate);
                    break;
                case EIntervalType.Once:
                    nextDate = NextOccuranceOnce(fromDate);
                    break;
            }

            if(nextDate != null && nextDate > EndDate)
            {
                return null;
            }
            return nextDate;
        }

        private DateTime? NextOccuranceOnce(DateTime fromDate)
        {
            // for once of, return the start date if it in the future
            if (StartDate == null)
            {
                return null;
            }
            else
            {
                var startDateTime = StartDate.Value.Date;
                if (StartTime != null)
                {
                    startDateTime = startDateTime.Add(StartTime.Value);
                }
                if (startDateTime > fromDate)
                {
                    return startDateTime;
                }
                else
                {
                    return null;
                }
            }
        }

        private DateTime? NextOccurranceMonthly(DateTime fromDate)
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
            var currentMonth = nextDate.Month;
            while (!isValidDate || currentMonth != nextDate.Month)
            {
                isValidDate = IsValidDate(nextDate);
                if(!isValidDate)
                {
                    nextDate = nextDate.AddDays(1);
                }
            }

            if (isValidDate)
            {
                var startDateTime = new DateTime(nextDate.Year, nextDate.Month, nextDate.Day);
                if (StartTime != null)
                {
                    startDateTime.Add(StartTime.Value);
                }
                return startDateTime;
            }
            else
            {
                return null;
            }
        }

        private DateTime? NextOccurranceDaily(DateTime fromDate)
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
            while (!isValidDate || infiniteBreakCounter > 1000)
            {
                isValidDate = IsValidDate(nextDate);
                if (!isValidDate)
                {
                    nextDate = nextDate.AddDays(1);
                }
            }

            if (isValidDate)
            {
                var startDateTime = new DateTime(nextDate.Year, nextDate.Month, nextDate.Day);
                if (StartTime != null)
                {
                    startDateTime.Add(StartTime.Value);
                }
                return startDateTime;
            }
            else
            {
                return null;
            }
        }

        private DateTime? NextOccurranceInterval(DateTime fromDate)
        {
            TimeSpan dailyStart = StartTime == null ? new TimeSpan(0, 0, 0) : (TimeSpan)StartTime;
            TimeSpan dailyEnd = EndTime == null ? new TimeSpan(23, 59, 59) : (TimeSpan)EndTime;

            //set the initial start date
            DateTime startAt = StartDate == null || StartDate < fromDate ? fromDate.Date : (DateTime)StartDate.Value.Date;

            if (DaysOfWeek != null && DaysOfWeek.Length == 0)
            {
                throw new ManagedTaskTriggerException(this, "No days of the week have been selected.");
            }

            if (dailyStart > dailyEnd)
            {
                throw new ManagedTaskTriggerException(this, "The daily end time is after the daily start time.");
            }

            //loop through until we find a valid date.
            int validDateCounter = 0;
            while (!IsValidDate(startAt))
            {
                startAt = startAt.AddDays(1);

                // scan for 730 intervals before giving up on finding a valid date.
                validDateCounter++;
                if (validDateCounter > 730)
                {
                    return null;
                }
            }

            //Combine that start date and time to get a final start date/time
            startAt = startAt.Add(dailyStart);
            bool passDate = true;
            int recurrs = 1;

            //loop through the intervals until we find one that is greater than the current time.
            while (startAt < fromDate && passDate)
            {
                if (IntervalTime == null)
                {
                    if (startAt.TimeOfDay < fromDate.TimeOfDay)
                    {
                        startAt = startAt.Date.Add(fromDate.TimeOfDay);
                    }
                }
                else
                {
                    startAt = startAt.Add(IntervalTime.Value);
                }

                if (startAt > EndDate + dailyEnd)
                {
                    return null;
                }

                passDate = true;

                //if this is an invalid day, move to next day/starttime.
                if (DaysOfWeek != null)
                {
                    if (DaysOfWeek.Contains(DayOfWeek(startAt)) == false)
                    {
                        passDate = false;
                    }
                }

                if (startAt.TimeOfDay < dailyStart || startAt.TimeOfDay > dailyEnd)
                {
                    passDate = false;
                }

                if (passDate)
                {
                    recurrs++;
                    if (MaxRecurrs != null && recurrs > MaxRecurrs.Value)
                    {
                        // The trigger has exceeded the maximum recurrences
                        return null;
                    }
                }
                else
                {
                    //if the day of the week is invalid, move the the start of the next valid one.
                    if (DaysOfWeek == null)
                    {
                        startAt = startAt.AddDays(1);
                    }
                    else
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            startAt = startAt.AddDays(1);
                            if (DaysOfWeek.Contains(DayOfWeek(startAt)))
                                break;
                        }
                    }
                    startAt = startAt.Date.Add(dailyStart);
                }

                if (IntervalTime == null && startAt < fromDate)
                {
                    return null;
                }

            }
            return startAt;
        }

        /// <summary>
        /// Confirms if the day is a valid date
        /// </summary>
        /// <param name="checkDate"></param>
        public bool IsValidDate(DateTime checkDate)
        {
            if(IntervalType == EIntervalType.Once)
            {
                return true;
            }

            if(DaysOfWeek != null)
            {
                if (!DaysOfWeek.Contains(DayOfWeek(checkDate)))
                {
                    return false;
                }
            }

            if(DaysOfMonth != null)
            {
                if(!DaysOfMonth.Contains(checkDate.Day))
                {
                    return false;
                }
            }

            if(WeeksOfMonth != null)
            {
                var validWeekOfMonth = false;
                var dayOfMonth = checkDate.Day;
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
                            validWeekOfMonth = dayOfMonth > DateTime.DaysInMonth(checkDate.Year, checkDate.Month) -7;
                            break;
                    }
                    if(validWeekOfMonth)
                    {
                        break;
                    }
                }

                if(!validWeekOfMonth)
                {
                    return false;
                }
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
