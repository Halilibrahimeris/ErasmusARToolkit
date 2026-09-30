using System.Linq;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class DesignBuilderTests
    {
        DesignSpace design;

        [TearDown]
        public void TearDown()
        {
            if (design != null)
            {
                Object.DestroyImmediate(design.gameObject);
            }
        }

        static Material NoMaterial(string name, Color color) => null;

        [Test]
        public void Create_BuildsTheDesign_WithItsStartCamera_PlacedAtAppStart()
        {
            design = DesignBuilder.Create(new Vector3(8f, 3f, 6f), true, 1.5f, Color.cyan, NoMaterial);
            var placement = design.GetComponent<ModulePlacement>();
            Assert.IsNotNull(placement);
            Assert.AreEqual(PlacementMode.AtAppStart, placement.PlacementMode);
            Assert.IsNotNull(design.StartPoint);
            Assert.AreSame(design.StartPoint.transform, placement.StartPoint);
            Assert.AreEqual(1.5f, design.StartPoint.transform.localPosition.y, 0.0001f);
            Assert.AreEqual(new Vector3(8f, 3f, 6f), design.Size);
            Assert.AreEqual(Color.cyan, design.SkyColor);
        }

        [Test]
        public void Create_RoomShell_IsVirtualOnly_AndCannotBeTapped()
        {
            design = DesignBuilder.Create(new Vector3(4f, 2.5f, 5f), true, 1.5f, Color.white, NoMaterial);
            Assert.IsNotNull(design.VirtualOnly);
            var parts = design.VirtualOnly.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(6, parts.Length, "Floor, ceiling and four walls.");
            Assert.IsEmpty(design.VirtualOnly.GetComponentsInChildren<Collider>());

            var floor = parts.First(p => p.name == "Floor");
            Assert.AreEqual(4f, floor.transform.localScale.x, 0.0001f);
            Assert.AreEqual(5f, floor.transform.localScale.z, 0.0001f);
        }

        [Test]
        public void Create_WithoutShell_KeepsVirtualOnlyEmpty()
        {
            design = DesignBuilder.Create(new Vector3(4f, 2.5f, 5f), false, 1.5f, Color.white, NoMaterial);
            Assert.AreEqual(0, design.VirtualOnly.transform.childCount);
        }

        [Test]
        public void Create_Examples_HaveInfoCards()
        {
            design = DesignBuilder.Create(new Vector3(8f, 3f, 6f), false, 1.5f, Color.white, NoMaterial);
            var cards = design.GetComponentsInChildren<InfoCard>();
            Assert.AreEqual(2, cards.Length);
            Assert.IsTrue(cards.All(card => card.Info.HasContent));
        }

        [Test]
        public void Create_StartHeight_StaysInsideTheRoom()
        {
            design = DesignBuilder.Create(new Vector3(4f, 2.5f, 4f), false, 9f, Color.white, NoMaterial);
            Assert.Less(design.StartPoint.transform.localPosition.y, 2.5f);
        }

        [Test]
        public void CountTriangles_CountsEveryMesh()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Assert.AreEqual(12, DesignBuilder.CountTriangles(cube));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }
    }

    public class ModulePlacementTests
    {
        [Test]
        public void Place_WithoutAToolkit_MovesTheObject_AndHeightOffsetLiftsIt()
        {
            var go = new GameObject("Module");
            try
            {
                var placement = go.AddComponent<ModulePlacement>();
                var placedEvents = 0;
                placement.Placed += _ => placedEvents++;
                placement.Place(new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f)));
                Assert.IsTrue(placement.IsPlaced);
                Assert.AreEqual(1, placedEvents);
                Assert.AreEqual(new Vector3(1f, 0f, 2f), go.transform.position);

                placement.HeightOffset = 0.3f;
                Assert.AreEqual(0.3f, go.transform.position.y, 0.0001f);
                Assert.AreEqual(0f, placement.PlacedPose.position.y, 0.0001f, "The placed pose stays on the floor.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
