using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class DepthImageMappingTests
    {
        /// <summary>A portrait display matrix: texture u = viewport v, texture v = 1 - viewport u.</summary>
        static Matrix4x4 PortraitDisplay()
        {
            var matrix = Matrix4x4.identity;
            matrix.SetRow(0, new Vector4(0f, -1f, 0f, 0f));
            matrix.SetRow(1, new Vector4(1f, 0f, 0f, 0f));
            matrix.SetRow(2, new Vector4(0f, 1f, 1f, 0f));
            return matrix;
        }

        [Test]
        public void ToTextureUv_IdentityMatrix_KeepsTheViewportPoint()
        {
            var mapping = new DepthImageMapping(false, false);

            Assert.AreEqual(new Vector2(0.25f, 0.75f), mapping.ToTextureUv(new Vector2(0.25f, 0.75f), Matrix4x4.identity));
        }

        [Test]
        public void ToTextureUv_RowVectorConvention_MatchesTheArcoreBackgroundShader()
        {
            // Shader: textureCoord = mul(float4(u, v, 1, 0), _UnityDisplayTransform).xy
            var uv = new DepthImageMapping(false, false).ToTextureUv(new Vector2(0.2f, 0.7f), PortraitDisplay());

            Assert.AreEqual(0.7f, uv.x, 1e-5f);
            Assert.AreEqual(0.8f, uv.y, 1e-5f);
        }

        [Test]
        public void ToTextureUv_Transposed_EqualsRowVectorWithTheTransposedMatrix()
        {
            var viewport = new Vector2(0.3f, 0.6f);
            var matrix = PortraitDisplay();

            var transposed = new DepthImageMapping(true, false).ToTextureUv(viewport, matrix);
            var expected = new DepthImageMapping(false, false).ToTextureUv(viewport, matrix.transpose);

            Assert.AreEqual(expected.x, transposed.x, 1e-5f);
            Assert.AreEqual(expected.y, transposed.y, 1e-5f);
        }

        [Test]
        public void TryGetPixel_MapsUvToImagePixels_AndCanFlipRows()
        {
            var viewport = new Vector2(0.25f, 0.625f); // texture uv (0.625, 0.75) with the portrait matrix

            Assert.IsTrue(new DepthImageMapping(false, false).TryGetPixel(viewport, PortraitDisplay(), 160, 120,
                out var x, out var y));
            Assert.AreEqual(100, x);
            Assert.AreEqual(90, y);

            Assert.IsTrue(new DepthImageMapping(false, true).TryGetPixel(viewport, PortraitDisplay(), 160, 120,
                out _, out var flippedY));
            Assert.AreEqual(120 - 1 - 90, flippedY);
        }

        [Test]
        public void TryGetPixel_OutsideTheImage_ReturnsFalse()
        {
            var shifted = Matrix4x4.identity;
            shifted.SetRow(2, new Vector4(0.5f, 0f, 1f, 0f));

            Assert.IsFalse(new DepthImageMapping(false, false).TryGetPixel(new Vector2(0.8f, 0.5f), shifted, 160, 120,
                out _, out _));
        }

        [Test]
        public void Candidates_CoverAllFourVariants()
        {
            Assert.AreEqual(4, DepthImageMapping.Candidates.Length);
            CollectionAssert.AllItemsAreUnique(DepthImageMapping.Candidates);
        }

        [Test]
        public void PointFromDepth_StraightDown_LandsOnTheFloor()
        {
            var point = DepthGeometry.PointFromDepth(new Vector3(0f, 1.5f, 0f), Vector3.down, Vector3.down, 1.5f);

            Assert.AreEqual(Vector3.zero.x, point.x, 1e-5f);
            Assert.AreEqual(0f, point.y, 1e-5f);
        }

        [Test]
        public void PointFromDepth_UsesDepthAlongTheViewAxis_NotAlongTheRay()
        {
            var ray = new Vector3(0.6f, -0.8f, 0f);

            var point = DepthGeometry.PointFromDepth(new Vector3(0f, 1.5f, 0f), Vector3.down, ray, 1.2f);

            Assert.AreEqual(0.9f, point.x, 1e-5f);
            Assert.AreEqual(0.3f, point.y, 1e-5f);
        }

        [Test]
        public void ViewDirection_MatchesTheCamerasOwnViewportRays()
        {
            var cameraObject = new GameObject("Test Camera", typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.fieldOfView = 63f;
                camera.aspect = 0.46f;
                cameraObject.transform.SetPositionAndRotation(new Vector3(1f, 1.4f, -2f), Quaternion.Euler(35f, 20f, 0f));
                var inverse = camera.projectionMatrix.inverse;

                foreach (var viewport in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.1f, 0.9f), new Vector2(0.8f, 0.2f) })
                {
                    var expected = camera.ViewportPointToRay(viewport).direction;
                    var actual = camera.cameraToWorldMatrix.MultiplyVector(DepthGeometry.ViewDirection(inverse, viewport)).normalized;

                    Assert.Less(Vector3.Angle(expected, actual), 0.05f, viewport.ToString());
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void IsUpFacing_FloorSeenFromAbove_Yes_EitherTangentOrder()
        {
            var toCamera = new Vector3(0.3f, 1f, -0.5f);

            Assert.IsTrue(DepthGeometry.IsUpFacing(Vector3.right, Vector3.forward, toCamera, 0.9f));
            Assert.IsTrue(DepthGeometry.IsUpFacing(Vector3.forward, Vector3.right, toCamera, 0.9f));
        }

        [Test]
        public void IsUpFacing_UndersideOfATable_No()
        {
            // The camera looks up at the table from below: the surface faces down, toward the camera.
            var toCamera = new Vector3(0.3f, -1f, -0.5f);

            Assert.IsFalse(DepthGeometry.IsUpFacing(Vector3.right, Vector3.forward, toCamera, 0.9f));
            Assert.IsFalse(DepthGeometry.IsUpFacing(Vector3.forward, Vector3.right, toCamera, 0.9f));
            Assert.IsTrue(DepthGeometry.IsHorizontal(Vector3.right, Vector3.forward, 0.9f),
                "It is horizontal all the same, which is why the scan no longer asks for that.");
        }

        [Test]
        public void IsUpFacing_Wall_TiltedSurface_AndNoSurface_No()
        {
            var toCamera = new Vector3(0f, 1f, -1f);
            var tilted = new Vector3(0f, Mathf.Sin(40f * Mathf.Deg2Rad), Mathf.Cos(40f * Mathf.Deg2Rad));

            Assert.IsFalse(DepthGeometry.IsUpFacing(Vector3.right, Vector3.up, toCamera, 0.9f));
            Assert.IsFalse(DepthGeometry.IsUpFacing(Vector3.right, tilted, toCamera, 0.9f));
            Assert.IsFalse(DepthGeometry.IsUpFacing(Vector3.right, Vector3.right, toCamera, 0.9f));
        }

        [Test]
        public void IsHorizontal_FloorYes_WallNo_EitherTangentOrder()
        {
            Assert.IsTrue(DepthGeometry.IsHorizontal(Vector3.right, Vector3.forward, 0.85f));
            Assert.IsTrue(DepthGeometry.IsHorizontal(Vector3.forward, Vector3.right, 0.85f));
            Assert.IsFalse(DepthGeometry.IsHorizontal(Vector3.right, Vector3.up, 0.85f));
            Assert.IsFalse(DepthGeometry.IsHorizontal(Vector3.zero, Vector3.right, 0.85f));
        }
    }
}
