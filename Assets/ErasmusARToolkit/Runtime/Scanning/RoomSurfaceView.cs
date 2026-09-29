using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Draws the scanned surfaces. Per level:
    /// <list type="bullet">
    /// <item>a grid fill and an outline ribbon (hidden with "Hide Planes"),</item>
    /// <item>for tables, an invisible depth-only occluder down to the floor,</item>
    /// <item>a depth correction that replaces noisy real-world depth on the surface with the surface's own
    /// depth (<see cref="SurfaceDepthMaterials"/>), so grids and objects standing on it are not cut. The
    /// floor gets one first, then the largest surfaces; the correction of a trusted floor covers the
    /// floor all around the camera.</item>
    /// </list>
    /// Meshes are in the space of the scan, under the root of its <see cref="MapFrame"/>, and lie at
    /// height 0 of their level's object: a height that changes moves the object and needs no new mesh.
    /// The grid is drawn in the space of the scan too (<see cref="SetGridFrame"/>).
    /// </summary>
    internal sealed class RoomSurfaceView : IRoomModelSink
    {
        /// <summary>
        /// Grids on a surface with a depth correction are pulled toward the camera by this fraction of
        /// the distance only: the depth under them is the surface's own, so real things in front of the
        /// grid (chair legs) hide it as they should.
        /// </summary>
        public const float SteadyBias = 0.004f;

        /// <summary>The top of an occluder lies this far (meters) below its surface, under the grid.</summary>
        public const float OccluderDrop = 0.015f;

        static readonly int GridFrameId = Shader.PropertyToID("_GridFrame");
        static readonly int DepthBiasId = Shader.PropertyToID("_DepthBias");
        static readonly int HeightToleranceId = Shader.PropertyToID("_HeightTolerance");
        static readonly int MaxDepthBiasId = Shader.PropertyToID("_MaxDepthBias");

        readonly Transform root;
        readonly Material fillMaterial;
        readonly Material outlineMaterial;
        readonly Material steadyFillMaterial;
        readonly Material steadyOutlineMaterial;
        readonly Material occluderMaterial;
        readonly SurfaceDepthMaterials depthMaterials;
        readonly Dictionary<int, LevelView> views = new Dictionary<int, LevelView>();
        readonly List<int> staleIds = new List<int>();
        readonly List<LevelView> ranked = new List<LevelView>();
        readonly List<Vector3> windowVertices = new List<Vector3>();
        readonly List<int> windowTriangles = new List<int>();
        Vector4 gridFrame = new Vector4(0f, 0f, 1f, 0f);
        Vector3 cameraPosition;
        bool visible = true;

        /// <param name="surfaceRoot">Parent of the drawn surfaces. It belongs to the scan, not to the view.</param>
        /// <param name="fill">Grid material of the planes. The view draws with copies of its own.</param>
        public RoomSurfaceView(Transform surfaceRoot, Material fill, Material outline, Material occluder,
            Material surfaceDepth)
        {
            root = surfaceRoot;
            fillMaterial = Copy(fill, "scan", false);
            outlineMaterial = Copy(outline, "scan", false);
            steadyFillMaterial = Copy(fill, "scan, steady", true);
            steadyOutlineMaterial = Copy(outline, "scan, steady", true);
            occluderMaterial = occluder;
            depthMaterials = new SurfaceDepthMaterials(surfaceDepth);
        }

        public bool HasOccluderMaterial => occluderMaterial != null;

        public bool WantsOccluders => occluderMaterial != null;

        public int LevelCount => views.Count;

        /// <summary>Surfaces that have a depth correction right now.</summary>
        public int DepthSlotsUsed => depthMaterials.UsedCount;

        /// <summary>The surface has a depth correction: what stands on it is not cut by depth noise.</summary>
        public bool HasDepthSlot(int levelId)
        {
            return views.TryGetValue(levelId, out var view) && view.DepthSlot >= 0;
        }

        /// <summary>
        /// Tells the grid where the space of the scan lies, so the grid lines keep their place on the
        /// surfaces when the scan moves or turns.
        /// </summary>
        public void SetGridFrame(Transform frame)
        {
            var position = frame.position;
            var yaw = frame.eulerAngles.y * Mathf.Deg2Rad;
            var value = new Vector4(position.x, position.z, Mathf.Cos(yaw), Mathf.Sin(yaw));
            if (value == gridFrame)
            {
                return;
            }

            gridFrame = value;
            ApplyGridFrame(fillMaterial);
            ApplyGridFrame(outlineMaterial);
            ApplyGridFrame(steadyFillMaterial);
            ApplyGridFrame(steadyOutlineMaterial);
        }

        /// <summary>Where the camera is, in the space of the scan: the steadied floor goes along with it.</summary>
        public void SetCamera(Vector3 mapPosition)
        {
            cameraPosition = mapPosition;
            foreach (var view in views.Values)
            {
                view.FollowCamera(mapPosition);
            }
        }

        public void Show(int levelId, SurfaceMeshData data)
        {
            if (root == null)
            {
                return;
            }

            GetView(levelId).Apply(data);
        }

        public void Place(int levelId, in LevelPlacement placement)
        {
            if (root == null)
            {
                return;
            }

            var view = GetView(levelId);
            view.Place(placement);
            view.FollowCamera(cameraPosition);
        }

        /// <summary>Removes levels that no longer exist (merged or reset), and hands out the depth corrections.</summary>
        public void Keep(ICollection<int> levelIds)
        {
            staleIds.Clear();
            foreach (var id in views.Keys)
            {
                if (!levelIds.Contains(id))
                {
                    staleIds.Add(id);
                }
            }

            foreach (var id in staleIds)
            {
                views[id].Destroy();
                views.Remove(id);
            }

            AssignDepthSlots();
        }

        /// <summary>Hides the grid and outline. Occluders and depth corrections stay: they are about the real world.</summary>
        public void SetVisible(bool value)
        {
            visible = value;
            foreach (var view in views.Values)
            {
                view.SetVisible(value);
            }
        }

        public void Clear()
        {
            foreach (var view in views.Values)
            {
                view.Destroy();
            }

            views.Clear();
        }

        public void Dispose()
        {
            Clear();
            depthMaterials.Dispose();
            ObjectUtility.SafeDestroy(fillMaterial);
            ObjectUtility.SafeDestroy(outlineMaterial);
            ObjectUtility.SafeDestroy(steadyFillMaterial);
            ObjectUtility.SafeDestroy(steadyOutlineMaterial);
        }

        LevelView GetView(int levelId)
        {
            if (!views.TryGetValue(levelId, out var view))
            {
                view = new LevelView(root, levelId, this);
                view.SetVisible(visible);
                views.Add(levelId, view);
            }

            return view;
        }

        /// <summary>
        /// There are fewer depth corrections than there can be surfaces. The floor comes first (objects
        /// stand on it most), then the largest surfaces. A surface keeps the slot it has while it is
        /// among them.
        /// </summary>
        void AssignDepthSlots()
        {
            ranked.Clear();
            foreach (var view in views.Values)
            {
                if (view.WantsDepth)
                {
                    ranked.Add(view);
                }
                else
                {
                    view.SetDepthSlot(-1);
                }
            }

            ranked.Sort(LevelView.ByRank);
            var slots = depthMaterials.IsAvailable ? depthMaterials.SlotCount : 0;
            for (var i = slots; i < ranked.Count; i++)
            {
                ranked[i].SetDepthSlot(-1);
            }

            for (var i = 0; i < ranked.Count && i < slots; i++)
            {
                if (ranked[i].DepthSlot < 0)
                {
                    ranked[i].SetDepthSlot(depthMaterials.Acquire());
                }
            }
        }

        static Material Copy(Material source, string suffix, bool steady)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new Material(source) { name = $"{source.name} ({suffix})" };
            if (steady && copy.HasProperty(DepthBiasId))
            {
                copy.SetFloat(DepthBiasId, SteadyBias);
                copy.SetFloat(HeightToleranceId, 0f);
                copy.SetFloat(MaxDepthBiasId, SteadyBias);
            }

            return copy;
        }

        void ApplyGridFrame(Material material)
        {
            if (material != null && material.HasProperty(GridFrameId))
            {
                material.SetVector(GridFrameId, gridFrame);
            }
        }

        sealed class LevelView
        {
            public static readonly System.Comparison<LevelView> ByRank = Compare;

            readonly RoomSurfaceView surfaceView;
            readonly GameObject owner;
            readonly int id;
            readonly Part fill;
            readonly Part outline;
            readonly Part occluder;
            readonly Mesh steadyMesh;
            Mesh windowMesh;
            Part depthMark;
            Part depthUnmark;
            Part depthWrite;
            Vector2 windowCenter;
            bool isFloor;
            bool wholeFloor;
            float area;

            public LevelView(Transform parent, int levelId, RoomSurfaceView view)
            {
                surfaceView = view;
                id = levelId;
                owner = new GameObject("Surface " + levelId);
                owner.transform.SetParent(parent, false);
                fill = new Part(owner.transform, "Fill", view.fillMaterial, null);
                outline = new Part(owner.transform, "Outline", view.outlineMaterial, null);
                if (view.occluderMaterial != null)
                {
                    occluder = new Part(owner.transform, "Occluder", view.occluderMaterial, null);
                    occluder.Transform.localPosition = new Vector3(0f, -OccluderDrop, 0f);
                    occluder.SetShown(false);
                }

                steadyMesh = new Mesh { name = owner.name + " Steady" };
                steadyMesh.MarkDynamic();
            }

            public int DepthSlot { get; private set; } = -1;

            /// <summary>There is something to steady: the surface has cells, or it is the floor all around.</summary>
            public bool WantsDepth => wholeFloor || steadyMesh.vertexCount > 0;

            public void Apply(SurfaceMeshData data)
            {
                fill.Apply(data.FillVertices, data.FillTriangles);
                outline.Apply(data.OutlineVertices, data.OutlineTriangles);
                occluder?.Apply(data.OccluderVertices, data.OccluderTriangles);
                Part.Fill(steadyMesh, data.SteadyVertices, data.SteadyTriangles);
                RefreshDepthParts();
            }

            public void Place(in LevelPlacement placement)
            {
                isFloor = placement.IsFloor;
                area = placement.Area;
                owner.transform.localPosition = new Vector3(0f, placement.Height, 0f);
                if (wholeFloor != placement.WholeFloor)
                {
                    wholeFloor = placement.WholeFloor;
                    RefreshDepthParts();
                }

                if (occluder == null)
                {
                    return;
                }

                // The walls of the occluder are one meter long and are scaled to reach the floor.
                var tall = placement.FloorHeight.HasValue && !placement.IsFloor
                    ? placement.Height - placement.FloorHeight.Value
                    : 0f;
                var shown = tall >= SurfaceMeshBuilder.MinOccluderHeight;
                occluder.SetShown(shown);
                if (shown)
                {
                    occluder.Transform.localScale = new Vector3(1f, tall - OccluderDrop, 1f);
                }
            }

            /// <summary>The steadied floor is a window around the camera; it moves on when the camera leaves its middle.</summary>
            public void FollowCamera(Vector3 mapPosition)
            {
                if (!wholeFloor)
                {
                    return;
                }

                var center = SurfaceMeshBuilder.FloorWindowCenter(mapPosition);
                if (windowMesh != null && Vector2.Distance(center, windowCenter) <= SurfaceMeshBuilder.FloorWindowReach)
                {
                    return;
                }

                if (windowMesh == null)
                {
                    windowMesh = new Mesh { name = owner.name + " Floor Window" };
                    windowMesh.MarkDynamic();
                }

                windowCenter = center;
                SurfaceMeshBuilder.BuildFloorWindow(center, surfaceView.windowVertices, surfaceView.windowTriangles);
                Part.Fill(windowMesh, surfaceView.windowVertices, surfaceView.windowTriangles);
                RefreshDepthParts();
            }

            public void SetDepthSlot(int slot)
            {
                if (slot == DepthSlot)
                {
                    return;
                }

                var materials = surfaceView.depthMaterials;
                materials.Release(DepthSlot);
                DepthSlot = slot;
                var steady = slot >= 0;
                fill.SetMaterial(steady ? surfaceView.steadyFillMaterial : surfaceView.fillMaterial);
                outline.SetMaterial(steady ? surfaceView.steadyOutlineMaterial : surfaceView.outlineMaterial);
                if (steady && depthMark == null)
                {
                    depthMark = new Part(owner.transform, "Depth Mark", null, steadyMesh);
                    depthUnmark = new Part(owner.transform, "Depth Unmark", null, steadyMesh);
                    depthWrite = new Part(owner.transform, "Depth Write", null, steadyMesh);
                }

                depthMark?.SetMaterial(steady ? materials.Mark(slot) : null);
                depthUnmark?.SetMaterial(steady ? materials.Unmark(slot) : null);
                depthWrite?.SetMaterial(steady ? materials.Write(slot) : null);
                RefreshDepthParts();
            }

            public void SetVisible(bool value)
            {
                fill.SetVisible(value);
                outline.SetVisible(value);
            }

            public void Destroy()
            {
                surfaceView.depthMaterials.Release(DepthSlot);
                DepthSlot = -1;
                fill.Destroy();
                outline.Destroy();
                occluder?.Destroy();
                ObjectUtility.SafeDestroy(steadyMesh);
                ObjectUtility.SafeDestroy(windowMesh);
                ObjectUtility.SafeDestroy(owner);
            }

            void RefreshDepthParts()
            {
                if (depthMark == null)
                {
                    return;
                }

                var mesh = wholeFloor && windowMesh != null ? windowMesh : steadyMesh;
                depthMark.SetMesh(mesh);
                depthUnmark.SetMesh(mesh);
                depthWrite.SetMesh(mesh);
            }

            static int Compare(LevelView a, LevelView b)
            {
                if (a.isFloor != b.isFloor)
                {
                    return a.isFloor ? -1 : 1;
                }

                var byArea = b.area.CompareTo(a.area);
                return byArea != 0 ? byArea : a.id.CompareTo(b.id);
            }
        }

        sealed class Part
        {
            readonly bool ownsMesh;
            readonly MeshFilter filter;
            readonly MeshRenderer renderer;
            bool shown = true;

            /// <param name="sharedMesh">Another part's mesh to draw, or null for a mesh of its own.</param>
            public Part(Transform parent, string name, Material material, Mesh sharedMesh)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                Transform = go.transform;
                ownsMesh = sharedMesh == null;
                Mesh = sharedMesh != null ? sharedMesh : new Mesh { name = parent.name + " " + name };
                if (ownsMesh)
                {
                    Mesh.MarkDynamic();
                }

                filter = go.GetComponent<MeshFilter>();
                filter.sharedMesh = Mesh;
                renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Refresh();
            }

            public Mesh Mesh { get; private set; }

            public Transform Transform { get; }

            public static void Fill(Mesh mesh, List<Vector3> vertices, List<int> triangles)
            {
                mesh.Clear();
                mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
            }

            public void Apply(List<Vector3> vertices, List<int> triangles)
            {
                Fill(Mesh, vertices, triangles);
                Refresh();
            }

            /// <summary>Draws another mesh (one it does not own).</summary>
            public void SetMesh(Mesh mesh)
            {
                if (!ownsMesh && Mesh != mesh)
                {
                    Mesh = mesh;
                    filter.sharedMesh = mesh;
                }

                Refresh();
            }

            public void SetMaterial(Material material)
            {
                if (renderer.sharedMaterial != material)
                {
                    renderer.sharedMaterial = material;
                }

                Refresh();
            }

            /// <summary>Switches the part off whatever it has to draw (an occluder of a surface that is too low).</summary>
            public void SetShown(bool value)
            {
                shown = value;
                Refresh();
            }

            public void SetVisible(bool value)
            {
                renderer.forceRenderingOff = !value;
            }

            public void Destroy()
            {
                if (ownsMesh)
                {
                    ObjectUtility.SafeDestroy(Mesh);
                }
            }

            /// <summary>Draws only while the mesh has geometry and there is a material.</summary>
            void Refresh()
            {
                renderer.enabled = shown && Mesh != null && Mesh.vertexCount > 0 && renderer.sharedMaterial != null;
            }
        }
    }
}
