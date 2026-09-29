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
    /// depth (<see cref="SurfaceDepthMaterials"/>), so grids and objects standing on it are not cut.</item>
    /// </list>
    /// Meshes are in world space under one root object at the world origin.
    /// </summary>
    internal sealed class RoomSurfaceView
    {
        readonly Material fillMaterial;
        readonly Material outlineMaterial;
        readonly Material occluderMaterial;
        readonly SurfaceDepthMaterials depthMaterials;
        readonly Dictionary<int, LevelView> views = new Dictionary<int, LevelView>();
        readonly List<int> staleIds = new List<int>();
        GameObject root;
        bool visible = true;

        public RoomSurfaceView(Material fill, Material outline, Material occluder, Material surfaceDepth)
        {
            fillMaterial = fill;
            outlineMaterial = outline;
            occluderMaterial = occluder;
            depthMaterials = new SurfaceDepthMaterials(surfaceDepth);
        }

        public bool HasOccluderMaterial => occluderMaterial != null;

        public int LevelCount => views.Count;

        public void Show(int levelId, SurfaceMeshData data)
        {
            if (!views.TryGetValue(levelId, out var view))
            {
                view = new LevelView(Root.transform, levelId, this);
                views.Add(levelId, view);
            }

            view.Apply(data);
            view.SetVisible(visible);
        }

        /// <summary>Removes levels that no longer exist (merged or reset).</summary>
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
                views[id].Destroy(depthMaterials);
                views.Remove(id);
            }
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
                view.Destroy(depthMaterials);
            }

            views.Clear();
        }

        public void Dispose()
        {
            Clear();
            depthMaterials.Dispose();
            ObjectUtility.SafeDestroy(root);
            root = null;
        }

        GameObject Root
        {
            get
            {
                if (root == null)
                {
                    root = new GameObject("Room Surfaces (scanned)");
                }

                return root;
            }
        }

        sealed class LevelView
        {
            readonly GameObject owner;
            readonly Part fill;
            readonly Part outline;
            readonly Part occluder;
            readonly Part depthMark;
            readonly Part depthWrite;
            readonly int depthSlot;

            public LevelView(Transform parent, int id, RoomSurfaceView surfaceView)
            {
                owner = new GameObject("Surface " + id);
                owner.transform.SetParent(parent, false);
                fill = new Part(owner.transform, "Fill", surfaceView.fillMaterial, null);
                outline = new Part(owner.transform, "Outline", surfaceView.outlineMaterial, null);
                if (surfaceView.occluderMaterial != null)
                {
                    occluder = new Part(owner.transform, "Occluder", surfaceView.occluderMaterial, null);
                }

                depthSlot = surfaceView.depthMaterials.Acquire();
                if (depthSlot >= 0)
                {
                    // Both steps draw the fill mesh itself.
                    depthMark = new Part(owner.transform, "Depth Mark", surfaceView.depthMaterials.Mark(depthSlot), fill.Mesh);
                    depthWrite = new Part(owner.transform, "Depth Write", surfaceView.depthMaterials.Write(depthSlot), fill.Mesh);
                }
            }

            public void Apply(SurfaceMeshData data)
            {
                fill.Apply(data.FillVertices, data.FillTriangles);
                outline.Apply(data.OutlineVertices, data.OutlineTriangles);
                occluder?.Apply(data.OccluderVertices, data.OccluderTriangles);
                depthMark?.RefreshShared();
                depthWrite?.RefreshShared();
            }

            public void SetVisible(bool value)
            {
                fill.SetVisible(value);
                outline.SetVisible(value);
            }

            public void Destroy(SurfaceDepthMaterials depthMaterials)
            {
                fill.Destroy();
                outline.Destroy();
                occluder?.Destroy();
                depthMaterials.Release(depthSlot);
                ObjectUtility.SafeDestroy(owner);
            }
        }

        sealed class Part
        {
            readonly bool ownsMesh;
            readonly MeshRenderer renderer;

            /// <param name="sharedMesh">Another part's mesh to draw, or null for a mesh of its own.</param>
            public Part(Transform parent, string name, Material material, Mesh sharedMesh)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                ownsMesh = sharedMesh == null;
                Mesh = sharedMesh != null ? sharedMesh : new Mesh { name = parent.name + " " + name };
                if (ownsMesh)
                {
                    Mesh.MarkDynamic();
                }

                go.GetComponent<MeshFilter>().sharedMesh = Mesh;
                renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            public Mesh Mesh { get; }

            public void Apply(List<Vector3> vertices, List<int> triangles)
            {
                Mesh.Clear();
                Mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                Mesh.SetVertices(vertices);
                Mesh.SetTriangles(triangles, 0, false);
                Mesh.RecalculateBounds();
                RefreshShared();
            }

            /// <summary>Draws only while the mesh has geometry and there is a material.</summary>
            public void RefreshShared()
            {
                renderer.enabled = Mesh.vertexCount > 0 && renderer.sharedMaterial != null;
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
        }
    }
}
