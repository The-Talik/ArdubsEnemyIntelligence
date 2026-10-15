using System;
using System.Globalization;
using UnityEngine;
using UnityModManagerNet;

namespace ArdubsEnemyIntelligence;

public sealed class Settings : UnityModManager.ModSettings
{
    public bool ApplyToEnemies = true;
    public bool ApplyToAlliedNpcs = true;
    public bool AvoidSleepingTargets = true;
    public bool FixAttackOfOpportunityAwareness = true;
    public bool ExpandReinforcementDetection = true;
    public bool DefaultReinforcementRangeMultiplier = true;
    public float ReinforcementRangeMultiplier = 2f;
    private string? reinforcementRangeMultiplierText;
    internal float ActiveReinforcementRangeMultiplier => DefaultReinforcementRangeMultiplier ? 2f : ReinforcementRangeMultiplier;
    public bool EnableBodyBlocking = true;
    public bool BlockHostilePassage = true;
    public bool AllowAlliedPassage = true;
    public bool IgnoreFriendlyBlockingOnFiveFootStep = true;
    public bool AllowFriendlyStoppingOverlap;
    public bool AllowFlyingEnemyPassage = true;
    public bool IgnoreDownedUnitSpace = true;
    public bool PreventStoppingOverlap = true;
    public bool BodyBlockingMovementGuard = true;
    public bool PreserveSpecialBodyMovement = true;
    public bool BodyBlockingCharges = true;
    public bool ShowBodyBlockingInLog = true;
    public bool ProfileCursorPerformance;
    public bool ShowDestinationFootprint = true;
    public bool ShowFlyingPassageVisual = true;
    // Retained so an existing single-toggle setting can initialize both destinations.
    public bool DebugLogging = true;
    public bool SeparateLoggingConfigured;
    public bool InGameLogging = true;
    public bool UmmLogging = true;
    public bool ShowLearningInLog = true;
    public bool ShowKnowledgeInHoverInspector;

    public bool DefaultReassessmentRange = true;
    public int ReassessmentMinimum = 1;
    public int ReassessmentMaximum = 20;
    public bool DefaultStrengthRange = true;
    public int StrengthMinimum = 1;
    public int StrengthMaximum = 10;
    public bool DefaultAttackBonusRange = true;
    public int AttackBonusMinimum = 5;
    public int AttackBonusMaximum = 18;
    public bool DefaultDamageRange = true;
    public int DamageMinimum = 8;
    public int DamageMaximum = 22;
    public bool DefaultCasterRange = true;
    public int CasterMinimum = 5;
    public int CasterMaximum = 16;
    public bool DefaultCasterLevelRange = true;
    public int CasterLevelMinimum = 10;
    public int CasterLevelMaximum = 25;
    public bool DefaultArmorClassRange = true;
    public int ArmorClassMinimum = 10;
    public int ArmorClassMaximum = 25;
    public bool DefaultTouchArmorClassRange = true;
    public int TouchArmorClassMinimum = 10;
    public int TouchArmorClassMaximum = 25;
    public bool DefaultSavingThrowRange = true;
    public int SavingThrowMinimum = 10;
    public int SavingThrowMaximum = 25;
    public bool DefaultConditionVulnerabilityRange = true;
    public int ConditionVulnerabilityMinimum = 5;
    public int ConditionVulnerabilityMaximum = 15;
    public bool DefaultHitPointsRange = true;
    public int HitPointsMinimum = 5;
    public int HitPointsMaximum = 15;
    public bool DefaultSneakRange = true;
    public int SneakMinimum = 10;
    public int SneakMaximum = 25;
    public bool DefaultExtraDamageRange = true;
    public int ExtraDamageMinimum = 8;
    public int ExtraDamageMaximum = 22;
    public bool DefaultStatDrainRange = true;
    public int StatDrainMinimum = 10;
    public int StatDrainMaximum = 25;
    public bool DefaultRoutineRange = true;
    public int RoutineMinimum = 8;
    public int RoutineMaximum = 22;
    public bool DefaultDamageResponseRange = true;
    public int DamageResponseMinimum = 10;
    public int DamageResponseMaximum = 25;

    public bool DefaultCasterThreatMultiplier = true;
    public float CasterThreatMultiplier = 1f;
    private string? casterThreatMultiplierText;
    internal decimal ActiveCasterThreatMultiplier => DefaultCasterThreatMultiplier ? 1m : ScoringWeights.Read(CasterThreatMultiplier);
    public bool DefaultPhysicalThreatMultiplier = true;
    public float PhysicalThreatMultiplier = 1f;
    private string? physicalThreatMultiplierText;
    internal decimal ActivePhysicalThreatMultiplier => DefaultPhysicalThreatMultiplier ? 1m : ScoringWeights.Read(PhysicalThreatMultiplier);
    public bool DefaultSneakThreatMultiplier = true;
    public float SneakThreatMultiplier = 1f;
    private string? sneakThreatMultiplierText;
    internal decimal ActiveSneakThreatMultiplier => DefaultSneakThreatMultiplier ? 1m : ScoringWeights.Read(SneakThreatMultiplier);
    public bool DefaultExtraDamageThreatMultiplier = true;
    public float ExtraDamageThreatMultiplier = 1f;
    private string? extraDamageThreatMultiplierText;
    internal decimal ActiveExtraDamageThreatMultiplier => DefaultExtraDamageThreatMultiplier ? 1m : ScoringWeights.Read(ExtraDamageThreatMultiplier);
    public bool DefaultStatDrainThreatMultiplier = true;
    public float StatDrainThreatMultiplier = 1f;
    private string? statDrainThreatMultiplierText;
    internal decimal ActiveStatDrainThreatMultiplier => DefaultStatDrainThreatMultiplier ? 1m : ScoringWeights.Read(StatDrainThreatMultiplier);
    public bool DefaultRiskImportance = true;
    public float RiskImportance = 1f;
    private string? riskImportanceText;
    internal decimal ActiveRiskImportance => DefaultRiskImportance ? 1m : ScoringWeights.Read(RiskImportance);
    public bool DefaultSquishImportance = true;
    public float SquishImportance = 1f;
    private string? squishImportanceText;
    internal decimal ActiveSquishImportance => DefaultSquishImportance ? 1m : ScoringWeights.Read(SquishImportance);
    public bool DefaultHitPointsImportance = true;
    public float HitPointsImportance = 1f;
    private string? hitPointsImportanceText;
    internal decimal ActiveHitPointsImportance => DefaultHitPointsImportance ? 1m : ScoringWeights.Read(HitPointsImportance);
    public bool DefaultDistanceImportance = true;
    public float DistanceImportance = 1f;
    private string? distanceImportanceText;
    internal decimal ActiveDistanceImportance => DefaultDistanceImportance ? 1m : ScoringWeights.Read(DistanceImportance);

    private string? reassessmentMinimumText;
    private string? reassessmentMaximumText;
    private string? strengthMinimumText;
    private string? strengthMaximumText;
    private string? attackBonusMinimumText;
    private string? attackBonusMaximumText;
    private string? damageMinimumText;
    private string? damageMaximumText;
    private string? casterMinimumText;
    private string? casterMaximumText;
    private string? casterLevelMinimumText;
    private string? casterLevelMaximumText;
    private string? armorClassMinimumText;
    private string? armorClassMaximumText;
    private string? touchArmorClassMinimumText;
    private string? touchArmorClassMaximumText;
    private string? savingThrowMinimumText;
    private string? savingThrowMaximumText;
    private string? conditionVulnerabilityMinimumText;
    private string? conditionVulnerabilityMaximumText;
    private string? hitPointsMinimumText;
    private string? hitPointsMaximumText;
    private string? sneakMinimumText;
    private string? sneakMaximumText;
    private string? extraDamageMinimumText;
    private string? extraDamageMaximumText;
    private string? statDrainMinimumText;
    private string? statDrainMaximumText;
    private string? routineMinimumText;
    private string? routineMaximumText;
    private string? damageResponseMinimumText;
    private string? damageResponseMaximumText;

    internal int ThreatMinimum(ThreatKnowledge kind) => kind switch
    {
        ThreatKnowledge.Sneak => DefaultSneakRange ? 10 : SneakMinimum,
        ThreatKnowledge.ExtraDamage => DefaultExtraDamageRange ? 8 : ExtraDamageMinimum,
        ThreatKnowledge.StatDrain => DefaultStatDrainRange ? 10 : StatDrainMinimum,
        ThreatKnowledge.Routine => DefaultRoutineRange ? 8 : RoutineMinimum,
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
    };
    internal int ThreatMaximum(ThreatKnowledge kind) => kind switch
    {
        ThreatKnowledge.Sneak => DefaultSneakRange ? 25 : SneakMaximum,
        ThreatKnowledge.ExtraDamage => DefaultExtraDamageRange ? 22 : ExtraDamageMaximum,
        ThreatKnowledge.StatDrain => DefaultStatDrainRange ? 25 : StatDrainMaximum,
        ThreatKnowledge.Routine => DefaultRoutineRange ? 22 : RoutineMaximum,
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
    };
    internal int ActiveDamageResponseMinimum => DefaultDamageResponseRange ? 10 : DamageResponseMinimum;
    internal int ActiveDamageResponseMaximum => DefaultDamageResponseRange ? 25 : DamageResponseMaximum;
    internal int ActiveConditionVulnerabilityMinimum => DefaultConditionVulnerabilityRange ? 5 : ConditionVulnerabilityMinimum;
    internal int ActiveConditionVulnerabilityMaximum => DefaultConditionVulnerabilityRange ? 15 : ConditionVulnerabilityMaximum;
    internal int ActiveHitPointsMinimum => DefaultHitPointsRange ? 5 : HitPointsMinimum;
    internal int ActiveHitPointsMaximum => DefaultHitPointsRange ? 15 : HitPointsMaximum;
    internal bool ModEnabled => ApplyToEnemies || ApplyToAlliedNpcs;
    internal bool BodyBlockingEnabled => ModEnabled && EnableBodyBlocking;
    internal bool AnyLogging => ModEnabled && (InGameLogging || UmmLogging);
    internal int ActiveReassessmentMinimum => DefaultReassessmentRange ? 1 : ReassessmentMinimum;
    internal int ActiveReassessmentMaximum => DefaultReassessmentRange ? 20 : ReassessmentMaximum;
    internal int ActiveStrengthMinimum => DefaultStrengthRange ? 1 : StrengthMinimum;
    internal int ActiveStrengthMaximum => DefaultStrengthRange ? 10 : StrengthMaximum;
    internal int ActiveAttackBonusMinimum => DefaultAttackBonusRange ? 5 : AttackBonusMinimum;
    internal int ActiveAttackBonusMaximum => DefaultAttackBonusRange ? 18 : AttackBonusMaximum;
    internal int ActiveDamageMinimum => DefaultDamageRange ? 8 : DamageMinimum;
    internal int ActiveDamageMaximum => DefaultDamageRange ? 22 : DamageMaximum;
    internal int ActiveCasterMinimum => DefaultCasterRange ? 5 : CasterMinimum;
    internal int ActiveCasterMaximum => DefaultCasterRange ? 16 : CasterMaximum;
    internal int ActiveCasterLevelMinimum => DefaultCasterLevelRange ? 10 : CasterLevelMinimum;
    internal int ActiveCasterLevelMaximum => DefaultCasterLevelRange ? 25 : CasterLevelMaximum;
    internal int ActiveArmorClassMinimum => DefaultArmorClassRange ? 10 : ArmorClassMinimum;
    internal int ActiveArmorClassMaximum => DefaultArmorClassRange ? 25 : ArmorClassMaximum;
    internal int DefenseMinimum(DefenseKind kind) => kind == DefenseKind.ArmorClass ? ActiveArmorClassMinimum :
        kind == DefenseKind.TouchArmorClass ? (DefaultTouchArmorClassRange ? 10 : TouchArmorClassMinimum) :
        DefaultSavingThrowRange ? 10 : SavingThrowMinimum;
    internal int DefenseMaximum(DefenseKind kind) => kind == DefenseKind.ArmorClass ? ActiveArmorClassMaximum :
        kind == DefenseKind.TouchArmorClass ? (DefaultTouchArmorClassRange ? 25 : TouchArmorClassMaximum) :
        DefaultSavingThrowRange ? 25 : SavingThrowMaximum;

    internal void MigrateLegacyLogging()
    {
        if (SeparateLoggingConfigured)
        {
            return;
        }

        InGameLogging = DebugLogging;
        UmmLogging = DebugLogging;
        SeparateLoggingConfigured = true;
    }

    private static readonly Settings WotrPathDefaults = PresetSnapshot(PathfindingPreset.Wotr);
    private static readonly Settings ResetPathDefaults = PresetSnapshot(PathfindingPreset.Reset);
    private static readonly Settings PnpPathDefaults = PresetSnapshot(PathfindingPreset.Pnp);

    private static Settings PresetSnapshot(PathfindingPreset preset)
    {
        var result = new Settings();
        result.ConfigurePathfindingPreset(preset);
        return result;
    }

    private static GUIContent PathOption(string label, Func<Settings, bool> checkedValue, string? tooltip = null)
    {
        static string State(bool enabled) => enabled ? "On" : "Off";
        return new GUIContent($"{label} (Default {State(checkedValue(WotrPathDefaults))}/" +
            $"{State(checkedValue(ResetPathDefaults))}/{State(checkedValue(PnpPathDefaults))})", tooltip);
    }

    public void Draw()
    {
        DrawSection("Intelligence", () =>
        {
            DrawSubheading("Scope");
            DrawScope();
            GUILayout.Space(8);
            DrawSubheading("Options");
            AvoidSleepingTargets = GUILayout.Toggle(AvoidSleepingTargets,
                new GUIContent("Enemies: avoid sleeping targets while active opponents remain (Default On)",
                    "This option only affects enemy AI. Unreachable or unseen awake combat opponents still count. Enemy AI can target sleepers once all other opponents are dead or incapacitated. Enabled allied NPCs never directly target sleepers, even with this option off. Player commands are unchanged."));
            FixAttackOfOpportunityAwareness = GUILayout.Toggle(FixAttackOfOpportunityAwareness,
                new GUIContent("Fix AoO awareness check (Default On)",
                    "In turn-based combat, the attacker must know about the provoking unit, not vice versa. Being unaware of an attacker no longer prevents that attacker's opportunity attack. Applies to both sides, including player characters. Native reach, concealment, reaction limits, conditions, and movement exemptions are unchanged. Off restores the vanilla awareness check."));
            ExpandReinforcementDetection = GUILayout.Toggle(ExpandReinforcementDetection,
                new GUIContent("Expand enemy reinforcement detection (Default On)",
                    "While a turn-based battle is underway, eligible waiting enemies can spot its participants farther away. Requires native visibility and a complete terrain-clearance route. One qualifying observer alerts its group through vanilla combat joining. Script-controlled, passive, ambush, non-aggro, and explicit-vision-override groups are left alone. Ordinary exploration detection is unchanged."));
            DrawWeight("Reinforcement detection multiplier", "Multiplier of the waiting enemy's normal sight range. 1 keeps vanilla detection. Only applies with enemy scope and reinforcement detection enabled.",
                ref DefaultReinforcementRangeMultiplier, ref ReinforcementRangeMultiplier,
                ref reinforcementRangeMultiplierText, 2f, 1f);
            GUILayout.Space(8);
            DrawSubheading("Learning");
            DrawDescription("Values are Intelligence scores: 0% chance at or below the 0% value; 100% chance at or above the 100% value.");
            DrawIntelligenceOptions();
            GUILayout.Space(8);
            DrawSubheading("Attack Preferences");
            DrawWeighting();
        });
        DrawSection("Pathfinding", DrawPathfinding);
        DrawSection("Logging", DrawLogging);
        DrawSection("Developer", () =>
        {
            ShowKnowledgeInHoverInspector = GUILayout.Toggle(ShowKnowledgeInHoverInspector,
                new GUIContent("Show AI knowledge in hover and expanded inspectors (Default Off)",
                    "Shows the inspected unit's stored assessments and latest Risk / Squish / Dist calculation for each other combatant in the hover tooltip and click-to-expand info window. Unknown and not assessed are distinct. Read-only: inspecting never teaches the AI or recalculates scores. Reopen the inspector to update the snapshot."));
        });
    }

    private static void DrawSection(string heading, Action content)
    {
        GUILayout.Space(8);
        GUILayout.Box(GUIContent.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
        GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
        var headingStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize = System.Math.Max(GUI.skin.label.fontSize + 2, 15)
        };
        GUILayout.Label(heading, headingStyle, GUILayout.Width(120));
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        try { content(); }
        finally
        {
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
    }

    private static void DrawSubheading(string heading) =>
        GUILayout.Label(heading, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

    private static void DrawDescription(string description)
    {
        var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
        style.normal.textColor = new Color(0.67f, 0.78f, 0.84f);
        GUILayout.Label(description, style);
    }

    private void DrawScope()
    {
        var oldEnemies = ApplyToEnemies;
        var oldAllies = ApplyToAlliedNpcs;
        ApplyToEnemies = GUILayout.Toggle(ApplyToEnemies, new GUIContent("Apply to Enemies (Default On)",
            "If both scope options are off, targeting, body blocking, footprint displays and gameplay logs are inactive."));
        ApplyToAlliedNpcs = GUILayout.Toggle(ApplyToAlliedNpcs, new GUIContent("Apply to allied NPCs (Default On)",
            "Also use improved targeting for non-player-controlled friendly NPCs, including neutral-faction members of friendly combat groups. These NPCs never directly attack sleeping opponents. Party members, pets, allied summons and unrelated neutrals stay vanilla."));
        if (oldEnemies != ApplyToEnemies || oldAllies != ApplyToAlliedNpcs)
        {
            TargetingState.Reset();
            RefreshBodyRules();
        }
    }

    private void DrawPathfinding()
    {
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && ModEnabled;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("WotR Defaults", "Disable all pathfinding overrides and footprint displays; AI settings and logging are unchanged.")))
            ApplyPathfindingPreset(PathfindingPreset.Wotr);
        if (GUILayout.Button(new GUIContent("Mod Defaults", "Restore this mod's pathfinding defaults; AI settings and logging are unchanged.")))
            ApplyPathfindingPreset(PathfindingPreset.Reset);
        if (GUILayout.Button(new GUIContent("PnP Defaults", "Closest supported Pathfinder 1e settings, not full tabletop rules. No friendly stopping, including five-foot steps. No squeezing/Acrobatics or hidden-creature collision system.")))
            ApplyPathfindingPreset(PathfindingPreset.Pnp);
        GUILayout.EndHorizontal();
        var oldBodyBlocking = EnableBodyBlocking;
        EnableBodyBlocking = GUILayout.Toggle(EnableBodyBlocking, PathOption("Enable combat body blocking",
            settings => settings.EnableBodyBlocking,
            "Turn-based combat only, for party members and NPCs. Inactive when both AI scope options are off."));
        if (oldBodyBlocking != EnableBodyBlocking) RefreshBodyRules();
        GUI.enabled = previousEnabled && BodyBlockingEnabled;
        BlockHostilePassage = GUILayout.Toggle(BlockHostilePassage, PathOption(
            "Block passage through visible enemies", settings => settings.BlockHostilePassage));
        AllowAlliedPassage = !GUILayout.Toggle(!AllowAlliedPassage, PathOption("Block passage through allies",
            settings => !settings.AllowAlliedPassage,
            "Unchecked preserves normal friendly passage. Friendly five-foot-step exceptions can bypass this restriction; charges block allies when charge blocking is enabled."));
        IgnoreFriendlyBlockingOnFiveFootStep = GUILayout.Toggle(IgnoreFriendlyBlockingOnFiveFootStep,
            PathOption("Ignore friendly blocking during a 5 ft step",
                settings => settings.IgnoreFriendlyBlockingOnFiveFootStep,
                "The Mod Defaults preset enables this; PnP disables it. Native five-foot steps may pass through and stop inside friendly space. Hostile blocking and the native step range remain unchanged."));
        AllowFriendlyStoppingOverlap = GUILayout.Toggle(AllowFriendlyStoppingOverlap,
            PathOption("Allow stopping in friendly occupied space",
                settings => settings.AllowFriendlyStoppingOverlap,
                "Allows ending movement/turns overlapping allies, while enemies retain occupied-endpoint protection. Ordinary friendly passage still follows its separate setting."));
        AllowFlyingEnemyPassage = GUILayout.Toggle(AllowFlyingEnemyPassage, PathOption("Allow flying units to pass through enemies",
            settings => settings.AllowFlyingEnemyPassage,
            "Uses the active Flying mechanics feature. Passage is allowed, but overlapping stopping positions remain prohibited when that setting is enabled."));
        ShowFlyingPassageVisual = GUILayout.Toggle(ShowFlyingPassageVisual, PathOption("Show flying-passage visual",
            settings => settings.ShowFlyingPassageVisual,
            "Marks bypassed enemy footprints in pale blue and smoothly lifts only the rendered model while a mechanically flying unit crosses them."));
        IgnoreDownedUnitSpace = GUILayout.Toggle(IgnoreDownedUnitSpace, PathOption("Allow passage and stopping on sleeping/downed units",
            settings => settings.IgnoreDownedUnitSpace,
            "Sleeping and unconscious-condition units do not occupy space. Conscious prone units still do. Dead and HP-unconscious units remain excluded regardless of this option."));
        PreventStoppingOverlap = GUILayout.Toggle(PreventStoppingOverlap, PathOption(
            "Prevent overlapping stopping positions", settings => settings.PreventStoppingOverlap));
        BodyBlockingMovementGuard = GUILayout.Toggle(BodyBlockingMovementGuard, PathOption("Check body clearance during movement",
            settings => settings.BodyBlockingMovementGuard,
            "Safety check for changed occupants and precomputed paths. Disabling leaves planning/simulation checks only."));
        PreserveSpecialBodyMovement = !GUILayout.Toggle(!PreserveSpecialBodyMovement, PathOption("Apply blocking to special/incorporeal/swarm units",
            settings => !settings.PreserveSpecialBodyMovement,
            "Unchecked preserves native movement exemptions. Checked opts these units into the mod's body rules."));
        BodyBlockingCharges = GUILayout.Toggle(BodyBlockingCharges, PathOption("Also apply to charges",
            settings => settings.BodyBlockingCharges,
            "Enabled by Mod Defaults and PnP Defaults. Forced movement, teleportation and active cutscenes always remain native."));
        GUI.enabled = previousEnabled && ModEnabled;
        ShowDestinationFootprint = GUILayout.Toggle(ShowDestinationFootprint, PathOption("Show character footprint at destination pin",
            settings => settings.ShowDestinationFootprint,
            "Cyan body-size outline at movement previews and destination pins in turn-based combat only. Follows nearby ground; independent of body blocking."));
        GUI.enabled = previousEnabled;
    }

    private void DrawLogging()
    {
        InGameLogging = GUILayout.Toggle(InGameLogging, "Show targeting details in the in-game combat log (Default On)");
        UmmLogging = GUILayout.Toggle(UmmLogging, "Write targeting details to the UMM log (Default On)");
        ShowLearningInLog = GUILayout.Toggle(ShowLearningInLog, "Show learning in log (Default On)");
        ShowBodyBlockingInLog = GUILayout.Toggle(ShowBodyBlockingInLog, "Log pathfinding diagnostics (Default On)");
        ProfileCursorPerformance = GUILayout.Toggle(ProfileCursorPerformance,
            "Profile cursor/pathfinding timings in UMM log (Default Off)");
    }

    private void DrawWeighting()
    {
        DrawWeight("Caster threat", "Higher makes recognized casters more attractive targets, even with poor melee.", ref DefaultCasterThreatMultiplier,
            ref CasterThreatMultiplier, ref casterThreatMultiplierText);
        DrawWeight("Physical threat", "Scales weapon danger without scaling the standalone caster threat.", ref DefaultPhysicalThreatMultiplier,
            ref PhysicalThreatMultiplier, ref physicalThreatMultiplierText);
        DrawWeight("Sneak / precision threat", "Scales independently learned precision damage.", ref DefaultSneakThreatMultiplier,
            ref SneakThreatMultiplier, ref sneakThreatMultiplierText);
        DrawWeight("Elemental / enchantment threat", "Scales independently learned extra HP damage.", ref DefaultExtraDamageThreatMultiplier,
            ref ExtraDamageThreatMultiplier, ref extraDamageThreatMultiplierText);
        DrawWeight("Stat damage / drain threat", "Scales independently learned ability damage, drain and negative levels.", ref DefaultStatDrainThreatMultiplier,
            ref StatDrainThreatMultiplier, ref statDrainThreatMultiplierText);
        DrawWeight("Risk importance", "Higher favors dangerous targets more strongly; 0 removes this preference.", ref DefaultRiskImportance,
            ref RiskImportance, ref riskImportanceText);
        DrawWeight("Squish importance", "Higher favors easier-to-damage targets more strongly; 0 removes this preference.", ref DefaultSquishImportance,
            ref SquishImportance, ref squishImportanceText);
        DrawWeight("Remaining HP importance", "Favors wounded targets for HP-damaging attacks. Full HP is neutral; half HP gives 1.33 before weighting. 0 disables this preference. Control/drain-only abilities are unchanged.", ref DefaultHitPointsImportance,
            ref HitPointsImportance, ref hitPointsImportanceText);
        DrawWeight("Distance importance", "Higher penalizes movement, delays and opportunity risk more strongly; 0 removes this preference.", ref DefaultDistanceImportance,
            ref DistanceImportance, ref distanceImportanceText);
        DrawDescription("Importance uses powers around neutral 1. Impossible/zero scores remain zero. Type a larger value to extend a slider.");
    }

    private void DrawIntelligenceOptions()
    {
        DrawRange("Reassessment chance", 1, 20, ref DefaultReassessmentRange,
            ref ReassessmentMinimum, ref ReassessmentMaximum,
            ref reassessmentMinimumText, ref reassessmentMaximumText);
        DrawRange("Strength knowledge", 1, 10, ref DefaultStrengthRange,
            ref StrengthMinimum, ref StrengthMaximum,
            ref strengthMinimumText, ref strengthMaximumText);
        DrawRange("Attack bonus knowledge", 5, 18, ref DefaultAttackBonusRange,
            ref AttackBonusMinimum, ref AttackBonusMaximum,
            ref attackBonusMinimumText, ref attackBonusMaximumText);
        DrawRange("Damage knowledge", 8, 22, ref DefaultDamageRange,
            ref DamageMinimum, ref DamageMaximum,
            ref damageMinimumText, ref damageMaximumText);
        DrawRange("Sneak/precision knowledge", 10, 25, ref DefaultSneakRange,
            ref SneakMinimum, ref SneakMaximum,
            ref sneakMinimumText, ref sneakMaximumText);
        DrawRange("Elemental/enchantment knowledge", 8, 22, ref DefaultExtraDamageRange,
            ref ExtraDamageMinimum, ref ExtraDamageMaximum,
            ref extraDamageMinimumText, ref extraDamageMaximumText);
        DrawRange("Stat damage/drain & negative levels", 10, 25, ref DefaultStatDrainRange,
            ref StatDrainMinimum, ref StatDrainMaximum,
            ref statDrainMinimumText, ref statDrainMaximumText);
        DrawRange("Attack frequency & criticals", 8, 22, ref DefaultRoutineRange,
            ref RoutineMinimum, ref RoutineMaximum,
            ref routineMinimumText, ref routineMaximumText);
        DrawRange("Damage/spell immunity knowledge", 10, 25, ref DefaultDamageResponseRange,
            ref DamageResponseMinimum, ref DamageResponseMaximum,
            ref damageResponseMinimumText, ref damageResponseMaximumText);
        DrawRange("Caster recognition", 5, 16, ref DefaultCasterRange,
            ref CasterMinimum, ref CasterMaximum,
            ref casterMinimumText, ref casterMaximumText);
        DrawRange("Caster level knowledge", 10, 25, ref DefaultCasterLevelRange,
            ref CasterLevelMinimum, ref CasterLevelMaximum,
            ref casterLevelMinimumText, ref casterLevelMaximumText);
        DrawRange("AC knowledge", 10, 25, ref DefaultArmorClassRange,
            ref ArmorClassMinimum, ref ArmorClassMaximum,
            ref armorClassMinimumText, ref armorClassMaximumText);
        DrawRange("Touch AC knowledge", 10, 25, ref DefaultTouchArmorClassRange,
            ref TouchArmorClassMinimum, ref TouchArmorClassMaximum,
            ref touchArmorClassMinimumText, ref touchArmorClassMaximumText);
        DrawRange("Saving throw knowledge", 10, 25, ref DefaultSavingThrowRange,
            ref SavingThrowMinimum, ref SavingThrowMaximum,
            ref savingThrowMinimumText, ref savingThrowMaximumText);
        DrawRange("Condition vulnerability knowledge", 5, 15, ref DefaultConditionVulnerabilityRange,
            ref ConditionVulnerabilityMinimum, ref ConditionVulnerabilityMaximum,
            ref conditionVulnerabilityMinimumText, ref conditionVulnerabilityMaximumText);
        DrawRange("Remaining HP knowledge", 5, 15, ref DefaultHitPointsRange,
            ref HitPointsMinimum, ref HitPointsMaximum,
            ref hitPointsMinimumText, ref hitPointsMaximumText);
    }

    internal enum PathfindingPreset { Wotr, Reset, Pnp }

    internal void ApplyPathfindingPreset(PathfindingPreset preset)
    {
        ConfigurePathfindingPreset(preset);
        RefreshBodyRules();
    }

    // No scene access: preset behavior and settings isolation can be tested offline.
    internal void ConfigurePathfindingPreset(PathfindingPreset preset)
    {
        if (preset != PathfindingPreset.Wotr && preset != PathfindingPreset.Reset && preset != PathfindingPreset.Pnp)
            throw new System.ArgumentOutOfRangeException(nameof(preset));
        var vanilla = preset == PathfindingPreset.Wotr;
        var pnp = preset == PathfindingPreset.Pnp;
        EnableBodyBlocking = !vanilla;
        BlockHostilePassage = !vanilla;
        AllowAlliedPassage = true; // Inverted UI checkbox: unchecked.
        // Keep permissive friendly-space choices checked for WotR Defaults,
        // even though body blocking is disabled and these overrides are inactive.
        IgnoreFriendlyBlockingOnFiveFootStep = !pnp;
        AllowFriendlyStoppingOverlap = vanilla;
        AllowFlyingEnemyPassage = !vanilla && !pnp;
        IgnoreDownedUnitSpace = !vanilla;
        PreventStoppingOverlap = !vanilla;
        BodyBlockingMovementGuard = !vanilla;
        PreserveSpecialBodyMovement = true; // Inverted UI checkbox: unchecked.
        BodyBlockingCharges = !vanilla;
        ShowDestinationFootprint = !vanilla;
        ShowFlyingPassageVisual = !vanilla;
    }

    private static void RefreshBodyRules()
    {
        try { NavmeshBodyRules.RefreshBeforePath(); }
        catch (System.Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Refreshing pathfinding preferences: " + exception); }
    }

    internal void ValidateRanges(UnityModManager.ModEntry modEntry)
    {
        ValidateWeight("Reinforcement detection multiplier", ref DefaultReinforcementRangeMultiplier,
            ref ReinforcementRangeMultiplier, modEntry, 2f, 1f);
        ValidateWeight("Caster threat", ref DefaultCasterThreatMultiplier, ref CasterThreatMultiplier, modEntry);
        ValidateWeight("Physical threat", ref DefaultPhysicalThreatMultiplier, ref PhysicalThreatMultiplier, modEntry);
        ValidateWeight("Sneak / precision threat", ref DefaultSneakThreatMultiplier, ref SneakThreatMultiplier, modEntry);
        ValidateWeight("Elemental / enchantment threat", ref DefaultExtraDamageThreatMultiplier, ref ExtraDamageThreatMultiplier, modEntry);
        ValidateWeight("Stat damage / drain threat", ref DefaultStatDrainThreatMultiplier, ref StatDrainThreatMultiplier, modEntry);
        ValidateWeight("Risk importance", ref DefaultRiskImportance, ref RiskImportance, modEntry);
        ValidateWeight("Squish importance", ref DefaultSquishImportance, ref SquishImportance, modEntry);
        ValidateWeight("Remaining HP importance", ref DefaultHitPointsImportance, ref HitPointsImportance, modEntry);
        ValidateWeight("Distance importance", ref DefaultDistanceImportance, ref DistanceImportance, modEntry);
        ValidateRange("Reassessment chance", ref DefaultReassessmentRange, ReassessmentMinimum,
            ReassessmentMaximum, modEntry);
        ValidateRange("Strength knowledge", ref DefaultStrengthRange, StrengthMinimum,
            StrengthMaximum, modEntry);
        ValidateRange("Attack bonus knowledge", ref DefaultAttackBonusRange, AttackBonusMinimum,
            AttackBonusMaximum, modEntry);
        ValidateRange("Damage knowledge", ref DefaultDamageRange, DamageMinimum,
            DamageMaximum, modEntry);
        ValidateRange("Sneak/precision knowledge", ref DefaultSneakRange, SneakMinimum,
            SneakMaximum, modEntry);
        ValidateRange("Elemental/enchantment knowledge", ref DefaultExtraDamageRange, ExtraDamageMinimum,
            ExtraDamageMaximum, modEntry);
        ValidateRange("Stat damage/drain & negative levels", ref DefaultStatDrainRange, StatDrainMinimum,
            StatDrainMaximum, modEntry);
        ValidateRange("Attack frequency & criticals", ref DefaultRoutineRange, RoutineMinimum,
            RoutineMaximum, modEntry);
        ValidateRange("Damage/spell immunity knowledge", ref DefaultDamageResponseRange, DamageResponseMinimum,
            DamageResponseMaximum, modEntry);
        ValidateRange("Caster recognition", ref DefaultCasterRange, CasterMinimum,
            CasterMaximum, modEntry);
        ValidateRange("Caster level knowledge", ref DefaultCasterLevelRange, CasterLevelMinimum,
            CasterLevelMaximum, modEntry);
        ValidateRange("AC knowledge", ref DefaultArmorClassRange, ArmorClassMinimum,
            ArmorClassMaximum, modEntry);
        ValidateRange("Touch AC knowledge", ref DefaultTouchArmorClassRange, TouchArmorClassMinimum,
            TouchArmorClassMaximum, modEntry);
        ValidateRange("Saving throw knowledge", ref DefaultSavingThrowRange, SavingThrowMinimum,
            SavingThrowMaximum, modEntry);
        ValidateRange("Condition vulnerability knowledge", ref DefaultConditionVulnerabilityRange,
            ConditionVulnerabilityMinimum, ConditionVulnerabilityMaximum, modEntry);
        ValidateRange("Remaining HP knowledge", ref DefaultHitPointsRange, HitPointsMinimum,
            HitPointsMaximum, modEntry);
    }

    private static void ValidateRange(string name, ref bool useDefault, int minimum,
        int maximum, UnityModManager.ModEntry modEntry)
    {
        if (!useDefault && minimum >= maximum)
        {
            modEntry.Logger.Warning($"Invalid {name} range [{minimum}, {maximum}]; using displayed defaults.");
            useDefault = true;
        }
    }

    private static void DrawRange(string name, int defaultMinimum, int defaultMaximum,
        ref bool useDefault, ref int minimum, ref int maximum,
        ref string? minimumText, ref string? maximumText)
    {
        GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
        var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = false };
        var toggleStyle = new GUIStyle(GUI.skin.toggle) { wordWrap = false };
        GUILayout.Label($"{name} (Default {defaultMinimum}-{defaultMaximum})", labelStyle,
            GUILayout.ExpandWidth(true));
        useDefault = GUILayout.Toggle(useDefault, "Default", toggleStyle,
            GUILayout.Width(ControlWidth(toggleStyle, "Default")));
        GUILayout.Space(8);
        GUILayout.Label("0%", labelStyle, GUILayout.Width(ControlWidth(labelStyle, "0%")));
        GUILayout.Space(4);
        var wasEnabled = GUI.enabled;
        string enteredMinimumText;
        string enteredMaximumText;
        try
        {
            GUI.enabled = wasEnabled && !useDefault;
            enteredMinimumText = GUILayout.TextField(useDefault
                ? defaultMinimum.ToString(CultureInfo.InvariantCulture)
                : minimumText ?? minimum.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(InputWidth(minimumText ?? minimum.ToString(CultureInfo.InvariantCulture))));
            GUI.enabled = wasEnabled;
            GUILayout.Space(8);
            GUILayout.Label("100%", labelStyle, GUILayout.Width(ControlWidth(labelStyle, "100%")));
            GUILayout.Space(4);
            GUI.enabled = wasEnabled && !useDefault;
            enteredMaximumText = GUILayout.TextField(useDefault
                ? defaultMaximum.ToString(CultureInfo.InvariantCulture)
                : maximumText ?? maximum.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(InputWidth(maximumText ?? maximum.ToString(CultureInfo.InvariantCulture))));
        }
        finally
        {
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }
        if (useDefault)
        {
            return;
        }

        minimumText = enteredMinimumText;
        maximumText = enteredMaximumText;
        if (int.TryParse(minimumText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enteredMinimum) &&
            int.TryParse(maximumText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enteredMaximum) &&
            enteredMinimum < enteredMaximum)
        {
            minimum = enteredMinimum;
            maximum = enteredMaximum;
        }
        else
        {
            DrawDescription("Enter whole Intelligence scores with the 0% value below the 100% value (previous valid range remains active).");
        }
    }

    private static float ControlWidth(GUIStyle style, string text) =>
        style.CalcSize(new GUIContent(text)).x + 8f;

    private static float InputWidth(string text) =>
        ControlWidth(GUI.skin.textField, text.Length < 3 ? "000" : text);

    private static void ValidateWeight(string label, ref bool useDefault, ref float value,
        UnityModManager.ModEntry modEntry, float defaultValue = 1f, float minimumValue = 0f)
    {
        if (ScoringWeights.IsValid(value) && value >= minimumValue) return;
        modEntry.Logger.Warning($"Invalid {label} weight [{value}]; using default {defaultValue}.");
        useDefault = true;
        value = defaultValue;
    }

    private static void DrawWeight(string label, string explanation, ref bool useDefault,
        ref float value, ref string? text, float defaultValue = 1f, float minimumValue = 0f)
    {
        var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = false };
        var toggleStyle = new GUIStyle(GUI.skin.toggle) { wordWrap = false };
        GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
        var defaultText = defaultValue.ToString("0.##", CultureInfo.InvariantCulture);
        GUILayout.Label(new GUIContent(label + " (Default " + defaultText + ")", explanation), labelStyle, GUILayout.ExpandWidth(true));
        useDefault = GUILayout.Toggle(useDefault, "Default", toggleStyle,
            GUILayout.Width(ControlWidth(toggleStyle, "Default")));
        GUILayout.Space(8);
        var wasEnabled = GUI.enabled;
        string entered;
        try
        {
            GUI.enabled = wasEnabled && !useDefault;
            var current = useDefault ? defaultValue : value;
            var moved = GUILayout.HorizontalSlider(current, minimumValue, System.Math.Max(defaultValue + 2f, current),
                GUILayout.MinWidth(80f), GUILayout.ExpandWidth(true));
            if (!useDefault && moved != current)
            {
                value = (float)System.Math.Round(moved, 2);
                text = value.ToString("0.##", CultureInfo.InvariantCulture);
            }
            GUILayout.Space(8);
            entered = GUILayout.TextField(useDefault ? defaultText : text ?? value.ToString("R", CultureInfo.InvariantCulture),
                GUILayout.Width(InputWidth(text ?? value.ToString("R", CultureInfo.InvariantCulture))));
        }
        finally
        {
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }
        if (useDefault) return;
        text = entered;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            ScoringWeights.IsValid(parsed) && parsed >= minimumValue)
            value = parsed;
        else DrawDescription(minimumValue == 0f ? "Enter a finite, nonnegative number (previous valid value remains active)." :
            $"Enter a finite number of at least {minimumValue} (previous valid value remains active).");
    }

    public override void Save(UnityModManager.ModEntry modEntry)
    {
        Save(this, modEntry);
    }
}
