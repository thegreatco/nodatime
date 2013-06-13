﻿// Copyright 2013 The Noda Time Authors. All rights reserved.
// Use of this source code is governed by the Apache License 2.0,
// as found in the LICENSE.txt file.

using System;

namespace NodaTime.Calendars
{
    internal sealed class IslamicYearMonthDayCalculator : YearMonthDayCalculator
    {
                // These are ugly, but we have unit tests which will spot if they get out of sync...
        private const int MinEpochNumber = 1;
        private const int MaxEpochNumber = 2;

        private const int MinLeapYearPatternNumber = 1;
        private const int MaxLeapYearPatternNumber = 4;

        /// <summary>Days in a pair of months, in days.</summary>
        private const int MonthPairLength = 59;

        /// <summary>The length of a long month, in days.</summary>
        private const int LongMonthLength = 30;

        /// <summary>The length of a short month, in days.</summary>
        private const int ShortMonthLength = 29;

        /// <summary>The ticks in a typical month.</summary>
        private const long TicksPerMonth = (long) (29.53056 * NodaConstants.TicksPerStandardDay);

        /// <summary>The typical number of ticks in a year.</summary>
        private const long TicksPerYear = (long) (354.36667 * NodaConstants.TicksPerStandardDay);

        /// <summary>The number of days in a non-leap year.</summary>
        private const long DaysPerNonLeapYear = 354;

        /// <summary>The number of days in a leap year.</summary>
        private const long DaysPerLeapYear = 355;

        /// <summary>The number of ticks in a non-leap year.</summary>
        private const long TicksPerNonLeapYear = DaysPerNonLeapYear * NodaConstants.TicksPerStandardDay;

        /// <summary>The number of ticks in a leap year.</summary>
        private const long TicksPerLeapYear = DaysPerLeapYear * NodaConstants.TicksPerStandardDay;

        /// <summary>The ticks for the civil (Friday) epoch of July 16th 622CE.</summary>
        private const long TicksAtCivilEpoch = -425215872000000000L;

        /// <summary>The ticks for the civil (Thursday) epoch of July 15th 622CE.</summary>
        private const long TicksAtAstronomicalEpoch = TicksAtCivilEpoch - NodaConstants.TicksPerStandardDay;

        /// <summary>The length of the cycle of leap years.</summary>
        private const int LeapYearCycleLength = 30;

        /// <summary>The number of ticks in leap cycle.</summary>
        private const long TicksPerLeapCycle = 19L * TicksPerNonLeapYear + 11 * TicksPerLeapYear;

        /// <summary>The pattern of leap years within a cycle, one bit per year, for this calendar.</summary>
        private readonly int leapYearPatternBits;
        /// <summary>The ticks at the start of the epoch for this calendar.</summary>
        private readonly long epochTicks;

        private static readonly long[] TotalTicksByMonth;

        private static readonly YearMonthDayCalculator[,] Calculators;

        static IslamicYearMonthDayCalculator()
        {
            long ticks = 0;
            TotalTicksByMonth = new long[12];
            for (int i = 0; i < 12; i++)
            {
                TotalTicksByMonth[i] = ticks;
                // Here, the month number is 0-based, so even months are long
                int days = (i & 1) == 0 ? LongMonthLength : ShortMonthLength;
                // This doesn't take account of leap years, but that doesn't matter - because
                // it's not used on the last iteration, and leap years only affect the final month
                // in the Islamic calendar.
                ticks += days * NodaConstants.TicksPerStandardDay;
            }
            Calculators = new YearMonthDayCalculator[(MaxLeapYearPatternNumber - MinLeapYearPatternNumber + 1), (MaxEpochNumber - MinEpochNumber + 1)];
            for (int i = MinLeapYearPatternNumber; i <= MaxLeapYearPatternNumber; i++)
            {
                for (int j = MinEpochNumber; j <= MaxEpochNumber; j++)
                {
                    Calculators[i - MinLeapYearPatternNumber, j - MinEpochNumber] = new IslamicYearMonthDayCalculator((IslamicLeapYearPattern) i, (IslamicEpoch) j);
                }
            }
        }

        internal static YearMonthDayCalculator GetInstance(IslamicLeapYearPattern leapYearPattern, IslamicEpoch epoch)
        {
            int leapYearPatternNumber = (int)leapYearPattern;
            int epochNumber = (int)epoch;
            if (leapYearPatternNumber < MinLeapYearPatternNumber || leapYearPatternNumber > MaxLeapYearPatternNumber)
            {
                throw new ArgumentOutOfRangeException("leapYearPattern");
            }
            if (epochNumber < MinEpochNumber || epochNumber > MaxEpochNumber)
            {
                throw new ArgumentOutOfRangeException("epoch");
            }
            return Calculators[leapYearPatternNumber - MinLeapYearPatternNumber, epochNumber - MinEpochNumber];
        }

        private IslamicYearMonthDayCalculator(IslamicLeapYearPattern leapYearPattern, IslamicEpoch epoch)
            : base(1, 31513, 12, TicksPerYear, -GetEpochTicks(epoch), new[] { Era.AnnoHegirae })
        {
            this.leapYearPatternBits = GetLeapYearPatternBits(leapYearPattern);
            this.epochTicks = GetEpochTicks(epoch);
        }

        internal override int GetYear(LocalInstant localInstant)
        {
            // This may overflow the bounds of long, but it will always be positive.
            // It's the number of ticks from the start of the epoch of this calendar.
            ulong ticksIslamic = unchecked((ulong)(localInstant.Ticks - epochTicks));            
            ulong cycles = ticksIslamic / TicksPerLeapCycle;
            ulong cycleRemainder = ticksIslamic % TicksPerLeapCycle;

            int year = (int)((cycles * LeapYearCycleLength) + 1L);
            ulong yearTicks = (ulong) GetTicksInYear(year);
            while (cycleRemainder >= yearTicks)
            {
                cycleRemainder -= yearTicks;
                year++;
                yearTicks = (ulong) GetTicksInYear(year);
            }
            return year;
        }

        internal override LocalInstant SetYear(LocalInstant localInstant, int year)
        {
            // Optimized implementation of SetYear, due to fixed months.
            int thisYear = GetYear(localInstant);
            int dayOfYear = GetDayOfYear(localInstant, thisYear);
            // Truncate final day of leap year.
            if (dayOfYear == DaysPerLeapYear && !IsLeapYear(year))
            {
                dayOfYear--;
            }
            long tickOfDay = TimeOfDayCalculator.GetTickOfDay(localInstant);

            return new LocalInstant(GetYearTicks(year) + ((dayOfYear - 1) * NodaConstants.TicksPerStandardDay) + tickOfDay);
        }

        protected override long GetTicksFromStartOfYearToStartOfMonth(int year, int month)
        {
            // The number of ticks at the *start* of a month isn't affected by
            // the year as the only month length which varies by year is the last one.
            return TotalTicksByMonth[month - 1];
        }

        internal override int GetDayOfMonth(LocalInstant localInstant)
        {
            // Note: this is zero-based
            int dayOfYear = GetDayOfYear(localInstant) - 1;
            if (dayOfYear == 354)
            {
                return 30;
            }
            return (dayOfYear % MonthPairLength) % LongMonthLength + 1; 
        }

        private long GetTicksInYear(int year)
        {
            return IsLeapYear(year) ? TicksPerLeapYear : TicksPerNonLeapYear;
        }

        internal override bool IsLeapYear(int year)
        {
            int key = 1 << (year % LeapYearCycleLength);
            return (leapYearPatternBits & key) > 0;
        }

        internal override int GetDaysInYear(int year)
        {
            return IsLeapYear(year) ? 355 : 354;
        }

        internal override int GetDaysInMonth(int year, int month)
        {
            if (month == 12 && IsLeapYear(year))
            {
                return LongMonthLength;
            }
            // Note: month is 1-based here, so even months are the short ones
            return (month & 1) == 0 ? ShortMonthLength : LongMonthLength;
        }

        internal override int GetDaysInMonthMax(int month)
        {
            if (month == 12)
            {
                return LongMonthLength;
            }
            // Note: month is 1-based here, so even months are the long ones
            return (month & 1) == 0 ? ShortMonthLength : LongMonthLength;
        }

        protected internal override int GetMonthOfYear(LocalInstant localInstant, int year)
        {
            int dayOfYearZeroBased = (int)((localInstant.Ticks - GetYearTicks(year)) / NodaConstants.TicksPerStandardDay);
            if (dayOfYearZeroBased == 354)
            {
                return 12;
            }
            return ((dayOfYearZeroBased * 2) / MonthPairLength) + 1;
        }

        protected override long CalculateYearTicks(int year)
        {
            // The first cycle starts in year 1, not year 0.
            int cycle = (year - 1) / LeapYearCycleLength;
            int yearAtStartOfCycle = (cycle * LeapYearCycleLength) + 1;

            // The cycle * TicksPerLeapCycle may overflow (near the end of representable time), but then
            // adding the epoch ticks will bring it back to a sensible range.
            long ticks = unchecked (epochTicks + cycle * TicksPerLeapCycle);

            // We've got the ticks at the start of the cycle (e.g. at the start of year 1, 31, 61 etc).
            // Now go from that year to (but not including) the year we're looking for, adding the right
            // number of ticks in each year. So if we're trying to find the start of year 34, we would
            // find the ticks at the start of year 31, then add the ticks *in* year 31, the ticks in year 32,
            // and the ticks in year 33.
            // If this ever proves to be a bottleneck, we could create an array for each IslamicLeapYearPattern
            // with "the number of ticks for the first n years in a cycle".
            for (int i = yearAtStartOfCycle; i < year; i++)
            {
                ticks += GetTicksInYear(i);
            }
            return ticks;
        }

        /// <summary>
        /// Returns the pattern of leap years within a cycle, one bit per year, for the specified pattern.
        /// Note that although cycle years are usually numbered 1-30, the bit pattern is for 0-29; cycle year
        /// 30 is represented by bit 0.
        /// </summary>
        private static int GetLeapYearPatternBits(IslamicLeapYearPattern leapYearPattern)
        {
            switch (leapYearPattern)
            {
                // When reading bit patterns, don't forget to read right to left...
                case IslamicLeapYearPattern.Base15:        return 623158436; // 0b100101001001001010010010100100
                case IslamicLeapYearPattern.Base16:        return 623191204; // 0b100101001001010010010010100100
                case IslamicLeapYearPattern.Indian:        return 690562340; // 0b101001001010010010010100100100
                case IslamicLeapYearPattern.HabashAlHasib: return 153692453; // 0b001001001010010010100100100101
                default: throw new ArgumentOutOfRangeException("leapYearPattern");
            }
        }

        /// <summary>
        /// Returns the LocalInstant ticks at the specified epoch.
        /// </summary>
        private static long GetEpochTicks(IslamicEpoch epoch)
        {
            switch (epoch)
            {
                // Epoch 1970-01-01 ISO = 1389-10-22 Islamic (civil) or 1389-10-23 Islamic (astronomical)
                case IslamicEpoch.Astronomical: return TicksAtAstronomicalEpoch;
                case IslamicEpoch.Civil:        return TicksAtCivilEpoch;
                default: throw new ArgumentOutOfRangeException("epoch");
            }
        }
    }
}
