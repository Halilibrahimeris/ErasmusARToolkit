using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class PortalCrossingTests
    {
        static readonly Vector3 Eye = new Vector3(0f, 1.5f, 0f);

        [Test]
        public void WalkingThroughTheDoor_PutsYouInside_AndBackOut()
        {
            var crossing = new PortalCrossing(1.2f, 2.2f);
            crossing.Update(Eye + Vector3.forward * 2f);
            Assert.AreEqual(PortalState.Outside, crossing.State);
            Assert.IsTrue(crossing.Update(Eye + Vector3.back * 0.5f));
            Assert.AreEqual(PortalState.Inside, crossing.State);
            Assert.IsFalse(crossing.Update(Eye + Vector3.back * 3f + Vector3.right * 2f), "Walking around inside stays inside.");
            Assert.IsTrue(crossing.Update(Eye + Vector3.forward));
            Assert.AreEqual(PortalState.Outside, crossing.State);
        }

        [Test]
        public void WalkingAroundTheFrame_LeavesYouBehind()
        {
            var crossing = new PortalCrossing(1.2f, 2.2f);
            crossing.Update(Eye + new Vector3(2f, 0f, 1f));
            crossing.Update(Eye + new Vector3(2f, 0f, -1f));
            Assert.AreEqual(PortalState.Behind, crossing.State);
            crossing.Update(Eye + new Vector3(0f, 0f, -1f));
            Assert.AreEqual(PortalState.Behind, crossing.State, "Standing behind the door is not inside.");
            crossing.Update(Eye + new Vector3(0f, 0f, 1f));
            Assert.AreEqual(PortalState.Outside, crossing.State);
        }

        [Test]
        public void StartingBehind_IsBehind()
        {
            var crossing = new PortalCrossing(1.2f, 2.2f);
            crossing.Update(Eye + Vector3.back);
            Assert.AreEqual(PortalState.Behind, crossing.State);
        }

        [Test]
        public void GoingThroughAboveTheDoor_DoesNotCount()
        {
            var crossing = new PortalCrossing(1.2f, 2.2f);
            crossing.Update(new Vector3(0f, 3.5f, 1f));
            crossing.Update(new Vector3(0f, 3.5f, -1f));
            Assert.AreEqual(PortalState.Behind, crossing.State);
        }
    }

    public class PortalMeshesTests
    {
        [Test]
        public void Shell_LeavesTheDoorwayOpen()
        {
            var mesh = PortalMeshes.Shell(1.2f, 2.2f, new Vector3(6f, 3f, 6f));
            try
            {
                Assert.AreEqual(7 * 6, mesh.triangles.Length, "Three front pieces, back, two sides and the top.");
                Assert.IsFalse(RayHits(mesh, new Ray(new Vector3(0f, 1f, 1f), Vector3.back), 0.01f),
                    "Nothing covers the doorway from the front.");
                Assert.IsTrue(RayHits(mesh, new Ray(new Vector3(2f, 1f, 1f), Vector3.back), 0.01f), "Beside the door the shell hides the world.");
                Assert.AreEqual(-6f, mesh.bounds.min.z, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Sky_HasNoFront()
        {
            var mesh = PortalMeshes.Sky(new Vector3(6f, 3f, 6f));
            try
            {
                Assert.Less(mesh.bounds.max.z, 0f);
                Assert.AreEqual(4 * 6, mesh.triangles.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>The ray meets a triangle of the mesh within the first meter.</summary>
        static bool RayHits(Mesh mesh, Ray ray, float epsilon)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (var i = 0; i < t.Length; i += 3)
            {
                var plane = new Plane(v[t[i]], v[t[i + 1]], v[t[i + 2]]);
                if (!plane.Raycast(ray, out var distance) || distance > 1.5f)
                {
                    continue;
                }

                var p = ray.GetPoint(distance);
                if (InTriangle(p, v[t[i]], v[t[i + 1]], v[t[i + 2]], epsilon))
                {
                    return true;
                }
            }

            return false;
        }

        static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float epsilon)
        {
            var n = Vector3.Cross(b - a, c - a);
            var s1 = Vector3.Dot(Vector3.Cross(b - a, p - a), n);
            var s2 = Vector3.Dot(Vector3.Cross(c - b, p - b), n);
            var s3 = Vector3.Dot(Vector3.Cross(a - c, p - c), n);
            return (s1 >= -epsilon && s2 >= -epsilon && s3 >= -epsilon) || (s1 <= epsilon && s2 <= epsilon && s3 <= epsilon);
        }
    }

    public class PortalBuilderTests
    {
        static Material NoMaterial(string name, Color color) => null;

        [Test]
        public void Create_BuildsFrameAndAnExampleWorld()
        {
            var portal = PortalBuilder.Create(1.2f, 2.2f, new Vector3(6f, 3f, 6f), Color.magenta, Color.cyan, null,
                PlacementMode.TapToPlace, NoMaterial);
            try
            {
                Assert.IsNotNull(portal.InsideWorld);
                Assert.AreEqual(PortalBuilder.WorldName, portal.InsideWorld.name);
                Assert.IsTrue(portal.InsideWorld.GetComponentsInChildren<Renderer>().All(r => r.bounds.center.z < 0.01f),
                    "The world lies behind the door.");
                Assert.IsNotNull(portal.GetComponentInChildren<InfoCard>());
                Assert.AreEqual(3, portal.transform.Find("Frame").childCount);
                Assert.AreEqual(PlacementMode.TapToPlace, portal.GetComponent<ModulePlacement>().PlacementMode);
            }
            finally
            {
                Object.DestroyImmediate(portal.gameObject);
            }
        }
    }
}
