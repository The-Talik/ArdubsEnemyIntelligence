using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Serialization;

// Read real game metadata classes without launching Unity or triggering rules.
internal static class GameMetadataTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly game;
    private static Assembly mod;
    private static object New(string name) => Activator.CreateInstance(game.GetType(name, true));
    private static FieldInfo FindField(object value, string name)
    {
        for (var type = value.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null) return field;
        }
        throw new Exception("Missing metadata field " + name + " on " + value.GetType());
    }
    private static object Field(object value, string name) => FindField(value, name).GetValue(value);
    private static void Set(object value, string name, object data) => FindField(value, name).SetValue(value, data);
    private static void SetEnum(object value, string name, string member) => Set(value, name,
        Enum.Parse(value.GetType().GetField(name, Flags).FieldType, member));

    private static object Actions(params object[] actions)
    {
        var list = New("Kingmaker.ElementsSystem.ActionList");
        var array = Array.CreateInstance(game.GetType("Kingmaker.ElementsSystem.GameAction"), actions.Length);
        for (var i = 0; i < actions.Length; i++) array.SetValue(actions[i], i);
        Set(list, "Actions", array);
        return list;
    }

    private static object Damage(string type, int dice, string sides, int bonus = 0, bool drain = false)
    {
        var action = New("Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDealDamage");
        SetEnum(action, "m_Type", type); SetEnum(action, "AbilityType", "Strength"); Set(action, "Drain", drain);
        var value = New("Kingmaker.UnitLogic.Mechanics.ContextDiceValue");
        SetEnum(value, "DiceType", sides);
        var count = New("Kingmaker.UnitLogic.Mechanics.ContextValue");
        var flat = New("Kingmaker.UnitLogic.Mechanics.ContextValue");
        SetEnum(count, "ValueType", "Simple"); Set(count, "Value", dice);
        SetEnum(flat, "ValueType", "Simple"); Set(flat, "Value", bonus);
        Set(value, "DiceCountValue", count); Set(value, "BonusValue", flat); Set(action, "Value", value);
        return action;
    }

    private static object Read(object actions) => mod.GetType("ArdubsEnemyIntelligence.OnHitAssessment")
        .GetMethod("ReadActions", Flags).Invoke(null, new object[] { actions, null,
            Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(game.GetType("Kingmaker.Blueprints.BlueprintScriptableObject"))), 0 });

    private static void Equal(decimal expected, object actual, string label)
    {
        if (Math.Abs(Convert.ToDecimal(actual) - expected) > 0.000000001m) throw new Exception(label + ": expected " + expected + ", got " + actual);
    }
    private static object Property(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value, null);

    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        var opcodes = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.FieldType == typeof(OpCode))
            {
                var opcode = (OpCode)field.GetValue(null);
                opcodes[opcode.Value] = opcode;
            }
        var bytes = method.GetMethodBody().GetILAsByteArray();
        for (var index = 0; index < bytes.Length;)
        {
            short code = bytes[index++];
            if (code == 0xfe) code = (short)(0xfe00 | bytes[index++]);
            var opcode = opcodes[code];
            switch (opcode.OperandType)
            {
                case OperandType.InlineMethod:
                    yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, index));
                    index += 4; break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(bytes, index); index += 4 + count * 4; break;
                case OperandType.InlineI8: case OperandType.InlineR: index += 8; break;
                case OperandType.InlineBrTarget: case OperandType.InlineField: case OperandType.InlineI:
                case OperandType.InlineSig: case OperandType.InlineString: case OperandType.InlineTok:
                case OperandType.InlineType: case OperandType.ShortInlineR: index += 4; break;
                case OperandType.InlineVar: index += 2; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: index++; break;
            }
        }
    }

    private static bool SkipNativeProfiling(ref IDisposable __result)
    {
        __result = null;
        return false;
    }

    private static void RememberedTargetTests()
    {
        var unitType = game.GetType("Kingmaker.EntitySystem.Entities.UnitEntityData");
        var descriptorType = game.GetType("Kingmaker.UnitLogic.UnitDescriptor");
        var groupType = game.GetType("Kingmaker.UnitLogic.Groups.UnitGroup");
        var combatType = game.GetType("Kingmaker.Controllers.Combat.UnitCombatState");
        var partyGroup = Activator.CreateInstance(groupType, Flags, null, new object[] { "awareness-party" }, null);
        var enemyGroup = Activator.CreateInstance(groupType, Flags, null, new object[] { "awareness-golems" }, null);
        Set(enemyGroup, "m_IsEnemyForEveryone", true);
        object Unit(string id, object group)
        {
            var unit = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(unitType);
            var descriptor = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(descriptorType);
            Set(descriptor, "State", Activator.CreateInstance(game.GetType("Kingmaker.UnitLogic.UnitState"), true));
            Set(unit, "<Descriptor>k__BackingField", descriptor);
            Set(unit, "<UniqueId>k__BackingField", id);
            Set(unit, "m_Group", group);
            var combat = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(combatType);
            Set(combat, "m_InCombat", true);
            Set(unit, "<CombatState>k__BackingField", combat);
            return unit;
        }
        var actor = Unit("awareness-golem", enemyGroup);
        var target = Unit("awareness-attacker", partyGroup);
        var memory = Field(enemyGroup, "Memory");
        var entries = (IDictionary)Field(memory, "m_Units");
        var info = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
            game.GetType("Kingmaker.UnitLogic.UnitGroupMemory+UnitInfo"));
        var targeting = mod.GetType("ArdubsEnemyIntelligence.TargetingState");
        var eligible = targeting.GetMethod("IsValidTarget", Flags);
        bool Eligible() => (bool)eligible.Invoke(null, new[] { actor, target });

        // Bypass only Unity profiling; use native group allegiance, combat/life
        // state and group-memory Contains/ContainsVisible in the real adapter.
        var harmonyAssembly = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(game.Location), "0Harmony.dll"));
        var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony");
        const string owner = "ArdubsEnemyIntelligence.AwarenessFixture";
        var harmony = Activator.CreateInstance(harmonyType, new object[] { owner });
        var profile = game.GetType("Kingmaker.Utility.ProfileScope").GetMethods(Flags).Single(method =>
            method.Name == "New" && method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType.FullName == "UnityEngine.Object");
        var prefix = Activator.CreateInstance(harmonyAssembly.GetType("HarmonyLib.HarmonyMethod"),
            new object[] { typeof(GameMetadataTests).GetMethod("SkipNativeProfiling", Flags) });
        harmonyType.GetMethod("Patch", Flags).Invoke(harmony, new object[] { profile, prefix, null, null, null });
        try
        {
            if (Eligible()) throw new Exception("Unknown opponents must not be discovered by target scoring.");
            entries.Add("awareness-attacker", info);
            if (!(bool)memory.GetType().GetMethod("Contains", Flags, null, new[] { unitType }, null)
                    .Invoke(memory, new[] { target }) ||
                (bool)memory.GetType().GetMethod("ContainsVisible", Flags).Invoke(memory, new[] { target }))
                throw new Exception("Fixture must reproduce a remembered but currently unseen attacker.");
            if (!Eligible()) throw new Exception("A remembered unseen attacker must remain eligible for pursuit.");
            Set(info, "Visible", true);
            if (!Eligible()) throw new Exception("A currently visible remembered opponent must remain eligible.");
            Set(info, "Visible", false);
            if (!Eligible()) throw new Exception("Losing sight must not discard a still-remembered opponent.");
            var combat = Property(target, "CombatState");
            Set(combat, "m_InCombat", false);
            if (Eligible()) throw new Exception("Remembering an opponent must not override combat eligibility.");
            Set(combat, "m_InCombat", true);
            var state = Property(target, "State");
            var life = FindField(state, "<LifeState>k__BackingField");
            life.SetValue(state, Enum.ToObject(life.FieldType, 1));
            if (Eligible()) throw new Exception("Remembering an opponent must not override unconsciousness.");
            life.SetValue(state, Enum.ToObject(life.FieldType, 2));
            if (Eligible()) throw new Exception("Remembering an opponent must not override death.");
            life.SetValue(state, Enum.ToObject(life.FieldType, 0));
            Set(target, "m_Group", enemyGroup);
            if (Eligible()) throw new Exception("Remembering a former opponent must not override current allegiance.");
            Set(target, "m_Group", partyGroup);
            entries.Clear();
            if (Eligible()) throw new Exception("An expired/removed native memory must stop granting awareness.");
        }
        finally { harmonyType.GetMethod("UnpatchAll", Flags).Invoke(harmony, new object[] { owner }); }
        if (Calls(eligible).Any(call => call.Name == "ContainsVisible") ||
            !Calls(eligible).Any(call => call.DeclaringType.Name == "UnitGroupMemory" && call.Name == "Contains"))
            throw new Exception("Pursuit eligibility must use native awareness, not the current-visibility flag.");
    }

    private static void OpportunityAllegianceTests()
    {
        var unitType = game.GetType("Kingmaker.EntitySystem.Entities.UnitEntityData");
        var groupType = game.GetType("Kingmaker.UnitLogic.Groups.UnitGroup");
        var combatType = game.GetType("Kingmaker.Controllers.Combat.UnitCombatState");
        var partyGroup = Activator.CreateInstance(groupType, Flags, null, new object[] { "aoo-party" }, null);
        var enemyGroup = Activator.CreateInstance(groupType, Flags, null, new object[] { "aoo-spectre" }, null);
        Set(enemyGroup, "m_IsEnemyForEveryone", true);
        var spectre = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(unitType);
        var mover = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(unitType);
        var descriptorType = game.GetType("Kingmaker.UnitLogic.UnitDescriptor");
        Set(spectre, "<Descriptor>k__BackingField", System.Runtime.Serialization.FormatterServices.GetUninitializedObject(descriptorType));
        Set(mover, "<Descriptor>k__BackingField", System.Runtime.Serialization.FormatterServices.GetUninitializedObject(descriptorType));
        Set(spectre, "m_Group", enemyGroup);
        Set(mover, "m_Group", partyGroup);
        var hostile = mod.GetType("ArdubsEnemyIntelligence.OpportunityAllegiance").GetMethod("IsHostile", Flags);
        // Only bypass Unity profiling; execute the actual game's group/hostility
        // logic. The test process removes its own temporary patch after each call.
        var harmonyAssembly = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(game.Location), "0Harmony.dll"));
        var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony");
        const string owner = "ArdubsEnemyIntelligence.AllegianceFixture";
        var harmony = Activator.CreateInstance(harmonyType, new object[] { owner });
        var profile = game.GetType("Kingmaker.Utility.ProfileScope").GetMethods(Flags).Single(method =>
            method.Name == "New" && method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType.FullName == "UnityEngine.Object");
        var prefix = Activator.CreateInstance(harmonyAssembly.GetType("HarmonyLib.HarmonyMethod"),
            new object[] { typeof(GameMetadataTests).GetMethod("SkipNativeProfiling", Flags) });
        bool CanReact()
        {
            harmonyType.GetMethod("Patch", Flags).Invoke(harmony, new object[] { profile, prefix, null, null, null });
            try { return (bool)hostile.Invoke(null, new[] { spectre, mover }); }
            finally { harmonyType.GetMethod("UnpatchAll", Flags).Invoke(harmony, new object[] { owner }); }
        }
        if (!CanReact()) throw new Exception("A hostile spectre must retain its normal opportunity attack.");

        // Keep the real native engagement dictionary intact while changing sides,
        // reproducing the stale relation that native IsEngage still accepts.
        var combat = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(combatType);
        var engagedType = typeof(Dictionary<,>).MakeGenericType(unitType, typeof(TimeSpan));
        var engaged = (IDictionary)Activator.CreateInstance(engagedType);
        engaged.Add(mover, TimeSpan.Zero);
        Set(combat, "m_EngagedUnits", engaged);
        Set(spectre, "m_Group", partyGroup);
        if (!(bool)combatType.GetMethod("IsEngage", Flags).Invoke(combat, new[] { mover }))
            throw new Exception("Fixture must retain native stale engagement after control changes allegiance.");
        if (CanReact()) throw new Exception("Stale engagement must not permit a former enemy to attack an ally.");
        Set(spectre, "m_Group", enemyGroup);
        if (!CanReact()) throw new Exception("Opportunity attacks must resume if control ends and hostility returns.");

        var safeguard = mod.GetType("ArdubsEnemyIntelligence.OpportunityAllegiance");
        foreach (var hook in new[] { "Engagement", "Reaction", "QueuedReaction" })
        {
            var method = safeguard.GetNestedType(hook, Flags).GetMethod(hook == "Engagement" ? "Postfix" : "Prefix", Flags);
            if (!Calls(method).Contains(hostile))
                throw new Exception(hook + " must check current native allegiance.");
        }
    }

    private static void AlliedNpcScopeTests()
    {
        // Reproduce the real Friends faction's AllExcludePlayer behaviour using
        // native faction metadata, without launching a Unity scene.
        var friends = New("Kingmaker.Blueprints.BlueprintFaction");
        var party = New("Kingmaker.Blueprints.BlueprintFaction");
        SetEnum(friends, "m_AllyFactionsBehaviour", "AllExcludePlayer");
        Set(party, "IsDirectlyControllable", true);
        if ((bool)friends.GetType().GetMethod("IsAlly", Flags).Invoke(friends, new[] { party }))
            throw new Exception("Native Friends/player relation contract changed; recheck allied scope.");
        var policy = mod.GetType("ArdubsEnemyIntelligence.ManagedActorPolicy");
        bool Friendly(object faction) => (bool)policy.GetMethod("IsFriendlyCombatant", Flags).Invoke(null,
            new object[] { false, false, false, Field(faction, "Neutral"),
                Field(faction, "AlwaysEnemy"), Field(faction, "EnemyForEveryone") });
        if (!Friendly(friends)) throw new Exception("Non-hostile Friends faction must be recognized despite formal IsAlly=false.");
        var neutral = New("Kingmaker.Blueprints.BlueprintFaction");
        Set(neutral, "Neutral", true);
        if (Friendly(neutral)) throw new Exception("Unrelated neutral factions must not qualify on non-hostility alone.");
        var group = new[] { Friendly(neutral), Friendly(friends), Friendly(neutral) };
        bool Allied(bool hostile) => (bool)policy.GetMethod("IsAlliedNpc", Flags).Invoke(null,
            new object[] { hostile, Friendly(neutral), group });
        if (!Allied(false) || Allied(true)) throw new Exception("Compiled friendly-group policy must adopt neutrals only without hostility.");
        var targeting = mod.GetType("ArdubsEnemyIntelligence.TargetingState");
        var scopeAdapter = false;
        foreach (var call in Calls(targeting.GetMethod("IsManaged", Flags)))
            if (call.DeclaringType == targeting && call.Name == "IsAlliedNpc") scopeAdapter = true;
        if (!scopeAdapter) throw new Exception("Actual AI scope must use the tested friendly-group adapter.");
        var nativeGroup = false;
        var pureGroup = false;
        foreach (var call in Calls(targeting.GetMethod("IsAlliedNpc", Flags)))
        {
            if (call.DeclaringType.Name == "UnitGroup" && call.Name == "Select") nativeGroup = true;
            if (call.DeclaringType == policy && call.Name == "IsAlliedNpc") pureGroup = true;
            if (call.Name == "get_Units" || call.Name == "ContainsVisible" || call.Name == "FindPath")
                throw new Exception("Ally group adapter must not adopt unrelated nearby or reachable combatants.");
        }
        if (!nativeGroup || !pureGroup) throw new Exception("Ally adapter must examine actual native group members with shared tested policy.");
        var sleepingUsesScope = false;
        foreach (var call in Calls(targeting.GetMethod("AllowsSleepingCommand", Flags)))
            if (call.Name == "IsManaged") sleepingUsesScope = true;
        if (!sleepingUsesScope) throw new Exception("Sleeping command protection must include the expanded allied scope.");
    }

    private static void SleepingTargetTests()
    {
        var targeting = mod.GetType("ArdubsEnemyIntelligence.TargetingState");
        var incapacitated = targeting.GetMethod("IsIncapacitatedForSleepingPolicy", Flags);
        var conditionsType = game.GetType("Kingmaker.UnitLogic.UnitCondition");
        var state = Activator.CreateInstance(game.GetType("Kingmaker.UnitLogic.UnitState"), true);
        var conditions = (sbyte[])Field(state, "m_Conditions");
        var disabling = new HashSet<string> { "Sleeping", "Unconscious", "Helpless", "Paralyzed",
            "Petrified", "Stunned", "Dazed", "CantAct" };
        if ((bool)incapacitated.Invoke(null, new[] { state }))
            throw new Exception("A normal awake unit must count as active.");
        foreach (var name in Enum.GetNames(conditionsType))
        {
            Array.Clear(conditions, 0, conditions.Length);
            conditions[Convert.ToInt32(Enum.Parse(conditionsType, name))] = 1;
            if ((bool)incapacitated.Invoke(null, new[] { state }) != disabling.Contains(name))
                throw new Exception("Sleep fallback incapacity mismatch: " + name);
        }
        foreach (var method in new[] { "IsValidTarget", "AllowTargetScore", "AllowsSleepingCommand" })
        {
            var checksSleep = false;
            foreach (var call in Calls(targeting.GetMethod(method, Flags)))
                if (call.Name == "SleepingTargetBlocker") checksSleep = true;
            if (!checksSleep) throw new Exception(method + " must apply the shared sleeping-target filter.");
        }
        var submissionChecksSleep = false;
        foreach (var call in Calls(targeting.GetMethod("AllowCommand", Flags)))
            if (call.Name == "AllowsSleepingCommand") submissionChecksSleep = true;
        if (!submissionChecksSleep) throw new Exception("Command submission must reject sleeping fallback targets.");
        foreach (var call in Calls(targeting.GetMethod("SleepingTargetBlocker", Flags)))
            if (call.Name == "VisibleTargets" || call.Name == "ContainsVisible" || call.Name == "IsValidTarget" || call.Name == "FindPath")
                throw new Exception("Awake combat opponents must not be filtered by visibility, validity or paths.");
        var alliedSleepPolicy = false;
        var sharedSleepPolicy = false;
        foreach (var call in Calls(targeting.GetMethod("SleepingTargetBlocker", Flags)))
        {
            if (call.Name == "IsAlliedNpc") alliedSleepPolicy = true;
            if (call.DeclaringType.Name == "SleepingTargetPolicy" && call.Name == "Allows" && call.GetParameters().Length == 4)
                sharedSleepPolicy = true;
        }
        if (!alliedSleepPolicy || !sharedSleepPolicy)
            throw new Exception("Shared sleeping-target filter must distinguish allied NPCs using the tested absolute prohibition.");
        var allows = mod.GetType("ArdubsEnemyIntelligence.SleepingTargetPolicy").GetMethod("Allows", Flags);
        foreach (var enemyOption in new[] { false, true })
        foreach (var activeOpponent in new[] { false, true })
        {
            if ((bool)allows.Invoke(null, new object[] { enemyOption, true, activeOpponent, true }))
                throw new Exception("Allied NPCs must never target sleeping opponents, even alone or with enemy protection off.");
            if ((bool)allows.Invoke(null, new object[] { enemyOption, true, activeOpponent, false }) != (!enemyOption || !activeOpponent))
                throw new Exception("Enemy sleep-target fallback behaviour must remain unchanged.");
        }
        if ((string)mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain").GetField("DisplayName", Flags).GetRawConstantValue() != "Ardub's Enemy Intelligence")
            throw new Exception("Public mod name was not updated.");
        var interrupts = false;
        foreach (var call in Calls(targeting.GetMethod("StopSleepingCommand", Flags)))
        {
            if (call.Name == "Interrupt") interrupts = true;
            if (call.Name == "BeginTurn" || call.Name == "TryCommit" || call.Name == "Reset")
                throw new Exception("Cancelling an attack must not unlock mid-turn retargeting.");
        }
        if (!interrupts) throw new Exception("Execution safety check must interrupt, not merely skip a tick.");
        var downedPolicy = mod.GetType("ArdubsEnemyIntelligence.TargetingPolicy").GetMethod("ReleasesDownedTarget", Flags);
        if (!(bool)downedPolicy.Invoke(null, new object[] { true, false, false }) ||
            (bool)downedPolicy.Invoke(null, new object[] { true, false, true }) ||
            !(bool)downedPolicy.Invoke(null, new object[] { false, true, true }))
            throw new Exception("Only a downed, non-sleeping target or dead target should release the lock.");
        var releaseOnRead = false;
        foreach (var call in Calls(targeting.GetMethod("CurrentMemory", Flags)))
            if (call.Name == "ReleaseDownedTarget") releaseOnRead = true;
        if (!releaseOnRead) throw new Exception("Every managed targeting path must release a downed lock.");
        var nativeAttack = game.GetType("Kingmaker.UnitLogic.Commands.UnitAttack");
        if (nativeAttack.GetMethod("UpdateTarget", Flags) == null ||
            targeting.GetMethod("FinishAttackRetarget", Flags) == null ||
            mod.GetType("ArdubsEnemyIntelligence.TargetingPatches")
                .GetNestedType("RememberNativeAttackRetargetPatch", Flags)?.GetMethod("Postfix", Flags) == null)
            throw new Exception("The native attack fallback must retain its newly chosen target lock.");
    }

    private static void LiveMovementContractTests()
    {
        var bodyType = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking");
        var preview = game.GetType("Kingmaker.TurnBasedMode.PathVisualizer");
        if (!Calls(preview.GetMethod("CommitRealPath", Flags)).Any(call => call.Name == "Clear") ||
            !Calls(bodyType.GetMethod("CommitPreviewReplacement", Flags))
                .Any(call => call.Name == "CommitReplacementRoute") ||
            !Calls(mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches")
                .GetNestedType("PreserveClearApproachRoute", Flags).GetMethod("Prefix", Flags))
                .Any(call => call.Name == "CommitPreviewReplacement"))
            throw new Exception("The visualizer must not overwrite a clear searched attack route with an occupied replacement route.");
        var relation = bodyType.GetMethod("IsFriendlyForMovement", Flags);
        var allyCalls = 0;
        var enemyCalls = 0;
        var pureRelation = false;
        foreach (var call in Calls(relation))
        {
            if (call.Name == "IsAlly") allyCalls++;
            if (call.Name == "IsEnemy") enemyCalls++;
            if (call.DeclaringType.Name == "BodyBlockingPolicy" && call.Name == "IsFriendlyForMovement") pureRelation = true;
        }
        if (allyCalls != 2 || enemyCalls != 2 || !pureRelation)
            throw new Exception("Movement friendship must check both directions with hostility taking precedence.");
        foreach (var method in new[] { bodyType.GetMethod("Capture", Flags),
            mod.GetType("ArdubsEnemyIntelligence.NavmeshBodyRules").GetMethod("ShouldSkip", Flags) })
        {
            var sharedRelation = false;
            foreach (var call in Calls(method))
            {
                if (call.DeclaringType == bodyType && call.Name == "IsFriendlyForMovement") sharedRelation = true;
                if (call.Name == "IsAlly") throw new Exception(method.Name + " still uses asymmetric friendship.");
            }
            if (!sharedRelation) throw new Exception(method.Name + " must use the shared movement friendship rule.");
        }
        var guard = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking").GetMethod("GuardMove", Flags);
        var nativeBudgetMode = false;
        var sharedLimit = false;
        foreach (var call in Calls(guard))
        {
            if (call.Name == "CheckApproachRadius")
                throw new Exception("Live body guard must not treat entering an approach radius as ending movement: native TickMovement continues to its final waypoint.");
            if (call.Name == "GetEnabledSingleActionMove") nativeBudgetMode = true;
            if (call.Name == "LimitStep") sharedLimit = true;
        }
        if (!nativeBudgetMode || !sharedLimit)
            throw new Exception("Live movement must use the native movement-limit mode and the tested path/tick adapter.");
        var legalBudget = false;
        foreach (var call in Calls(guard))
            if (call.Name == "MayUseTotalMovement") legalBudget = true;
        if (!legalBudget) throw new Exception("Live movement must select the full legal crossing budget, not hardcode first-action range.");
        var simulationBudget = false;
        foreach (var call in Calls(bodyType.GetMethod("GuardSimulation", Flags)))
            if (call.Name == "SimulationCrossingBudget") simulationBudget = true;
        if (!simulationBudget) throw new Exception("Simulation must distinguish preview action boundaries from actual stops.");
        var nativeStepMode = false;
        foreach (var call in Calls(mod.GetType("ArdubsEnemyIntelligence.BodyBlocking").GetMethod("UsesFiveFootStep", Flags)))
            if (call.Name == "GetEnabledFiveFootStep") nativeStepMode = true;
        if (!nativeStepMode) throw new Exception("Friendly step exception must use native five-foot mode, not infer it from route length.");
        var nativeGridStepMode = false;
        var sharedStoppingRule = false;
        foreach (var call in Calls(mod.GetType("ArdubsEnemyIntelligence.NavmeshBodyRules").GetMethod("ShouldSkip", Flags)))
        {
            if (call.Name == "GetEnabledFiveFootStep") nativeGridStepMode = true;
            if (call.Name == "BlocksStopping") sharedStoppingRule = true;
        }
        if (!nativeGridStepMode || !sharedStoppingRule)
            throw new Exception("Native occupancy must use the same friendly step/stopping rules as custom clearance.");
        foreach (var call in Calls(game.GetType("Kingmaker.View.UnitMovementAgent").GetMethod("TickMovement", Flags)))
            if (call.Name == "CheckApproachRadius")
                throw new Exception("Native movement contract changed; recheck its final-waypoint stopping behavior.");
        var command = game.GetType("Kingmaker.UnitLogic.Commands.Base.UnitCommand");
        var approachDecision = command.GetMethod("get_ShouldUnitApproach", Flags);
        if (approachDecision == null || !Calls(approachDecision).Any(call =>
            call.Name == "IsUnitCloseEnough" && call.GetParameters().Length == 0))
            throw new Exception("Native attack approach no longer uses the zero-argument close-enough decision.");
        var tickApproach = command.GetMethod("TickApproaching", Flags);
        if (tickApproach == null || !Calls(tickApproach).Any(call => call.Name == "StopMoving"))
            throw new Exception("Native attack approach no longer stops movement after becoming close enough.");
        var stopCheck = bodyType.GetMethod("CanStopAttackApproach", Flags);
        if (stopCheck == null || !Calls(stopCheck).Any(call => call.Name == "Capture") ||
            !Calls(stopCheck).Any(call => call.Name == "CanStop") ||
            !Calls(stopCheck).Any(call => call.Name == "get_SaddledUnit") ||
            Calls(stopCheck).Any(call => call.Name == "get_AiAction"))
            throw new Exception("Player and AI attack stops must use the moving mount's footprint when mounted.");
        if (mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches")
            .GetNestedType("AttackStopClearance", Flags)?.GetMethod("Postfix", Flags) == null)
            throw new Exception("The attack close-enough decision must have a clearance hook.");
        var attackStopHook = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches")
            .GetNestedType("AttackStopClearance", Flags).GetMethod("Postfix", Flags);
        if (Calls(attackStopHook).Any(call => call.Name == "get_AiAction"))
            throw new Exception("Player-issued attacks must not bypass occupied-stop clearance.");
        var bodyPatches = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches");
        if (bodyPatches.GetNestedType("PreviewPathDiagnostics", Flags)?.GetMethod("Postfix", Flags) == null ||
            bodyPatches.GetNestedType("CommittedPathDiagnostics", Flags)?.GetMethod("Postfix", Flags) == null ||
            bodyType.GetMethod("LogPreviewPath", Flags) == null ||
            bodyType.GetMethod("LogLivePathFailure", Flags) == null)
            throw new Exception("Preview and committed path failures must retain their diagnostic hooks.");
        var simulationPatch = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches").GetNestedType("SimulationClearance", Flags);
        var prefix = simulationPatch.GetMethod("Prefix", Flags);
        var simulator = Activator.CreateInstance(game.GetType("Kingmaker.View.MovementSimulator"));
        var args = new object[] { simulator, 0.25f, null };
        prefix.Invoke(null, args);
        Equal(0.25m, Field(args[2], "Budget"), "simulation prefix preserves original budget before native starg mutation");
        foreach (var parameter in simulationPatch.GetMethod("Postfix", Flags).GetParameters())
            if (parameter.Name == "__2") throw new Exception("Postfix must not use native-mutated budget argument.");
    }

    private static void BodyStepTests()
    {
        var policy = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPolicy");
        var pointType = policy.GetNestedType("Point", Flags);
        var bodyType = policy.GetNestedType("Body", Flags);
        var blocking = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking");
        var snapshotType = blocking.GetNestedType("Snapshot", Flags);
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        object Point(double x) => Activator.CreateInstance(pointType, Flags, null, new object[] { x, 0d, 0d }, null);
        object Vector(float x) => Activator.CreateInstance(vectorType, new object[] { x, 0f, 0f });
        object Snapshot(double center, double radius, bool blocks, bool prevent = true, bool stops = true)
        {
            var bodies = Array.CreateInstance(bodyType, 1);
            bodies.SetValue(Activator.CreateInstance(bodyType, Flags, null,
                new object[] { Point(center), radius, blocks, stops }, null), 0);
            var flyovers = Array.CreateInstance(bodyType, 0);
            return Activator.CreateInstance(snapshotType, Flags, null,
                new object[] { Point(0), bodies, flyovers, prevent }, null);
        }
        object Route(params float[] xs)
        {
            var path = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
            var points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType));
            foreach (var x in xs) points.Add(Vector(x));
            Set(path, "vectorPath", points);
            return path;
        }
        decimal Limit(object snapshot, float start, float next, object path, int waypoint, double budget, bool stops = false) =>
            Convert.ToDecimal(blocking.GetMethod("LimitStep", Flags).Invoke(null,
                new object[] { snapshot, Vector(start), Vector(next), path, waypoint, budget, stops }));

        var ally = Snapshot(3, 1, false);
        var direct = Route(0, 6);
        // Previous live code shortened the budget to a single frame whenever
        // this point was within MaxApproachRadius, freezing at an allied edge.
        Equal(4m, Limit(ally, 2, 2.25f, direct, 1, 4), "near-destination allied crossing retains full route, not just current frame");
        Equal(0m, Limit(ally, 2, 2.25f, direct, 1, 0.25), "a genuine insufficient budget still prevents entry");
        for (var start = 0f; start < 6f; start += 0.25f)
            Equal(6m - (decimal)start, Limit(ally, start, start + 0.25f, direct, 1, 6 - start),
                "every actual tick crosses and exits the ally without a false stop at its boundary/center");

        var intermediate = Route(0, 3, 6);
        Equal(4m, Limit(ally, 2, 2.25f, intermediate, 1, 4), "allied crossing through a native intermediate waypoint");
        Equal(3m, Limit(ally, 3, 3.25f, intermediate, 2, 3), "native waypoint advance does not backtrack inside the ally");
        Equal(0.5m, Limit(ally, 1.5f, 2.25f, direct, 1, 4.5, true), "actual native range/LOS stop is not mistaken for a continued crossing");
        Equal(0.5m, Limit(ally, 1.5f, 2.25f, Route(0, 3), 1, 4.5), "occupied final waypoint remains prohibited");
        Equal(0.5m, Limit(Snapshot(3, 1, true), 1.5f, 2.25f, direct, 1, 4.5), "hostile or explicitly blocked allied passage still stops at entry");
        Equal(0.25m, Limit(Snapshot(3, 1, false, false), 2, 2.25f, direct, 1, 0.25), "overlap toggle remains independent of allied passage");
        Equal(0.25m, Limit(ally, 3.5f, 3.75f, direct, 1, 0.25), "short outward escape from pre-existing allied overlap");
        var friendlyExempt = Snapshot(3, 1, false, true, false);
        Equal(1m, Limit(friendlyExempt, 2, 2.25f, direct, 1, 1),
            "live shared adapter permits budget ending at an exempt friendly center");
        Equal(0.25m, Limit(friendlyExempt, 2.75f, 3, direct, 1, 0.25, true),
            "native simulation/range stop inside an exempt friendly body remains legal");
        Equal(1m, Limit(friendlyExempt, 2, 2.25f, Route(0, 3), 1, 1),
            "an exempt friendly final waypoint is not shortened");
        Equal(0.5m, Limit(Snapshot(3, 1, true, true, true), 1.5f, 2.25f, direct, 1, 4.5),
            "friendly exceptions must not change hostile live clearance");
        Equal(0.25m, Limit(friendlyExempt, 2.75f, 3, direct, 1, -0.1, true),
            "native borrowed endpoint can safely stop inside an explicitly exempt friendly body");
        Equal(0m, Limit(ally, 2.75f, 3, direct, 1, -0.1, true),
            "native borrowed endpoint does not silently bypass normal friendly occupancy protection");

        var large = Snapshot(6, 3, false);
        var longRoute = Route(0, 10);
        for (var start = 0f; start < 10f; start += 0.25f)
            Equal(10m - (decimal)start, Limit(large, start, start + 0.25f, longRoute, 1, 10 - start),
                "large allied footprint can be crossed frame by frame");
        Equal(0.25m, Limit(large, 2.75f, 3.25f, longRoute, 1, 6), "large ally requires enough budget to exit");

        // Exercise the compiled adapter with the game's actual settings struct,
        // not merely geometry supplied a hand-picked budget by the test.
        var settings = Activator.CreateInstance(game.GetType("Kingmaker.TurnBasedMode.PathVisualizer+VisualPathSettings"));
        Set(settings, "RemainingMovementRange", 4.1f);
        Set(settings, "RemainingMovementRangeTotal", 8f);
        SetEnum(settings, "MovePredictionState", "Available");
        SetEnum(settings, "StandardPredictionState", "Available");
        decimal Budget(double travelled, double segment, bool autoStop = false) =>
            Convert.ToDecimal(blocking.GetMethod("PreviewCrossingBudget", Flags).Invoke(null,
                new object[] { settings, travelled, segment, autoStop }));
        Equal(8m, Budget(0, 4.1), "preview crossing uses both available movement actions");
        Equal(3m, Budget(5, 1), "preview budget shrinks by actual travelled distance along a curved route");
        var sizeSensitive = Snapshot(3, 2, false);
        var eightMetres = Route(0, 8);
        for (var start = 0f; start < 8f; start += 0.25f)
        {
            var segment = start < 4 ? 4 - start : 8 - start;
            Equal(8m - (decimal)start, Limit(sizeSensitive, start, start + 0.25f, eightMetres, 1,
                (double)Budget(start, segment)), "compiled native-budget adapter crosses large ally over action boundary");
        }
        Equal(4.1m, Budget(0, 4.1, true), "stop-after-first-move does not gain a second action");
        foreach (var mode in new[] { "EnabledFiveFeetStep", "EnabledSingleActionMove" })
        {
            Set(settings, mode, true);
            Equal(4.1m, Budget(0, 4.1), mode + " retains native action limit");
            Set(settings, mode, false);
        }
        foreach (var state in new[] { "Unable", "WillBeUsed", "WillBeLost", "Used", "Lost" })
        {
            SetEnum(settings, "StandardPredictionState", state);
            Equal(4.1m, Budget(0, 4.1), "reserved/unavailable standard action is not spent: " + state);
        }
        SetEnum(settings, "StandardPredictionState", "Available");
        Set(settings, "MoveActionCooldown", 3f);
        Equal(4.1m, Budget(0, 4.1), "spent move action cannot generate another action segment");
        Set(settings, "MoveActionCooldown", 0f);
        foreach (var state in new[] { "Unable", "WillBeUsed", "WillBeLost" })
        {
            SetEnum(settings, "MovePredictionState", state);
            Equal(4.1m, Budget(0, 4.1), "native preview cannot advance reserved/unavailable move: " + state);
        }
    }

    private static void SimulationApproachStopTests()
    {
        var blocking = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking");
        var policy = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPolicy");
        var pointType = policy.GetNestedType("Point", Flags);
        var bodyType = policy.GetNestedType("Body", Flags);
        var snapshotType = blocking.GetNestedType("Snapshot", Flags);
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        var simulatorType = game.GetType("Kingmaker.View.MovementSimulator");
        object Point(double x) => Activator.CreateInstance(pointType, Flags, null, new object[] { x, 0d, 0d }, null);
        object Vector(float x) => Activator.CreateInstance(vectorType, new object[] { x, 0f, 0f });
        var path = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
        var bodies = Array.CreateInstance(bodyType, 1);
        bodies.SetValue(Activator.CreateInstance(bodyType, Flags, null,
            new object[] { Point(3), 1d, false, true }, null), 0);
        object Snapshot(bool prevent) => Activator.CreateInstance(snapshotType, Flags, null,
            new object[] { Point(0), bodies, Array.CreateInstance(bodyType, 0), prevent }, null);
        var snapshot = Snapshot(true);
        blocking.GetMethod("Remember", Flags).Invoke(null, new[] { path, snapshot });

        // Compile the actual transpiler's output for the native stop-success
        // branch. No Unity scene is needed to exercise its branch/label handling.
        var harmonyAssembly = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(game.Location), "0Harmony.dll"));
        var instructionType = harmonyAssembly.GetType("HarmonyLib.CodeInstruction");
        var codes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instructionType));
        var tick = new DynamicMethod("OccupiedApproachStop", typeof(bool),
            new[] { typeof(bool), simulatorType.MakeByRefType(), vectorType, vectorType }, typeof(GameMetadataTests), true);
        var il = tick.GetILGenerator();
        var candidate = il.DeclareLocal(vectorType);
        var next = il.DeclareLocal(vectorType);
        var stopped = il.DeclareLocal(typeof(bool));
        object Add(OpCode opcode, object operand = null)
        {
            var code = Activator.CreateInstance(instructionType, new[] { (object)opcode, operand });
            codes.Add(code); return code;
        }
        Add(OpCodes.Ldarg_2); Add(OpCodes.Stloc_S, candidate);
        Add(OpCodes.Ldarg_3); Add(OpCodes.Stloc_S, next);
        var stopLabel = il.DefineLabel();
        Add(OpCodes.Ldarg_0); Add(OpCodes.Brtrue, stopLabel);
        var loadCandidate = Add(OpCodes.Ldloc_S, candidate);
        ((IList)Field(loadCandidate, "labels")).Add(stopLabel);
        Add(OpCodes.Stloc_S, next); Add(OpCodes.Ldc_I4_1); Add(OpCodes.Stloc_S, stopped);
        Add(OpCodes.Ldarg_1); Add(OpCodes.Ldloc_S, next);
        Add(OpCodes.Call, simulatorType.GetProperty("Position", Flags).GetSetMethod(true));
        Add(OpCodes.Ldloc_S, stopped); Add(OpCodes.Ret);
        var transpiler = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches")
            .GetNestedType("SimulationClearance", Flags).GetMethod("Transpiler", Flags);
        var transformed = (IEnumerable)transpiler.Invoke(null, new object[] { codes, il });
        foreach (var code in transformed)
        {
            foreach (Label label in (IEnumerable)Field(code, "labels")) il.MarkLabel(label);
            var opcode = (OpCode)Field(code, "opcode");
            var operand = Field(code, "operand");
            if (operand == null) il.Emit(opcode);
            else if (operand is LocalBuilder local) il.Emit(opcode, local);
            else if (operand is Label label) il.Emit(opcode, label);
            else if (operand is FieldInfo field) il.Emit(opcode, field);
            else if (operand is MethodInfo method) il.Emit(opcode, method);
            else throw new Exception("Unexpected simulation instruction operand " + operand);
        }
        var call = tick.CreateDelegate(System.Linq.Expressions.Expression.GetDelegateType(
            typeof(bool), simulatorType.MakeByRefType(), vectorType, vectorType, typeof(bool)));
        object[] Tick(float stop, float step, bool branch)
        {
            var simulator = Activator.CreateInstance(simulatorType);
            Set(simulator, "Path", path);
            var args = new[] { (object)branch, simulator, Vector(stop), Vector(step) };
            var didStop = (bool)call.DynamicInvoke(args);
            return new[] { (object)didStop, Property(args[1], "Position") };
        }
        foreach (var branch in new[] { false, true })
        {
            var result = Tick(2.25f, 2.5f, branch);
            if ((bool)result[0]) throw new Exception("Occupied in-range point incorrectly ends the native tick.");
            Equal(2.5m, Field(result[1], "x"), "occupied range stop keeps native movement, not retracted preview");
            result = Tick(4.125f, 4.25f, branch);
            if (!(bool)result[0]) throw new Exception("Clear in-range stopping point must retain the native stop.");
            Equal(4.125m, Field(result[1], "x"), "clear range stop retains native chosen endpoint");
        }
        Equal(2m, Field(snapshot, "OccupiedApproachStopsRejected"), "rejected early stops are traceable");
        blocking.GetMethod("Remember", Flags).Invoke(null, new[] { path, Snapshot(false) });
        if (!(bool)Tick(2.25f, 2.5f, true)[0]) throw new Exception("Disabled overlap rule must preserve native stopping.");
        blocking.GetMethod("Remember", Flags).Invoke(null, new object[] { path, null });
        if (!(bool)Tick(2.25f, 2.5f, true)[0]) throw new Exception("Unmodified paths must preserve native stopping.");
    }

    private static void DestinationFootprintTests()
    {
        var footprint = mod.GetType("ArdubsEnemyIntelligence.DestinationFootprint");
        var ring = footprint.GetMethod("RingPoints", Flags);
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        object Center(float x, float y, float z) => Activator.CreateInstance(vectorType, new object[] { x, y, z });
        foreach (var radius in new[] { 0.25f, 0.5f, 1f, 1.5f, 3f })
        {
            var points = (Array)ring.Invoke(null, new object[] { Center(10, 2, 30), radius });
            if (points.Length != 48) throw new Exception("Outline must have enough vertices for a smooth closed circle.");
            Equal(10m + (decimal)radius, Field(points.GetValue(0), "x"), "east edge uses mechanical body radius");
            Equal(10m - (decimal)radius, Field(points.GetValue(24), "x"), "west edge uses mechanical body radius");
            Equal(30m + (decimal)radius, Field(points.GetValue(12), "z"), "north edge uses mechanical body radius");
            Equal(30m - (decimal)radius, Field(points.GetValue(36), "z"), "south edge uses mechanical body radius");
            foreach (var point in points)
            {
                var x = Convert.ToDouble(Field(point, "x")) - 10;
                var z = Convert.ToDouble(Field(point, "z")) - 30;
                if (Math.Abs(Math.Sqrt(x * x + z * z) - radius) > 0.00001)
                    throw new Exception("World-space footprint must stay circular regardless of pin scale/rotation.");
                if (Math.Abs(Convert.ToDouble(Field(point, "y")) - 2.05) > 0.000001)
                    throw new Exception("Outline must be raised slightly above ground.");
            }
        }
        foreach (var radius in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            try
            {
                ring.Invoke(null, new object[] { Center(0, 0, 0), radius });
                throw new Exception("Invalid footprint radius was accepted.");
            }
            catch (TargetInvocationException exception)
            {
                if (!(exception.InnerException is ArgumentOutOfRangeException)) throw;
            }
        }
        // Ensure hooks stay on the native pin lifecycle rather than creating a
        // mod-defined component on an entity/saved command.
        foreach (var name in new[] { "Update", "Cleanup", "PreviewCleanup" })
            if (footprint.GetNestedType(name, Flags) == null)
                throw new Exception("Missing native footprint lifecycle hook: " + name);
        var nativeEndpoint = false;
        foreach (var call in Calls(footprint.GetMethod("RefreshPreview", Flags)))
            if (call.Name == "GetLastPathElement") nativeEndpoint = true;
        if (!nativeEndpoint) throw new Exception("Turn-based footprint must use the native simulated endpoint, not the cursor.");
        var previewRefreshed = false;
        foreach (var call in Calls(footprint.GetMethod("RefreshAll", Flags)))
            if (call.Name == "RefreshPreview") previewRefreshed = true;
        if (!previewRefreshed) throw new Exception("UI updates must refresh turn-based footprints even with no click pins.");
        var modeChecked = false;
        var removedOutsideTurnBased = false;
        foreach (var call in Calls(footprint.GetMethod("Refresh", Flags)))
        {
            if (call.DeclaringType?.Name == "CombatController" && call.Name == "IsInTurnBasedCombat")
                modeChecked = true;
            if (call.DeclaringType == footprint && call.Name == "Remove")
                removedOutsideTurnBased = true;
        }
        if (!modeChecked || !removedOutsideTurnBased)
            throw new Exception("Destination footprints must be removed outside turn-based combat.");
        var hiddenBeforeDestroy = false;
        foreach (var call in Calls(footprint.GetMethod("Remove", Flags)))
            if (call.Name == "SetActive") hiddenBeforeDestroy = true;
        if (!hiddenBeforeDestroy)
            throw new Exception("Removing a footprint must hide it immediately before deferred Unity destruction.");
    }

    private static void NativeBodyUpdateTests()
    {
        var astar = Assembly.Load("AstarPathfindingProject");
        var node = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.GridNode"));
        var updateType = astar.GetType("Pathfinding.GraphUpdateObject");
        var apply = updateType.GetMethod("Apply", Flags);
        var suppress = mod.GetType("ArdubsEnemyIntelligence.NavmeshBodyRules").GetMethod("SuppressBodyUpdate", Flags);
        object Update(bool hostile)
        {
            var value = Activator.CreateInstance(updateType);
            Set(value, "modifyDynamicWalkability", hostile); Set(value, "setDynamicWalkability", false);
            Set(value, "modifyAllyBlocked", !hostile); Set(value, "setAllyBlocked", true);
            Set(value, "modifyTag", hostile); Set(value, "setTag", hostile ? 31 : 0);
            return value;
        }
        // Preserve other occupants and static terrain. Suppression must not
        // set a node globally walkable or clear another unit's blocker count.
        // The native Walkable setter notifies a live Astar graph. Initialize
        // its backing terrain bits directly in this offline metadata fixture.
        Set(node, "flags", (uint)0);
        node.GetType().GetProperty("DynamicBlocked", Flags).SetValue(node, (sbyte)2, null);
        node.GetType().GetProperty("AllyBlocked", Flags).SetValue(node, (sbyte)3, null);
        node.GetType().GetProperty("Tag", Flags).SetValue(node, (uint)7, null);
        foreach (var hostile in new[] { false, true })
        {
            var ignored = Update(hostile);
            suppress.Invoke(null, new[] { ignored });
            apply.Invoke(ignored, new[] { node });
            Equal(2m, Property(node, "DynamicBlocked"), "flying/downed suppression preserves other hostile occupants");
            Equal(3m, Property(node, "AllyBlocked"), "downed suppression preserves other allied occupants");
            Equal(7m, Property(node, "Tag"), "body exception preserves unrelated terrain tags");
            if ((bool)Property(node, "StaticWalkable")) throw new Exception("Body exception relaxed static terrain.");
            // Mirror native RevertNavmeshForUnit: inverse booleans but same
            // modifier flags. An ignored update must also reverse as a no-op.
            Set(ignored, "setDynamicWalkability", true); Set(ignored, "setAllyBlocked", false); Set(ignored, "setTag", 0);
            apply.Invoke(ignored, new[] { node });
            Equal(2m, Property(node, "DynamicBlocked"), "reversing ignored unit update does not underflow hostile counts");
            Equal(3m, Property(node, "AllyBlocked"), "reversing ignored unit update does not underflow allied counts");
        }
        var awake = Update(false);
        apply.Invoke(awake, new[] { node });
        Equal(4m, Property(node, "AllyBlocked"), "awake unit still contributes native blocking");
        Set(awake, "setAllyBlocked", false); apply.Invoke(awake, new[] { node });
        Equal(3m, Property(node, "AllyBlocked"), "sleeping/waking refresh reverts the previously applied blocker");
    }

    private static void BodyRouteTests()
    {
        var policy = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPolicy");
        var pointType = policy.GetNestedType("Point", Flags);
        var bodyType = policy.GetNestedType("Body", Flags);
        var blocking = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking");
        var snapshotType = blocking.GetNestedType("Snapshot", Flags);
        var origin = Activator.CreateInstance(pointType, Flags, null, new object[] { 0d, 0d, 0d }, null);
        var center = Activator.CreateInstance(pointType, Flags, null, new object[] { 3d, 0d, 0d }, null);
        var body = Activator.CreateInstance(bodyType, Flags, null, new object[] { center, 1d, false }, null);
        var bodies = Array.CreateInstance(bodyType, 1); bodies.SetValue(body, 0);
        var snapshot = Activator.CreateInstance(snapshotType, Flags, null,
            new object[] { origin, bodies, Array.CreateInstance(bodyType, 0), true }, null);
        var path = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        var points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType));
        points.Add(Activator.CreateInstance(vectorType, new object[] { 0f, 0f, 0f }));
        points.Add(Activator.CreateInstance(vectorType, new object[] { 3f, 0f, 0f }));
        Set(path, "vectorPath", points);
        var remember = blocking.GetMethod("Remember", Flags);
        remember.Invoke(null, new object[] { path, snapshot });
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(2m, Field(points[points.Count - 1], "x"), "actual native vectorPath trims before occupied clicked endpoint");
        // A pooled path used by an excluded mover must not retain earlier rules.
        remember.Invoke(null, new object[] { path, null });
        points[points.Count - 1] = Activator.CreateInstance(vectorType, new object[] { 3f, 0f, 0f });
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(3m, Field(points[points.Count - 1], "x"), "pooled native path forgets the previous owner's clearance");
        // Ending conditions remain native objects; no mod-defined object is
        // installed into a command/path that might participate in a save.
        var endingType = game.GetType("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance+LosPathConditionForGrid");
        var ending = Activator.CreateInstance(endingType, Flags, null, new object[] { path, 1f, false, 0f, 6f }, null);
        Set(path, "endingCondition", ending);
        remember.Invoke(null, new object[] { path, snapshot });
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        if (!ReferenceEquals(ending, Field(path, "endingCondition")))
            throw new Exception("Body clearance must preserve native ending conditions for Dist and saves.");
        var astar = Assembly.Load("AstarPathfindingProject");
        var node = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.GridNode"));
        var int3 = Activator.CreateInstance(astar.GetType("Pathfinding.Int3"), new object[] { 3000, 0, 0 });
        Set(node, "position", int3);
        var pathNode = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.PathNode"));
        Set(pathNode, "node", node);
        var patches = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches");
        var endpointArgs = new object[] { path, pathNode, true };
        patches.GetNestedType("EndpointClearance", Flags).GetMethod("Postfix", Flags).Invoke(null, endpointArgs);
        if ((bool)endpointArgs[2]) throw new Exception("Native attack endpoint hook must reject an occupied graph node.");
        var exemptBody = Activator.CreateInstance(bodyType, Flags, null, new object[] { center, 1d, false, false }, null);
        bodies.SetValue(exemptBody, 0);
        endpointArgs[2] = true;
        patches.GetNestedType("EndpointClearance", Flags).GetMethod("Postfix", Flags).Invoke(null, endpointArgs);
        if (!(bool)endpointArgs[2]) throw new Exception("Native endpoint hook rejected friendly occupied space despite its exemption.");
        points[points.Count - 1] = Activator.CreateInstance(vectorType, new object[] { 3f, 0f, 0f });
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(3m, Field(points[points.Count - 1], "x"), "native route trimming preserves exempt friendly destinations");
        var hostileBody = Activator.CreateInstance(bodyType, Flags, null, new object[] { center, 1d, true }, null);
        bodies.SetValue(hostileBody, 0);
        var traversalArgs = new object[] { path, node, true };
        patches.GetNestedType("Traversal", Flags).GetMethod("Postfix", Flags).Invoke(null, traversalArgs);
        if ((bool)traversalArgs[2]) throw new Exception("Native traversal hook must reject the hostile body node.");
        traversalArgs[2] = false;
        Set(node, "position", Activator.CreateInstance(astar.GetType("Pathfinding.Int3"), new object[] { 6000, 0, 0 }));
        patches.GetNestedType("Traversal", Flags).GetMethod("Postfix", Flags).Invoke(null, traversalArgs);
        if ((bool)traversalArgs[2]) throw new Exception("Body clearance must never revive a native terrain rejection.");
        var mainSettings = mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain").GetField("Settings", Flags);
        var originalSettings = mainSettings.GetValue(null);
        var inactiveSettings = Activator.CreateInstance(mod.GetType("ArdubsEnemyIntelligence.Settings"));
        Set(inactiveSettings, "ApplyToEnemies", false); Set(inactiveSettings, "ApplyToAlliedNpcs", false);
        mainSettings.SetValue(null, inactiveSettings);
        try
        {
            if (blocking.GetMethod("For", Flags).Invoke(null, new object[] { path }) != null)
                throw new Exception("Both scopes off must stop enforcing existing path snapshots.");
            if ((bool)mod.GetType("ArdubsEnemyIntelligence.NavmeshBodyRules").GetMethod("ShouldSkip", Flags)
                .Invoke(null, new object[] { null, null }))
                throw new Exception("Both scopes off must retain native grid occupancy without scene reads.");
            if ((bool)mod.GetType("ArdubsEnemyIntelligence.TargetingState").GetMethod("IsManaged", Flags)
                .Invoke(null, new object[] { null }))
                throw new Exception("Both scopes off must retain vanilla targeting.");
            mod.GetType("ArdubsEnemyIntelligence.TargetingLog").GetMethod("Write", Flags)
                .Invoke(null, new object[] { "Disabled logging must not access Unity or the logger." });
        }
        finally { mainSettings.SetValue(null, originalSettings); }
    }

    private static void SpellApproachTests()
    {
        var policy = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPolicy");
        var pointType = policy.GetNestedType("Point", Flags);
        var bodyType = policy.GetNestedType("Body", Flags);
        var blocking = mod.GetType("ArdubsEnemyIntelligence.BodyBlocking");
        var snapshotType = blocking.GetNestedType("Snapshot", Flags);
        var origin = Activator.CreateInstance(pointType, Flags, null, new object[] { 0d, 0d, 0d }, null);
        var center = Activator.CreateInstance(pointType, Flags, null, new object[] { 4.5d, 0d, 0d }, null);
        var body = Activator.CreateInstance(bodyType, Flags, null, new object[] { center, 1d, false, true }, null);
        var bodies = Array.CreateInstance(bodyType, 1); bodies.SetValue(body, 0);
        var snapshot = Activator.CreateInstance(snapshotType, Flags, null,
            new object[] { origin, bodies, Array.CreateInstance(bodyType, 0), true }, null);
        var path = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        Func<float, float, object> vector = (x, z) => Activator.CreateInstance(vectorType, new object[] { x, 0f, z });
        Set(path, "originalEndPoint", vector(6, 0));
        var endingType = game.GetType("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance+LosPathConditionForGrid");
        var ending = Activator.CreateInstance(endingType, Flags, null, new object[] { path, 1.6f, false, 0f, 12f }, null);
        Set(path, "endingCondition", ending);
        var astar = Assembly.Load("AstarPathfindingProject");
        var node = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.GridNode"));
        Set(node, "position", Activator.CreateInstance(astar.GetType("Pathfinding.Int3"), new object[] { 5000, 0, 1000 }));
        var nodes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(astar.GetType("Pathfinding.GraphNode")));
        nodes.Add(node); Set(path, "path", nodes);
        blocking.GetMethod("Remember", Flags).Invoke(null, new object[] { path, snapshot });
        var hook = mod.GetType("ArdubsEnemyIntelligence.BodyBlockingPatches").GetNestedType("PreserveApproachEndpoint", Flags)
            .GetMethod("Postfix", Flags);
        var args = new object[] { path, false, true, 17, vector(4.5f, 0) };
        hook.Invoke(null, args);
        Equal(5m, Field(args[4], "x"), "actual endpoint hook restores accepted casting position");
        Equal(1m, Field(args[4], "z"), "accepted casting position is outside friendly footprint");
        if ((bool)args[2] || (int)args[3] != 0)
            throw new Exception("Restoring an endpoint must use native SnapToNode bookkeeping.");
        var points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType));
        points.Add(vector(0, 0)); points.Add(args[4]); Set(path, "vectorPath", points);
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(5m, Field(points[points.Count - 1], "x"), "restored buff endpoint survives final body-clearance trimming");
        if ((bool)Field(snapshot, "ClearanceLimited"))
            throw new Exception("A safe restored endpoint must not mark the cast unusable.");
        args = new object[] { path, true, true, 17, vector(4.5f, 0) };
        hook.Invoke(null, args);
        Equal(4.5m, Field(args[4], "x"), "start-point snapping remains native");
        args = new object[] { path, false, true, 17, vector(5.2f, 1) };
        hook.Invoke(null, args);
        Equal(5.2m, Field(args[4], "x"), "safe in-range endpoint remains native");
        if (!(bool)args[2] || (int)args[3] != 17)
            throw new Exception("Valid projections must retain native bookkeeping.");
        Set(node, "position", Activator.CreateInstance(astar.GetType("Pathfinding.Int3"), new object[] { 2000, 0, 0 }));
        args = new object[] { path, false, true, 17, vector(1.8f, 0) };
        hook.Invoke(null, args);
        Equal(1.8m, Field(args[4], "x"), "native movement-limited partial route is not extended into casting range");
        // Native connection snapping can enter an occupied body even when the
        // accepted grid node lies outside attack range. Keep the usable point
        // on that same connection, never the occupied projected endpoint.
        Set(path, "originalEndPoint", vector(5, 0));
        args = new object[] { path, false, true, 17, vector(4.5f, 0) };
        hook.Invoke(null, args);
        Equal(3.5m, Field(args[4], "x"), "occupied projection retracts to clear in-range point on native connection");
        if (!(bool)args[2] || (int)args[3] != 17)
            throw new Exception("A corrected connection point must retain native connection bookkeeping.");
        points[1] = args[4];
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(3.5m, Field(points[points.Count - 1], "x"), "clear projected attack endpoint survives final route validation");
        Set(path, "originalEndPoint", vector(6, 0));
        points[1] = vector(4.5f, 0);
        blocking.GetMethod("FinishRoute", Flags).Invoke(null, new object[] { path });
        Equal(3.5m, Field(points[points.Count - 1], "x"), "reproduced unusable buff route retains collision safety");
        if (!(bool)Field(snapshot, "ClearanceLimited"))
            throw new Exception("Final route shortening must identify that the mod changed this path.");
        var spell = mod.GetType("ArdubsEnemyIntelligence.SpellApproach");
        var nativeClose = false;
        var matchingPolicy = false;
        foreach (var call in Calls(spell.GetMethod("ContinueApproaching", Flags)))
        {
            if (call.Name == "IsUnitCloseEnough" && call.GetParameters().Length == 6) nativeClose = true;
            if (call.Name == "CancelBlockedCast") matchingPolicy = true;
        }
        if (!nativeClose || !matchingPolicy)
            throw new Exception("Queued casts must use native endpoint range/LOS and require a matching mod-limited route.");
        var touchException = false;
        foreach (var call in Calls(spell.GetMethod("Applies", Flags)))
            if (call.Name == "IsTouchAbility") touchException = true;
        if (!touchException) throw new Exception("Sticky-touch initial casts must not be rejected by approach clearance.");
        var pendingCalls = new HashSet<string>();
        foreach (var call in Calls(spell.GetMethod("Pending", Flags))) pendingCalls.Add(call.Name);
        foreach (var required in new[] { "get_IsStarted", "get_IsActed", "get_IsFinished", "get_Cutscene", "get_AiAction", "get_IsDirectlyControllable" })
            if (!pendingCalls.Contains(required)) throw new Exception("Missing pending player-cast guard: " + required);
        var interrupt = false;
        foreach (var call in Calls(spell.GetMethod("Cancel", Flags)))
        {
            if (call.Name == "Interrupt") interrupt = true;
            if (call.Name.Contains("Cooldown") || call.Name.Contains("Spend") || call.Name.Contains("Refund"))
                throw new Exception("Blocked casts must not rewrite resources/action cooldowns.");
        }
        if (!interrupt) throw new Exception("Use native unstarted command cleanup instead of editing action state.");
        var reserve = false;
        foreach (var call in Calls(blocking.GetMethod("GuardMove", Flags)))
            if (call.Name == "ReservesStandard") reserve = true;
        if (!reserve) throw new Exception("Live crossing budget must reserve a queued standard spell.");
        var ownMove = false;
        foreach (var call in Calls(spell.GetMethod("CancelAfterBodyStop", Flags)))
            if (call.Name == "get_IsMoveUnit") ownMove = true;
        if (!ownMove) throw new Exception("A cast queued behind an unrelated move must not be cancelled by that move's body stop.");
    }

    private static void CompleteAttackRouteTests()
    {
        var routes = mod.GetType("ArdubsEnemyIntelligence.CompleteAttackRoutes");
        var current = routes.GetField("Current", Flags);
        var previous = current.GetValue(null);
        var configure = routes.GetMethod("Configure", Flags);
        var context = New("Kingmaker.AI.DecisionContext");
        var path = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
        var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3");
        Func<float, object> vector = x => Activator.CreateInstance(vectorType, new object[] { x, 0f, 0f });
        var match = routes.GetMethod("MatchesHostileTarget", Flags);
        bool PreviewMatch(float end, float target, float range, bool hostile) =>
            (bool)match.Invoke(null, new object[] { vector(end), vector(target), range, hostile });
        if (!PreviewMatch(5, 5, 1.5f, true) || PreviewMatch(5, 5, 1.5f, false) ||
            PreviewMatch(5, 5, 0f, true) || PreviewMatch(5, 6, 1.5f, true))
            throw new Exception("Only a hostile unit-target preview should use a complete approach search.");
        var previewScoped = false;
        foreach (var call in Calls(mod.GetType("ArdubsEnemyIntelligence.BodyBlocking").GetMethod("Attach", Flags)))
            if (call.Name == "IsHostilePreview") previewScoped = true;
        var previewChecksTarget = false;
        foreach (var call in Calls(mod.GetType("ArdubsEnemyIntelligence.BodyBlocking").GetMethod("IsHostilePreview", Flags)))
            if (call.DeclaringType == routes && call.Name == "MatchesHostileTarget") previewChecksTarget = true;
        if (!previewScoped || !previewChecksTarget)
            throw new Exception("Native preview paths must recognize hostile unit target points before search.");
        Set(path, "originalStartPoint", vector(0)); Set(path, "originalEndPoint", vector(5));
        Set(context, "DestinationPoint", vector(5));
        var endingType = game.GetType("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance+LosPathConditionForGrid");
        Func<object> bounded = () => Activator.CreateInstance(endingType, Flags, null,
            new object[] { path, 2f, false, 0.25f, 18.29f }, null);
        var ending = bounded(); Set(path, "endingCondition", ending);
        var astar = Assembly.Load("AstarPathfindingProject");
        var node = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.GridNode"));
        Action<int> position = x => Set(node, "position",
            Activator.CreateInstance(astar.GetType("Pathfinding.Int3"), new object[] { x, 0, 0 }));
        position(-19000);
        var pathNode = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(astar.GetType("Pathfinding.PathNode"));
        Set(pathNode, "node", node);
        var found = endingType.GetMethod("TargetFound", Flags);
        if (!(bool)found.Invoke(ending, new[] { pathNode }))
            throw new Exception("Regression setup: native one-turn horizon should accept an endpoint opposite the target.");
        try
        {
            current.SetValue(null, null);
            configure.Invoke(null, new object[] { path, true, false });
            if (!ReferenceEquals(ending, Field(path, "endingCondition")))
                throw new Exception("Unscoped player/spell/native paths must keep their original ending condition.");
            configure.Invoke(null, new object[] { path, false, true });
            if ((float)Field(Field(path, "endingCondition"), "m_MaxDistance") != float.MaxValue)
                throw new Exception("Committed hostile-target movement must find an actual approach position.");
            Set(path, "endingCondition", ending);
            current.SetValue(null, context);
            configure.Invoke(null, new object[] { path, false, false });
            if (!ReferenceEquals(ending, Field(path, "endingCondition")))
                throw new Exception("Another actor's path must not inherit the weapon-search scope.");
            Set(context, "DestinationPoint", vector(9));
            configure.Invoke(null, new object[] { path, true, false });
            if (!ReferenceEquals(ending, Field(path, "endingCondition")))
                throw new Exception("Another destination's path must not inherit the weapon-search scope.");
            Set(context, "DestinationPoint", vector(5));
            configure.Invoke(null, new object[] { path, true, false });
            var full = Field(path, "endingCondition");
            if (full.GetType() != endingType || !ReferenceEquals(path, Field(full, "abPath")))
                throw new Exception("Complete searches must retain the native ending-condition type and owner for saves.");
            if ((float)Field(full, "m_MaxDistance") != float.MaxValue)
                throw new Exception("Managed weapon search must use the native unlimited-distance sentinel.");
            Equal(2m, Field(full, "m_ApproachRadius"), "complete search preserves native attack range");
            Equal(0.25m, Field(full, "m_IgnoredBlockerRadius"), "complete search preserves ignored LOS blocker radius");
            if ((bool)Field(full, "m_CheckLos")) throw new Exception("Configure changed the requested LOS mode.");
            var losEnding = Activator.CreateInstance(endingType, Flags, null,
                new object[] { path, 2f, true, 0.25f, 18.29f }, null);
            Set(path, "endingCondition", losEnding);
            configure.Invoke(null, new object[] { path, true, false });
            if (!(bool)Field(Field(path, "endingCondition"), "m_CheckLos"))
                throw new Exception("Complete attack searches must preserve required native LOS checks.");
            Set(path, "endingCondition", full);
            if ((bool)found.Invoke(full, new[] { pathNode }))
                throw new Exception("A dead-end opposite-direction horizon must no longer count as finding the target.");
            position(19000);
            if ((bool)found.Invoke(full, new[] { pathNode }))
                throw new Exception("Distance alone must not make any out-of-range horizon endpoint valid.");
            position(4000);
            if (!(bool)found.Invoke(full, new[] { pathNode }))
                throw new Exception("A real in-range endpoint must remain reachable through native checks.");
            Set(path, "endingCondition", bounded());
            current.SetValue(null, null);
            configure.Invoke(null, new object[] { path, true, false });
            Equal(18.29m, Field(Field(path, "endingCondition"), "m_MaxDistance"),
                "a pooled path reused outside scope retains the new owner's native movement horizon");
            var mainSettings = mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain").GetField("Settings", Flags);
            var enabledSettings = mainSettings.GetValue(null);
            var disabledSettings = Activator.CreateInstance(mod.GetType("ArdubsEnemyIntelligence.Settings"));
            Set(disabledSettings, "ApplyToEnemies", false); Set(disabledSettings, "ApplyToAlliedNpcs", false);
            mainSettings.SetValue(null, disabledSettings);
            current.SetValue(null, context);
            try
            {
                var nativeEnding = Field(path, "endingCondition");
                configure.Invoke(null, new object[] { path, true, false });
                if (!ReferenceEquals(nativeEnding, Field(path, "endingCondition")))
                    throw new Exception("Both scopes off must keep the native movement horizon even for an existing request.");
            }
            finally { mainSettings.SetValue(null, enabledSettings); }
            var failed = New("Kingmaker.TurnBasedMode.Pathfinding.ABPathWithClearance");
            Set(failed, "completeState", Enum.Parse(astar.GetType("Pathfinding.PathCompleteState"), "Error"));
            var points = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vectorType));
            points.Add(vector(0)); Set(failed, "vectorPath", points);
            Set(context, "m_CurrentPath", failed);
            current.SetValue(null, context);
            mod.GetType("ArdubsEnemyIntelligence.TargetingPatches").GetMethod("FixAndCaptureGameRoute", Flags)
                .Invoke(null, new[] { context, vector(5) });
            if (!ReferenceEquals(failed, Field(context, "m_CurrentPath")) || points.Count != 1)
                throw new Exception("A failed one-point search must not become an unchecked straight-line FakePath.");
            var rejection = mod.GetType("ArdubsEnemyIntelligence.DistanceScoring").GetMethod("Capture", Flags).Invoke(null, new[] { context });
            if (Field(rejection, "Rejection") == null || (decimal)Field(rejection, "Meters") != 0m)
                throw new Exception("Failed full searches must produce an unavailable route, not an estimated continuation.");
            var patch = mod.GetType("ArdubsEnemyIntelligence.TargetingPatches").GetNestedType("ScoreDistancePatch", Flags);
            var prefixArgs = new object[] { null, context, null };
            patch.GetMethod("Prefix", Flags).Invoke(null, prefixArgs);
            if (current.GetValue(null) != null) throw new Exception("Unmanaged scoring must clear an enclosing search scope.");
            patch.GetMethod("Postfix", Flags).Invoke(null, new[] { prefixArgs[2] });
            if (!ReferenceEquals(current.GetValue(null), context)) throw new Exception("Postfix must restore the enclosing search scope.");
            current.SetValue(null, null);
            var exception = new InvalidOperationException("fixture search failure");
            var restoredException = patch.GetMethod("Finalizer", Flags).Invoke(null, new[] { prefixArgs[2], exception });
            if (!ReferenceEquals(current.GetValue(null), context) || !ReferenceEquals(restoredException, exception))
                throw new Exception("Exceptional scoring must restore scope without swallowing native failures.");
            var usesConfigure = false;
            foreach (var call in Calls(mod.GetType("ArdubsEnemyIntelligence.BodyBlocking").GetMethod("Attach", Flags)))
                if (call.DeclaringType == routes && call.Name == "Configure") usesConfigure = true;
            if (!usesConfigure) throw new Exception("Full-route configuration must happen before the actual native search.");
            var carriesSelectedPath = false;
            foreach (var call in Calls(game.GetType("Kingmaker.AI.AiBrainController").GetMethod("SelectAction", Flags)))
                if (call.Name == "set_ForcedPath") carriesSelectedPath = true;
            if (!carriesSelectedPath) throw new Exception("Native command pipeline changed: recheck reuse of the verified selected path.");
        }
        finally { current.SetValue(null, previous); }
    }

    private static void TouchDeliveryPathTests()
    {
        // The game's sticky-touch effect queues a second command. Verify the
        // native command pipeline still has the two handoff points we patch.
        var sticky = game.GetType("Kingmaker.UnitLogic.Abilities.Components.AbilityEffectStickyTouch");
        var apply = sticky.GetMethod("Apply", Flags);
        var createsDelivery = false;
        var queuesDelivery = false;
        foreach (var call in Calls(apply))
        {
            if (call.Name == "CreateCastCommand") createsDelivery = true;
            if (call.Name == "AddToQueueFirst") queuesDelivery = true;
        }
        if (!createsDelivery || !queuesDelivery)
            throw new Exception("Native sticky-touch command handoff changed; recheck AI path transfer.");

        var helper = mod.GetType("ArdubsEnemyIntelligence.TouchDeliveryPath", true);
        var commitPatch = mod.GetType("ArdubsEnemyIntelligence.TargetingPatches")
            .GetNestedType("CommitBeforeApproachPatch", Flags);
        var retainsAtCommit = false;
        foreach (var call in Calls(commitPatch.GetMethod("Postfix", Flags)))
            if (call.Name == "Remember") retainsAtCommit = true;
        if (!retainsAtCommit)
            throw new Exception("AI cast path must be retained before asynchronous sticky-touch execution.");
        var transfer = helper.GetMethod("Transfer", Flags);
        var claimsSelectedPath = false;
        foreach (var call in Calls(transfer))
            if (call.Name == "set_ForcedPath") claimsSelectedPath = true;
        if (!claimsSelectedPath)
            throw new Exception("AI sticky-touch delivery must receive the selected path through its claiming setter.");
        var remembersPlayerPreview = false;
        foreach (var call in Calls(helper.GetMethod("Remember", Flags)))
            if (call.Name == "PreviewPath") remembersPlayerPreview = true;
        if (!remembersPlayerPreview)
            throw new Exception("Player touch casts must retain a matching movement preview before casting.");
        var recoveryCalls = Calls(helper.GetMethod("RecoverNearbyPlayerPath", Flags)).Select(call => call.Name).ToArray();
        if (!recoveryCalls.Contains("FindPath") || !recoveryCalls.Contains("BlockUntilCalculated") ||
            !recoveryCalls.Contains("set_ForcedPath"))
            throw new Exception("Nearby player touch delivery must recover a native path before the next command tick.");
        if (!Calls(helper.GetMethod("Handles", Flags)).Any(call => call.Name == "get_IsDirectlyControllable"))
            throw new Exception("Touch delivery must include directly controllable casters.");
        var nativeClaimsPath = false;
        foreach (var call in Calls(game.GetType("Kingmaker.UnitLogic.Commands.Base.UnitCommand")
            .GetMethod("set_ForcedPath", Flags)))
            if (call.Name == "Claim") nativeClaimsPath = true;
        if (!nativeClaimsPath)
            throw new Exception("Native ForcedPath ownership changed; recheck retained path release.");

        var targeting = mod.GetType("ArdubsEnemyIntelligence.TargetingState");
        var rejectsMissingRoute = false;
        var scoresAbilityTiming = false;
        foreach (var call in Calls(targeting.GetMethod("FinishPathScore", Flags)))
        {
            if (call.Name == "AbilityRouteIssue") rejectsMissingRoute = true;
            if (call.DeclaringType.Name == "DistanceScoring" && call.Name == "EvaluateAbility")
                scoresAbilityTiming = true;
        }
        if (!rejectsMissingRoute)
            throw new Exception("Every scored offensive ability must reject an unusable approach route.");
        if (!scoresAbilityTiming)
            throw new Exception("Offensive ability targets must account for the earliest turn they can be used.");
    }

    private static int Main(string[] args)
    {
        try
        {
            var managed = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var filename = new AssemblyName(eventArgs.Name).Name + ".dll";
                foreach (var root in new[] { managed, Path.Combine(managed, "UnityModManager") })
                    if (File.Exists(Path.Combine(root, filename))) return Assembly.LoadFrom(Path.Combine(root, filename));
                return null;
            };
            game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            mod = Assembly.Load(File.ReadAllBytes(args[1]));
            if (mod.GetName().Name != "ArdubsEnemyIntelligence")
                throw new Exception("Mod assembly name does not match its UMM identity.");
            var entryType = mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain", true);
            var load = entryType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
            if (load == null || load.ReturnType != typeof(bool) || load.GetParameters().Length != 1 ||
                load.GetParameters()[0].ParameterType.FullName != "UnityModManagerNet.UnityModManager+ModEntry")
                throw new Exception("UMM entry method is missing or has the wrong signature.");
            mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain").GetField("Settings", Flags)
                .SetValue(null, Activator.CreateInstance(mod.GetType("ArdubsEnemyIntelligence.Settings")));
            AlliedNpcScopeTests();
            OpportunityAllegianceTests();
            RememberedTargetTests();
            LiveMovementContractTests();
            SleepingTargetTests();
            BodyRouteTests();
            BodyStepTests();
            SpellApproachTests();
            TouchDeliveryPathTests();
            CompleteAttackRouteTests();
            DestinationFootprintTests();
            SimulationApproachStopTests();
            NativeBodyUpdateTests();
            Equal(1.75m, Property(Read(Actions(Damage("AbilityDamage", 1, "D6"))), "StatFactor"), "Shadow 1d6 Strength on-hit");
            Equal(2.25m, Property(Read(Actions(Damage("AbilityDamage", 1, "D8"))), "StatFactor"), "Greater Shadow 1d8");
            Equal(2.1875m, Property(Read(Actions(Damage("AbilityDamage", 1, "D6", drain: true))), "StatFactor"), "drain vs damage");
            Equal(2m, Property(Read(Actions(Damage("EnergyDrain", 0, "Zero", 2))), "StatFactor"), "negative levels");
            var branch = New("Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional");
            Set(branch, "IfTrue", Actions(Damage("AbilityDamage", 1, "D6")));
            Set(branch, "IfFalse", Actions(Damage("AbilityDamage", 1, "D6")));
            Equal(1.75m, Property(Read(Actions(branch)), "StatFactor"), "alternative branches not double counted");
            Equal(3.5m, Property(Read(Actions(Damage("AbilityDamage", 1, "D6"), Damage("AbilityDamage", 1, "D6"))), "StatFactor"), "sequential effects add");

            var buff = New("Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff");
            var activation = New("Kingmaker.UnitLogic.Mechanics.Components.AddFactContextActions");
            Set(activation, "Activated", Actions(Damage("AbilityDamage", 1, "D6")));
            var components = Array.CreateInstance(game.GetType("Kingmaker.Blueprints.BlueprintComponent"), 1);
            components.SetValue(activation, 0); Set(buff, "Components", components);
            var reference = New("Kingmaker.Blueprints.BlueprintBuffReference");
            game.GetType("Kingmaker.Blueprints.BlueprintReferenceBase").GetField("<Cached>k__BackingField", Flags).SetValue(reference, buff);
            var apply = New("Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff");
            Set(apply, "m_Buff", reference);
            Equal(1.75m, Property(Read(Actions(apply)), "StatFactor"), "literal buff activation used by Shadow alternate branch");
            Set(branch, "IfFalse", Actions(apply));
            Equal(1.75m, Property(Read(Actions(branch)), "StatFactor"), "Shadow conditional/buff alternatives do not double count");

            var hp = Damage("Damage", 1, "D6");
            var damageType = New("Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription");
            SetEnum(damageType, "Type", "Energy"); SetEnum(damageType, "Energy", "Fire"); Set(hp, "DamageType", damageType);
            Equal(3.5m, Property(Read(Actions(hp)), "Hp"), "fire on-hit mean is separate from stat effects");
            Equal(0m, Property(Read(Actions(hp)), "StatFactor"), "HP does not leak into stat danger");
            var unresolved = Damage("AbilityDamage", 1, "D6");
            SetEnum(Field(Field(unresolved, "Value"), "DiceCountValue"), "ValueType", "Shared");
            var potential = Read(Actions(unresolved));
            Equal(0m, Property(potential, "StatFactor"), "execution-dependent dice never roll or guess");
            Equal(1m, Property(Field(potential, "Unmodeled"), "Count"), "unsupported damage is visibly labeled, not known absent");

            var settingsType = mod.GetType("ArdubsEnemyIntelligence.Settings");
            var serializer = new XmlSerializer(settingsType);
            var settings = serializer.Deserialize(new StringReader("<Settings><InGameLogging>false</InGameLogging><UmmLogging>true</UmmLogging><DefaultStrengthRange>false</DefaultStrengthRange><StrengthMinimum>3</StrengthMinimum><StrengthMaximum>12</StrengthMaximum></Settings>"));
            if ((bool)Field(settings, "InGameLogging") || !(bool)Field(settings, "UmmLogging")) throw new Exception("Logging preferences not preserved.");
            Equal(3m, Field(settings, "StrengthMinimum"), "old custom settings preserved");
            Equal(10m, Field(settings, "SneakMinimum"), "new precision defaults initialized for old Settings.xml");
            Equal(25m, Field(settings, "StatDrainMaximum"), "new stat/drain defaults initialized");
            Equal(1m, Field(settings, "CasterThreatMultiplier"), "old settings get neutral caster tuning");
            Equal(1m, Field(settings, "DistanceImportance"), "old settings get neutral distance tuning");
            if (!(bool)Field(settings, "DefaultCasterThreatMultiplier")) throw new Exception("New weights must default to unchanged behavior.");
            if (!(bool)Field(Activator.CreateInstance(settingsType), "ApplyToAlliedNpcs")) throw new Exception("New settings must enable allied NPC AI by default.");
            if (!(bool)Field(settings, "ApplyToAlliedNpcs")) throw new Exception("Old settings without a scope preference must enable allied NPC AI.");
            if (!(bool)Field(settings, "ApplyToEnemies")) throw new Exception("Old settings without enemy scope must keep enemy targeting enabled.");
            if (!(bool)Field(settings, "AvoidSleepingTargets")) throw new Exception("Old settings must default to protecting sleeping targets.");
            var allowSleep = serializer.Deserialize(new StringReader("<Settings><AvoidSleepingTargets>false</AvoidSleepingTargets></Settings>"));
            var sleepSaved = new StringWriter(); serializer.Serialize(sleepSaved, allowSleep);
            allowSleep = serializer.Deserialize(new StringReader(sleepSaved.ToString()));
            if ((bool)Field(allowSleep, "AvoidSleepingTargets")) throw new Exception("The disabled sleeping-target option must persist.");
            foreach (var option in new[] { "EnableBodyBlocking", "BlockHostilePassage", "AllowAlliedPassage", "AllowFlyingEnemyPassage", "IgnoreDownedUnitSpace", "PreventStoppingOverlap", "BodyBlockingMovementGuard", "PreserveSpecialBodyMovement", "ShowBodyBlockingInLog", "IgnoreFriendlyBlockingOnFiveFootStep" })
                if (!(bool)Field(settings, option)) throw new Exception("Missing default-on pathfinding option: " + option);
            if ((bool)Field(settings, "BodyBlockingCharges")) throw new Exception("Charges must keep native movement by default.");
            if ((bool)Field(settings, "AllowFriendlyStoppingOverlap")) throw new Exception("Ordinary friendly stopping overlap must default off for old settings.");
            if (!(bool)Field(settings, "ShowDestinationFootprint")) throw new Exception("Old settings must enable the new footprint preview by default.");
            var savedSettings = new StringWriter();
            serializer.Serialize(savedSettings, settings);
            settings = serializer.Deserialize(new StringReader(savedSettings.ToString()));
            if (!(bool)Field(settings, "ApplyToAlliedNpcs")) throw new Exception("Allied NPC option did not survive saving/reloading.");
            if ((bool)Field(settings, "InGameLogging") || !(bool)Field(settings, "UmmLogging")) throw new Exception("Scope toggle must preserve logging preferences.");
            Equal(3m, Field(settings, "StrengthMinimum"), "scope toggle preserves custom knowledge settings");
            var disabledSettings = serializer.Deserialize(new StringReader("<Settings><ApplyToAlliedNpcs>false</ApplyToAlliedNpcs></Settings>"));
            if ((bool)Field(disabledSettings, "ApplyToAlliedNpcs")) throw new Exception("An explicitly disabled allied NPC option must stay disabled.");
            var savedDisabledSettings = new StringWriter();
            serializer.Serialize(savedDisabledSettings, disabledSettings);
            disabledSettings = serializer.Deserialize(new StringReader(savedDisabledSettings.ToString()));
            if ((bool)Field(disabledSettings, "ApplyToAlliedNpcs")) throw new Exception("Disabled allied NPC option did not survive saving/reloading.");
            var pathSettings = serializer.Deserialize(new StringReader("<Settings><EnableBodyBlocking>false</EnableBodyBlocking><BlockHostilePassage>false</BlockHostilePassage><AllowAlliedPassage>false</AllowAlliedPassage><PreventStoppingOverlap>false</PreventStoppingOverlap><BodyBlockingMovementGuard>false</BodyBlockingMovementGuard><PreserveSpecialBodyMovement>false</PreserveSpecialBodyMovement><BodyBlockingCharges>true</BodyBlockingCharges><ShowBodyBlockingInLog>false</ShowBodyBlockingInLog></Settings>"));
            var pathSaved = new StringWriter(); serializer.Serialize(pathSaved, pathSettings);
            pathSettings = serializer.Deserialize(new StringReader(pathSaved.ToString()));
            foreach (var option in new[] { "EnableBodyBlocking", "BlockHostilePassage", "AllowAlliedPassage", "PreventStoppingOverlap", "BodyBlockingMovementGuard", "PreserveSpecialBodyMovement", "ShowBodyBlockingInLog" })
                if ((bool)Field(pathSettings, option)) throw new Exception("Explicitly disabled pathfinding option did not persist: " + option);
            if (!(bool)Field(pathSettings, "BodyBlockingCharges")) throw new Exception("Charge opt-in did not persist.");
            var previewSettings = serializer.Deserialize(new StringReader("<Settings><ShowDestinationFootprint>false</ShowDestinationFootprint><EnableBodyBlocking>false</EnableBodyBlocking></Settings>"));
            var previewSaved = new StringWriter(); serializer.Serialize(previewSaved, previewSettings);
            previewSettings = serializer.Deserialize(new StringReader(previewSaved.ToString()));
            if ((bool)Field(previewSettings, "ShowDestinationFootprint")) throw new Exception("Explicitly disabled footprint preview did not persist.");
            var flightSettings = serializer.Deserialize(new StringReader("<Settings><AllowFlyingEnemyPassage>false</AllowFlyingEnemyPassage><IgnoreDownedUnitSpace>false</IgnoreDownedUnitSpace></Settings>"));
            var flightSaved = new StringWriter(); serializer.Serialize(flightSaved, flightSettings);
            flightSettings = serializer.Deserialize(new StringReader(flightSaved.ToString()));
            if ((bool)Field(flightSettings, "AllowFlyingEnemyPassage")) throw new Exception("Explicitly disabled flying passage did not persist.");
            if ((bool)Field(flightSettings, "IgnoreDownedUnitSpace")) throw new Exception("Explicitly disabled sleeping-space preference did not persist.");
            var friendlySettings = serializer.Deserialize(new StringReader("<Settings><IgnoreFriendlyBlockingOnFiveFootStep>false</IgnoreFriendlyBlockingOnFiveFootStep><AllowFriendlyStoppingOverlap>true</AllowFriendlyStoppingOverlap></Settings>"));
            var friendlySaved = new StringWriter(); serializer.Serialize(friendlySaved, friendlySettings);
            friendlySettings = serializer.Deserialize(new StringReader(friendlySaved.ToString()));
            if ((bool)Field(friendlySettings, "IgnoreFriendlyBlockingOnFiveFootStep")) throw new Exception("Explicitly disabled friendly five-foot exception did not persist.");
            if (!(bool)Field(friendlySettings, "AllowFriendlyStoppingOverlap")) throw new Exception("Explicitly enabled friendly stopping preference did not persist.");
            var inactive = serializer.Deserialize(new StringReader("<Settings><ApplyToEnemies>false</ApplyToEnemies><ApplyToAlliedNpcs>false</ApplyToAlliedNpcs></Settings>"));
            foreach (var property in new[] { "ModEnabled", "BodyBlockingEnabled", "AnyLogging" })
                if ((bool)Property(inactive, property)) throw new Exception("Both scopes off must deactivate " + property);
            var inactiveSaved = new StringWriter(); serializer.Serialize(inactiveSaved, inactive);
            inactive = serializer.Deserialize(new StringReader(inactiveSaved.ToString()));
            if ((bool)Field(inactive, "ApplyToEnemies") || (bool)Field(inactive, "ApplyToAlliedNpcs"))
                throw new Exception("Both disabled scopes must survive saving/reloading.");
            var presetType = settingsType.GetNestedType("PathfindingPreset", Flags);
            var configurePreset = settingsType.GetMethod("ConfigurePathfindingPreset", Flags);
            var presets = Activator.CreateInstance(settingsType);
            Set(presets, "ApplyToEnemies", false);
            Set(presets, "AvoidSleepingTargets", false);
            Set(presets, "InGameLogging", false);
            Set(presets, "ShowBodyBlockingInLog", false);
            Set(presets, "StrengthMinimum", 3);
            var pathOptions = new[] { "EnableBodyBlocking", "BlockHostilePassage", "AllowAlliedPassage",
                "IgnoreFriendlyBlockingOnFiveFootStep", "AllowFriendlyStoppingOverlap", "AllowFlyingEnemyPassage",
                "ShowFlyingPassageVisual",
                "IgnoreDownedUnitSpace", "PreventStoppingOverlap", "BodyBlockingMovementGuard",
                "PreserveSpecialBodyMovement", "BodyBlockingCharges", "ShowDestinationFootprint" };
            foreach (var presetName in new[] { "Wotr", "Pnp", "Reset" })
            {
                configurePreset.Invoke(presets, new[] { Enum.Parse(presetType, presetName) });
                foreach (var option in pathOptions)
                {
                    var expected = (bool)Field(Activator.CreateInstance(settingsType), option);
                    if (presetName == "Wotr") expected = option == "AllowAlliedPassage" || option == "PreserveSpecialBodyMovement";
                    if (presetName == "Pnp")
                    {
                        if (option == "IgnoreFriendlyBlockingOnFiveFootStep" || option == "AllowFlyingEnemyPassage") expected = false;
                        if (option == "BodyBlockingCharges") expected = true;
                    }
                    if ((bool)Field(presets, option) != expected) throw new Exception(presetName + " preset mismatch: " + option);
                }
                if ((bool)Field(presets, "ApplyToEnemies") || (bool)Field(presets, "AvoidSleepingTargets") || (bool)Field(presets, "InGameLogging") || (bool)Field(presets, "ShowBodyBlockingInLog"))
                    throw new Exception("Pathfinding presets must preserve AI scope and logging choices.");
                Equal(3m, Field(presets, "StrengthMinimum"), "pathfinding presets preserve custom knowledge");
            }
            var independentPreview = serializer.Deserialize(new StringReader("<Settings><EnableBodyBlocking>false</EnableBodyBlocking></Settings>"));
            if (!(bool)Field(independentPreview, "ShowDestinationFootprint")) throw new Exception("Footprint preview must remain independent of body-blocking enablement.");

            // Exercise the real scoring integration without an entity/Unity
            // scene. Frequency is unknown, so no observer AC is needed.
            mod.GetType("ArdubsEnemyIntelligence.ArdubsEnemyIntelligenceMain").GetField("Settings", Flags).SetValue(null, settings);
            var targeting = mod.GetType("ArdubsEnemyIntelligence.TargetingState");
            var knowledgeType = targeting.GetNestedType("TargetKnowledge", Flags);
            var knowledge = Activator.CreateInstance(knowledgeType, true);
            Set(knowledge, "AttackBonusKnown", true); Set(knowledge, "AttackBonus", 10);
            Set(knowledge, "CasterKnown", true); Set(knowledge, "IsCaster", true);
            Set(knowledge, "CasterLevelKnown", true); Set(knowledge, "CasterLevel", 20);
            var hitType = mod.GetType("ArdubsEnemyIntelligence.OffensiveAssessment").GetNestedType("Hit", Flags);
            var hit = Activator.CreateInstance(hitType, true);
            Set(hit, "BaseDamage", 10m); Set(hit, "SneakDamage", 7m); Set(hit, "ExtraDamage", 3.5m);
            Set(hit, "Effects", Read(Actions(Damage("AbilityDamage", 1, "D6"))));
            var hitArray = Array.CreateInstance(hitType, 1); hitArray.SetValue(hit, 0);
            var snapshotType = mod.GetType("ArdubsEnemyIntelligence.OffensiveAssessment").GetNestedType("Snapshot", Flags);
            var snapshot = Activator.CreateInstance(snapshotType, Flags, null, new object[] { hitArray }, null);
            Set(knowledge, "DamageEstimate", snapshot);
            var buildRisk = targeting.GetMethod("BuildRisk", Flags);
            Equal(289m / 49m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Risk"),
                "unlearned precision/extra/drain in cached snapshot must remain hidden");
            Equal(1m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Damage"),
                "standalone caster threat must not mask leaked unknown damage categories");
            Set(knowledge, "CasterLevel", 7);
            var levelSevenRisk = buildRisk.Invoke(null, new object[] { null, knowledge });
            Equal(2.25m, Property(levelSevenRisk, "Risk"), "actual scoring uses calibrated CL 7 caster threat");
            Equal(1.35m, Property(levelSevenRisk, "CastingSynergy"), "actual scoring keeps CL 7 melee synergy linear");
            var levelSevenLog = (string)targeting.GetMethod("DescribeRisk", Flags)
                .Invoke(null, new object[] { null, knowledge, levelSevenRisk });
            if (!levelSevenLog.Contains("CastingSynergy[CL highest 7 => 1.35]") ||
                !levelSevenLog.Contains("CasterOnly[CL highest 7 => 2.25]"))
                throw new Exception("Actual risk log must distinguish CL 7 synergy from standalone caster threat.");
            Set(knowledge, "CasterLevelKnown", false);
            Equal(1.25m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Risk"),
                "unlearned CL must still use the conservative recognized-caster fallback");
            Set(knowledge, "CasterLevelKnown", true);
            Set(knowledge, "CasterLevel", 20);
            var threats = (IDictionary)Field(knowledge, "Threats");
            var kindType = mod.GetType("ArdubsEnemyIntelligence.ThreatKnowledge");
            foreach (var kind in new[] { "Sneak", "ExtraDamage", "StatDrain" })
                Set(threats[Enum.Parse(kindType, kind)], "Estimate", snapshot);
            Equal(7.6m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Risk"),
                "known categories contribute independently with neutral default weights");
            foreach (var setting in new[] { "CasterThreatMultiplier", "PhysicalThreatMultiplier", "SneakThreatMultiplier", "ExtraDamageThreatMultiplier", "StatDrainThreatMultiplier" })
                Set(settings, "Default" + setting, false);
            Set(settings, "CasterThreatMultiplier", 2f); Set(settings, "PhysicalThreatMultiplier", 0.5f);
            Set(settings, "SneakThreatMultiplier", 2f); Set(settings, "ExtraDamageThreatMultiplier", 3f);
            Set(settings, "StatDrainThreatMultiplier", 4f);
            Equal(15.675m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Risk"),
                "actual BuildRisk reads all five independent component controls");
            Set(settings, "DefaultCasterThreatMultiplier", true);
            Equal(10.45m, Property(buildRisk.Invoke(null, new object[] { null, knowledge }), "Risk"),
                "Default checkbox restores neutral caster weight while retaining other custom values");
            Console.WriteLine("Game metadata tests passed: occupied in-range preview stops, remembered-target awareness, touch-delivery path handoff, complete attack-route regression, damage/risk integration, native body-clearance hooks, route trimming/pooling, native ending conditions, and settings migration/persistence.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
