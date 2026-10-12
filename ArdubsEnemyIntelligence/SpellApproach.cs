using Kingmaker.TurnBasedMode;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

// Only unstarted player commands altered by our clearance checks qualify for
// cancellation. AI standard-action casts still reserve their casting action
// when the shared movement guard checks a possible stopping position.
// Interrupting lets native cleanup remove the player's action prediction;
// never refund cooldowns, spell slots or movement, or touch a cast spell.
internal static class SpellApproach
{
    private static bool Pending(UnitUseAbility? cast) => cast != null && !cast.IsStarted &&
        !cast.IsActed && !cast.IsFinished && !cast.Cutscene && cast.AiAction == null &&
        cast.Executor != null && cast.Executor.IsDirectlyControllable;

    internal static bool ReservesStandard(UnitUseAbility? cast) =>
        (Pending(cast) || cast?.AiAction != null && cast.IsMoveUnit &&
         !cast.IsActed && !cast.IsFinished && !cast.Cutscene) &&
        cast!.Type == UnitCommand.CommandType.Standard;

    private static bool Applies(UnitUseAbility? cast) => ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled &&
        CombatController.IsInTurnBasedCombat() && Pending(cast) && cast!.Executor.IsInCombat &&
        !cast.IsTouchAbility(); // Sticky-touch spells cast first, then approach.

    internal static bool ContinueApproaching(UnitCommand command)
    {
        if (!(command is UnitUseAbility cast) || !Applies(cast) || cast.IsUnitCloseEnough()) return true;
        var path = cast.ForcedPath;
        if (path == null)
        {
            var preview = PathVisualizer.Instance;
            if (preview?.m_LastUpdateData == null ||
                !ReferenceEquals(preview.m_LastUpdateData.Settings.Unit, cast.Executor)) return true;
            path = preview.CurrentPathForUnit(cast.Executor.View);
        }
        if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count == 0) return true;
        var snapshot = BodyBlocking.For(path);
        if (snapshot == null || !snapshot.ClearanceLimited ||
            !(path is ABPathWithClearance approach) ||
            !(approach.endingCondition is ABPathWithClearance.LosPathConditionForGrid condition)) return true;
        var matching = (approach.originalEndPoint - cast.ApproachPoint).sqrMagnitude < 0.000001f &&
            System.Math.Abs(condition.m_ApproachRadius - cast.ApproachRadius) < 0.0001f &&
            condition.m_CheckLos == cast.NeedLoS;
        var endpoint = path.vectorPath[path.vectorPath.Count - 1];
        var canCast = UnitCommand.IsUnitCloseEnough(cast.ApproachPoint, endpoint,
            cast.Executor.EyePosition + endpoint - cast.Executor.Position, cast.ApproachRadius,
            cast.NeedLoS, cast.GetTargetLOSObjectFudgeRadius());
        if (!BodyBlockingPolicy.CancelBlockedCast(true, matching, canCast)) return true;
        Cancel(cast);
        return false;
    }

    internal static void CancelAfterBodyStop(UnitUseAbility? cast)
    {
        // A cast queued behind a separate move command does not own that move.
        if (Applies(cast) && cast!.IsMoveUnit && !cast.IsUnitCloseEnough()) Cancel(cast);
    }

    private static void Cancel(UnitUseAbility cast)
    {
        cast.Interrupt();
        if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
            TargetingLog.Write($"Pathfinding: {cast.Executor.CharacterName}: queued cast cancelled; " +
                "body-clearance endpoint is outside casting range/LOS; spell was not started");
    }
}
