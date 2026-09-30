using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit.Tests
{
    public class RealWorldOcclusionTests
    {
        GameObject cameraObject;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("AR Camera", typeof(Camera));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void Config_OcclusionIsOnByDefault()
        {
            var config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            try
            {
                Assert.IsTrue(config.RealWorldOcclusion);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Configure_Enabled_AddsManagerRequestingSmoothedDepth()
        {
            var manager = RealWorldOcclusion.Configure(cameraObject, true);

            Assert.IsNotNull(manager);
            Assert.IsTrue(manager.enabled);
            Assert.AreEqual(EnvironmentDepthMode.Fastest, manager.requestedEnvironmentDepthMode);
            Assert.IsTrue(manager.environmentDepthTemporalSmoothingRequested);
            Assert.AreEqual(OcclusionPreferenceMode.PreferEnvironmentOcclusion, manager.requestedOcclusionPreferenceMode);
        }

        [Test]
        public void Configure_Disabled_TurnsManagerOff()
        {
            var manager = RealWorldOcclusion.Configure(cameraObject, false);

            Assert.IsFalse(manager.enabled);
        }

        [Test]
        public void Configure_Twice_ReusesManager()
        {
            var first = RealWorldOcclusion.Configure(cameraObject, true);
            var second = RealWorldOcclusion.Configure(cameraObject, false);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, cameraObject.GetComponents<AROcclusionManager>().Length);
        }

        [Test]
        public void Configure_NoCamera_ReturnsNull()
        {
            Assert.IsNull(RealWorldOcclusion.Configure(null, true));
        }

        [TestCase(true, Supported.Supported, false, false)]
        [TestCase(true, Supported.Unsupported, false, true)]
        [TestCase(false, Supported.Unknown, true, true)]
        public void IsUnsupported_KnownAnswers(bool hasDescriptor, Supported support, bool tracking, bool expected)
        {
            Assert.AreEqual(expected, RealWorldOcclusion.IsUnsupported(hasDescriptor, support, tracking));
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void IsUnsupported_UnknownSupport_WaitsForAnswer(bool hasDescriptor, bool tracking)
        {
            Assert.IsNull(RealWorldOcclusion.IsUnsupported(hasDescriptor, Supported.Unknown, tracking));
        }

        [Test]
        public void RenderOrder_RealWorldLayersDrawBeforeVirtualObjects()
        {
            Assert.Less(RenderOrder.CameraImage, RenderOrder.VirtualGround);
            Assert.Less(RenderOrder.VirtualGround, RenderOrder.RoomOccluder);
            Assert.Less(RenderOrder.RoomOccluder, RenderOrder.PlaneFill);
            Assert.Less(RenderOrder.PlaneFill, RenderOrder.PlaneOutline);
            Assert.Less(RenderOrder.PlaneOutline, RenderOrder.SurfaceMark);
            Assert.Less(RenderOrder.SurfaceMark, (int)UnityEngine.Rendering.RenderQueue.Geometry);
        }

        [TestCase("ARToolkitPlane", RenderOrder.PlaneFill)]
        [TestCase("ARToolkitPlaneEdge", RenderOrder.PlaneOutline)]
        [TestCase("ARToolkitHighlight", RenderOrder.SurfaceMark)]
        [TestCase("ARToolkitImageTarget", RenderOrder.CameraImage)]
        [TestCase("ARToolkitOccluder", RenderOrder.RoomOccluder)]
        [TestCase("ARToolkitSurfaceDepth", RenderOrder.SurfaceDepth)]
        [TestCase("ARToolkitObstacle", RenderOrder.ObstacleFill)]
        [TestCase("ARToolkitObstacleEdge", RenderOrder.ObstacleOutline)]
        public void ToolkitMaterials_UseTheirRenderOrder(string materialName, int expectedQueue)
        {
            var material = Editor.ToolkitAssets.FindAsset<Material>(materialName);

            Assert.IsNotNull(material, materialName + " is missing; run Tools > Erasmus AR Toolkit > Run Setup.");
            Assert.AreEqual(expectedQueue, material.renderQueue);
        }
    }
}
