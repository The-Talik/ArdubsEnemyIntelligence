using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.TurnBasedMode;
using Kingmaker.View;
using Pathfinding;
using TurnBased.Controllers;
using UnityEngine;
using UnityEngine.Rendering;
using Body = ArdubsEnemyIntelligence.BodyBlockingPolicy.Body;

namespace ArdubsEnemyIntelligence;

// UI-only feedback for the flying-passage rule. The model child is lifted;
// the unit root, selection circle, mechanics position, collider, reach and
// pathfinding footprint remain untouched.
internal static class FlyingPassageVisual
{
    private const float GroundOffset = 0.11f;
    private static readonly Color PassageColor = new Color(0.35f, 0.82f, 1f, 0.95f);
    private static readonly Dictionary<Component, Mark> Marks = new Dictionary<Component, Mark>();
    private static readonly Dictionary<UnitMovementAgent, Lift> Lifts = new Dictionary<UnitMovementAgent, Lift>();

    private sealed class Mark
    {
        internal readonly List<LineRenderer> Lines = new List<LineRenderer>();
        internal readonly List<LineRenderer> Wings = new List<LineRenderer>();
        internal Material? Material;
        internal bool OwnsMaterial;
        internal int SeenFrame;
        internal bool Warned;
    }

    private sealed class Lift
    {
        internal Transform? Visual;
        internal Path? Path;
        internal bool LoggedCrossing;
        internal float Applied;
        internal float Target;
        internal int SeenFrame;
    }

    private readonly struct Interval
    {
        internal readonly Vector3 Start;
        internal readonly Vector3 End;
        internal Interval(Vector3 start, Vector3 end) { Start = start; End = end; }
    }

    internal static void ObserveMovement(UnitMovementAgent agent, Vector3 position, BodyBlocking.Snapshot? snapshot)
    {
        if (!Enabled(snapshot)) return;
        var lift = GetLift(agent);
        if (!ReferenceEquals(lift.Path, agent.Path))
        {
            lift.Path = agent.Path;
            lift.LoggedCrossing = false;
        }
        var fraction = BodyBlockingPolicy.FlyoverHeightFraction(
            BodyBlocking.ToPoint(position), snapshot!.FlyoverBodies);
        var radius = snapshot.FlyoverBodies.Length == 0 ? 0 : snapshot.FlyoverBodies.Max(body => body.Radius);
        lift.Target = Mathf.SmoothStep(0, 1, (float)fraction) * Mathf.Clamp((float)radius * 0.9f, 0.75f, 1.8f);
        lift.SeenFrame = Time.frameCount;
        if (fraction > 0.01 && !lift.LoggedCrossing && ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
        {
            lift.LoggedCrossing = true;
            TargetingLog.Write($"Flying passage: {agent.Unit.EntityData.CharacterName} entered an enemy footprint; " +
                $"overlap {fraction:0.00}; target model lift {lift.Target:0.00}m; " +
                $"visual child {(VisualTransform(agent) == null ? "missing" : "present")}");
        }

        if (agent.Path?.vectorPath != null)
        {
            var route = new List<Vector3> { position };
            for (var index = Math.Max(0, agent.m_NextPointIndex); index < agent.Path.vectorPath.Count; index++)
                if ((agent.Path.vectorPath[index] - route[route.Count - 1]).sqrMagnitude > 0.000001f)
                    route.Add(agent.Path.vectorPath[index]);
            RefreshMark(agent, route, snapshot.FlyoverBodies);
        }
    }

    internal static string DescribeMovement(UnitMovementAgent agent, Vector3 from, BodyBlocking.Snapshot snapshot)
    {
        if (!agent.Unit.EntityData.State.Features.Flying ||
            !ArdubsEnemyIntelligenceMain.Settings.ShowFlyingPassageVisual) return "";
        var route = new List<Vector3> { from };
        var path = agent.Path;
        if (path?.vectorPath != null)
            for (var index = Math.Max(0, agent.m_NextPointIndex); index < path.vectorPath.Count; index++)
                if ((path.vectorPath[index] - route[route.Count - 1]).sqrMagnitude > 0.000001f)
                    route.Add(path.vectorPath[index]);
        var crossings = FindIntervals(route, snapshot.FlyoverBodies).Count;
        return $"; flight visual [mechanical Flying yes; hostile bodies {snapshot.FlyoverBodies.Length}; " +
            $"planned crossings {crossings}; visual child {(VisualTransform(agent) == null ? "missing" : "present")}]";
    }

    internal static void Update(float deltaTime)
    {
        RefreshPreview();
        var frame = Time.frameCount;
        foreach (var pair in Marks.ToArray())
        {
            if (pair.Key == null)
            {
                RemoveMark(pair.Key!, pair.Value);
                continue;
            }
            if (pair.Value.SeenFrame < frame - 1) Hide(pair.Value);
        }

        foreach (var pair in Lifts.ToArray())
        {
            var agent = pair.Key;
            var lift = pair.Value;
            if (agent == null || !ArdubsEnemyIntelligenceMain.Settings.ModEnabled || !ArdubsEnemyIntelligenceMain.Settings.ShowFlyingPassageVisual ||
                !CombatController.IsInTurnBasedCombat() || lift.SeenFrame < frame - 1)
                lift.Target = 0;
            ApplyLift(agent!, lift, deltaTime);
            if (lift.Applied <= 0.0001f && lift.Target <= 0.0001f)
                Lifts.Remove(agent!);
        }
    }

    private static bool Enabled(BodyBlocking.Snapshot? snapshot) =>
        snapshot != null && snapshot.FlyoverBodies.Length > 0 && ArdubsEnemyIntelligenceMain.Settings.ModEnabled &&
        ArdubsEnemyIntelligenceMain.Settings.ShowFlyingPassageVisual && ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled &&
        ArdubsEnemyIntelligenceMain.Settings.AllowFlyingEnemyPassage && CombatController.IsInTurnBasedCombat();

    private static Lift GetLift(UnitMovementAgent agent)
    {
        if (!Lifts.TryGetValue(agent, out var lift))
        {
            lift = new Lift();
            Lifts.Add(agent, lift);
        }
        return lift;
    }

    private static Transform? VisualTransform(UnitMovementAgent agent)
    {
        var view = agent.Unit?.EntityData?.View;
        if (view == null) return null;
        var avatar = view.CharacterAvatar?.transform;
        if (avatar != null && !ReferenceEquals(avatar, view.transform)) return avatar;
        var animator = view.Animator?.transform;
        return animator != null && !ReferenceEquals(animator, view.transform) ? animator : null;
    }

    private static void ApplyLift(UnitMovementAgent agent, Lift lift, float deltaTime)
    {
        var visual = agent == null ? null : VisualTransform(agent);
        if (lift.Visual != null && lift.Visual != visual && lift.Applied > 0)
            lift.Visual.localPosition -= Vector3.up * lift.Applied;
        lift.Visual = visual;
        if (visual == null)
        {
            lift.Applied = 0;
            return;
        }
        var basePosition = visual.localPosition - Vector3.up * lift.Applied;
        lift.Applied = Mathf.MoveTowards(lift.Applied, lift.Target, Mathf.Max(0.01f, deltaTime) * 4f);
        visual.localPosition = basePosition + Vector3.up * lift.Applied;
    }

    private static void RefreshPreview()
    {
        var preview = PathVisualizer.Instance;
        if (preview == null) return;
        var path = preview.m_CurrentPath;
        var visible = path?.vectorPath != null && preview.m_Renderer != null && preview.m_Renderer.enabled &&
            preview.m_Renderer.positionCount > 1 && preview.m_LastUpdateData != null;
        var snapshot = visible && path != null ? BodyBlocking.For(path) : null;
        if (!Enabled(snapshot))
        {
            if (Marks.TryGetValue(preview, out var old)) Hide(old);
            return;
        }
        RefreshMark(preview, path!.vectorPath, snapshot!.FlyoverBodies);
    }

    private static List<Interval> FindIntervals(IReadOnlyList<Vector3> route, IReadOnlyList<Body> bodies)
    {
        var result = new List<Interval>();
        for (var index = 1; index < route.Count; index++)
        {
            var a = route[index - 1];
            var z = route[index];
            foreach (var body in bodies)
            {
                if (!BodyBlockingPolicy.TryInsideInterval(BodyBlocking.ToPoint(a), BodyBlocking.ToPoint(z),
                        body, out var enter, out var exit)) continue;
                result.Add(new Interval(Vector3.Lerp(a, z, (float)enter), Vector3.Lerp(a, z, (float)exit)));
            }
        }
        return result;
    }

    private static void RefreshMark(Component owner, IReadOnlyList<Vector3> route, IReadOnlyList<Body> bodies)
    {
        if (!Marks.TryGetValue(owner, out var mark))
        {
            mark = new Mark();
            Marks.Add(owner, mark);
        }
        mark.SeenFrame = Time.frameCount;
        try
        {
            var intervals = FindIntervals(route, bodies);
            EnsureRenderers(owner, mark, intervals.Count);
            for (var index = 0; index < mark.Lines.Count; index++)
            {
                var active = index < intervals.Count;
                mark.Lines[index].gameObject.SetActive(active);
                mark.Wings[index].gameObject.SetActive(active);
                if (!active) continue;
                SetInterval(mark.Lines[index], mark.Wings[index], intervals[index]);
            }
        }
        catch (Exception exception)
        {
            Hide(mark);
            if (!mark.Warned)
            {
                mark.Warned = true;
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Flying-passage visual: " + exception);
            }
        }
    }

    private static void EnsureRenderers(Component owner, Mark mark, int count)
    {
        if (mark.Material == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            if (shader != null && shader.isSupported)
            {
                mark.Material = new Material(shader) { hideFlags = HideFlags.DontSave, color = Color.white };
                mark.OwnsMaterial = true;
            }
            else
                mark.Material = PathVisualizer.Instance?.m_Renderer?.sharedMaterial;
            if (mark.Material == null) throw new InvalidOperationException("No usable line material is available.");
        }
        while (mark.Lines.Count < count)
        {
            mark.Lines.Add(CreateRenderer(owner, mark.Material, "Flying Passage", 0.09f));
            mark.Wings.Add(CreateRenderer(owner, mark.Material, "Flying Passage Wing", 0.075f));
        }
    }

    private static LineRenderer CreateRenderer(Component owner, Material material, string name, float width)
    {
        var obj = new GameObject("ArdubsEnemyIntelligence " + name) { hideFlags = HideFlags.DontSave };
        obj.transform.SetParent(owner.transform, false);
        obj.layer = PathVisualizer.Instance?.m_Renderer?.gameObject.layer ?? 0;
        var renderer = obj.AddComponent<LineRenderer>();
        renderer.sharedMaterial = material;
        renderer.useWorldSpace = true;
        renderer.widthCurve = AnimationCurve.Constant(0, 1, 1);
        renderer.widthMultiplier = width;
        renderer.startColor = PassageColor;
        renderer.endColor = PassageColor;
        renderer.numCornerVertices = 3;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static void SetInterval(LineRenderer line, LineRenderer wing, Interval interval)
    {
        var start = interval.Start + Vector3.up * GroundOffset;
        var end = interval.End + Vector3.up * GroundOffset;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        var direction = end - start;
        direction.y = 0;
        if (direction.sqrMagnitude < 0.000001f) direction = Vector3.forward;
        direction.Normalize();
        var right = Vector3.Cross(Vector3.up, direction);
        var center = (start + end) * 0.5f + Vector3.up * 0.025f;
        var size = Mathf.Clamp(Vector3.Distance(start, end) * 0.22f, 0.18f, 0.42f);
        wing.positionCount = 5;
        wing.SetPositions(new[]
        {
            center - right * size + direction * size * 0.35f,
            center - right * size * 0.35f,
            center + direction * size * 0.28f,
            center + right * size * 0.35f,
            center + right * size + direction * size * 0.35f
        });
    }

    private static void Hide(Mark mark)
    {
        foreach (var renderer in mark.Lines) if (renderer != null) renderer.gameObject.SetActive(false);
        foreach (var renderer in mark.Wings) if (renderer != null) renderer.gameObject.SetActive(false);
    }

    private static void RemoveMark(Component owner, Mark mark)
    {
        Marks.Remove(owner);
        foreach (var renderer in mark.Lines) if (renderer != null) UnityEngine.Object.Destroy(renderer.gameObject);
        foreach (var renderer in mark.Wings) if (renderer != null) UnityEngine.Object.Destroy(renderer.gameObject);
        if (mark.OwnsMaterial && mark.Material != null) UnityEngine.Object.Destroy(mark.Material);
    }
}
