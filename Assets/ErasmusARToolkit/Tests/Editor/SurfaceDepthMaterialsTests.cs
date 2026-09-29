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
        public void MarkAndWrite_OfASlot_RunBackToBack_InTheirOwnQueues()
        {
            Assert.AreEqual(RenderOrder.SurfaceDepth + 4, materials.Mark(2).renderQueue);
            Assert.AreEqual(RenderOrder.SurfaceDepth + 5, materials.Write(2).renderQueue);
            Assert.Less(materials.Write(2).renderQueue, materials.Mark(3).renderQueue);
        }

        [Test]
        public void Mark_TestsDepthWithAPull_Write_ReplacesDepthOnMarkedPixels()
        {
            var mark = materials.Mark(0);
            Assert.AreEqual(SurfaceDepthMaterials.PullHeight, mark.GetFloat("_PullHeight"));
            Assert.AreEqual((float)CompareFunction.LessEqual, mark.GetFloat("_ZTest"));
            Assert.AreEqual(0f, mark.GetFloat("_ZWrite"));
            Assert.AreEqual((float)StencilOp.Replace, mark.GetFloat("_StencilPass"));

            var write = materials.Write(0);
            Assert.AreEqual(0f, write.GetFloat("_PullHeight"));
            Assert.Greater(write.GetFloat("_PushBack"), 0f);
            Assert.AreEqual((float)CompareFunction.Always, write.GetFloat("_ZTest"));
            Assert.AreEqual(1f, write.GetFloat("_ZWrite"));
            Assert.AreEqual((float)CompareFunction.Equal, write.GetFloat("_StencilComp"));
            Assert.AreEqual((float)StencilOp.Zero, write.GetFloat("_StencilPass"));
        }

        [Test]
        public void RenderOrder_DepthCorrectionRunsBeforeOccludersAndGrids()
        {
            Assert.LessOrEqual(RenderOrder.SurfaceDepth + 2 * RenderOrder.SurfaceDepthSlots, RenderOrder.RoomOccluder);
            Assert.Less(RenderOrder.RoomOccluder, RenderOrder.PlaneFill);
        }
    }
}
