using System;
using System.Globalization;

namespace ArdubsEnemyIntelligence;

// Display only: never round the inputs to any scoring calculation.
internal static class LogNumber
{
    internal static string Format(decimal value)
    {
        if (value != 0m && Math.Round(value, 2) == 0m)
            return value > 0m ? "<0.01" : ">-0.01";
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
