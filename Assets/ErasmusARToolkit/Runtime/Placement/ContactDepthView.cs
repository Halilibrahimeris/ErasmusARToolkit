using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Keeps the real floor from cutting into the objects that stand on it. With AR occlusion the depth
    /// of the real world is in the depth buffer, and it is noisy: in patches the floor is measured
    /// closer than it is and hides the bottom of an object. The room scan steadies the depth of the
    /// surfaces it scanned (see <see cref="RoomSurfaceView"/>); this does the same under the objects that
    /// stand where nothing was scanned: a flat patch at the height the object was placed at, a little
    /// larger than the object, drawn into depth only ("Erasmus AR Toolkit/Surface Depth").
    /// <para>
    /// Only for AR with occlusion. In Desktop mode the camera picture is drawn later than these patches
    /// and would fail the depth test behind them.
    /// </para>
    /// </summary>
    internal sealed class ContactDepthView : IDisposable
    {
        /// <summary>The patch reaches this far (meters) past the object on every side.</summary>
        public const float Margin = 0.15f;

        /// <summary>Smallest side (meters) of a patch.</summary>
        public const float MinSize = 0.3f;

        /// <summary>Objects whose support heights differ by less than this (meters) share a patch mesh.</summary>
        public const float HeightStep = 0.01f;

        const string RootName = "Contact Depth";

        readonly SurfaceDepthMaterials materials;
        readonly List<Group> groups = new List<Group>();
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();
        GameObject root;

        public ContactDepthView(Material surfaceDepthMaterial)
        {
            materials = new SurfaceDepthMaterials(surfaceDepthMaterial, RenderOrder.ContactDepth,
                RenderOrder.ContactDepthSlots);
        }

        public bool IsAvailable => materials.IsAvailable;

        /// <summary>Heights that have a patch mesh right now.</summary>
        public int GroupCount
        {
            get
            {
                var count = 0;
                foreach (var group in groups)
                {
                    if (group.Patches > 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Patches drawn right now.</summary>
        public int PatchCount
        {
            get
            {
                var count = 0;
                foreach (var group in groups)
                {
                    count += group.Patches;
                }

                return count;
            }
        }

        /// <summary>Builds the patches for the objects as they stand now.</summary>
        /// <param name="coveredByScan">True for an object the room scan already steadies the depth under.</param>
        public void Refresh(IReadOnlyList<ToolkitObject> objects, Func<ToolkitObject, bool> coveredByScan)
        {
            foreach (var group in groups)
            {
                group.Begin();
            }

            if (materials.IsAvailable && objects != null)
            {
                for (var i = 0; i < objects.Count; i++)
                {
                    var target = objects[i];
                    if (target != null && target.gameObject.activeInHierarchy
                        && (coveredByScan == null || !coveredByScan(target)))
                    {
                        Add(target);
                    }
                }
            }

            foreach (var group in groups)
            {
                group.End(vertices, triangles);
            }
        }

        public void Clear()
        {
            Refresh(null, null);
        }

        public void Dispose()
        {
            foreach (var group in groups)
            {
                group.Destroy();
            }

            groups.Clear();
            materials.Dispose();
            ObjectUtility.SafeDestroy(root);
            root = null;
        }

        /// <summary>The rectangle (x and z in world space) a patch under an object covers.</summary>
        internal static Rect FootprintOf(Bounds bounds)
        {
            var width = Mathf.Max(bounds.size.x + 2f * Margin, MinSize);
            var depth = Mathf.Max(bounds.size.z + 2f * Margin, MinSize);
            return new Rect(bounds.center.x - width * 0.5f, bounds.center.z - depth * 0.5f, width, depth);
        }

        /// <summary>
        /// The height of what the object stands on: where it was placed. An object that was raised still
        /// stands above that spot.
        /// </summary>
        internal static float SupportHeightOf(ToolkitObject target)
        {
            var parent = target.transform.parent;
            return parent != null ? parent.position.y : target.transform.position.y;
        }

        void Add(ToolkitObject target)
        {
            var group = FindGroup(SupportHeightOf(target));
            if (group == null)
            {
                return;
            }

            target.GetComponentsInChildren(renderers);
            var bounds = new Bounds(target.transform.position, Vector3.zero);
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer is LineRenderer || renderer is ParticleSystemRenderer || !renderer.enabled)
                {
                    continue;
                }

                if (hasBounds)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
            }

            group.Add(FootprintOf(bounds));
        }

        /// <summary>The group of a height, made if there is a free slot for it. Null when all slots are taken.</summary>
        Group FindGroup(float height)
        {
            var step = Mathf.RoundToInt(height / HeightStep);
            Group free = null;
            foreach (var group in groups)
            {
                if (group.Patches > 0 && group.Step == step)
                {
                    return group;
                }

                if (group.Patches == 0 && free == null)
                {
                    free = group;
                }
            }

            if (free == null && groups.Count < materials.SlotCount)
            {
                if (root == null)
                {
                    root = new GameObject(RootName);
                }

                var slot = materials.Acquire();
                if (slot < 0)
                {
                    return null;
                }

                free = new Group(root.transform, materials, slot);
                groups.Add(free);
            }

            free?.Use(step, step * HeightStep);
            return free;
        }

        /// <summary>The patches of one height: one mesh, drawn by the three steps of a depth correction.</summary>
        sealed class Group
        {
            readonly GameObject owner;
            readonly Mesh mesh;
            readonly MeshRenderer[] steps = new MeshRenderer[3];
            readonly List<Rect> patches = new List<Rect>();

            public Group(Transform parent, SurfaceDepthMaterials materials, int slot)
            {
                owner = new GameObject("Contact " + slot);
                owner.transform.SetParent(parent, false);
                mesh = new Mesh { name = owner.name };
                mesh.MarkDynamic();
                steps[0] = AddStep("Depth Mark", materials.Mark(slot));
                steps[1] = AddStep("Depth Unmark", materials.Unmark(slot));
                steps[2] = AddStep("Depth Write", materials.Write(slot));
            }

            public int Step { get; private set; }

            public int Patches => patches.Count;

            public void Begin()
            {
                patches.Clear();
            }

            public void Use(int step, float height)
            {
                Step = step;
                owner.transform.position = new Vector3(0f, height, 0f);
            }

            public void Add(Rect patch)
            {
                patches.Add(patch);
            }

            public void End(List<Vector3> vertices, List<int> triangles)
            {
                vertices.Clear();
                triangles.Clear();
                foreach (var patch in patches)
                {
                    var start = vertices.Count;
                    vertices.Add(new Vector3(patch.xMin, 0f, patch.yMin));
                    vertices.Add(new Vector3(patch.xMin, 0f, patch.yMax));
                    vertices.Add(new Vector3(patch.xMax, 0f, patch.yMax));
                    vertices.Add(new Vector3(patch.xMax, 0f, patch.yMin));
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                }

                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
                foreach (var step in steps)
                {
                    step.enabled = patches.Count > 0;
                }
            }

            public void Destroy()
            {
                ObjectUtility.SafeDestroy(mesh);
                ObjectUtility.SafeDestroy(owner);
            }

            MeshRenderer AddStep(string name, Material material)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(owner.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
                return renderer;
            }
        }
    }
}
