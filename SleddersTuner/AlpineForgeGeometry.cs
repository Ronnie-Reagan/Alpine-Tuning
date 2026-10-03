using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AlpineTuning
{
    internal static class AlpineForgeGeometry
    {
        // Paint registrations are not assembly registrations. For example,
        // seatParts can contain belly panels and handlebarParts can contain hoods.
        internal static bool Matches(string name, SledForgeSlot slot)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            switch (slot)
            {
                case SledForgeSlot.RunningBoards:
                    return new[] { "running", "footboard", "footrest", "footwell", "astin", "jalkatuki" }.Any(value.Contains);
                case SledForgeSlot.Seat:
                    return new[] { "seat", "seating", "saddle", "penk", "sacsiege" }.Any(value.Contains);
                case SledForgeSlot.Bumper: return value.Contains("bumper") || value.Contains("bumber");
                case SledForgeSlot.Hood:
                    return new[] { "hood", "cowling", "frontbody", "sidepanel", "side_panel", "lateralpanel", "lateral_panel", "coverplate", "nosepanel", "centralpanel" }.Any(value.Contains);
                case SledForgeSlot.BodyShell:
                    return Matches(value, SledForgeSlot.Hood) || Matches(value, SledForgeSlot.Seat) ||
                        Matches(value, SledForgeSlot.RunningBoards) || value.Contains("body") || value.Contains("chassis") ||
                        value.Contains("tunnel") || value.Contains("belly");
                default: return false;
            }
        }

        internal static bool RearBumper(string name) =>
            (name ?? string.Empty).IndexOf("rear", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (name ?? string.Empty).IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool LowerDetail(string name) => Regex.IsMatch(name ?? string.Empty,
            @"(?:lod[_ ]?[1-9]|l[1-9]$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static Renderer[] HighestDetail(IEnumerable<Renderer> renderers)
        {
            Renderer[] all = renderers.Where(item => item != null).Distinct().ToArray();
            var excluded = new HashSet<Renderer>();
            foreach (LODGroup group in all.Select(item => item.GetComponentInParent<LODGroup>()).Where(item => item != null).Distinct())
            {
                LOD[] levels = group.GetLODs();
                int first = Array.FindIndex(levels, level => level.renderers.Any(all.Contains));
                if (first < 0) continue;
                foreach (LOD level in levels.Skip(first + 1)) excluded.UnionWith(level.renderers);
            }
            return all.Where(item => !excluded.Contains(item) && !LowerDetail(item.name)).ToArray();
        }

        internal static bool TryBounds(Transform frame, IEnumerable<Renderer> renderers, out Bounds bounds)
        {
            bounds = new Bounds();
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Bounds local = mesh.bounds;
                Matrix4x4 matrix = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!Finite(point)) return false;
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found && Finite(bounds.size) && bounds.size.sqrMagnitude > 0.000001f;
        }

        internal static Vector3 Mount(Bounds bounds, SledForgeSlot slot)
        {
            Vector3 mount = bounds.center;
            // Boards meet the front of the footwell; seats rest on the tunnel.
            if (slot == SledForgeSlot.RunningBoards) { mount.y = bounds.min.y; mount.z = bounds.max.z; }
            else if (slot == SledForgeSlot.Seat || slot == SledForgeSlot.Hood || slot == SledForgeSlot.BodyShell)
                mount.y = bounds.min.y;
            return mount;
        }

        internal static bool TryScale(Bounds source, Bounds donor, SledForgeSlot slot, out Vector3 scale)
        {
            scale = Vector3.one;
            if (!Finite(source.size) || !Finite(donor.size) || donor.size.x < 0.001f) return false;
            float width = source.size.x / donor.size.x;
            if (width < 0.5f || width > 2f) return false;
            scale = Vector3.one * width;
            if (slot == SledForgeSlot.RunningBoards)
            {
                if (donor.size.z < 0.001f) return false;
                float length = source.size.z / donor.size.z;
                if (length < 0.5f || length > 2f) return false;
                scale = new Vector3(width, 1f, length);
            }
            return true;
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
