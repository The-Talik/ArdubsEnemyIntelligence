using System.Text.RegularExpressions;

namespace ArdubsEnemyIntelligence;

internal static class LogPresentation
{
    private static readonly Regex Headings = new Regex(
        @"^(?<indent>[ \t]*)(?<label>(?:Risk|Squish|Dist)(?: weight)?|Math|Damage|Routine):(?= )",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // Presentation belongs at the combat-log boundary. The source message and
    // UMM output remain plain text, and values/inline factor names stay normal.
    internal static string InGame(string message) => Headings.Replace(message,
        match => match.Groups["indent"].Value + "<b>" + match.Groups["label"].Value + ":</b>");
}
