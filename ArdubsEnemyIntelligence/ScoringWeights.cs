using System;

namespace ArdubsEnemyIntelligence;

internal static class ScoringWeights
{
    internal static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) &&
        value >= 0f && decimal.TryParse(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    internal static decimal Read(float value)
    {
        if (!IsValid(value)) throw new ArgumentOutOfRangeException(nameof(value), "Weight must be finite, nonnegative and representable.");
        return decimal.Parse(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Powers tune relative importance around neutral 1; a global multiplier on
    // every score would not change targeting. Genuine zero scores stay zero.
    internal static decimal Importance(decimal factor, decimal weight)
    {
        if (factor < 0m || weight < 0m) throw new ArgumentOutOfRangeException(nameof(factor));
        if (factor == 0m || weight == 1m) return factor;
        if (weight == 0m || factor == 1m) return 1m;
        var result = Math.Pow((double)factor, (double)weight);
        if (double.IsNaN(result) || double.IsInfinity(result) || result == 0d ||
            result > (double)decimal.MaxValue || result < 1e-28)
            throw new ArithmeticException("Weighted targeting score is outside representable precision; lower the tuning weight.");
        return (decimal)result;
    }
}
