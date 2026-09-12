using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AlpineTuning
{
    /// <summary>Reversible black closure plate and double-sided body material projection.</summary>
    internal sealed class AlpineHeadlightDeleteProjection
    {
        private readonly List<RendererSnapshot> _renderers = new List<RendererSnapshot>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private GameObject _plate;
        private int _controllerId = int.MinValue;

        private sealed class RendererSnapshot
        {
            public Renderer renderer;
            public Material[] materials;
        }

        internal bool Install(SnowmobileController controller, IEnumerable<GameObject> headlightObjects, out string reason)
        {
            return Install(controller != null ? controller.transform : null, headlightObjects, out reason);
        }

        internal bool Install(Transform root, IEnumerable<GameObject> headlightObjects, out string reason)
        {
            reason = null;
            if (root == null)
            {
                reason = "No live sled is available for the headlight delete.";
                return false;
            }
            int id = root.GetInstanceID();
            if (_plate != null && _controllerId == id)
                return true;
            Restore();

            List<GameObject> targets = (headlightObjects ?? Enumerable.Empty<GameObject>())
                .Where(item => item != null).Distinct().ToList();
            if (targets.Count == 0)
            {
                reason = "No validated native headlight housing was found.";
                return false;
            }

            try
            {
                _controllerId = id;
                BuildPlate(root, targets);
                MakeOpaqueBodyDoubleSided(root, targets);
                return _plate != null;
            }
            catch (Exception ex)
            {
                reason = "Headlight closure installation failed safely (" + ex.GetType().Name + ").";
                Restore();
                return false;
            }
        }

        internal void Restore()
        {
            foreach (RendererSnapshot snapshot in _renderers)
            {
                if (snapshot?.renderer != null && snapshot.materials != null)
                    snapshot.renderer.sharedMaterials = snapshot.materials;
            }
            _renderers.Clear();
            foreach (UnityEngine.Object owned in _owned)
            {
                if (owned != null)
                    UnityEngine.Object.Destroy(owned);
            }
            _owned.Clear();
            _plate = null;
            _controllerId = int.MinValue;
        }

        private void BuildPlate(Transform sledRoot, List<GameObject> targets)
        {
            MeshFilter sourceFilter = targets.SelectMany(target =>
                    target.GetComponentsInChildren<MeshFilter>(true))
                .FirstOrDefault(filter => filter != null && filter.sharedMesh != null);

            _plate = new GameObject("Alpine Carbon Headlight Delete Plate");
            _plate.transform.SetParent(sourceFilter != null ? sourceFilter.transform.parent : sledRoot, false);
            Mesh mesh;
            if (sourceFilter != null)
            {
                _plate.transform.localPosition = sourceFilter.transform.localPosition;
                _plate.transform.localRotation = sourceFilter.transform.localRotation;
                _plate.transform.localScale = sourceFilter.transform.localScale;
                mesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
                mesh.name = sourceFilter.sharedMesh.name + " Alpine Closure";
            }
            else
            {
                Bounds worldBounds = AggregateBounds(targets);
                _plate.transform.position = worldBounds.center + sledRoot.forward * 0.003f;
                _plate.transform.rotation = Quaternion.LookRotation(sledRoot.forward, sledRoot.up);
                _plate.transform.SetParent(sledRoot, true);
                mesh = CreatePlateMesh(Mathf.Max(0.08f, worldBounds.size.x), Mathf.Max(0.04f, worldBounds.size.y));
            }

            var plateFilter = _plate.AddComponent<MeshFilter>();
            plateFilter.sharedMesh = mesh;
            var renderer = _plate.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreatePlateMaterial();
            _owned.Add(mesh);
            _owned.Add(renderer.sharedMaterial);
            _owned.Add(_plate);
        }

        private void MakeOpaqueBodyDoubleSided(Transform root, List<GameObject> targets)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.gameObject == _plate || IsExcludedRenderer(renderer, targets))
                    continue;
                Material[] originals = renderer.sharedMaterials;
                if (originals == null || originals.Length == 0)
                    continue;
                Material[] replacements = new Material[originals.Length];
                bool changed = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    if (!IsOpaqueBodyMaterial(source))
                    {
                        replacements[i] = source;
                        continue;
                    }
                    Material clone = new Material(source) { name = source.name + " Alpine Double Sided" };
                    if (clone.HasProperty("_Cull")) clone.SetFloat("_Cull", 0f);
                    if (clone.HasProperty("_CullMode")) clone.SetFloat("_CullMode", 0f);
                    if (clone.HasProperty("_RenderFace")) clone.SetFloat("_RenderFace", 0f);
                    replacements[i] = clone;
                    _owned.Add(clone);
                    changed = true;
                }
                if (!changed)
                    continue;
                _renderers.Add(new RendererSnapshot { renderer = renderer, materials = originals });
                renderer.sharedMaterials = replacements;
            }
        }

        private static bool IsExcludedRenderer(Renderer renderer, List<GameObject> targets)
        {
            string name = renderer.name.ToLowerInvariant();
            if (name.Contains("glass") || name.Contains("decal") || name.Contains("track") ||
                name.Contains("light") || name.Contains("lens") || name.Contains("lamp"))
                return true;
            return targets.Any(target => renderer.transform.IsChildOf(target.transform));
        }

        private static bool IsOpaqueBodyMaterial(Material material)
        {
            if (material == null || material.renderQueue > 2500)
                return false;
            string name = material.name.ToLowerInvariant();
            return !name.Contains("glass") && !name.Contains("decal") && !name.Contains("transparent") &&
                   !name.Contains("track") && !name.Contains("light");
        }

        private static Material CreatePlateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "Alpine Headlight Delete Black Metal" };
            material.color = new Color(0.012f, 0.014f, 0.017f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", material.color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.85f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.4f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.4f);
            material.renderQueue = 2000;
            return material;
        }

        private static Bounds AggregateBounds(List<GameObject> targets)
        {
            Renderer first = targets.SelectMany(item => item.GetComponentsInChildren<Renderer>(true)).FirstOrDefault();
            Bounds bounds = first != null ? first.bounds : new Bounds(targets[0].transform.position, new Vector3(0.3f, 0.12f, 0.02f));
            foreach (Renderer renderer in targets.SelectMany(item => item.GetComponentsInChildren<Renderer>(true)))
                if (renderer != null) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Mesh CreatePlateMesh(float width, float height)
        {
            float x = width * 0.5f;
            float y = height * 0.5f;
            var mesh = new Mesh { name = "Alpine Generated Headlight Closure" };
            mesh.vertices = new[]
            {
                new Vector3(-x, -y, 0f), new Vector3(x, -y, 0f),
                new Vector3(x, y, 0f), new Vector3(-x, y, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
