using System;
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

            var schedule = new ManagedTaskSchedule()
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
            var schedule = new ManagedTaskSchedule()
            {
                StartTime = currentDate.AddMinutes(-2).TimeOfDay,
                EndTime = currentDate.AddMinutes(-1).TimeOfDay
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
            var schedule = new ManagedTaskSchedule(startDate);
            Assert.Equal(startDate, schedule.NextOccurrence(DateTime.Now));

            //the schedule once should return null if the startdate is in the past.
            var startDate2 = DateTime.Now.AddHours(-1);
            var schedule2 = new ManagedTaskSchedule(startDate2);
            _output.WriteLine($"Schedule details {schedule.Details}.");
            Assert.Null(schedule2.NextOccurrence(DateTime.Now));

        }

//TODO more schedule tests

    }
}
