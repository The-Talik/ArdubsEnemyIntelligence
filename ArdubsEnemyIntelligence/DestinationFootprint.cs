using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.TurnBasedMode;
using Kingmaker.UI;
using TurnBased.Controllers;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArdubsEnemyIntelligence;

// UI-only native renderers, never a component on an entity or a saved command.
internal static class DestinationFootprint
{
    internal const int Segments = 48;
    private const float GroundOffset = 0.05f;
    // Same ground/obstacle raycast mask as UnitEntityView.OnMovementStarted.
    private const int DestinationGroundMask = 2099457;
    private static readonly Color RingColor = new Color(0.35f, 0.9f, 1f, 0.95f);

    private sealed class Mark
    {
        internal LineRenderer? Circle;
        internal Material? OwnedMaterial;
        internal bool Failed;
        internal bool Warned;
        internal bool HasGeometry;
        internal Vector3 Center;
        internal float Radius;
    }

    private static readonly ConditionalWeakTable<Component, Mark> Marks =
        new ConditionalWeakTable<Component, Mark>();

    // World-space vertices are independent of the pin's rotation/scale.
    internal static Vector3[] RingPoints(Vector3 center, float radius)
    {
        if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), "Invalid destination footprint radius.");
        var points = new Vector3[Segments];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * (2 * Math.PI / points.Length);
            points[i] = new Vector3(center.x + radius * (float)Math.Cos(angle),
                center.y + GroundOffset, center.z + radius * (float)Math.Sin(angle));
        }
        return points;
    }

    private static void Hide(Component owner)
    {
        if (Marks.TryGetValue(owner, out var mark) && mark.Circle != null)
            mark.Circle.gameObject.SetActive(false);
    }

    private static bool CreateCircle(Component owner, Mark mark)
    {
        // An explicit line does not depend on an initialized selection prefab or
        // the screen-space decal registry. These shaders support vertex colors.
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
        Material? material = null;
        if (shader != null && shader.isSupported)
        {
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.color = Color.white;
            mark.OwnedMaterial = material;
        }
        else
        {
            // The game's visible movement line provides an existing material if
            // built-in shaders were stripped from this build.
            material = PathVisualizer.Instance?.m_Renderer?.sharedMaterial;
        }
        if (material == null)
        {
            if (!mark.Warned)
            {
                mark.Warned = true;
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Warning("Destination footprint: no usable outline material yet; retrying when the movement preview initializes.");
            }
            return false;
        }
        var obj = new GameObject("ArdubsEnemyIntelligence Destination Footprint");
        obj.SetActive(false);
        obj.hideFlags = HideFlags.DontSave;
        // GUI pin objects can be on a layer excluded by the world camera.
        // Use the visible native path line's layer, or the normal world layer.
        obj.layer = PathVisualizer.Instance?.m_Renderer?.gameObject.layer ?? 0;
        obj.transform.SetParent(owner.transform, false);
        mark.Circle = obj.AddComponent<LineRenderer>();
        mark.Circle.sharedMaterial = material;
        mark.Circle.useWorldSpace = true;
        mark.Circle.loop = true;
        mark.Circle.widthCurve = AnimationCurve.Constant(0, 1, 1);
        mark.Circle.widthMultiplier = 0.04f;
        mark.Circle.startColor = RingColor;
        mark.Circle.endColor = RingColor;
        mark.Circle.numCornerVertices = 2;
        mark.Circle.shadowCastingMode = ShadowCastingMode.Off;
        mark.Circle.receiveShadows = false;
        ArdubsEnemyIntelligenceMain.ModEntry.Logger.Log("Destination footprint: initialized outline for " +
            owner.GetType().Name + " using " + material.shader.name + ".");
        return true;
    }

    private static void Refresh(Component owner, UnitEntityData? unit, Vector3 center, bool visible)
    {
        // Remove the UI-only outline as soon as combat leaves turn-based mode.
        if (!CombatController.IsInTurnBasedCombat())
        {
            Remove(owner);
            return;
        }
        if (!ArdubsEnemyIntelligenceMain.Settings.ModEnabled || !ArdubsEnemyIntelligenceMain.Settings.ShowDestinationFootprint || !visible || unit?.View == null)
        {
            Hide(owner);
            return;
        }
        var mark = Marks.GetValue(owner, _ => new Mark());
        if (mark.Failed) return;
        try
        {
            // Rider and mount share the mount's physical footprint.
            var mover = unit.RiderPart?.SaddledUnit ?? unit;
            if (mover.View == null) { Hide(owner); return; }
            if (mark.Circle == null && !CreateCircle(owner, mark)) return;
            var radius = mover.View.AgentASP.Corpulence;
            if (!mark.HasGeometry || mark.Center != center || mark.Radius != radius)
            {
                var points = RingPoints(center, radius);
                var height = radius + 1;
                for (var i = 0; i < points.Length; i++)
                {
                    // Conform to nearby ground; don't repeatedly raycast unchanged pins.
                    var ray = new Ray(new Vector3(points[i].x, center.y + height, points[i].z), Vector3.down);
                    if (Physics.Raycast(ray, out var hit, 2 * height, DestinationGroundMask))
                        points[i].y = hit.point.y + GroundOffset;
                }
                mark.Circle!.positionCount = points.Length;
                mark.Circle.SetPositions(points);
                mark.Center = center;
                mark.Radius = radius;
                mark.HasGeometry = true;
            }
            mark.Circle!.gameObject.SetActive(true);
        }
        catch (Exception exception)
        {
            mark.Failed = true;
            Hide(owner);
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Destination footprint (" + owner.GetType().Name + "): " + exception);
        }
    }

    internal static void RefreshAll(ClickPointerManager manager)
    {
        foreach (var pair in manager.m_PreviewUnitMarks)
            if (pair.Value != null) Refresh(pair.Value, pair.Key, pair.Value.transform.position, pair.Value.IsVisible);
        foreach (var pair in manager.m_UnitMarks)
            if (pair.Value != null) Refresh(pair.Value, pair.Key, pair.Value.transform.position, pair.Value.IsVisible);
        // The existing UI update runs whether or not any click pins exist.
        // This also covers the independent turn-based cursor preview.
        if (PathVisualizer.Instance != null) RefreshPreview(PathVisualizer.Instance);
    }

    private static void RefreshPreview(PathVisualizer preview)
    {
        // Turn-based hover previews do not require a ClickPointerPrefab. Use the
        // native simulated endpoint, not the raw cursor or another selected unit.
        var visible = preview.m_CurrentPath != null && preview.m_Renderer != null &&
            preview.m_Renderer.enabled && preview.m_Renderer.positionCount > 1 &&
            preview.m_VisualPath.Count > 1 && preview.m_LastUpdateData != null;
        var unit = visible ? preview.m_LastUpdateData!.Settings.Unit : null;
        var center = visible ? preview.GetLastPathElement().Position : Vector3.zero;
        Refresh(preview, unit, center, visible);
    }

    private static void Remove(Component owner)
    {
        if (ReferenceEquals(owner, null) || !Marks.TryGetValue(owner, out var mark)) return;
        Marks.Remove(owner);
        if (mark.Circle != null)
        {
            mark.Circle.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(mark.Circle.gameObject);
        }
        if (mark.OwnedMaterial != null) UnityEngine.Object.Destroy(mark.OwnedMaterial);
    }

    [HarmonyPatch(typeof(ClickPointerManager), "Update")]
    private static class Update
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state) => __state = PerformanceDiagnostics.Start();

        [HarmonyPostfix]
        private static void Postfix(ClickPointerManager __instance, long __state)
        {
            var start = PerformanceDiagnostics.Start();
            try { RefreshAll(__instance); }
            finally
            {
                PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.Footprint, start);
                PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.CursorUpdate, __state);
            }
        }
    }

    [HarmonyPatch(typeof(ClickPointerManager), "OnDisable")]
    private static class Cleanup
    {
        [HarmonyPostfix]
        private static void Postfix(ClickPointerManager __instance)
        {
            foreach (var pointer in __instance.m_PreviewUnitMarks.Values) Remove(pointer);
            foreach (var pointer in __instance.m_UnitMarks.Values) Remove(pointer);
        }
    }

    [HarmonyPatch(typeof(PathVisualizer), "OnDestroy")]
    private static class PreviewCleanup
    {
        [HarmonyPostfix]
        private static void Postfix(PathVisualizer __instance) => Remove(__instance);
    }
}
