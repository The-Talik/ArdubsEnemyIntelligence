using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kingmaker.EntitySystem.Entities;

namespace ArdubsEnemyIntelligence;

internal static partial class TargetingState
{
    // Deliberately avoid CurrentMemory/GetEnemy/GetKnowledge: even those accessors
    // can release a target lock, create memory, learn, or consume a knowledge roll.
    // All values below are stored snapshots; nothing queries another unit's stats.
    internal static string InspectKnowledge(UnitEntityData observer, IEnumerable<UnitEntityData> units,
        Func<UnitEntityData, string>? label = null)
    {
        label ??= UnitLabel;
        Enemies.TryGetValue(observer, out var memory);
        var text = new StringBuilder("Stored mod assessments, not live character stats.\n");
        text.Append(memory == null ? "No AI assessments recorded for this unit." :
            "Current target: " + (memory.CommittedTarget is null ? "none" : label(memory.CommittedTarget)) +
            "; turn lock: " + (memory.TargetLock.Target is null ? "none" : label(memory.TargetLock.Target)));
        var targets = units.Concat(memory?.Targets.Keys ?? Enumerable.Empty<UnitEntityData>())
            .Where(unit => unit is not null && !ReferenceEquals(unit, observer))
            .Distinct(ReferenceComparer<UnitEntityData>.Instance)
            .Select(unit => new { Unit = unit, Label = label(unit) })
            .OrderBy(entry => entry.Label, StringComparer.Ordinal).ToArray();
        foreach (var target in targets)
        {
            text.Append("\n\n<b>").Append(target.Label).Append("</b>\n");
            if (memory == null || !memory.Targets.TryGetValue(target.Unit, out var knowledge))
            {
                text.Append("Not assessed; all knowledge Unknown.");
                continue;
            }
            AppendInspectorKnowledge(text, knowledge, memory);
            text.Append("\n<b>Latest calculation</b>: ");
            if (memory.LastCalculations.TryGetValue(target.Unit, out var calculation))
                text.Append(calculation.Action).Append('\n').Append(LogPresentation.InGame(calculation.Details));
            else text.Append("Not scored yet.");
        }
        if (targets.Length == 0) text.Append("\nNo other combatants recorded.");
        return text.ToString();
    }

    private static void AppendInspectorKnowledge(StringBuilder text, TargetKnowledge knowledge, EnemyMemory memory)
    {
        string Number(bool known, int value) => known ? value.ToString(CultureInfo.InvariantCulture) : "Unknown";
        text.Append("<b>Offense</b>: Str ").Append(Number(knowledge.StrengthKnown, knowledge.Strength))
            .Append("; AB ").Append(Number(knowledge.AttackBonusKnown, knowledge.AttackBonus));
        if (knowledge.AttackBonusKnown) text.Append(" [").Append(knowledge.AttackWeapon).Append(']');
        text.Append("\nDamage: ").Append(knowledge.DamageEstimate == null ? "Unknown" : DescribeDamage(knowledge.DamageEstimate))
            .Append("\nSpellcasting: ").Append(knowledge.CasterKnown ? knowledge.IsCaster ? "Yes" : "No" : "Unknown")
            .Append("; highest CL: ").Append(Number(knowledge.CasterLevelKnown, knowledge.CasterLevel));
        foreach (var threat in knowledge.Threats)
            text.Append('\n').Append(ThreatLabel(threat.Key)).Append(": ")
                .Append(threat.Value.Estimate == null ? "Unknown" : DescribeThreat(threat.Key, threat.Value.Estimate));
        text.Append("\n<b>Defenses</b>: ");
        text.Append(string.Join("; ", Enum.GetValues(typeof(DefenseKind)).Cast<DefenseKind>().Select(kind =>
            AttackAssessment.Label(kind) + " " +
            (knowledge.Defenses.TryGetValue(kind, out var defense) ? Number(defense.Known, defense.Value) : "Unknown"))));
        text.Append("\nRemaining HP: ").Append(knowledge.HitPointsKnown
            ? $"{knowledge.RemainingHitPoints}/{knowledge.MaximumHitPoints}" : "Unknown")
            .Append("\nCondition vulnerabilities: ").Append(knowledge.Conditions.Known
                ? knowledge.Conditions.Conditions.ToString() : "Unknown (none recognized)")
            .Append("\nDamage/spell protection: ").Append(knowledge.DamageResponseKnown
                ? knowledge.Protection.Count == 0 ? "Known; no attack-specific assessments cached" : "Known; attack-specific assessments below"
                : "Unknown");
        if (knowledge.DamageResponseKnown)
            foreach (var protection in knowledge.Protection.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                text.Append("\n  ").Append(InspectorAttackLabel(protection.Key, memory)).Append(": ")
                    .Append(protection.Value.Description).Append("; factor ").Append(LogNumber.Format(protection.Value.Factor));
        var ineffective = knowledge.Effectiveness.Snapshot();
        text.Append("\n<b>Experience</b>: targeted ").Append(knowledge.HasTargeted ? "Yes" : "No")
            .Append("; ineffective events: ").Append(ineffective.Length == 0 ? "none recorded" : "");
        foreach (var entry in ineffective.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append("\n  ").Append(InspectorAttackLabel(entry.Key, memory)).Append(": ").Append(entry.Value);
    }

    private static string InspectorAttackLabel(string key, EnemyMemory memory) =>
        memory.Profiles.Values.SelectMany(profiles => profiles)
            .FirstOrDefault(profile => profile.EvidenceKey == key)?.Name ?? key;
}
