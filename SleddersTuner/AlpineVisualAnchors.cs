using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlpineTuning
{
    internal enum AlpineVisualAnchorRole { LeftSki, RightSki, Handlebars }

    internal static class AlpineVisualAnchors
    {
        private static readonly HashSet<Transform> OwnedRoots = new HashSet<Transform>();
        private static readonly Dictionary<Renderer, AlpineVisibilityState> Visibility = new Dictionary<Renderer, AlpineVisibilityState>();

        internal static void Own(Transform root)
        {
            OwnedRoots.RemoveWhere(item => item == null);
            OwnedRoots.Add(root);
        }

        internal static void Disown(Transform root)
        {
            OwnedRoots.Remove(root);
            OwnedRoots.RemoveWhere(item => item == null);
        }
        internal static bool Projected(Renderer renderer) =>
            renderer != null && OwnedRoots.Any(root => Under(renderer.transform, root));

        internal static void Hide(Renderer renderer, object owner)
        {
            if (renderer == null) return;
            if (!Visibility.TryGetValue(renderer, out var state))
                Visibility.Add(renderer, state = new AlpineVisibilityState(renderer.enabled, renderer.forceRenderingOff));
            state.Acquire(owner);
            renderer.enabled = state.Enabled;
            renderer.forceRenderingOff = state.ForceRenderingOff;
        }

        internal static void Release(Renderer renderer, object owner)
        {
            // Unity's destroyed-object comparison is unsuitable for dictionary
            // identity; remove the entry even when the native renderer is gone.
            if (ReferenceEquals(renderer, null) || !Visibility.TryGetValue(renderer, out var state)) return;
            state.Release(owner);
            if (renderer != null)
            {
                renderer.enabled = state.Enabled;
                renderer.forceRenderingOff = state.ForceRenderingOff;
            }
            if (state.OwnerCount == 0) Visibility.Remove(renderer);
        }

        internal static Transform Native(SnowmobileStructure structure, AlpineVisualAnchorRole role)
        {
            if (structure == null) return null;
            switch (role)
            {
                case AlpineVisualAnchorRole.LeftSki: return SkiPivot(structure, structure.leftSki, structure.rightSki);
                case AlpineVisualAnchorRole.RightSki: return SkiPivot(structure, structure.rightSki, structure.leftSki);
                default:
                    Transform[] active = (structure.handlebars ?? new Transform[0])
                        .Where(item => item != null && item != structure.transform && ActiveBelow(item, structure.transform)).Distinct().ToArray();
                    // Multiple active pivot candidates are ambiguous; retain native.
                    return active.Length == 1 ? active[0] : null;
            }
        }

        private static Transform SkiPivot(SnowmobileStructure structure, Transform pivot, Transform opposite)
        {
            return pivot != structure.transform && Under(pivot, structure.transform) &&
                !Under(pivot, opposite) && !Under(opposite, pivot) ? pivot : null;
        }

        internal static bool ActiveBelow(Transform item, Transform root)
        {
            for (Transform current = item; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return Under(item, root);
        }

        internal static bool Under(Transform item, Transform root) =>
            item != null && root != null && (item == root || item.IsChildOf(root));

        internal static bool TryRole(string name, float localX, out AlpineVisualAnchorRole role)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            role = AlpineVisualAnchorRole.Handlebars;
            if (float.IsNaN(localX) || float.IsInfinity(localX)) return false;
            if (value.Contains("handlebar") || value.Contains("handle_bar")) return true;
            // Do not interpret skids, skinned meshes, Ski-Doo body names or a
            // generic 'bar' as a ski/handlebar anchor.
            if (value.Contains("skid") || value.Contains("skin") || value.Contains("skidoo") ||
                value.Contains("ski-doo")) return false;
            bool skiName = value.StartsWith("ski") || value.EndsWith("ski") ||
                new[] { "_ski", " ski", "-ski", ".ski", "ski_", "ski-", "ski.", "leftski", "rightski", "skileft", "skiright" }
                    .Any(token => value.Contains(token));
            if (!skiName) return false;
            bool left = value.Contains("left") || value.EndsWith("_l") || value.EndsWith(".l") || value.StartsWith("l_");
            bool right = value.Contains("right") || value.EndsWith("_r") || value.EndsWith(".r") || value.StartsWith("r_");
            if (left && right) return false;
            if (!left && !right)
            {
                if (Math.Abs(localX) < 0.02f) return false;
                left = localX < 0f;
            }
            role = left ? AlpineVisualAnchorRole.LeftSki : AlpineVisualAnchorRole.RightSki;
            return true;
        }

        internal static Transform Prop(GameObject prop, AlpineVisualAnchorRole role)
        {
            Transform root = prop.transform;
            Transform[] matches = root.GetComponentsInChildren<Transform>(true).Where(item => item != root &&
                ActiveBelow(item, root) && TryRole(item.name, root.InverseTransformPoint(item.position).x, out var found) &&
                found == role && item.GetComponentsInChildren<MeshRenderer>(true).Any()).ToArray();
            // Select the outer role-specific branch, retaining its authored pivot.
            Transform[] branches = matches.Where(item => !matches.Any(other => other != item && item.IsChildOf(other))).ToArray();
            return branches.Length == 1 ? branches[0] : null;
        }

        internal static bool Moving(Renderer renderer, SnowmobileStructure structure)
        {
            if (renderer == null || structure == null) return false;
            if (Under(renderer.transform, structure.leftSki) || Under(renderer.transform, structure.rightSki) ||
                Under(renderer.transform, structure.traxBody) ||
                (structure.handlebars ?? new Transform[0]).Any(anchor => Under(renderer.transform, anchor))) return true;
            for (Transform current = renderer.transform; current != null && current != structure.transform; current = current.parent)
            {
                string name = current.name.ToLowerInvariant();
                if (TryRole(name, structure.transform.InverseTransformPoint(current.position).x, out _) ||
                    name.Contains("track") || name.Contains("trax") ||
                    (name.Contains("skid") && !name.Contains("skidoo") && !name.Contains("ski-doo")) || name.Contains("spindle")) return true;
            }
            return false;
        }

        internal static IEnumerable<AlpineVisualAnchorRole> Roles(SledForgeSlot slot)
        {
            if (slot == SledForgeSlot.Skis)
            {
                yield return AlpineVisualAnchorRole.LeftSki;
                yield return AlpineVisualAnchorRole.RightSki;
            }
            else if (slot == SledForgeSlot.Handlebars) yield return AlpineVisualAnchorRole.Handlebars;
        }
    }

    internal sealed class AlpineVisibilityState
    {
        private readonly bool _original;
        private readonly bool _originalForceRenderingOff;
        private readonly HashSet<object> _owners = new HashSet<object>();
        internal AlpineVisibilityState(bool original, bool forceRenderingOff = false)
        { _original = original; _originalForceRenderingOff = forceRenderingOff; }
        internal int OwnerCount => _owners.Count;
        internal bool Enabled => _owners.Count == 0 && _original;
        internal bool ForceRenderingOff => _owners.Count > 0 || _originalForceRenderingOff;
        internal void Acquire(object owner) { if (owner != null) _owners.Add(owner); }
        internal void Release(object owner) { if (owner != null) _owners.Remove(owner); }
    }
}
