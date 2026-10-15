using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Kingmaker.AI;
using Kingmaker.AI.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Mechanics.ContextData;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;

namespace ArdubsEnemyIntelligence;

internal static partial class TargetingState
{
    private static readonly Dictionary<UnitEntityData, EnemyMemory> Enemies =
        new Dictionary<UnitEntityData, EnemyMemory>(ReferenceComparer<UnitEntityData>.Instance);

    // Do not advance the game's combat RNG merely because an AI score was inspected.
    private static readonly Random Random = new Random();

    internal static void Reset()
    {
        TouchDeliveryPath.Clear();
        ReinforcementDetection.Reset();
        Enemies.Clear();
    }

    internal static bool IsManaged(UnitEntityData? unit)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.ModEnabled || unit == null || !unit.IsInCombat ||
            !CombatController.IsInTurnBasedCombat())
        {
            return false;
        }

        var player = Kingmaker.Game.Instance?.Player;
        if (player == null || player.MainCharacter.Value == null)
        {
            return false;
        }

        var playerUnit = player.GetMainPartyUnit();
        return playerUnit != null && ManagedActorPolicy.Allows(
            unit.IsInCombat, CombatController.IsInTurnBasedCombat(), unit.IsDirectlyControllable,
            player.PartyAndPets.Contains(unit), unit.IsPlayerFaction, unit.IsEnemy(playerUnit),
            ArdubsEnemyIntelligenceMain.Settings.ApplyToAlliedNpcs && IsAlliedNpc(unit, playerUnit),
            unit.IsPet, unit.IsSummoned(), ArdubsEnemyIntelligenceMain.Settings.ApplyToAlliedNpcs, ArdubsEnemyIntelligenceMain.Settings.ApplyToEnemies);
    }

    private static bool IsFriendlyCombatant(UnitEntityData unit, UnitEntityData playerUnit) =>
        ManagedActorPolicy.IsFriendlyCombatant(unit.IsEnemy(playerUnit) || playerUnit.IsEnemy(unit),
            unit.IsAlly(playerUnit), playerUnit.IsAlly(unit), unit.Faction.Neutral,
            unit.Faction.AlwaysEnemy, unit.Faction.EnemyForEveryone);

    internal static bool IsAlliedNpc(UnitEntityData unit, UnitEntityData playerUnit)
    {
        // Groups aggregate hostility, so it must override a friendly member or
        // faction before inheriting anything. No shared-enemy heuristic: it
        // would also adopt unrelated third-party neutral combatants.
        var group = unit.Group;
        return ManagedActorPolicy.IsAlliedNpc(unit.IsEnemy(playerUnit) || playerUnit.IsEnemy(unit),
            IsFriendlyCombatant(unit, playerUnit),
            group?.Select(member => member != null && IsFriendlyCombatant(member, playerUnit)) ?? Enumerable.Empty<bool>());
    }

    internal static void BeginTurn(TurnController turn, UnitEntityData? unit)
    {
        if (!IsManaged(unit))
        {
            return;
        }

        var actor = unit!;
        var memory = GetEnemy(actor);
        if (ReferenceEquals(memory.Turn, turn))
        {
            return;
        }

        TouchDeliveryPath.Forget(actor);
        memory.Turn = turn;
        if (!memory.Initialized)
        {
            // Do not let a priority selected by the vanilla impatience controller
            // before this mod saw the unit override our turn-start decision.
            var aiData = actor.CombatState.AIData;
            aiData.ImpatiencePriorityTarget = null;
            aiData.DislikedTargets.Clear();
            aiData.HardTargets.Clear();
            memory.Initialized = true;
        }

        var previous = memory.CommittedTarget;
        if (!IsValidTarget(actor, previous))
        {
            previous = actor.CombatState.LastTarget;
        }

        if (!IsValidTarget(actor, previous))
        {
            previous = actor.CombatState.AIData.LastOffensiveStandardActionTarget;
        }

        var intelligence = Intelligence(actor);
        var chance = TargetingPolicy.ReassessmentChance(intelligence,
            ArdubsEnemyIntelligenceMain.Settings.ActiveReassessmentMinimum, ArdubsEnemyIntelligenceMain.Settings.ActiveReassessmentMaximum);
        var previousValid = IsValidTarget(actor, previous);
        var roll = previousValid ? (double?)Random.NextDouble() : null;
        var reassess = !previousValid || roll < chance;
        memory.TargetLock.BeginTurn(reassess ? null : previous);
        memory.LockedByTurnRoll = !reassess;
        memory.DownedTargetReleased = false;
        memory.DecisionReported = false;
        memory.Scores.Clear();
        memory.Profiles.Clear();
        memory.OffensiveEstimates.Clear();
        memory.UsefulNonHp.Clear();
        memory.SkippedScores.Clear();
        memory.VisibleWhenScored.Clear();

        TargetingLog.Write($"{UnitLabel(actor)} (Int {intelligence}): reassess chance {chance:P1}; " +
            (roll.HasValue
                ? $"roll {roll.Value:P1}; " + (reassess
                    ? "reassessing"
                    : $"holding {UnitLabel(previous!)}; other targets excluded this turn")
                : "no valid previous target; reassessing") +
            $"; visible potential targets [{DescribeVisibleTargets(actor)}]" +
            $"; remembered out of sight [{DescribeTargets(KnownTargets(actor).Where(target => !actor.Memory.ContainsVisible(target)))}]");
    }

    private static EnemyMemory? CurrentMemory(UnitEntityData? actor)
    {
        if (!IsManaged(actor) || !Enemies.TryGetValue(actor!, out var memory) ||
            !ReferenceEquals(memory.Turn, Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn))
            return null;
        ReleaseDownedTarget(actor!, memory);
        return memory;
    }

    private static void ReleaseDownedTarget(UnitEntityData actor, EnemyMemory memory)
    {
        var locked = memory.TargetLock.Target;
        if (locked == null || !TargetingPolicy.ReleasesDownedTarget(
                locked.State.IsUnconscious, locked.State.IsDead,
                locked.State.HasCondition(UnitCondition.Sleeping)) ||
            !memory.TargetLock.Release(locked)) return;
        if (ReferenceEquals(memory.CommittedTarget, locked)) memory.CommittedTarget = null;
        memory.LockedByTurnRoll = false;
        memory.DownedTargetReleased = true;
        memory.DecisionReported = false;
        memory.Scores.Clear();
        memory.Profiles.Clear();
        memory.SkippedScores.Clear();
        memory.VisibleWhenScored.Clear();
        TargetingLog.Write($"{UnitLabel(actor)}: {UnitLabel(locked)} is down; target lock released");
    }

    internal static bool AllowTargetScore(AiAction action, DecisionContext context)
    {
        var actor = context.Unit;
        var target = context.Target?.Unit;
        var memory = CurrentMemory(actor);
        if (IsManaged(actor) && target != null && actor.IsEnemy(target) &&
            SleepingTargetBlocker(actor, target) is { } sleepReason)
        {
            context.CurrentScore = 0m;
            if (memory != null)
                RecordSkipped(memory, action, target,
                    $"sleeping; {sleepReason}; Score[0]");
            return false;
        }
        if (memory?.TargetLock.Target == null || target == null || !actor.IsEnemy(target))
        {
            return true;
        }

        if (memory.TargetLock.Allows(target) && IsValidTarget(actor, target))
        {
            return true;
        }

        context.CurrentScore = 0m;
        if (AttackAssessment.IsScoredAction(action, context))
        {
            var reason = memory.LockedByTurnRoll ? "turn-start reassessment roll failed" :
                "target chosen before approach";
            RecordSkipped(memory, action, target,
                $"target locked to {UnitLabel(memory.TargetLock.Target)} for this turn ({reason}); " +
                "no mid-turn retargeting; " + DescribeKnownFacts(memory, target));
        }
        return false;
    }

    private static bool IsTargetedAiCommand(UnitCommand command) =>
        command.AiAction != null && (command is UnitAttack || command is UnitUseAbility);

    internal static bool AllowCommand(UnitEntityData actor, UnitCommand command)
    {
        var target = command.TargetUnit;
        var memory = CurrentMemory(actor);
        if (!AllowsSleepingCommand(actor, command)) return false;
        return memory == null || !IsTargetedAiCommand(command) || target == null ||
               !actor.IsEnemy(target) || memory.TargetLock.Allows(target);
    }

    private static bool AllowsSleepingCommand(UnitEntityData actor, UnitCommand command) =>
        !(command is UnitAttack || command is UnitUseAbility) ||
        command.TargetUnit == null || !command.TargetUnit.State.HasCondition(UnitCondition.Sleeping) ||
        !IsManaged(actor) || !actor.IsEnemy(command.TargetUnit) ||
        SleepingTargetBlocker(actor, command.TargetUnit) == null;

    internal static bool StopSleepingCommand(UnitCommand command)
    {
        if (command.IsFinished || AllowsSleepingCommand(command.Executor, command)) return false;
        TargetingLog.Write($"{UnitLabel(command.Executor)}: cancelled attack on sleeping " +
            $"{UnitLabel(command.TargetUnit)}; {SleepingTargetBlocker(command.Executor, command.TargetUnit)}; " +
            "target lock unchanged for this turn");
        command.Interrupt();
        return true;
    }

    internal static void CommitCommand(UnitCommand command)
    {
        var actor = command.Executor;
        var target = command.TargetUnit;
        var memory = CurrentMemory(actor);
        if (memory == null || !IsTargetedAiCommand(command) || target == null ||
            !actor.IsEnemy(target) || !memory.TargetLock.TryCommit(target))
        {
            return;
        }

        memory.CommittedTarget = target;
        memory.DownedTargetReleased = false;
        if (memory.DecisionReported)
        {
            return;
        }
        memory.DecisionReported = true;
        if (memory.Scores.ContainsKey(command.AiAction!) || memory.SkippedScores.ContainsKey(command.AiAction!))
        {
            ReportTargetDecision(actor, target, command.AiAction, memory);
        }
        TargetingLog.Write($"{UnitLabel(actor)}: " +
            (command.ShouldUnitApproach ? "approaching " : "targeting ") +
            $"{UnitLabel(target)}; target locked for this turn");
    }

    internal static bool KeepApproach(UnitCommand command)
    {
        var memory = CurrentMemory(command.Executor);
        return memory?.TargetLock.Target != null && IsTargetedAiCommand(command) &&
               ReferenceEquals(command.TargetUnit, memory.TargetLock.Target) &&
               IsValidTarget(command.Executor, memory.TargetLock.Target);
    }

    internal static UnitEntityData RestrictAttackTarget(UnitAttack command, UnitEntityData proposed)
    {
        var memory = CurrentMemory(command.Executor);
        return command.AiAction != null && memory?.TargetLock.Target != null
            ? memory.TargetLock.Target : proposed;
    }

    internal static bool FinishAttackRetarget(UnitAttack command, bool nativeResult)
    {
        var memory = CurrentMemory(command.Executor);
        if (memory == null || command.AiAction == null) return nativeResult;
        var target = command.Target;
        if (target == null) return nativeResult;
        // Preserve native UpdateTarget's fallback: it first looks for another
        // nearby conscious target, then may continue on the unconscious one.
        if (TargetingPolicy.ReleasesDownedTarget(target.State.IsUnconscious,
                target.State.IsDead, target.State.HasCondition(UnitCondition.Sleeping))) return nativeResult;
        if (nativeResult && memory.DownedTargetReleased)
        {
            if (!IsValidTarget(command.Executor, target)) return false;
            if (memory.TargetLock.TryCommit(target))
            {
                memory.CommittedTarget = target;
                memory.DownedTargetReleased = false;
                TargetingLog.Write($"{UnitLabel(command.Executor)}: continuing attack on " +
                    $"{UnitLabel(target)} after prior target fell; target locked for this turn");
            }
        }
        return nativeResult;
    }

    internal static void ObserveCommand(UnitCommand command)
    {
        var actor = command.Executor;
        var target = command.TargetUnit;
        if (actor == null || target == null || !actor.IsEnemy(target))
        {
            return;
        }

        if (IsManaged(actor))
        {
            var memory = GetEnemy(actor);
            // DidStart can be emitted by the base OnStart before InitAttacks.
            // Do not dereference PlannedAttack's not-yet-initialized backing list.
            var profiles = command is UnitUseAbility cast
                ? new[] { AttackAssessment.Spell(cast.Ability) }
                : command is UnitAttack attack && attack.AllAttacks.Count > 0
                    ? attack.AllAttacks.Where(hand => hand.Weapon != null)
                        .Select(hand => AttackAssessment.Weapon(hand.Weapon)).ToArray()
                    : command is UnitAttack && command.AiAction != null &&
                      memory.Profiles.TryGetValue(command.AiAction, out var cached)
                        ? cached
                        : command is UnitAttack
                            ? UnitAttack.EnumerateFullAttack(actor).Where(info => info.Hand?.MaybeWeapon != null)
                                .Select(info => AttackAssessment.Weapon(info.Hand.MaybeWeapon!)).ToArray()
                            : Array.Empty<AttackAssessment>();
            if (command.AiAction != null)
            {
                TargetingLog.Write(command is UnitUseAbility use
                    ? $"{UnitLabel(actor)}: uses {use.Ability.Name} on {UnitLabel(target)}"
                    : $"{UnitLabel(actor)}: attacks {UnitLabel(target)}");
                memory.Scores.Clear();
                memory.Profiles.Clear();
                memory.SkippedScores.Clear();
                memory.VisibleWhenScored.Clear();
            }

            var defenses = RelevantDefenses(profiles).ToArray();
            var knowledge = GetKnowledge(actor, target, memory, defenses);
            knowledge.HasTargeted = true;
            var checks = LearnDefenses(actor, target, knowledge, defenses, 10);
            LearnDamageResponse(actor, knowledge, 10);
            LearnHitPoints(actor, target, knowledge, 10);
            LearnConditions(actor, target, knowledge, afterAttack: true);
            ReportLearningAssessment(actor, target, knowledge, null, checks);
        }

        if (IsManaged(target))
        {
            var memory = GetEnemy(target);
            var knowledge = GetKnowledge(target, actor, memory);
            if (!knowledge.OffensiveRerollUsed)
            {
                knowledge.OffensiveRerollUsed = true;
                memory.OffensiveEstimates.Remove(actor);
                var checks = LearnOffense(target, actor, knowledge, 10);
                ReportLearningAssessment(target, actor, knowledge, checks);
            }
        }
    }

    internal static void ObserveDamage(RuleDealDamage damage)
    {
        var actor = damage.Initiator;
        var target = damage.Target;
        if (target != null && actor != null && actor.IsEnemy(target) && !damage.IsFake && !damage.IsDot &&
            damage.SourceArea == null && damage.AttackRoll?.IsFake != true && damage.AttackRoll?.IsHit != false && IsManaged(target))
        {
            var observed = new List<ThreatKnowledge>();
            if (damage.AttackRoll?.Weapon != null) observed.Add(ThreatKnowledge.Routine);
            if (damage.ResultList?.Any(value => value.FinalValue > 0 && value.Source != null &&
                (value.Source.Sneak || value.Source.Precision)) == true) observed.Add(ThreatKnowledge.Sneak);
            if (damage.ResultList?.Any(value => value.FinalValue > 0 && value.Source is EnergyDamage) == true)
                observed.Add(ThreatKnowledge.ExtraDamage);
            ObserveThreat(target, actor, observed);
        }
        if (actor == null || !IsManaged(actor) || target == null || !actor.IsEnemy(target) ||
            damage.IsFake || damage.IsDot || damage.SourceArea != null ||
            damage.AttackRoll?.IsFake == true || damage.AttackRoll?.IsHit == false)
        {
            return;
        }

        var ability = damage.SourceAbility;
        var weapon = damage.AttackRoll?.Weapon;
        if (ability == null && weapon == null)
        {
            return;
        }
        var key = ability != null ? AttackAssessment.SpellKey(ability) : AttackAssessment.WeaponKey(weapon!);
        var label = ability != null ? ability.Name : weapon!.Name;
        var memory = GetEnemy(actor);
        var knowledge = GetKnowledge(actor, target, memory);
        // On-hit stat damage can happen before or after the HP callback. Keep
        // the same attack useful in either order; no cross-target/global reset.
        if (damage.AttackRoll != null && memory.UsefulNonHp.Contains(damage.AttackRoll))
        {
            knowledge.Effectiveness.Observe(key, 1, 1, 1, false);
            return;
        }
        var previousHits = knowledge.Effectiveness.Hits(key);
        var immune = damage.ResultList?.Any(value => value.Source?.Immune == true) == true;
        var hits = knowledge.Effectiveness.Observe(key,
            damage.Result, damage.RawResult, damage.DamageWithoutReduction, immune);
        if (hits < previousHits)
        {
            TargetingLog.WriteLearning($"{UnitLabel(actor)} damaged {UnitLabel(target)}; " +
                $"cleared ineffective-damage evidence for {label} only");
        }
        else if (hits > previousHits)
        {
            TargetingLog.WriteLearning($"{UnitLabel(actor)}'s {label} dealt 0 to {UnitLabel(target)}; " +
                $"ineffective damage events for this attack: {hits}; " +
                $"Damage[raw {damage.RawResult}; before DR {damage.DamageWithoutReduction}; " +
                $"final {damage.Result}; immune {(immune ? "yes" : "no")}]");
        }
    }

    private static void ObserveThreat(UnitEntityData observer, UnitEntityData source,
        IEnumerable<ThreatKnowledge> kinds)
    {
        var memory = GetEnemy(observer);
        var knowledge = GetKnowledge(observer, source, memory);
        memory.OffensiveEstimates.Remove(source);
        var checks = new OffensiveChecks();
        LearnThreats(observer, source, knowledge, checks, 10, kinds);
        ReportLearningAssessment(observer, source, knowledge, checks);
    }

    internal static void ObserveStatDamage(RuleDealStatDamage damage) =>
        ObserveNonHp(damage.Initiator, damage.Target, damage.Result, damage.Immune,
            damage.Reason, damage.Stat.Type + (damage.IsDrain ? " drain" : " damage"));

    internal static void ObserveEnergyDrain(RuleDrainEnergy damage) =>
        ObserveNonHp(damage.Initiator, damage.Target, damage.Result, damage.TargetIsImmune,
            damage.Reason, "negative levels");

    private static void ObserveNonHp(UnitEntityData actor, UnitEntityData target, int result, bool immune,
        Kingmaker.RuleSystem.RuleReason reason, string label)
    {
        if (actor == null || target == null || !actor.IsEnemy(target)) return;
        var attack = ContextData<ContextAttackData>.Current?.AttackRoll ??
            (reason?.Rule as RuleAttackWithWeapon)?.AttackRoll ?? reason?.Rule as RuleAttackRoll;
        if (attack?.IsFake == true || attack?.IsHit == false) return;
        if (IsManaged(target) && result > 0)
            ObserveThreat(target, actor, new[] { ThreatKnowledge.StatDrain });
        if (!IsManaged(actor)) return;
        var memory = GetEnemy(actor);
        var knowledge = GetKnowledge(actor, target, memory);
        var ability = AttackAssessment.SourceSpell(reason);
        if (result > 0 && attack?.Weapon != null)
        {
            memory.UsefulNonHp.Add(attack);
            knowledge.Effectiveness.Observe(AttackAssessment.WeaponKey(attack.Weapon), 1, 1, 1, false);
        }
        if (ability != null && (result > 0 || immune))
        {
            var hits = knowledge.Effectiveness.Observe(AttackAssessment.SpellKey(ability), result,
                result, result, immune);
            TargetingLog.WriteLearning($"{UnitLabel(actor)}'s {ability.Name} -> {UnitLabel(target)}: " +
                $"{label}[{result}; immune {(immune ? "yes" : "no")}]; ineffective events for this ability {hits}");
        }
        else if (result > 0 && attack?.Weapon != null)
        {
            TargetingLog.WriteLearning($"{UnitLabel(actor)} dealt {label} {result} to {UnitLabel(target)}; " +
                "this weapon hit was useful even if HP damage was zero");
        }
        else if (result > 0 || immune)
            TargetingLog.WriteLearning($"{UnitLabel(actor)} -> {UnitLabel(target)}: {label}[{result}; " +
                $"immune {(immune ? "yes" : "no")}; weapon correlation {(attack?.Weapon == null ? "unavailable" : "known")}]");
    }

    internal static void ScoreAttack(AiAction action, DecisionContext context)
    {
        var actor = context.Unit;
        var target = context.Target?.Unit;
        if (!IsManaged(actor) || target == null || !actor.IsEnemy(target))
        {
            return;
        }

        var memory = GetEnemy(actor);
        if (!ReferenceEquals(memory.Turn, Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn))
        {
            return;
        }

        if (ArdubsEnemyIntelligenceMain.Settings.AnyLogging && !memory.VisibleWhenScored.ContainsKey(action))
        {
            memory.VisibleWhenScored.Add(action,
                new HashSet<UnitEntityData>(VisibleTargets(actor), ReferenceComparer<UnitEntityData>.Instance));
        }

        var eligible = IsValidTarget(actor, target);
        // A skipped assessment must also exclude the target from the game's
        // comparison. Returning alone leaves a positive vanilla score selectable.
        context.CurrentScore = TargetingPolicy.EligibleTargetScore(context.CurrentScore, eligible);
        if (!eligible)
        {
            RecordSkipped(memory, action, target,
                $"target ineligible; Combat[{target.IsInCombat}]; Conscious[{target.State.IsConscious}]; " +
                $"Hostile[{actor.IsEnemy(target)}]; Visible[{actor.Memory.ContainsVisible(target)}]; " +
                $"Remembered[{actor.Memory.Contains(target)}]; Score[0]");
            return;
        }

        if (context.CurrentScore <= 0)
        {
            RecordSkipped(memory, action, target,
                $"game score {LogNumber.Format(context.CurrentScore)} is not positive");
            return;
        }

        if (!memory.Profiles.TryGetValue(action, out var profiles))
        {
            profiles = AttackAssessment.Resolve(action, context);
            memory.Profiles.Add(action, profiles);
        }
        var knowledge = GetKnowledge(actor, target, memory, RelevantDefenses(profiles));
        RefreshPerfectKnowledge(actor, target, knowledge);
        var risk = BuildRisk(actor, knowledge);

        var squish = EvaluateSquish(actor, target, action, context, knowledge, profiles);

        // Keep the vanilla rejection gate above, but discard its positive
        // weights. Only our Risk/Squish/Dist factors determine this target score.
        var breakdown = new TargetScoreBreakdown(context.CurrentScore, risk.Risk,
            1m, squish.Factor, 1m, riskImportance: ArdubsEnemyIntelligenceMain.Settings.ActiveRiskImportance,
            squishImportance: ArdubsEnemyIntelligenceMain.Settings.ActiveSquishImportance, distanceImportance: ArdubsEnemyIntelligenceMain.Settings.ActiveDistanceImportance);
        context.CurrentScore = breakdown.Score;
        // Also retain the primary-only score so Dist can choose the actual
        // single-attack routine. This math must not depend on logging toggles.
        {
            var single = profiles.Length > 1
                ? EvaluateSquish(actor, target, action, context, knowledge, profiles.Take(1).ToArray()) : squish;
            var trace = new ScoreTrace(breakdown,
                new[] { DescribeRisk(actor, knowledge, risk), "", squish.Description, "" }, single);
            if (!UsesDistance(action, context))
            {
                trace.SetDistance(1m,
                    "neutral[ability; weapon Dist not applied; vanilla ability path scoring follows]");
            }
            RecordScore(memory, action, target, trace);
        }
    }

    internal sealed class PathScoreState
    {
        internal readonly AiAction Action;
        internal readonly DecisionContext Context;
        internal readonly decimal BeforePath;
        internal readonly bool WeaponDistance;
        internal DistanceScoring.Route? Route;

        internal PathScoreState(AiAction action, DecisionContext context)
        {
            Action = action;
            Context = context;
            BeforePath = context.CurrentScore;
            WeaponDistance = UsesDistance(action, context);
        }
    }

    internal static PathScoreState? BeginPathScore(AiAction action, DecisionContext context)
    {
        var memory = CurrentMemory(context.Unit);
        if (!UsesCustomScore(action, context) || context.CurrentScore <= 0m)
        {
            return null;
        }
        var state = new PathScoreState(action, context);
        memory!.ActivePathScore = state;
        return state;
    }

    internal static bool UsesDistance(AiAction action, DecisionContext context) =>
        action.Blueprint is BlueprintAiAttack && IsValidTarget(context.Unit, context.Target?.Unit) &&
        CurrentMemory(context.Unit) != null && context.Unit.Brain.GetAvailableAutoUseAbility() == null;

    internal static bool UsesCustomScore(AiAction action, DecisionContext context) =>
        CurrentMemory(context.Unit) != null && AttackAssessment.IsScoredAction(action, context);

    private static IEnumerable<DefenseKind> RelevantDefenses(IEnumerable<AttackAssessment> profiles) =>
        profiles.SelectMany(profile => new[] { profile.ArmorDefense, profile.SaveDefense })
            .Where(kind => kind.HasValue).Select(kind => kind!.Value).Distinct();

    private readonly struct SquishResult
    {
        internal readonly decimal Factor;
        internal readonly string Description;
        internal SquishResult(decimal factor, string description)
        {
            Factor = factor;
            Description = description;
        }
    }

    private static SquishResult EvaluateSquish(UnitEntityData actor, UnitEntityData target,
        AiAction action, DecisionContext context, TargetKnowledge knowledge, AttackAssessment[] profiles)
    {
        if (profiles.Length == 0)
        {
            return new SquishResult(1m, "Defense[no normal attack routine available => 1]");
        }
        var terms = new List<string>();
        var contributions = new List<decimal>();
        int? dc = profiles.Any(profile => profile.SaveDefense.HasValue)
            ? (action.GetAbilityParams(context) ?? profiles.First(profile => profile.SaveDefense.HasValue)
                .Ability?.CalculateParams())?.DC : null;
        foreach (var profile in profiles)
        {
            var defenseFactor = 1m;
            var parts = new List<string>();
            if (profile.ArmorDefense.HasValue)
            {
                var kind = profile.ArmorDefense.Value;
                var defense = knowledge.Defenses[kind];
                // A remembered target's current conditions are not visible facts.
                // Unknown AC stays unknown: recognition only supplies the
                // condition delta, applied to the existing neutral AC estimate.
                var visible = actor.Memory.ContainsVisible(target);
                var conditions = visible ? AttackAssessment.ObservableConditions(target) : VisibleDefenseCondition.None;
                var recognized = visible && knowledge.Conditions.Known && knowledge.Conditions.Conditions == conditions;
                var adjustment = recognized ? profile.ConditionArmorAdjustment(target, conditions) : 0;
                var assessedArmor = ConditionDefensePolicy.AssessedArmor(defense.Known, defense.Value, adjustment);
                var factor = defense.Known || adjustment != 0 ? TargetingPolicy.ArmorFactor(assessedArmor) : 1m;
                defenseFactor *= factor;
                var basis = defense.Known ? defense.Value.ToString(CultureInfo.InvariantCulture) : "Unknown; neutral estimate 20";
                parts.Add(conditions == VisibleDefenseCondition.None || !recognized
                    ? !defense.Known ? $"{AttackAssessment.Label(kind)}[Unknown => 1]" :
                        $"{AttackAssessment.Label(kind)}[{defense.Value} => {LogNumber.Format(factor)}]"
                    : $"{AttackAssessment.Label(kind)}[{basis}; Conditions[{conditions}; adjustment {adjustment:+0;-0;0}]" +
                        $" => assessed {assessedArmor} => {LogNumber.Format(factor)}]");
                if (conditions != VisibleDefenseCondition.None && !recognized)
                    parts.Add($"Conditions[{conditions}; not recognized => no AC adjustment]");
            }
            if (profile.SaveDefense.HasValue)
            {
                var kind = profile.SaveDefense.Value;
                var defense = knowledge.Defenses[kind];
                var factor = defense.Known && dc.HasValue
                    ? SquishPolicy.SaveFactor(defense.Value, dc.Value, profile.HalfOnSave) : 1m;
                defenseFactor *= factor;
                if (!defense.Known || !dc.HasValue)
                {
                    parts.Add($"{kind}[{(defense.Known ? defense.Value.ToString() : "Unknown")}; " +
                        $"DC {(dc.HasValue ? dc.Value.ToString() : "Unknown")} => 1]");
                }
                else
                {
                    var failure = SquishPolicy.SaveFailureChance(defense.Value, dc.Value);
                    parts.Add($"{kind}[{defense.Value}; DC {dc}; fail {failure:P1}; " +
                        $"{(profile.HalfOnSave ? "half damage on save" : "save failure weight")}; " +
                        $"=> {LogNumber.Format(factor)}]");
                }
            }
            if (parts.Count == 0)
            {
                parts.Add("Defense[no supported attack roll or top-level save => 1]");
            }
            if (profile.WeaponItem != null || profile.Ability != null)
            {
                if (knowledge.DamageResponseKnown)
                {
                    var visible = actor.Memory.ContainsVisible(target);
                    if (!knowledge.Protection.TryGetValue(profile.EvidenceKey, out var protection) ||
                        visible)
                    {
                        // Observed buffs can change even for a middle-Int enemy.
                        // Refresh known protection while visible, but never read
                        // newly gained/lost hidden buffs from a remembered unit.
                        if (!visible) protection = DamageProtection.Unavailable;
                        else if (profile.WeaponItem != null)
                        {
                            var preview = GetOffensiveEstimate(actor, actor)?.Hits
                                .FirstOrDefault(hit => ReferenceEquals(hit.Item, profile.WeaponItem));
                            protection = preview == null ? DamageProtection.Unavailable : DamageProtection.Evaluate(target, preview);
                        }
                        else protection = AbilityProtection.Evaluate(target, profile.Ability!);
                        knowledge.Protection[profile.EvidenceKey] = protection;
                    }
                    defenseFactor *= protection.Factor;
                    parts.Add(protection.Description);
                }
                else parts.Add("Protection[Unknown => 1]");
            }
            if (profile.DealsHitPointDamage)
            {
                if (knowledge.HitPointsKnown)
                {
                    var rawHp = SquishPolicy.HitPointsFactor(knowledge.RemainingHitPoints, knowledge.MaximumHitPoints);
                    var importance = ArdubsEnemyIntelligenceMain.Settings.ActiveHitPointsImportance;
                    var hp = ScoringWeights.Importance(rawHp, importance);
                    defenseFactor *= hp;
                    var lastSeen = actor.Memory.ContainsVisible(target) ? "" : "; last seen";
                    parts.Add($"HP[{knowledge.RemainingHitPoints}/{knowledge.MaximumHitPoints}{lastSeen}; " +
                        $"health factor {LogNumber.Format(rawHp)} ^ Importance[{LogNumber.Format(importance)}] => {LogNumber.Format(hp)}]");
                }
                else parts.Add("HP[Unknown => 1]");
            }
            else parts.Add("HP[no direct HP damage => 1]");
            var hits = knowledge.Effectiveness.Hits(profile.EvidenceKey);
            var ineffective = SquishPolicy.IneffectiveFactor(hits, Intelligence(actor));
            parts.Add($"Ineffective[{profile.Name}; {hits} events; Int {Intelligence(actor)} => " +
                $"{LogNumber.Format(ineffective)}]");
            var contribution = defenseFactor * ineffective;
            contributions.Add(contribution);
            terms.Add(string.Join(" * ", parts));
        }
        // Mean, not a product: one immune bite must not erase useful claws.
        // This weights defenses by the game's normal full routine, not damage.
        return new SquishResult(SquishPolicy.RoutineFactor(contributions), profiles.Length == 1
            ? terms[0]
            : $"Routine[({string.Join(" + ", terms.Select(term => "(" + term + ")"))}) / {profiles.Length} attacks]");
    }

    internal static void CapturePath(DecisionContext context)
    {
        var state = CurrentMemory(context.Unit)?.ActivePathScore;
        if (state?.WeaponDistance == true && ReferenceEquals(state.Context, context))
        {
            state.Route = DistanceScoring.Capture(context);
        }
    }

    internal static void ApplyMovementMode(DecisionContext context, DistanceScoring.Result result) =>
        context.CurrentEnableFiveFootStep = result.Factor > 0m && result.FiveFootStep;

    internal static void FinishPathScore(PathScoreState? state)
    {
        if (state == null)
        {
            return;
        }
        var context = state.Context;
        var memory = CurrentMemory(context.Unit);
        if (memory == null)
        {
            return;
        }
        try
        {
            if (!state.WeaponDistance)
            {
                if (BodyBlocking.AbilityRouteIssue(context) is { } issue)
                {
                    ApplyMovementMode(context, new DistanceScoring.Result(0m, issue));
                    if (memory.Scores.TryGetValue(state.Action, out var rejectedScores) &&
                        rejectedScores.TryGetValue(context.Target.Unit, out var rejectedTrace))
                        rejectedTrace.SetDistance(0m, $"Unavailable[{issue} => 0]");
                    context.CurrentScore = 0m;
                    return;
                }
                var abilityResult = DistanceScoring.EvaluateAbility(context,
                    context.CurrentScore / state.BeforePath);
                ApplyMovementMode(context, abilityResult);
                context.CurrentScore = state.BeforePath * abilityResult.Factor;
                if (memory.Scores.TryGetValue(state.Action, out var abilityScores) &&
                    abilityScores.TryGetValue(context.Target.Unit, out var abilityTrace))
                {
                    abilityTrace.SetDistance(abilityResult.Factor, abilityResult.Description);
                    context.CurrentScore = abilityTrace.ModifiedScore;
                }
                return;
            }
            if (state.Route == null)
            {
                throw new InvalidOperationException("No game route was captured for Dist scoring.");
            }
            // Retain every vanilla hard rejection. Replace only its soft path
            // preferences, so distance/AoOs are not counted a second time.
            var result = context.CurrentScore <= 0m
                ? new DistanceScoring.Result(0m, "Unavailable[vanilla movement score rejected action => 0]")
                : DistanceScoring.Evaluate(context, state.Route, UnitLabel);
            context.CurrentScore = state.BeforePath * result.Factor;
            ApplyMovementMode(context, result);
            if (memory.Scores.TryGetValue(state.Action, out var scores) &&
                scores.TryGetValue(context.Target.Unit, out var trace))
            {
                if (result.FirstAttacks == 1)
                {
                    trace.UseSingleAttack();
                }
                trace.SetDistance(result.Factor, result.Description);
                context.CurrentScore = trace.ModifiedScore;
            }
        }
        catch
        {
            if (memory.Scores.TryGetValue(state.Action, out var scores) &&
                scores.TryGetValue(context.Target.Unit, out var trace))
            {
                trace.SetDistance(context.CurrentScore / state.BeforePath,
                    "vanilla path fallback[Dist evaluation failed; see UMM error]");
            }
            throw;
        }
        finally
        {
            if (memory.Scores.TryGetValue(state.Action, out var completedScores) &&
                completedScores.TryGetValue(context.Target.Unit, out var completedTrace))
                RememberCalculation(memory, state.Action, context.Target.Unit, completedTrace);
            memory.ActivePathScore = null;
        }
    }

    private static void RecordScore(EnemyMemory memory, AiAction action, UnitEntityData target,
        ScoreTrace trace)
    {
        if (!memory.Scores.TryGetValue(action, out var scores))
        {
            scores = new Dictionary<UnitEntityData, ScoreTrace>(ReferenceComparer<UnitEntityData>.Instance);
            memory.Scores.Add(action, scores);
        }

        scores[target] = trace;
        RememberCalculation(memory, action, target, trace);
        if (memory.SkippedScores.TryGetValue(action, out var skipped))
        {
            skipped.Remove(target);
        }
    }

    private static void RememberCalculation(EnemyMemory memory, AiAction action, UnitEntityData target,
        ScoreTrace trace)
    {
        var actionName = memory.Profiles.TryGetValue(action, out var profiles) && profiles.Length == 1
            ? profiles[0].Name : action.Blueprint.name;
        memory.LastCalculations[target] = new CalculationSnapshot(actionName, string.Join("\n", trace.Details));
    }

    private static void RecordSkipped(EnemyMemory memory, AiAction action, UnitEntityData target,
        string reason)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.AnyLogging)
        {
            return;
        }

        if (!memory.SkippedScores.TryGetValue(action, out var skipped))
        {
            skipped = new Dictionary<UnitEntityData, string>(ReferenceComparer<UnitEntityData>.Instance);
            memory.SkippedScores.Add(action, skipped);
        }
        skipped[target] = reason;
        if (memory.Scores.TryGetValue(action, out var scores))
        {
            scores.Remove(target);
        }
    }

    private static void ReportTargetDecision(UnitEntityData actor, UnitEntityData target,
        AiAction action, EnemyMemory memory)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.AnyLogging)
        {
            return;
        }

        memory.Scores.TryGetValue(action, out var scores);
        memory.SkippedScores.TryGetValue(action, out var skipped);
        var visibleAtSelection = VisibleTargets(actor).ToArray();
        memory.VisibleWhenScored.TryGetValue(action, out var visibleWhenScored);
        var submitted = ((IEnumerable<UnitEntityData>?)scores?.Keys ?? Enumerable.Empty<UnitEntityData>())
            .Concat((IEnumerable<UnitEntityData>?)skipped?.Keys ?? Enumerable.Empty<UnitEntityData>())
            .Distinct(ReferenceComparer<UnitEntityData>.Instance)
            .Select(UnitLabel)
            .OrderBy(name => name)
            .ToArray();
        TargetingLog.Write($"  Submitted to selected action [{action.DebugName}]: " +
            (submitted.Length == 0 ? "none captured" : string.Join(", ", submitted)));
        if (scores == null || scores.Count == 0)
        {
            TargetingLog.Write("  No scored options were captured for this action");
        }
        else
        {
            foreach (var option in scores
                         .OrderByDescending(entry => ReferenceEquals(entry.Key, target))
                         .ThenByDescending(entry => entry.Value.ModifiedScore))
            {
                var score = option.Value;
                var label = ReferenceEquals(option.Key, target) ? "chosen" : "option";
                var awareness = actor.Memory.ContainsVisible(option.Key) ? "" : " [remembered; out of sight]";
                TargetingLog.Write($"  {label} {UnitLabel(option.Key)}{awareness}:\n{score.Details[0]}");
                foreach (var detail in score.Details.Skip(1))
                {
                    TargetingLog.Write("    " + detail);
                }
            }
        }
        if (skipped != null)
        {
            foreach (var option in skipped.OrderBy(entry => UnitLabel(entry.Key)))
            {
                TargetingLog.Write($"  skipped {UnitLabel(option.Key)}: {option.Value}");
            }
        }
        if (visibleWhenScored == null)
        {
            TargetingLog.Write("  No scoring-time visibility snapshot was captured for this action");
        }
        else
        {
            var notSubmitted = visibleWhenScored.Where(unit =>
                    (scores == null || !scores.ContainsKey(unit)) &&
                    (skipped == null || !skipped.ContainsKey(unit)))
                .Select(UnitLabel)
                .OrderBy(name => name)
                .ToArray();
            TargetingLog.Write("  Visible at first scoring callback but not scored by selected action: " +
                (notSubmitted.Length == 0 ? "none" : string.Join(", ", notSubmitted)));
            var newlyVisible = visibleAtSelection.Where(unit => !visibleWhenScored.Contains(unit))
                .Select(UnitLabel)
                .OrderBy(name => name)
                .ToArray();
            TargetingLog.Write("  Newly visible by target selection: " +
                (newlyVisible.Length == 0 ? "none" : string.Join(", ", newlyVisible)));
        }
    }

    private static string DescribeKnownFacts(EnemyMemory memory, UnitEntityData target)
    {
        if (!memory.Targets.TryGetValue(target, out var knowledge))
        {
            return "Known: not yet assessed; offensive and defensive facts unknown";
        }

        var strength = knowledge.StrengthKnown ? knowledge.Strength.ToString() : "unknown";
        var caster = knowledge.CasterKnown ? (knowledge.IsCaster ? "yes" : "no") : "unknown";
        var attack = knowledge.AttackBonusKnown ? knowledge.AttackBonus.ToString() : "unknown";
        var damage = knowledge.DamageEstimate == null ? "unknown" : DescribeDamage(knowledge.DamageEstimate);
        var casterLevel = knowledge.CasterLevelKnown ? knowledge.CasterLevel.ToString() : "unknown";
        var defenses = string.Join("; ", knowledge.Defenses.Select(entry =>
            $"{AttackAssessment.Label(entry.Key)} {(entry.Value.Known ? entry.Value.Value.ToString() : "unknown")}"));
        return $"Known: Str {strength}; AB {attack}; damage {damage}; caster {caster}; CL {casterLevel}; " +
            (defenses.Length == 0 ? "no defenses assessed" : defenses);
    }

    private static string DescribeDamage(OffensiveAssessment.Snapshot estimate) =>
        $"{LogNumber.Format(estimate.BaseDamage)} avg [{estimate.Weapon}; ordinary hit only]";

    private static IEnumerable<OffensiveAssessment.Hit> AssessedHits(OffensiveAssessment.Snapshot estimate,
        TargetKnowledge knowledge) => knowledge.Threats[ThreatKnowledge.Routine].Estimate == null
            ? estimate.Hits.Take(1) : estimate.Hits;

    private static decimal CriticalWeight(UnitEntityData actor, OffensiveAssessment.Hit hit, TargetKnowledge knowledge) =>
        RiskPolicy.CriticalFactor(hit.CriticalRange, hit.CriticalMultiplier,
            knowledge.AttackBonusKnown ? knowledge.AttackBonus : (int?)null,
            AttackAssessment.ReadDefense(actor, hit.Item != null &&
                (hit.Item.Blueprint.AttackType == Kingmaker.RuleSystem.AttackType.Touch ||
                 hit.Item.Blueprint.AttackType == Kingmaker.RuleSystem.AttackType.RangedTouch)
                ? DefenseKind.TouchArmorClass : DefenseKind.ArmorClass));

    private static RiskBreakdown BuildRisk(UnitEntityData actor, TargetKnowledge knowledge)
    {
        var routine = knowledge.Threats[ThreatKnowledge.Routine].Estimate;
        decimal? ordinary = knowledge.DamageEstimate == null ? (decimal?)null :
            AssessedHits(knowledge.DamageEstimate, knowledge).Select((hit, index) =>
                hit.BaseDamage * (routine != null && index < routine.Hits.Length
                    ? CriticalWeight(actor, routine.Hits[index], knowledge) : 1m)).Sum();
        var sneak = knowledge.Threats[ThreatKnowledge.Sneak].Estimate;
        var extra = knowledge.Threats[ThreatKnowledge.ExtraDamage].Estimate;
        var drain = knowledge.Threats[ThreatKnowledge.StatDrain].Estimate;
        var additions = (sneak == null ? 0m : AssessedHits(sneak, knowledge).Sum(hit => hit.SneakDamage) / 10m * ArdubsEnemyIntelligenceMain.Settings.ActiveSneakThreatMultiplier) +
            (extra == null ? 0m : AssessedHits(extra, knowledge).Sum(hit => hit.ExtraMean) / 10m * ArdubsEnemyIntelligenceMain.Settings.ActiveExtraDamageThreatMultiplier) +
            (drain == null ? 0m : AssessedHits(drain, knowledge).Sum(hit => hit.StatFactor) * ArdubsEnemyIntelligenceMain.Settings.ActiveStatDrainThreatMultiplier);
        return new RiskBreakdown(knowledge.AttackBonusKnown ? knowledge.AttackBonus : (int?)null,
            ordinary, knowledge.StrengthKnown ? knowledge.Strength : (int?)null,
            knowledge.CasterKnown ? knowledge.IsCaster : (bool?)null,
            knowledge.CasterLevelKnown ? knowledge.CasterLevel : (int?)null, additions,
            routine?.Hits.Sum(hit => CriticalWeight(actor, hit, knowledge)) ?? 1m,
            ArdubsEnemyIntelligenceMain.Settings.ActivePhysicalThreatMultiplier, ArdubsEnemyIntelligenceMain.Settings.ActiveCasterThreatMultiplier);
    }

    private static string DescribeRisk(UnitEntityData actor, TargetKnowledge knowledge, RiskBreakdown risk)
    {
        var fallback = knowledge.StrengthKnown
            ? $"Str[{knowledge.Strength} => {LogNumber.Format(TargetingPolicy.StrengthFactor(knowledge.Strength))}]"
            : "Str[Unknown => 1]";
        var attack = knowledge.AttackBonusKnown
            ? $"Attack[AB {knowledge.AttackBonus}; {knowledge.AttackWeapon} => " +
              $"{LogNumber.Format(risk.Attack)}]"
            : $"Attack[Unknown => fallback {fallback}]";
        var routine = knowledge.Threats[ThreatKnowledge.Routine].Estimate;
        var fallbackWeight = routine?.Hits.Sum(hit => CriticalWeight(actor, hit, knowledge)) ?? 1m;
        var baseMean = knowledge.DamageEstimate == null ? (decimal?)null :
            AssessedHits(knowledge.DamageEstimate, knowledge).Select((hit, index) => hit.BaseDamage *
                (routine != null && index < routine.Hits.Length ? CriticalWeight(actor, routine.Hits[index], knowledge) : 1m)).Sum();
        var ordinary = baseMean.HasValue
            ? $"Hit[avg {LogNumber.Format(baseMean.Value)} incl. known routine/crit weights => {LogNumber.Format(baseMean.Value / 10m)}]"
            : $"Hit[Unknown => fallback {fallback} * RoutineWeight[{LogNumber.Format(fallbackWeight)}]]";
        var sneak = knowledge.Threats[ThreatKnowledge.Sneak].Estimate;
        var extra = knowledge.Threats[ThreatKnowledge.ExtraDamage].Estimate;
        var drain = knowledge.Threats[ThreatKnowledge.StatDrain].Estimate;
        var sneakMean = sneak == null ? 0m : AssessedHits(sneak, knowledge).Sum(hit => hit.SneakDamage);
        var extraMean = extra == null ? 0m : AssessedHits(extra, knowledge).Sum(hit => hit.ExtraMean);
        var stat = drain == null ? 0m : AssessedHits(drain, knowledge).Sum(hit => hit.StatFactor);
        var damageDetails = $"\n  Damage: {LogNumber.Format(risk.Damage)} = {ordinary} + " +
            (sneak == null ? "Sneak[Unknown => 0]" : $"Sneak[potential avg {LogNumber.Format(sneakMean)} / 10 * Weight[{LogNumber.Format(ArdubsEnemyIntelligenceMain.Settings.ActiveSneakThreatMultiplier)}] => {LogNumber.Format(sneakMean / 10m * ArdubsEnemyIntelligenceMain.Settings.ActiveSneakThreatMultiplier)}]") + " + " +
            (extra == null ? "Extra[Unknown => 0]" : $"Extra[potential avg {LogNumber.Format(extraMean)} / 10 * Weight[{LogNumber.Format(ArdubsEnemyIntelligenceMain.Settings.ActiveExtraDamageThreatMultiplier)}] => {LogNumber.Format(extraMean / 10m * ArdubsEnemyIntelligenceMain.Settings.ActiveExtraDamageThreatMultiplier)}]") + " + " +
            (drain == null ? "Drain[Unknown => 0]" : $"Drain[{string.Join("; ", AssessedHits(drain, knowledge).Select(hit => hit.Effects.Describe(false)))}; normalized {LogNumber.Format(stat)} * Weight[{LogNumber.Format(ArdubsEnemyIntelligenceMain.Settings.ActiveStatDrainThreatMultiplier)}] => {LogNumber.Format(stat * ArdubsEnemyIntelligenceMain.Settings.ActiveStatDrainThreatMultiplier)}]") +
            "\n  Routine: " + (routine == null ? "Unknown => one hit; no crit estimate" :
                DescribeThreat(ThreatKnowledge.Routine, routine) + "; crit weights[" +
                string.Join(", ", routine.Hits.Select(hit => LogNumber.Format(CriticalWeight(actor, hit, knowledge)))) +
                (knowledge.AttackBonusKnown ? "; confirmation vs own AC using known primary AB" : "; confirmation Unknown => 50%") +
                "; ordinary hit only]");
        var caster = knowledge.CasterLevelKnown
            ? $"CastingSynergy[CL highest {knowledge.CasterLevel} => {LogNumber.Format(risk.CastingSynergy)}]"
            : !knowledge.CasterKnown ? "CastingSynergy[CL Unknown; caster Unknown => 1]"
            : knowledge.IsCaster ? $"CastingSynergy[CL Unknown; caster Yes => {LogNumber.Format(risk.CastingSynergy)}]" : "CastingSynergy[CL Unknown; caster No => 1]";
        var casterThreat = !knowledge.CasterKnown ? "caster Unknown => 0"
            : !knowledge.IsCaster ? "caster No => 0"
            : knowledge.CasterLevelKnown
                ? $"CL highest {knowledge.CasterLevel} => {LogNumber.Format(risk.CasterThreat)}"
                : $"CL Unknown; caster Yes => {LogNumber.Format(risk.CasterThreat)}";
        if (ArdubsEnemyIntelligenceMain.Settings.ActiveCasterThreatMultiplier != 1m)
            caster += $" [casting bonus * CasterWeight[{LogNumber.Format(ArdubsEnemyIntelligenceMain.Settings.ActiveCasterThreatMultiplier)}]]";
        return risk.Describe(attack, $"Damage[{LogNumber.Format(risk.Damage)}]", caster, casterThreat) + damageDetails;
    }

    private static IEnumerable<UnitEntityData> KnownTargets(UnitEntityData actor)
    {
        return Kingmaker.Game.Instance.State.Units
            .Concat(Kingmaker.Game.Instance.State.ExtraUnits)
            .Distinct(ReferenceComparer<UnitEntityData>.Instance)
            .Where(unit => IsValidTarget(actor, unit));
    }

    private static IEnumerable<UnitEntityData> VisibleTargets(UnitEntityData actor) =>
        KnownTargets(actor).Where(target => actor.Memory.ContainsVisible(target));

    private static string DescribeVisibleTargets(UnitEntityData actor) => DescribeTargets(VisibleTargets(actor));

    private static string DescribeTargets(IEnumerable<UnitEntityData> targets)
    {
        var names = targets.Select(UnitLabel).OrderBy(name => name).ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }

    private static string UnitLabel(UnitEntityData unit)
    {
        var game = Kingmaker.Game.Instance;
        var playerUnit = game?.Player?.GetMainPartyUnit();
        if (playerUnit == null)
        {
            return unit.CharacterName;
        }

        return EncounterLabels.Format(unit,
            game!.State.Units.Concat(game.State.ExtraUnits)
                .Distinct(ReferenceComparer<UnitEntityData>.Instance),
            candidate => !candidate.IsPlayerFaction && candidate.IsInCombat &&
                         candidate.IsEnemy(playerUnit),
            candidate => candidate.CharacterName);
    }

    private static EnemyMemory GetEnemy(UnitEntityData actor)
    {
        if (!Enemies.TryGetValue(actor, out var memory))
        {
            memory = new EnemyMemory();
            Enemies.Add(actor, memory);
        }

        return memory;
    }

    private static TargetKnowledge GetKnowledge(UnitEntityData actor, UnitEntityData target, EnemyMemory memory,
        IEnumerable<DefenseKind>? defenses = null)
    {
        OffensiveChecks? offense = null;
        if (!memory.Targets.TryGetValue(target, out var knowledge))
        {
            knowledge = new TargetKnowledge();
            memory.Targets.Add(target, knowledge);
            offense = LearnOffense(actor, target, knowledge, 0);
        }
        var defenseChecks = LearnDefenses(actor, target, knowledge,
            defenses ?? Enumerable.Empty<DefenseKind>(), knowledge.HasTargeted ? 10 : 0);
        LearnDamageResponse(actor, knowledge, knowledge.HasTargeted ? 10 : 0);
        LearnHitPoints(actor, target, knowledge, knowledge.HasTargeted ? 10 : 0);
        LearnConditions(actor, target, knowledge, afterAttack: false);
        ReportLearningAssessment(actor, target, knowledge, offense, defenseChecks);
        return knowledge;
    }

    private static void RefreshPerfectKnowledge(UnitEntityData actor, UnitEntityData target, TargetKnowledge knowledge)
    {
        var intelligence = Intelligence(actor);
        if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveStrengthMaximum && knowledge.StrengthKnown)
        {
            knowledge.Strength = target.Stats.Strength.ModifiedValue;
        }

        if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveCasterMaximum && knowledge.CasterKnown)
        {
            knowledge.IsCaster = target.Spellbooks.Any();
        }

        if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveCasterLevelMaximum && knowledge.CasterLevelKnown)
        {
            ReadCasterLevel(target, knowledge);
        }
        if ((intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveAttackBonusMaximum && knowledge.AttackBonusKnown) ||
            (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveDamageMaximum && knowledge.DamageEstimate != null))
        {
            var estimate = GetOffensiveEstimate(actor, target);
            if (estimate != null)
            {
                if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveAttackBonusMaximum && knowledge.AttackBonusKnown)
                {
                    knowledge.AttackBonus = estimate.AttackBonus;
                    knowledge.AttackWeapon = estimate.Weapon;
                }
                if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.ActiveDamageMaximum && knowledge.DamageEstimate != null)
                    knowledge.DamageEstimate = estimate;
            }
        }

        foreach (var defense in knowledge.Defenses)
        {
            if (intelligence >= ArdubsEnemyIntelligenceMain.Settings.DefenseMaximum(defense.Key) && defense.Value.Known)
            {
                defense.Value.Value = AttackAssessment.ReadDefense(target, defense.Key);
            }
        }
        foreach (var entry in knowledge.Threats)
            if (entry.Value.Estimate != null && intelligence >= ArdubsEnemyIntelligenceMain.Settings.ThreatMaximum(entry.Key))
                entry.Value.Estimate = GetOffensiveEstimate(actor, target);
    }

    private static KnowledgeCheck? TryLearnStrength(UnitEntityData actor, UnitEntityData target,
        TargetKnowledge knowledge, int bonus)
    {
        if (knowledge.StrengthKnown)
        {
            return null;
        }

        var check = RollKnowledge(Intelligence(actor) + bonus,
            ArdubsEnemyIntelligenceMain.Settings.ActiveStrengthMinimum, ArdubsEnemyIntelligenceMain.Settings.ActiveStrengthMaximum,
            bonus == 0 ? "first assessment" : "after being attacked");

        if (check.Success)
        {
            knowledge.StrengthKnown = true;
            knowledge.Strength = target.Stats.Strength.ModifiedValue;
        }
        return check;
    }

    private static KnowledgeCheck? TryLearnCaster(UnitEntityData actor, UnitEntityData target,
        TargetKnowledge knowledge, int bonus)
    {
        if (knowledge.CasterKnown)
        {
            return null;
        }

        var check = RollKnowledge(Intelligence(actor) + bonus,
            ArdubsEnemyIntelligenceMain.Settings.ActiveCasterMinimum, ArdubsEnemyIntelligenceMain.Settings.ActiveCasterMaximum,
            bonus == 0 ? "first assessment" : "after being attacked");

        if (check.Success)
        {
            knowledge.CasterKnown = true;
            knowledge.IsCaster = target.Spellbooks.Any();
        }
        return check;
    }

    private static OffensiveChecks LearnOffense(UnitEntityData actor, UnitEntityData target,
        TargetKnowledge knowledge, int bonus)
    {
        var checks = new OffensiveChecks
        {
            Strength = TryLearnStrength(actor, target, knowledge, bonus),
            Caster = TryLearnCaster(actor, target, knowledge, bonus)
        };
        var intelligence = Intelligence(actor) + bonus;
        var source = bonus == 0 ? "first assessment" : "after being attacked";
        if (!knowledge.AttackBonusKnown)
        {
            checks.Attack = RollKnowledge(intelligence, ArdubsEnemyIntelligenceMain.Settings.ActiveAttackBonusMinimum,
                ArdubsEnemyIntelligenceMain.Settings.ActiveAttackBonusMaximum, source);
            if (checks.Attack.Value.Success)
            {
                var estimate = GetOffensiveEstimate(actor, target);
                if (estimate != null)
                {
                    knowledge.AttackBonusKnown = true;
                    knowledge.AttackBonus = estimate.AttackBonus;
                    knowledge.AttackWeapon = estimate.Weapon;
                }
            }
        }
        if (knowledge.DamageEstimate == null)
        {
            checks.Damage = RollKnowledge(intelligence, ArdubsEnemyIntelligenceMain.Settings.ActiveDamageMinimum,
                ArdubsEnemyIntelligenceMain.Settings.ActiveDamageMaximum, source);
            if (checks.Damage.Value.Success)
                knowledge.DamageEstimate = GetOffensiveEstimate(actor, target);
        }
        if (!knowledge.CasterLevelKnown)
        {
            checks.CasterLevel = RollKnowledge(intelligence, ArdubsEnemyIntelligenceMain.Settings.ActiveCasterLevelMinimum,
                ArdubsEnemyIntelligenceMain.Settings.ActiveCasterLevelMaximum, source);
            if (checks.CasterLevel.Value.Success) ReadCasterLevel(target, knowledge);
        }
        // A generic attack cannot reveal hidden precision or drain. The +10
        // reroll for new categories requires an actual relevant damage event.
        if (bonus == 0) LearnThreats(actor, target, knowledge, checks, 0,
            Enum.GetValues(typeof(ThreatKnowledge)).Cast<ThreatKnowledge>());
        return checks;
    }

    private static void LearnThreats(UnitEntityData actor, UnitEntityData target, TargetKnowledge knowledge,
        OffensiveChecks checks, int bonus, IEnumerable<ThreatKnowledge> kinds)
    {
        foreach (var kind in kinds.Distinct())
        {
            var fact = knowledge.Threats[kind];
            if (fact.Estimate != null || fact.HasRolled && (bonus == 0 || fact.RerollUsed)) continue;
            var check = RollKnowledge(Intelligence(actor) + bonus, ArdubsEnemyIntelligenceMain.Settings.ThreatMinimum(kind),
                ArdubsEnemyIntelligenceMain.Settings.ThreatMaximum(kind), bonus == 0 ? "first assessment" : "after observing " + ThreatLabel(kind));
            fact.HasRolled = true; fact.RerollUsed = bonus != 0;
            if (check.Success) fact.Estimate = GetOffensiveEstimate(actor, target);
            checks.Threats.Add(kind, check);
        }
    }

    private static string ThreatLabel(ThreatKnowledge kind) => kind switch
    {
        ThreatKnowledge.Sneak => "Sneak/precision",
        ThreatKnowledge.ExtraDamage => "Elemental/enchantment",
        ThreatKnowledge.StatDrain => "Stat damage/drain & levels",
        ThreatKnowledge.Routine => "Frequency/criticals",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string DescribeThreat(ThreatKnowledge kind, OffensiveAssessment.Snapshot estimate)
    {
        var first = estimate.First;
        var value = kind == ThreatKnowledge.Sneak
            ? $"{first.SneakDice}d{first.SneakSides}; {LogNumber.Format(first.SneakDamage)} potential avg per hit"
            : kind == ThreatKnowledge.ExtraDamage
                ? $"{LogNumber.Format(first.ExtraMean)} potential avg per hit; {first.Effects.Describe(true)}"
                : kind == ThreatKnowledge.StatDrain
                    ? (first.Effects.Describe(false) is string description && description.Length > 0 ? description : "none modeled")
                    : $"{estimate.Hits.Length} normal attacks; " + string.Join(", ", estimate.Hits.Select(hit =>
                        $"{hit.Weapon}: threat {21 - hit.CriticalRange}-20; crit *{hit.CriticalMultiplier}"));
        if (kind == ThreatKnowledge.ExtraDamage || kind == ThreatKnowledge.StatDrain)
        {
            if (first.Effects.Conditional) value += "; conditional potential, not guaranteed";
            if (first.Effects.Unmodeled.Count > 0) value += "; unmodeled: " + string.Join(", ", first.Effects.Unmodeled);
        }
        return value;
    }

    private static void LearnDamageResponse(UnitEntityData actor, TargetKnowledge knowledge, int bonus)
    {
        if (knowledge.DamageResponseKnown || knowledge.ResponseRolled && (bonus == 0 || knowledge.ResponseRerollUsed)) return;
        knowledge.ResponseRolled = true; knowledge.ResponseRerollUsed = bonus != 0;
        knowledge.ResponseCheck = RollKnowledge(Intelligence(actor) + bonus, ArdubsEnemyIntelligenceMain.Settings.ActiveDamageResponseMinimum,
            ArdubsEnemyIntelligenceMain.Settings.ActiveDamageResponseMaximum, bonus == 0 ? "first assessment" : "after targeting");
        knowledge.DamageResponseKnown = knowledge.ResponseCheck.Value.Success;
    }

    private static void LearnHitPoints(UnitEntityData actor, UnitEntityData target, TargetKnowledge knowledge, int bonus)
    {
        if (!actor.Memory.ContainsVisible(target)) return;
        if (!knowledge.HitPointsKnown && (!knowledge.HitPointsRolled || bonus != 0 && !knowledge.HitPointsRerollUsed))
        {
            knowledge.HitPointsRolled = true;
            knowledge.HitPointsRerollUsed = bonus != 0;
            knowledge.HitPointsCheck = RollKnowledge(Intelligence(actor) + bonus,
                ArdubsEnemyIntelligenceMain.Settings.ActiveHitPointsMinimum,
                ArdubsEnemyIntelligenceMain.Settings.ActiveHitPointsMaximum,
                bonus == 0 ? "first assessment" : "after targeting");
            knowledge.HitPointsKnown = knowledge.HitPointsCheck.Value.Success;
        }
        // Once recognized, track wounds/healing while visible, not just the
        // initial snapshot. Unseen targets retain their last observed health.
        if (!knowledge.HitPointsKnown) return;
        knowledge.RemainingHitPoints = target.HPLeft;
        knowledge.MaximumHitPoints = target.MaxHP;
        if (knowledge.MaximumHitPoints <= 0)
            throw new InvalidOperationException("Known target has non-positive maximum HP: " + UnitLabel(target));
    }

    private static void LearnConditions(UnitEntityData actor, UnitEntityData target,
        TargetKnowledge knowledge, bool afterAttack)
    {
        if (!actor.Memory.ContainsVisible(target)) return;
        var conditions = AttackAssessment.ObservableConditions(target);
        if (!knowledge.Conditions.ShouldRoll(conditions, afterAttack)) return;
        knowledge.ConditionCheck = RollKnowledge(Intelligence(actor) + (afterAttack ? 10 : 0),
            ArdubsEnemyIntelligenceMain.Settings.ActiveConditionVulnerabilityMinimum,
            ArdubsEnemyIntelligenceMain.Settings.ActiveConditionVulnerabilityMaximum,
            afterAttack ? "after targeting" : "observing conditions");
        knowledge.Conditions.Record(knowledge.ConditionCheck.Value.Success);
    }

    private static void ReadCasterLevel(UnitEntityData target, TargetKnowledge knowledge)
    {
        knowledge.CasterLevel = OffensiveAssessment.HighestCasterLevel(target);
        knowledge.CasterLevelKnown = true;
        // Knowing the spellbooks' level necessarily reveals whether they exist,
        // even if the independent, easier recognition roll happened to fail.
        knowledge.CasterKnown = true;
        knowledge.IsCaster = target.Spellbooks.Any();
    }

    private static OffensiveAssessment.Snapshot? GetOffensiveEstimate(UnitEntityData actor, UnitEntityData target)
    {
        var estimates = GetEnemy(actor).OffensiveEstimates;
        if (!estimates.TryGetValue(target, out var estimate))
        {
            try
            {
                estimate = OffensiveAssessment.Read(target);
            }
            catch (Exception exception)
            {
                // Cache failures too: report a real error once per turn, leave
                // this fact unknown, and use the explicit Strength fallback.
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error($"Risk weapon preview failed for {UnitLabel(target)}: {exception}");
                estimate = null;
            }
            estimates.Add(target, estimate);
        }
        return estimate;
    }

    private static Dictionary<DefenseKind, KnowledgeCheck> LearnDefenses(UnitEntityData actor,
        UnitEntityData target, TargetKnowledge knowledge, IEnumerable<DefenseKind> kinds, int bonus)
    {
        var checks = new Dictionary<DefenseKind, KnowledgeCheck>();
        foreach (var kind in kinds.Distinct())
        {
            if (!knowledge.Defenses.TryGetValue(kind, out var defense))
            {
                defense = new DefenseKnowledge();
                knowledge.Defenses.Add(kind, defense);
            }
            if (defense.Known || defense.HasRolled && (bonus == 0 || defense.RerollUsed))
            {
                continue;
            }
            var check = RollKnowledge(Intelligence(actor) + bonus,
                ArdubsEnemyIntelligenceMain.Settings.DefenseMinimum(kind), ArdubsEnemyIntelligenceMain.Settings.DefenseMaximum(kind),
                bonus == 0 ? "first assessment" : "after targeting");
            defense.HasRolled = true;
            defense.RerollUsed = bonus != 0;
            if (check.Success)
            {
                defense.Known = true;
                defense.Value = AttackAssessment.ReadDefense(target, kind);
            }
            checks.Add(kind, check);
        }
        return checks;
    }

    private static void ReportLearningAssessment(UnitEntityData actor, UnitEntityData target,
        TargetKnowledge knowledge, OffensiveChecks? offense,
        Dictionary<DefenseKind, KnowledgeCheck>? defenseChecks = null)
    {
        var responseCheck = knowledge.ResponseCheck;
        knowledge.ResponseCheck = null;
        var conditionCheck = knowledge.ConditionCheck;
        knowledge.ConditionCheck = null;
        var hitPointsCheck = knowledge.HitPointsCheck;
        knowledge.HitPointsCheck = null;
        var currentCheck = offense?.Strength ?? offense?.Caster ?? offense?.Attack ?? offense?.Damage ?? offense?.CasterLevel ??
            (offense?.Threats.Count > 0 ? (KnowledgeCheck?)offense.Threats.First().Value : null) ??
            (defenseChecks?.Count > 0 ? (KnowledgeCheck?)defenseChecks.First().Value : null) ?? responseCheck ?? conditionCheck ?? hitPointsCheck;
        if (!currentCheck.HasValue || !ArdubsEnemyIntelligenceMain.Settings.ShowLearningInLog || !ArdubsEnemyIntelligenceMain.Settings.AnyLogging)
        {
            return;
        }

        var context = currentCheck.Value.Source == "first assessment"
            ? ""
            : $" ({currentCheck.Value.Source}; effective Int {currentCheck.Value.EffectiveIntelligence})";
        var casterInferred = offense?.Caster?.Success == false && offense?.CasterLevel?.Success == true
            ? " [revealed by caster-level assessment]" : "";
        var attackUnavailable = offense?.Attack?.Success == true && !knowledge.AttackBonusKnown
            ? " [weapon preview unavailable]" : "";
        var damageUnavailable = offense?.Damage?.Success == true && knowledge.DamageEstimate == null
            ? " [weapon preview unavailable]" : "";
        TargetingLog.WriteLearning($"{UnitLabel(actor)} (Int {Intelligence(actor)}) assessing " +
            $"{UnitLabel(target)}{context}:\n" +
            $"Strength: {(knowledge.StrengthKnown ? knowledge.Strength.ToString(CultureInfo.InvariantCulture) : "Unknown")}" +
            $"{CheckSuffix(offense?.Strength)}\n" +
            $"Attack bonus: {(knowledge.AttackBonusKnown ? $"{knowledge.AttackBonus} [{knowledge.AttackWeapon}]" : "Unknown")}" +
            $"{CheckSuffix(offense?.Attack)}{attackUnavailable}\n" +
            $"Damage: {(knowledge.DamageEstimate == null ? "Unknown" : DescribeDamage(knowledge.DamageEstimate))}" +
            $"{CheckSuffix(offense?.Damage)}{damageUnavailable}\n" +
            $"Spellcasting: {(knowledge.CasterKnown ? (knowledge.IsCaster ? "Yes" : "No") : "Unknown")}" +
            $"{CheckSuffix(offense?.Caster)}{casterInferred}\n" +
            $"Caster level: {(knowledge.CasterLevelKnown ? knowledge.CasterLevel.ToString(CultureInfo.InvariantCulture) : "Unknown")}" +
            $"{CheckSuffix(offense?.CasterLevel)}" +
            string.Concat(knowledge.Threats.Select(entry => $"\n{ThreatLabel(entry.Key)}: " +
                (entry.Value.Estimate == null ? "Unknown" : DescribeThreat(entry.Key, entry.Value.Estimate)) +
                CheckSuffix(offense != null && offense.Threats.TryGetValue(entry.Key, out var threatCheck)
                    ? (KnowledgeCheck?)threatCheck : null))) +
            $"\nDamage/spell protection: {(knowledge.DamageResponseKnown ? "known; evaluated per proposed attack" : "Unknown")}" +
            CheckSuffix(responseCheck) +
            $"\nRemaining HP: {(knowledge.HitPointsKnown ? $"{knowledge.RemainingHitPoints}/{knowledge.MaximumHitPoints}" : "Unknown")}" +
            CheckSuffix(hitPointsCheck) +
            (knowledge.Conditions.Conditions == VisibleDefenseCondition.None ? "" :
                $"\nCondition vulnerability: {(knowledge.Conditions.Known ? "recognized" : "Unknown")}" +
                $" [{knowledge.Conditions.Conditions}]" + CheckSuffix(conditionCheck)) +
            string.Concat(knowledge.Defenses.Select(entry =>
                $"\n{AttackAssessment.Label(entry.Key)}: " +
                (entry.Value.Known ? entry.Value.Value.ToString(CultureInfo.InvariantCulture) : "Unknown") +
                CheckSuffix(defenseChecks != null && defenseChecks.TryGetValue(entry.Key, out var check)
                    ? (KnowledgeCheck?)check : null))));
    }

    private static string CheckSuffix(KnowledgeCheck? check)
    {
        if (!check.HasValue)
        {
            return "";
        }

        var roll = (check.Value.Roll * 100).ToString("0.#", CultureInfo.InvariantCulture);
        var chance = (check.Value.Chance * 100).ToString("0.#", CultureInfo.InvariantCulture);
        return $" ({roll}/{chance}%)";
    }

    private static KnowledgeCheck RollKnowledge(int intelligence, int zeroAt, int certainAt, string source)
    {
        var chance = TargetingPolicy.KnowledgeChance(intelligence, zeroAt, certainAt);
        var roll = Random.NextDouble();
        return new KnowledgeCheck(intelligence, chance, roll, source);
    }

    private static int Intelligence(UnitEntityData actor)
    {
        return actor.Stats.Intelligence.ModifiedValue;
    }

    private static bool IsValidTarget(UnitEntityData actor, UnitEntityData? target)
    {
        // Vanilla considers enemies in group memory, including attackers beyond
        // current sight. Visibility is not awareness: keep known opponents for
        // pursuit without discovering unknown units. Native action eligibility,
        // targeting, range/LOS and complete-route checks still run separately.
        return target != null && target.IsInCombat && target.State.IsConscious &&
               actor.IsEnemy(target) && actor.Memory.Contains(target) &&
               SleepingTargetBlocker(actor, target) == null;
    }

    private static string? SleepingTargetBlocker(UnitEntityData actor, UnitEntityData target)
    {
        if (!target.State.HasCondition(UnitCondition.Sleeping)) return null;
        var playerUnit = Kingmaker.Game.Instance.Player?.GetMainPartyUnit();
        var allied = playerUnit != null && IsAlliedNpc(actor, playerUnit);
        if (!SleepingTargetPolicy.Allows(ArdubsEnemyIntelligenceMain.Settings.AvoidSleepingTargets,
                sleeping: true, hasActiveOpponent: false, alliedNpc: allied))
            return "allied NPCs never attack sleeping targets";
        if (SleepingTargetPolicy.Allows(ArdubsEnemyIntelligenceMain.Settings.AvoidSleepingTargets,
                sleeping: true, hasActiveOpponent: true, alliedNpc: allied)) return null;
        // All loaded combat opponents, not the selected action's candidates or
        // visible/reachable list. Recheck at execution if conditions have changed.
        var awake = Kingmaker.Game.Instance.State.Units.Concat(Kingmaker.Game.Instance.State.ExtraUnits)
            .FirstOrDefault(unit => !ReferenceEquals(unit, target) &&
                SleepingTargetPolicy.IsActiveOpponent(unit.IsInCombat, actor.IsEnemy(unit),
                    unit.State.IsConscious, unit.State.HasCondition(UnitCondition.Sleeping),
                    IsIncapacitatedForSleepingPolicy(unit.State)));
        return awake == null ? null : $"active enemy {UnitLabel(awake)} remains (reachability does not permit waking sleepers)";
    }

    internal static bool IsIncapacitatedForSleepingPolicy(UnitState state) =>
        state.HasCondition(UnitCondition.Sleeping) || state.HasCondition(UnitCondition.Unconscious) ||
        state.HasCondition(UnitCondition.Helpless) || state.HasCondition(UnitCondition.Paralyzed) ||
        state.HasCondition(UnitCondition.Petrified) || state.HasCondition(UnitCondition.Stunned) ||
        state.HasCondition(UnitCondition.Dazed) || state.HasCondition(UnitCondition.CantAct);

    private sealed class EnemyMemory
    {
        internal bool Initialized;
        internal TurnController? Turn;
        internal UnitEntityData? CommittedTarget;
        internal readonly TurnTargetLock<UnitEntityData> TargetLock = new TurnTargetLock<UnitEntityData>();
        internal bool LockedByTurnRoll;
        internal bool DownedTargetReleased;
        internal bool DecisionReported;
        internal PathScoreState? ActivePathScore;
        internal readonly Dictionary<AiAction, Dictionary<UnitEntityData, ScoreTrace>> Scores =
            new Dictionary<AiAction, Dictionary<UnitEntityData, ScoreTrace>>(ReferenceComparer<AiAction>.Instance);
        internal readonly Dictionary<AiAction, AttackAssessment[]> Profiles =
            new Dictionary<AiAction, AttackAssessment[]>(ReferenceComparer<AiAction>.Instance);
        internal readonly Dictionary<AiAction, Dictionary<UnitEntityData, string>> SkippedScores =
            new Dictionary<AiAction, Dictionary<UnitEntityData, string>>(ReferenceComparer<AiAction>.Instance);
        internal readonly Dictionary<AiAction, HashSet<UnitEntityData>> VisibleWhenScored =
            new Dictionary<AiAction, HashSet<UnitEntityData>>(ReferenceComparer<AiAction>.Instance);
        internal readonly Dictionary<UnitEntityData, TargetKnowledge> Targets =
            new Dictionary<UnitEntityData, TargetKnowledge>(ReferenceComparer<UnitEntityData>.Instance);
        internal readonly Dictionary<UnitEntityData, CalculationSnapshot> LastCalculations =
            new Dictionary<UnitEntityData, CalculationSnapshot>(ReferenceComparer<UnitEntityData>.Instance);
        internal readonly Dictionary<UnitEntityData, OffensiveAssessment.Snapshot?> OffensiveEstimates =
            new Dictionary<UnitEntityData, OffensiveAssessment.Snapshot?>(ReferenceComparer<UnitEntityData>.Instance);
        internal readonly HashSet<RuleAttackRoll> UsefulNonHp = new HashSet<RuleAttackRoll>();
    }

    private sealed class CalculationSnapshot
    {
        internal readonly string Action;
        internal readonly string Details;
        internal CalculationSnapshot(string action, string details)
        {
            Action = action;
            Details = details;
        }
    }

    private sealed class ScoreTrace
    {
        private TargetScoreBreakdown Breakdown;
        private readonly string[] Terms;
        private readonly SquishResult SingleAttack;
        private string DistanceDescription = "pending game path evaluation";
        internal decimal ModifiedScore => Breakdown.Score;
        internal string[] Details => new[]
        {
            Breakdown.Describe(Terms[0], Terms[1], Terms[2], Terms[3], DistanceDescription)
        };

        internal ScoreTrace(TargetScoreBreakdown breakdown, string[] terms, SquishResult singleAttack)
        {
            Breakdown = breakdown;
            Terms = terms;
            SingleAttack = singleAttack;
        }

        internal void UseSingleAttack()
        {
            Breakdown = Breakdown.WithSquish(SingleAttack.Factor);
            Terms[2] = "Single attack[" + SingleAttack.Description + "]";
        }

        internal void SetDistance(decimal factor, string description)
        {
            Breakdown = Breakdown.WithDistance(factor);
            DistanceDescription = description;
        }
    }

    private sealed class TargetKnowledge
    {
        internal bool StrengthKnown;
        internal int Strength;
        internal bool AttackBonusKnown;
        internal int AttackBonus;
        internal string AttackWeapon = "";
        internal OffensiveAssessment.Snapshot? DamageEstimate;
        internal bool CasterKnown;
        internal bool IsCaster;
        internal bool CasterLevelKnown;
        internal int CasterLevel;
        internal readonly Dictionary<DefenseKind, DefenseKnowledge> Defenses =
            new Dictionary<DefenseKind, DefenseKnowledge>();
        internal readonly EffectivenessMemory Effectiveness = new EffectivenessMemory();
        internal bool OffensiveRerollUsed;
        internal bool HasTargeted;
        internal readonly Dictionary<ThreatKnowledge, ThreatFact> Threats =
            Enum.GetValues(typeof(ThreatKnowledge)).Cast<ThreatKnowledge>()
                .ToDictionary(kind => kind, kind => new ThreatFact());
        internal bool DamageResponseKnown;
        internal bool ResponseRolled;
        internal bool ResponseRerollUsed;
        internal KnowledgeCheck? ResponseCheck;
        internal bool HitPointsKnown;
        internal bool HitPointsRolled;
        internal bool HitPointsRerollUsed;
        internal int RemainingHitPoints;
        internal int MaximumHitPoints;
        internal KnowledgeCheck? HitPointsCheck;
        internal readonly ConditionRecognitionMemory Conditions = new ConditionRecognitionMemory();
        internal KnowledgeCheck? ConditionCheck;
        internal readonly Dictionary<string, DamageProtection.Result> Protection =
            new Dictionary<string, DamageProtection.Result>();
    }

    private sealed class ThreatFact
    {
        internal OffensiveAssessment.Snapshot? Estimate;
        internal bool HasRolled;
        internal bool RerollUsed;
    }

    private sealed class DefenseKnowledge
    {
        internal bool Known;
        internal int Value;
        internal bool HasRolled;
        internal bool RerollUsed;
    }

    private sealed class OffensiveChecks
    {
        internal KnowledgeCheck? Strength;
        internal KnowledgeCheck? Attack;
        internal KnowledgeCheck? Damage;
        internal KnowledgeCheck? Caster;
        internal KnowledgeCheck? CasterLevel;
        internal readonly Dictionary<ThreatKnowledge, KnowledgeCheck> Threats =
            new Dictionary<ThreatKnowledge, KnowledgeCheck>();
    }

    private readonly struct KnowledgeCheck
    {
        internal readonly int EffectiveIntelligence;
        internal readonly double Chance;
        internal readonly double Roll;
        internal readonly string Source;

        internal bool Success => Roll < Chance;

        internal KnowledgeCheck(int effectiveIntelligence, double chance, double roll, string source)
        {
            EffectiveIntelligence = effectiveIntelligence;
            Chance = chance;
            Roll = roll;
            Source = source;
        }

    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T left, T right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
