namespace ArdubsEnemyIntelligence;

internal readonly struct TargetScoreBreakdown
{
    private bool IsEligible { get; }
    private decimal RawRisk { get; }
    private decimal RawSquish { get; }
    private decimal RawDist { get; }
    private decimal RiskImportance { get; }
    private decimal SquishImportance { get; }
    private decimal DistanceImportance { get; }
    internal decimal Risk => ScoringWeights.Importance(RawRisk, RiskImportance);
    internal decimal Squish => ScoringWeights.Importance(RawSquish, SquishImportance);
    internal decimal Dist => ScoringWeights.Importance(RawDist, DistanceImportance);
    internal decimal Score => IsEligible ? Risk * Squish * Dist : 0m;

    internal TargetScoreBreakdown(decimal eligibilityScore, decimal strengthFactor,
        decimal casterFactor, decimal armorFactor, decimal ineffectiveFactor, decimal distanceFactor = 1m,
        decimal riskImportance = 1m, decimal squishImportance = 1m, decimal distanceImportance = 1m)
    {
        // The game's score is only an eligibility gate, never a weight. Every
        // positive input starts equally; zero/negative inputs remain rejected.
        IsEligible = eligibilityScore > 0m;
        RawRisk = strengthFactor * casterFactor;
        RawSquish = armorFactor * ineffectiveFactor;
        RawDist = distanceFactor;
        RiskImportance = riskImportance;
        SquishImportance = squishImportance;
        DistanceImportance = distanceImportance;
    }

    internal TargetScoreBreakdown WithDistance(decimal factor) =>
        new TargetScoreBreakdown(IsEligible ? 1m : 0m, RawRisk, 1m, RawSquish, 1m, factor,
            RiskImportance, SquishImportance, DistanceImportance);

    internal TargetScoreBreakdown WithSquish(decimal factor) =>
        new TargetScoreBreakdown(IsEligible ? 1m : 0m, RawRisk, 1m, factor, 1m, RawDist,
            RiskImportance, SquishImportance, DistanceImportance);

    internal string Describe(string strengthTerm, string casterTerm,
        string armorTerm, string ineffectiveTerm, string distanceTerm = "pending game path evaluation")
    {
        return $"Risk: {Number(RawRisk)} = {strengthTerm}" +
            (casterTerm.Length == 0 ? "\n" : $" * {casterTerm}\n") +
            $"Squish: {Number(RawSquish)} = {armorTerm}" +
            (ineffectiveTerm.Length == 0 ? "\n" : $" * {ineffectiveTerm}\n") +
            $"Dist: {Number(RawDist)} = {distanceTerm}\n" +
            DescribeWeight("Risk", RawRisk, Risk, RiskImportance) +
            DescribeWeight("Squish", RawSquish, Squish, SquishImportance) +
            DescribeWeight("Dist", RawDist, Dist, DistanceImportance) +
            $"Math: {Number(Score)} = Risk[{Number(Risk)}] * " +
            $"Squish[{Number(Squish)}] * Dist[{Number(Dist)}]";
    }

    private static string Number(decimal value)
    {
        return LogNumber.Format(value);
    }

    private static string DescribeWeight(string label, decimal raw, decimal weighted, decimal importance) =>
        importance == 1m ? "" : $"  {label} weight: {Number(weighted)} = " +
            $"Raw[{Number(raw)}] ^ Importance[{Number(importance)}]" +
            (raw == 0m ? " [zero remains unavailable]" : "") + "\n";
}
