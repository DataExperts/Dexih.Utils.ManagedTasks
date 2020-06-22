using System;
using System.Reflection.Metadata;
using Dexih.Utils.ManagedTasks;
using Xunit;
using Xunit.Abstractions;

namespace Dexih.Utils.Managed.Tasks.Tests
{
    public class ScheduleTests
    {
        private readonly ITestOutputHelper _output;

        public ScheduleTests(ITestOutputHelper output)
        {
            this._output = output;
        }

        /// <summary>
        /// Runs a test on two times, with a tolerance of 200ms.
        /// </summary>
        /// <param name="expectedTime"></param>
        /// <param name="actualTime"></param>
        /// <param name="millisecondTolerance"></param>
        private void TimeTest(DateTime expectedTime, DateTime actualTime, int millisecondTolerance = 200)
        {
            var tolerance = new TimeSpan(0, 0, 0, 0, millisecondTolerance);
            var expectedLowTime = expectedTime.Subtract(tolerance);
            var expectedHighTime = expectedTime.Add(tolerance);
            Assert.True(actualTime > expectedLowTime, $"The actual time {actualTime} is less than the expected time {expectedLowTime}.");
            Assert.True(actualTime < expectedHighTime, $"The actual time {actualTime} is greater than the expected time {expectedHighTime}.");
        }

        [Fact]
        public void StartDateTest()
        {
            //Set a starttime 1 minute from now
            DateTime currentDate = DateTime.Now;

            var schedule = new ManagedTaskTrigger()
            {
                StartDate = currentDate,
                StartTime = currentDate.AddMinutes(1).TimeOfDay
            };

            var nextSchedule = (DateTime)schedule.NextOccurrence(currentDate);
            _output.WriteLine($"Schedule details {schedule.Details}.");
            _output.WriteLine($"Schedule time {nextSchedule}.");

            TimeTest(DateTime.Now.AddMinutes(1), nextSchedule);
        }

        [Fact]
        public void EndTimeTest()
        {
            DateTime currentDate = DateTime.Now;

            // set a start time 2 minutes ago, with end time 1 minute ago
            // which should schedule the next run time tomorrow
            var schedule = new ManagedTaskTrigger()
            {
                StartTime = currentDate.AddMinutes(-2).TimeOfDay,
                EndTime = currentDate.AddMinutes(-1).TimeOfDay,
                IntervalType = EIntervalType.Daily
            };

            var nextSchedule = (DateTime)schedule.NextOccurrence(currentDate);
            _output.WriteLine($"Schedule details {schedule.Details}.");
            _output.WriteLine($"Schedule time {nextSchedule}.");

            TimeTest(DateTime.Now.AddDays(1).AddMinutes(-2), nextSchedule);
        }
        
        [Fact]
        public void ScheduleTestOnce()
        {
            //the schedule once, should be return the same date if in the future.
            var startDate = DateTime.Now.AddHours(1);
            var schedule = new ManagedTaskTrigger(startDate);
            Assert.Equal(startDate, schedule.NextOccurrence(DateTime.Now));

            //the schedule once should return null if the startdate is in the past.
            var startDate2 = DateTime.Now.AddHours(-1);
            var schedule2 = new ManagedTaskTrigger(startDate2);
            _output.WriteLine($"Schedule details {schedule.Details}.");
            Assert.Null(schedule2.NextOccurrence(DateTime.Now));

        }
        
        [Fact]
        public void ScheduleOnceDaily()
        {
            //the schedule once, should be return the same date if in the future.
            var startDate = DateTime.Now;
            var schedule = new ManagedTaskTrigger()
            {
                IntervalType = EIntervalType.Interval,
                StartDate = startDate.AddHours(-24),
                StartTime = new TimeSpan(1,0,0),
                MaxRecurs = 0,
                DaysOfWeek = new[]
                {
                    EDayOfWeek.Friday, EDayOfWeek.Monday, EDayOfWeek.Saturday, EDayOfWeek.Sunday, EDayOfWeek.Thursday,
                    EDayOfWeek.Tuesday, EDayOfWeek.Wednesday
                }
            };

            var scheduled = schedule.NextOccurrence(startDate);
            
            // add 25 hours to the base date which will give expected date next day at 1am.
            var expected = startDate.Date.AddHours(25).ToUniversalTime();
            Assert.Equal(expected, scheduled.Value.ToUniversalTime());
        }
        
        [Fact]
        public void ScheduleOnDayOfWeek()
        {
            //the schedule once, should be return the same date if in the future.
            var startDate = DateTime.Now;

            var expectedDate = startDate;
            // get the next friday date
            while (expectedDate.DayOfWeek != DayOfWeek.Friday)
            {
                expectedDate = expectedDate.AddDays(1);
            }
            
            // this date is a monday
            
            var schedule = new ManagedTaskTrigger()
            {
                IntervalType = EIntervalType.Interval,
                StartDate = startDate,
                StartTime = new TimeSpan(1,0,0),
                MaxRecurs = 0,
                DaysOfWeek = new[]
                {
                    EDayOfWeek.Friday
                }
            };

            var scheduled = schedule.NextOccurrence(startDate);
            
            // the expected date will be on the following friday
            var expected = expectedDate.Date.AddHours(1).ToUniversalTime();
            Assert.Equal(expected, scheduled.Value.ToUniversalTime());
        }
        
        [Fact]
        public void ScheduleOnDayOfMonth()
        {
            //the schedule once, should be return the same date if in the future.
            var startDate = DateTime.Now;

            var expectedDate = startDate;
            // get the next 10th of month date
            while (expectedDate.Day != 10)
            {
                expectedDate = expectedDate.AddDays(1);
            }
            
            // this date is a monday
            
            var schedule = new ManagedTaskTrigger()
            {
                IntervalType = EIntervalType.Interval,
                StartDate = startDate,
                StartTime = new TimeSpan(1,0,0),
                MaxRecurs = 0,
                DaysOfMonth = new[]
                {
                    10
                }
            };

            var scheduled = schedule.NextOccurrence(startDate);
            
            // the expected date will be on the following friday
            var expected = expectedDate.Date.AddHours(1).ToUniversalTime();
            Assert.Equal(expected, scheduled.Value.ToUniversalTime());
        }
        
        [Fact]
        public void ScheduleOnDayWeekAndDayOfMonth()
        {
            //the schedule once, should be return the same date if in the future.
            var startDate = DateTime.Now;

            var expectedDate = startDate;
            // get the next 10th of month date, that is a friday
            while (expectedDate.Day != 10 || expectedDate.DayOfWeek != DayOfWeek.Friday)
            {
                expectedDate = expectedDate.AddDays(1);
            }
            
            // this date is a monday
            
            var schedule = new ManagedTaskTrigger()
            {
                IntervalType = EIntervalType.Interval,
                StartDate = startDate,
                StartTime = new TimeSpan(1,0,0),
                MaxRecurs = 0,
                DaysOfMonth = new[]
                {
                    10
                },
                DaysOfWeek= new[]
                {
                    EDayOfWeek.Friday
                }
            };

            var scheduled = schedule.NextOccurrence(startDate);
            
            // the expected date will be on the following friday
            var expected = expectedDate.Date.AddHours(1).ToUniversalTime();
            Assert.Equal(expected, scheduled.Value.ToUniversalTime());
        }
        
//TODO more schedule tests

    }
}
