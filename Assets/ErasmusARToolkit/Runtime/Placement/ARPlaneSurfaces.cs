using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Keeps detected AR planes tidy. ARCore often reports one real floor as several overlapping planes
    /// a few centimeters apart. This component groups them into surfaces (<see cref="PlaneGrouping"/>),
    /// gives each surface a stencil bit so the "Erasmus AR Toolkit/AR Surface" shader draws it once
    /// (no darker overlaps, no outlines inside the surface), and knows each surface's shared height
    /// so placed objects sit at the same level. Planes are shown and hidden without being deactivated,
    /// so their meshes and colliders stay up to date.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARPlaneManager))]
    public sealed class ARPlaneSurfaces : MonoBehaviour
    {
        const float RecomputeIntervalSeconds = 0.25f;

        static readonly int StencilBitId = Shader.PropertyToID("_StencilBit");
        static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");
        static readonly int BaseMapTilingId = Shader.PropertyToID("_BaseMap_ST");

        /// <summary>Distance between the grid lines (meters) the plane materials are made for.</summary>
        const float MaterialGridCellSize = 0.25f;

        readonly PlaneGrouping grouping = new PlaneGrouping();
        readonly List<ARPlane> planes = new List<ARPlane>();
        readonly List<PlaneFootprint> footprints = new List<PlaneFootprint>();
        readonly Dictionary<TrackableId, float> surfaceHeights = new Dictionary<TrackableId, float>();

        // Index 0 is unused (no stencil = the prefab's own material); index k + 1 is stencil bit k.
        readonly Material[] fillMaterials = new Material[PlaneGrouping.StencilBitCount + 1];
        readonly Material[] edgeMaterials = new Material[PlaneGrouping.StencilBitCount + 1];

        ARPlaneManager planeManager;
        Material baseFill;
        Material baseEdge;
        MaterialPropertyBlock tiling;
        float gridCellSize = MaterialGridCellSize;
        bool visible = true;
        bool suppressed;
        bool dirty;
        float nextRecompute;

        public bool Visible => visible;

        /// <summary>The room scan draws its own surfaces, so the planes are hidden whatever <see cref="Visible"/> says.</summary>
        internal bool IsSuppressed => suppressed;

        /// <summary>Number of real surfaces found in the last update.</summary>
        public int SurfaceCount => grouping.GroupCount;

        void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
            FindBaseMaterials();
        }

        void OnEnable()
        {
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            dirty = true;
        }

        void OnDisable()
        {
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
        }

        void OnDestroy()
        {
            DestroyAll(fillMaterials);
            DestroyAll(edgeMaterials);
        }

        void Update()
        {
            if (dirty && Time.unscaledTime >= nextRecompute)
            {
                Recompute();
            }
        }

        /// <summary>Shows or hides every plane. Hidden planes keep tracking and keep their colliders.</summary>
        public void SetVisible(bool value)
        {
            visible = value;
            ApplyVisibilityToAll();
        }

        /// <summary>
        /// Distance between the grid lines on the planes, in meters. The plane material itself is made
        /// for 0.25 m and is not changed: other distances are set per plane.
        /// </summary>
        public void SetGridCellSize(float meters)
        {
            gridCellSize = Mathf.Clamp(meters, 0.1f, 1f);
            if (planeManager == null)
            {
                return;
            }

            foreach (var plane in planeManager.trackables)
            {
                ApplyGridCellSize(plane);
            }
        }

        void ApplyGridCellSize(ARPlane plane)
        {
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                return;
            }

            if (Mathf.Approximately(gridCellSize, MaterialGridCellSize))
            {
                meshRenderer.SetPropertyBlock(null);
                return;
            }

            tiling ??= new MaterialPropertyBlock();
            tiling.SetVector(BaseMapTilingId, new Vector4(1f / gridCellSize, 1f / gridCellSize, 0f, 0f));
            meshRenderer.SetPropertyBlock(tiling);
        }

        /// <summary>
        /// Hides the planes while the room scan draws its own surfaces (see <see cref="RoomScanner"/>),
        /// whatever <see cref="Visible"/> says. They keep tracking and still feed the scan.
        /// </summary>
        public void SetSuppressed(bool value)
        {
            suppressed = value;
            ApplyVisibilityToAll();
        }

        void ApplyVisibilityToAll()
        {
            if (planeManager == null)
            {
                return;
            }

            foreach (var plane in planeManager.trackables)
            {
                ApplyVisibility(plane);
            }
        }

        /// <summary>Shared height of the surface the plane belongs to (false for planes not grouped yet).</summary>
        public bool TryGetSurfaceHeight(TrackableId planeId, out float height)
        {
            return surfaceHeights.TryGetValue(planeId, out height);
        }

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> changes)
        {
            foreach (var plane in changes.added)
            {
                ShowOnlyWhileTracked(plane);
                ApplyVisibility(plane);
                ApplyGridCellSize(plane);
            }

            foreach (var plane in changes.updated)
            {
                // ARCore may change its mind about what a plane is.
                if (!plane.alignment.IsHorizontal())
                {
                    ApplyVisibility(plane);
                }
            }

            dirty = true;
            if (changes.added.Count > 0)
            {
                // Group new planes before they are first drawn, so they never show unmerged.
                Recompute();
            }
        }

        void Recompute()
        {
            dirty = false;
            nextRecompute = Time.unscaledTime + RecomputeIntervalSeconds;
            planes.Clear();
            footprints.Clear();
            surfaceHeights.Clear();
            foreach (var plane in planeManager.trackables)
            {
                if (plane.subsumedBy != null || !plane.alignment.IsHorizontal())
                {
                    continue;
                }

                planes.Add(plane);
                footprints.Add(PlaneFootprint.FromBoundary(
                    plane.transform.localToWorldMatrix, plane.boundary, plane.size.x * plane.size.y));
            }

            grouping.Compute(footprints);
            for (var i = 0; i < planes.Count; i++)
            {
                var group = grouping.GroupOf(i);
                surfaceHeights[planes[i].trackableId] = grouping.HeightOf(group);
                ApplyStencil(planes[i], grouping.StencilBitOf(group));
            }
        }

        void ApplyStencil(ARPlane plane, int stencilBit)
        {
            SetMaterial(plane.GetComponent<MeshRenderer>(),
                GetMaterial(fillMaterials, baseFill, stencilBit, StencilOp.Replace));
            SetMaterial(plane.GetComponent<LineRenderer>(),
                GetMaterial(edgeMaterials, baseEdge, stencilBit, StencilOp.Keep));
        }

        /// <summary>
        /// A plane whose tracking is limited has an unreliable pose and would be drawn in a wrong place.
        /// Plane prefabs made by older toolkit versions still show such planes.
        /// </summary>
        static void ShowOnlyWhileTracked(ARPlane plane)
        {
            var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (visualizer != null && visualizer.trackingStateVisibilityThreshold != TrackingState.Tracking)
            {
                visualizer.trackingStateVisibilityThreshold = TrackingState.Tracking;
            }
        }

        void ApplyVisibility(ARPlane plane)
        {
            // forceRenderingOff, not enabled: ARPlaneMeshVisualizer sets enabled every frame.
            // Vertical planes are never drawn: the grid is laid out for floors and would show as streaks
            // on a wall. The room scan draws the walls it makes of them.
            var hidden = !visible || suppressed || !plane.alignment.IsHorizontal();
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.forceRenderingOff = hidden;
            }

            var lineRenderer = plane.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.forceRenderingOff = hidden;
            }
        }

        /// <summary>Reads the plane prefab's materials. Materials without the toolkit plane shader are left alone.</summary>
        void FindBaseMaterials()
        {
            var prefab = planeManager.planePrefab;
            if (prefab == null)
            {
                return;
            }

            baseFill = UsableMaterial(prefab.GetComponent<MeshRenderer>());
            baseEdge = UsableMaterial(prefab.GetComponent<LineRenderer>());
        }

        static Material UsableMaterial(Renderer renderer)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            return material != null && material.HasProperty(StencilBitId) ? material : null;
        }

        /// <summary>The material for a stencil bit, made once from <paramref name="source"/>. Bit 0 = source itself.</summary>
        static Material GetMaterial(Material[] cache, Material source, int stencilBit, StencilOp pass)
        {
            var slot = SlotOf(stencilBit);
            if (source == null || slot == 0)
            {
                return source;
            }

            if (cache[slot] == null)
            {
                var material = new Material(source) { name = $"{source.name} (Surface {slot})" };
                material.SetFloat(StencilBitId, stencilBit);
                material.SetFloat(StencilCompId, (float)CompareFunction.NotEqual);
                material.SetFloat(StencilPassId, (float)pass);
                cache[slot] = material;
            }

            return cache[slot];
        }

        /// <summary>Cache slot of a stencil mask (1 → 1, 2 → 2, 4 → 3 ...), or 0 for no bit.</summary>
        static int SlotOf(int stencilBit)
        {
            for (var bit = 0; bit < PlaneGrouping.StencilBitCount; bit++)
            {
                if (stencilBit == 1 << bit)
                {
                    return bit + 1;
                }
            }

            return 0;
        }

        static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && material != null && renderer.sharedMaterial != material)
            {
                renderer.sharedMaterial = material;
            }
        }

        static void DestroyAll(Material[] materials)
        {
            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    Destroy(materials[i]);
                    materials[i] = null;
                }
            }
        }
    }
}
