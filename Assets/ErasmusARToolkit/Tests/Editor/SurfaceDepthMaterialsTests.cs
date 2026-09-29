using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Erasmus.ARToolkit.Tests
{
    public class SurfaceDepthMaterialsTests
    {
        Material source;
        SurfaceDepthMaterials materials;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find("Erasmus AR Toolkit/Surface Depth");
            Assert.IsNotNull(shader, "The Surface Depth shader should be in the toolkit.");
            source = new Material(shader);
            materials = new SurfaceDepthMaterials(source);
        }

        [TearDown]
        public void TearDown()
        {
            materials.Dispose();
            Object.DestroyImmediate(source);
        }

        [Test]
        public void Acquire_GivesEverySlotOnce_ThenNone_AndReleasedSlotsComeBack()
        {
            for (var slot = 0; slot < RenderOrder.SurfaceDepthSlots; slot++)
            {
                Assert.AreEqual(slot, materials.Acquire());
            }

            Assert.AreEqual(-1, materials.Acquire());

            materials.Release(3);
            Assert.AreEqual(3, materials.Acquire());
        }

        [Test]
        public void Acquire_WithoutMaterial_GivesNoSlot()
        {
            Assert.AreEqual(-1, new SurfaceDepthMaterials(null).Acquire());
        }

        [Test]
        public void MarkUnmarkAndWrite_OfASlot_RunBackToBack_InTheirOwnQueues()
        {
            Assert.AreEqual(RenderOrder.SurfaceDepth + 6, materials.Mark(2).renderQueue);
            Assert.AreEqual(RenderOrder.SurfaceDepth + 7, materials.Unmark(2).renderQueue);
            Assert.AreEqual(RenderOrder.SurfaceDepth + 8, materials.Write(2).renderQueue);
            Assert.Less(materials.Write(2).renderQueue, materials.Mark(3).renderQueue);
        }

        [Test]
        public void Mark_TestsDepthWithAPull_Write_ReplacesDepthOnMarkedPixels()
        {
            var mark = materials.Mark(0);
            Assert.AreEqual(1f, mark.GetFloat("_Direction"), "Pulled toward the camera.");
            Assert.AreEqual(SurfaceDepthMaterials.MinPullHeight, mark.GetFloat("_MinPull"));
            Assert.AreEqual(SurfaceDepthMaterials.MaxPullHeight, mark.GetFloat("_MaxPullHeight"));
            Assert.AreEqual(SurfaceDepthMaterials.NoiseBase, mark.GetFloat("_NoiseBase"));
            Assert.AreEqual(SurfaceDepthMaterials.NoiseGrowth, mark.GetFloat("_NoiseK"));
            Assert.AreEqual((float)CompareFunction.LessEqual, mark.GetFloat("_ZTest"));
            Assert.AreEqual(0f, mark.GetFloat("_ZWrite"));
            Assert.AreEqual((float)CompareFunction.Always, mark.GetFloat("_StencilComp"));
            Assert.AreEqual((float)StencilOp.Replace, mark.GetFloat("_StencilPass"));

            var write = materials.Write(0);
            Assert.AreEqual(0f, write.GetFloat("_Direction"));
            Assert.Greater(write.GetFloat("_PushBack"), 0f);
            Assert.AreEqual((float)CompareFunction.Always, write.GetFloat("_ZTest"));
            Assert.AreEqual(1f, write.GetFloat("_ZWrite"));
            Assert.AreEqual((float)CompareFunction.Equal, write.GetFloat("_StencilComp"));
            Assert.AreEqual((float)StencilOp.Zero, write.GetFloat("_StencilPass"));
        }

        [Test]
        public void Unmark_ClearsTheMark_WhereTheCameraSeesThroughTheSurface()
        {
            var unmark = materials.Unmark(0);
            Assert.AreEqual(-1f, unmark.GetFloat("_Direction"), "Pushed away from the camera.");
            Assert.AreEqual(0f, unmark.GetFloat("_PushBack"));
            Assert.AreEqual((float)CompareFunction.LessEqual, unmark.GetFloat("_ZTest"));
            Assert.AreEqual(0f, unmark.GetFloat("_ZWrite"));
            Assert.AreEqual((float)CompareFunction.Equal, unmark.GetFloat("_StencilComp"));
            Assert.AreEqual((float)StencilOp.Zero, unmark.GetFloat("_StencilPass"));
        }

        [Test]
        public void RenderOrder_DepthCorrectionRunsBeforeOccludersAndGrids()
        {
            var queues = RenderOrder.SurfaceDepthQueuesPerSlot;
            Assert.LessOrEqual(RenderOrder.ContactDepth + queues * RenderOrder.ContactDepthSlots, RenderOrder.SurfaceDepth);
            Assert.LessOrEqual(RenderOrder.SurfaceDepth + queues * RenderOrder.SurfaceDepthSlots, RenderOrder.CameraImage);
            Assert.Less(RenderOrder.RoomOccluder, RenderOrder.PlaneFill);
            Assert.Less(RenderOrder.PlaneOutline, RenderOrder.ObstacleFill);
            Assert.Less(RenderOrder.ObstacleFill, RenderOrder.ObstacleOutline);
            Assert.Less(RenderOrder.ObstacleOutline, RenderOrder.SurfaceMark);
        }

        [Test]
        public void OwnQueuesAndSlots_AreUsed()
        {
            var contact = new SurfaceDepthMaterials(source, RenderOrder.ContactDepth, RenderOrder.ContactDepthSlots);
            try
            {
                Assert.AreEqual(RenderOrder.ContactDepthSlots, contact.SlotCount);
                Assert.AreEqual(RenderOrder.ContactDepth, contact.Mark(0).renderQueue);
                Assert.AreEqual(RenderOrder.ContactDepth + 5, contact.Write(1).renderQueue);
                for (var slot = 0; slot < RenderOrder.ContactDepthSlots; slot++)
                {
                    Assert.AreEqual(slot, contact.Acquire());
                }

                Assert.AreEqual(-1, contact.Acquire());
            }
            finally
            {
                contact.Dispose();
            }
        }
    }
}
