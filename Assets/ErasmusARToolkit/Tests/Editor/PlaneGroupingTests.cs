using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class PlaneGroupingTests
    {
        /// <summary>A square footprint of <paramref name="size"/> meters centered on (x, z).</summary>
        static PlaneFootprint Square(float height, float x, float z, float size)
        {
            var half = size * 0.5f;
            return new PlaneFootprint(height, new Rect(x - half, z - half, size, size), size * size);
        }

        static PlaneGrouping Compute(params PlaneFootprint[] planes)
        {
            var grouping = new PlaneGrouping();
            grouping.Compute(planes);
            return grouping;
        }

        [Test]
        public void Compute_OverlappingPlanesAFewCentimetersApart_AreOneSurface()
        {
            var grouping = Compute(Square(0f, 0f, 0f, 2f), Square(0.02f, 0.8f, 0.3f, 1f));

            Assert.AreEqual(1, grouping.GroupCount);
            Assert.AreEqual(grouping.GroupOf(0), grouping.GroupOf(1));
        }

        [Test]
        public void Compute_TableAboveFloor_IsSeparateSurface()
        {
            var grouping = Compute(Square(0f, 0f, 0f, 3f), Square(0.72f, 0f, 0f, 1f));

            Assert.AreEqual(2, grouping.GroupCount);
            Assert.AreNotEqual(grouping.GroupOf(0), grouping.GroupOf(1));
        }

        [Test]
        public void Compute_SameHeightButFarApart_AreSeparateSurfaces()
        {
            var grouping = Compute(Square(0f, 0f, 0f, 1f), Square(0f, 3f, 0f, 1f));

            Assert.AreEqual(2, grouping.GroupCount);
        }

        [Test]
        public void Compute_PlanesWithinMargin_AreOneSurface()
        {
            // 5 cm gap between the two footprints: inside the bounds margin.
            var grouping = Compute(Square(0f, 0f, 0f, 1f), Square(0f, 1.05f, 0f, 1f));

            Assert.AreEqual(1, grouping.GroupCount);
        }

        [Test]
        public void Compute_ChainOfOverlaps_JoinsAllPlanes()
        {
            // A touches B and B touches C, but A and C are far apart.
            var grouping = Compute(Square(0f, 0f, 0f, 1f), Square(0.01f, 0.9f, 0f, 1f), Square(0.02f, 1.8f, 0f, 1f));

            Assert.AreEqual(1, grouping.GroupCount);
            Assert.AreEqual(grouping.GroupOf(0), grouping.GroupOf(2));
        }

        [Test]
        public void Compute_SurfaceHeight_IsLargestMemberHeight()
        {
            var grouping = Compute(Square(0.03f, 0f, 0f, 0.5f), Square(0.01f, 0.2f, 0f, 2f));

            Assert.AreEqual(0.01f, grouping.HeightOf(grouping.GroupOf(0)), 1e-5f);
        }

        [Test]
        public void Compute_StencilBits_GoToLargestSurfacesFirst()
        {
            var grouping = Compute(Square(0f, 0f, 0f, 1f), Square(0f, 10f, 0f, 3f), Square(0f, 20f, 0f, 2f));

            Assert.AreEqual(1, grouping.StencilBitOf(grouping.GroupOf(1)));
            Assert.AreEqual(2, grouping.StencilBitOf(grouping.GroupOf(2)));
            Assert.AreEqual(4, grouping.StencilBitOf(grouping.GroupOf(0)));
        }

        [Test]
        public void Compute_MoreSurfacesThanStencilBits_SmallestGetNoBit()
        {
            var planes = new List<PlaneFootprint>();
            for (var i = 0; i <= PlaneGrouping.StencilBitCount; i++)
            {
                // Largest first, 5 m apart so each is its own surface.
                planes.Add(Square(0f, i * 5f, 0f, 2f - i * 0.1f));
            }

            var grouping = new PlaneGrouping();
            grouping.Compute(planes);

            Assert.AreEqual(PlaneGrouping.StencilBitCount + 1, grouping.GroupCount);
            var usedBits = 0;
            for (var i = 0; i < PlaneGrouping.StencilBitCount; i++)
            {
                var bit = grouping.StencilBitOf(grouping.GroupOf(i));
                Assert.AreEqual(0, usedBits & bit, "Each bit is used once.");
                usedBits |= bit;
            }

            Assert.AreEqual(0xFF, usedBits);
            Assert.AreEqual(0, grouping.StencilBitOf(grouping.GroupOf(PlaneGrouping.StencilBitCount)));
        }

        [Test]
        public void Compute_EmptyInput_HasNoGroups()
        {
            var grouping = Compute();

            Assert.AreEqual(0, grouping.GroupCount);
        }

        [Test]
        public void Compute_Recompute_ForgetsPreviousPlanes()
        {
            var grouping = new PlaneGrouping();
            grouping.Compute(new[] { Square(0f, 0f, 0f, 1f), Square(0.7f, 0f, 0f, 1f) });
            grouping.Compute(new[] { Square(0f, 0f, 0f, 1f) });

            Assert.AreEqual(1, grouping.GroupCount);
        }

        [Test]
        public void FromBoundary_UsesWorldPoseOfPlane()
        {
            // Plane 0.5 m up, turned 90 degrees: local x becomes world -z.
            var localToWorld = Matrix4x4.TRS(new Vector3(1f, 0.5f, 2f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            var boundary = new NativeArray<Vector2>(4, Allocator.Temp);
            try
            {
                boundary[0] = new Vector2(-1f, -0.25f);
                boundary[1] = new Vector2(1f, -0.25f);
                boundary[2] = new Vector2(1f, 0.25f);
                boundary[3] = new Vector2(-1f, 0.25f);

                var footprint = PlaneFootprint.FromBoundary(localToWorld, boundary, 1f);

                Assert.AreEqual(0.5f, footprint.Height, 1e-5f);
                Assert.AreEqual(1f, footprint.Area, 1e-5f);
                Assert.AreEqual(0.75f, footprint.Bounds.xMin, 1e-4f);
                Assert.AreEqual(1.25f, footprint.Bounds.xMax, 1e-4f);
                Assert.AreEqual(1f, footprint.Bounds.yMin, 1e-4f);
                Assert.AreEqual(3f, footprint.Bounds.yMax, 1e-4f);
            }
            finally
            {
                boundary.Dispose();
            }
        }

        [Test]
        public void FromBoundary_EmptyBoundary_IsPointAtPlaneCenter()
        {
            var localToWorld = Matrix4x4.Translate(new Vector3(1f, 0.2f, 3f));
            var boundary = new NativeArray<Vector2>(0, Allocator.Temp);
            try
            {
                var footprint = PlaneFootprint.FromBoundary(localToWorld, boundary, 0f);

                Assert.AreEqual(0.2f, footprint.Height, 1e-5f);
                Assert.AreEqual(new Rect(1f, 3f, 0f, 0f), footprint.Bounds);
            }
            finally
            {
                boundary.Dispose();
            }
        }
    }
}
