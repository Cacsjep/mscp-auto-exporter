using System;
using AutoExporter.Contracts;
using Xunit;

namespace AutoExporter.Contracts.Tests
{
    public class TimeRangeTests
    {
        private static readonly DateTime End = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Minutes() => Assert.Equal(End.AddMinutes(-30), TimeRange.Subtract(End, 30, "Minutes"));

        [Fact]
        public void Hours() => Assert.Equal(End.AddHours(-2), TimeRange.Subtract(End, 2, "Hours"));

        [Fact]
        public void Days() => Assert.Equal(End.AddDays(-3), TimeRange.Subtract(End, 3, "Days"));

        [Fact]
        public void Months_AreThirtyDays() => Assert.Equal(End.AddDays(-60), TimeRange.Subtract(End, 2, "Months"));

        [Fact]
        public void UnitIsCaseInsensitive() => Assert.Equal(End.AddHours(-1), TimeRange.Subtract(End, 1, "HOURS"));

        [Fact]
        public void UnknownUnit_FallsBackToDays() => Assert.Equal(End.AddDays(-5), TimeRange.Subtract(End, 5, "weeks"));

        [Fact]
        public void NullUnit_FallsBackToDays() => Assert.Equal(End.AddDays(-1), TimeRange.Subtract(End, 1, null));

        [Theory]
        [InlineData(0)]
        [InlineData(-7)]
        public void NonPositiveValue_TreatedAsOne(int value) => Assert.Equal(End.AddDays(-1), TimeRange.Subtract(End, value, "Days"));
    }
}
