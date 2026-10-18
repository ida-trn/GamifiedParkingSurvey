using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Centralized 1405 parking-policy values used by the survey UI, scoring and logger.
/// Money is stored in Iranian tomans.
///
/// IMPORTANT RTLTMPro NOTE:
/// RTLTMPro currently swaps numeric groups separated by punctuation when Farsi
/// numbers are enabled (for example ۲۴،۴۰۰ can render with reversed groups).
/// The formatting helpers below intentionally pre-reverse numeric groups so
/// RTLTMPro renders them in the correct visual order.
///
/// Smart on-street parking tariff used in this pilot:
/// - First 2 hours: 6,100 tomans per 30 minutes.
/// - After 2 hours: 8,000 tomans per 30 minutes.
///
/// Tow/impound information is informational only and does NOT affect score.
/// The 800,000 toman value is displayed as an approximate starting cost, not a fixed fee.
/// </summary>
public static class ParkingPolicy1405
{
    public const int SmartFirstTwoHoursPerHalfHourToman = 6100;
    public const int SmartAfterTwoHoursPerHalfHourToman = 8000;
    public const int TowApproxStartingCostToman = 800000;

    // Baseline illegal-parking fines shown to participants.
    // The scenario-level experimental manipulation multiplies these values by 1x or 2x.
    public const int NoParkingBaseFineToman = 50000;
    public const int NoStoppingBaseFineToman = 100000;
    public const int DisabledParkingFineToman = 100000;

    public static float GetFineMultiplier(ParkingScenario scenario)
    {
        return scenario != null
            ? scenario.GetIllegalFineMultiplier()
            : 1f;
    }

    public static int ApplyFineMultiplier(int baseFineToman, float multiplier)
    {
        return Mathf.Max(
            0,
            Mathf.RoundToInt(
                Mathf.Max(0, baseFineToman) *
                Mathf.Max(0f, multiplier)
            )
        );
    }

    public static int GetNoParkingFineToman(float multiplier)
    {
        return ApplyFineMultiplier(NoParkingBaseFineToman, multiplier);
    }

    public static int GetNoStoppingFineToman(float multiplier)
    {
        return ApplyFineMultiplier(NoStoppingBaseFineToman, multiplier);
    }

    /// <summary>
    /// True for illegal choices that intentionally have NO monetary citation
    /// in this study. They remain behaviorally illegal/selectable, but their
    /// citation probability, applied fine, realized cost and score deduction
    /// must all be zero.
    /// </summary>
    public static bool IsNoFinancialFineIllegalSpot(ParkingSpot spot)
    {
        if (spot == null || spot.parkingType != ParkingType.Illegal)
            return false;

        return spot.illegalReason == IllegalReason.IntersectionCorner ||
               spot.illegalReason == IllegalReason.PedestrianCrossing;
    }

    /// <summary>
    /// Returns the study baseline monetary fine for an illegal ParkingSpot.
    /// IntersectionCorner and PedestrianCrossing intentionally return 0.
    ///
    /// IMPORTANT FOR THIS STUDY:
    /// DrivewayEntrance is treated exactly like NoParkingSign for monetary
    /// fines. Therefore both use NoParkingBaseFineToman and both are affected
    /// by the same Current / Doubled scenario multiplier.
    ///
    /// NoStoppingSign uses the higher no-stopping baseline.
    /// Other fined illegal reasons keep the no-parking baseline.
    /// Legal spots return 0.
    /// </summary>
    public static int GetBaseIllegalFineToman(ParkingSpot spot)
    {
        if (spot == null || spot.parkingType != ParkingType.Illegal)
            return 0;

        if (IsNoFinancialFineIllegalSpot(spot))
            return 0;

        switch (spot.illegalReason)
        {
            case IllegalReason.NoStoppingSign:
                return NoStoppingBaseFineToman;

            case IllegalReason.NoParkingSign:
            case IllegalReason.DrivewayEntrance:
                return NoParkingBaseFineToman;

            default:
                return NoParkingBaseFineToman;
        }
    }

    public static int GetAppliedIllegalFineToman(
        ParkingSpot spot,
        ParkingScenario scenario)
    {
        return GetAppliedIllegalFineToman(
            spot,
            GetFineMultiplier(scenario)
        );
    }

    public static int GetAppliedIllegalFineToman(
        ParkingSpot spot,
        float multiplier)
    {
        return ApplyFineMultiplier(
            GetBaseIllegalFineToman(spot),
            multiplier
        );
    }

    public static string GetFinePolicyName(IllegalFinePolicyLevel level)
    {
        return level == IllegalFinePolicyLevel.Doubled
            ? "Doubled"
            : "Current";
    }

    public static string GetFinePolicyPersianLabel(IllegalFinePolicyLevel level)
    {
        return level == IllegalFinePolicyLevel.Doubled
            ? "سناریوی افزایش جریمه (۲ برابر)"
            : "تعرفه فعلی";
    }

    public static int CalculateSmartParkingFeeToman(float dwellTimeMinutes)
    {
        int safeMinutes = Mathf.Max(0, Mathf.CeilToInt(dwellTimeMinutes));

        if (safeMinutes <= 0)
            return 0;

        int halfHours = Mathf.CeilToInt(safeMinutes / 30f);

        if (halfHours <= 4)
            return halfHours * SmartFirstTwoHoursPerHalfHourToman;

        int firstTwoHours = 4 * SmartFirstTwoHoursPerHalfHourToman;
        int extraHalfHours = halfHours - 4;

        return firstTwoHours +
               extraHalfHours * SmartAfterTwoHoursPerHalfHourToman;
    }

    public static bool IsSmartParking(ParkingSpot spot)
    {
        return spot != null &&
               spot.parkingType == ParkingType.Legal &&
               spot.legalParkingFeature == LegalParkingFeature.SmartParking;
    }

    public static bool IsTowWarningEligible(ParkingSpot spot)
    {
        if (spot == null || spot.parkingType != ParkingType.Illegal)
            return false;

        return spot.illegalReason == IllegalReason.NoParkingSign ||
               spot.illegalReason == IllegalReason.NoStoppingSign;
    }

    public static string FormatInteger(int value)
    {
        return ToPersianDigits(
            value.ToString(CultureInfo.InvariantCulture)
        );
    }

    public static string FormatGroupedInteger(int value)
    {
        int safe = Mathf.Max(0, value);

        string western = safe.ToString("N0", CultureInfo.InvariantCulture);

        string[] parts = western.Split(',');

        if (parts.Length <= 1)
            return ToPersianDigits(western);

        Array.Reverse(parts);

        for (int i = 0; i < parts.Length; i++)
            parts[i] = ToPersianDigits(parts[i]);

        return string.Join("،", parts);
    }

    public static string FormatToman(float value)
    {
        int rounded = Mathf.RoundToInt(Mathf.Max(0f, value));
        return FormatGroupedInteger(rounded) + " تومان";
    }

    public static string FormatDecimal(float value, string format = "0.#")
    {
        string western = Mathf.Max(0f, value)
            .ToString(format, CultureInfo.InvariantCulture);

        if (western.Contains("."))
            return PrepareSeparatedNumberForRTLTMPro(western, '.');

        return ToPersianDigits(western);
    }

    public static string FormatPercent(float zeroToOneValue)
    {
        string western = (Mathf.Clamp01(zeroToOneValue) * 100f)
            .ToString("0.#", CultureInfo.InvariantCulture);

        string number = western.Contains(".")
            ? PrepareSeparatedNumberForRTLTMPro(western, '.')
            : ToPersianDigits(western);

        return number + " درصد";
    }

    public static string FormatClock(int minutes, int seconds)
    {
        minutes = Mathf.Max(0, minutes);
        seconds = Mathf.Clamp(seconds, 0, 59);

        string western =
            minutes.ToString(CultureInfo.InvariantCulture) +
            ":" +
            seconds.ToString("00", CultureInfo.InvariantCulture);

        return PrepareSeparatedNumberForRTLTMPro(western, ':');
    }

    private static string PrepareSeparatedNumberForRTLTMPro(
        string westernNumber,
        char separator)
    {
        if (string.IsNullOrEmpty(westernNumber))
            return westernNumber;

        string[] parts = westernNumber.Split(separator);

        if (parts.Length <= 1)
            return ToPersianDigits(westernNumber);

        Array.Reverse(parts);

        for (int i = 0; i < parts.Length; i++)
            parts[i] = ToPersianDigits(parts[i]);

        return string.Join(separator.ToString(), parts);
    }

    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        StringBuilder builder = new StringBuilder(input.Length);

        foreach (char c in input)
        {
            switch (c)
            {
                case '0': builder.Append('۰'); break;
                case '1': builder.Append('۱'); break;
                case '2': builder.Append('۲'); break;
                case '3': builder.Append('۳'); break;
                case '4': builder.Append('۴'); break;
                case '5': builder.Append('۵'); break;
                case '6': builder.Append('۶'); break;
                case '7': builder.Append('۷'); break;
                case '8': builder.Append('۸'); break;
                case '9': builder.Append('۹'); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }
}
