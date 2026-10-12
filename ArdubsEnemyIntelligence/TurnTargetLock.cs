namespace ArdubsEnemyIntelligence;

// Transient visibility and route changes do not release a target. A downed
// target may be released explicitly so the attack can switch or stop.
internal sealed class TurnTargetLock<T> where T : class
{
    internal T? Target { get; private set; }

    internal void BeginTurn(T? heldTarget)
    {
        Target = heldTarget;
    }

    internal bool Allows(T candidate) => Target == null || ReferenceEquals(Target, candidate);

    internal bool TryCommit(T candidate)
    {
        if (!Allows(candidate))
        {
            return false;
        }

        Target = candidate;
        return true;
    }

    internal bool Release(T target)
    {
        if (!ReferenceEquals(Target, target)) return false;
        Target = null;
        return true;
    }
}
