using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class TransformFormatterTests
    {
        [Test]
        public void Format_ShowsPositionRotationAndScale_WithInvariantCulture()
        {
            var text = TransformFormatter.Format(new Vector3(0.123f, 0f, -1.5f), 45.4f, 1.25f);
            Assert.AreEqual("Position  X 0.12  Y 0.00  Z -1.50 m\nRotation 45°    Scale 1.25x", text);
        }

        [Test]
        public void Format_NormalizesRotationTo0To360()
        {
            StringAssert.Contains("Rotation 270°", TransformFormatter.Format(Vector3.zero, -90f, 1f));
        }
    }

    public class ObjectManipulatorTests
    {
        GameObject parent;
        GameObject target;
        ObjectManipulator manipulator;

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Parent");
            target = new GameObject("Target");
            target.transform.SetParent(parent.transform, false);
            target.transform.localPosition = new Vector3(1f, 0f, 2f);
            manipulator = target.AddComponent<ObjectManipulator>();
            manipulator.Initialize(Vector3.one * 2f, new Vector2(0.5f, 3f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void Scale_IsRelativeToPlacedSize_AndClamped()
        {
            manipulator.SetScaleMultiplier(1.5f);
            Assert.AreEqual(3f, target.transform.localScale.x, 0.0001f);
            manipulator.SetScaleMultiplier(10f);
            Assert.AreEqual(3f, manipulator.ScaleMultiplier, 0.0001f);
        }

        [Test]
        public void Raise_MovesAlongParentUp()
        {
            parent.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // parent "up" now points along +Z
            var before = target.transform.position;
            manipulator.Raise(0.1f);
            var moved = target.transform.position - before;
            Assert.AreEqual(0.1f, Vector3.Dot(moved, parent.transform.up), 0.0001f);
        }

        [Test]
        public void RotateYaw_ChangesYaw()
        {
            manipulator.RotateYaw(30f);
            Assert.AreEqual(30f, manipulator.YawDegrees, 0.01f);
        }

        [Test]
        public void ResetPose_RestoresPlacement()
        {
            manipulator.RotateYaw(45f);
            manipulator.Raise(0.2f);
            manipulator.SetScaleMultiplier(2f);
            manipulator.ResetPose();

            Assert.AreEqual(new Vector3(1f, 0f, 2f), target.transform.localPosition);
            Assert.AreEqual(0f, manipulator.YawDegrees, 0.01f);
            Assert.AreEqual(1f, manipulator.ScaleMultiplier, 0.0001f);
        }

        [Test]
        public void CanMove_DefaultsTrue_AndCanBeDisabled()
        {
            Assert.IsTrue(manipulator.CanMove);
            var fixedObject = new GameObject("Fixed").AddComponent<ObjectManipulator>();
            try
            {
                fixedObject.Initialize(Vector3.one, Vector2.one, false);
                Assert.IsFalse(fixedObject.CanMove);
            }
            finally
            {
                Object.DestroyImmediate(fixedObject.gameObject);
            }
        }
    }

    public class ImageContentPlacementTests
    {
        [Test]
        public void ImageContent_SitsOnImageCenter_WhenModelRootIsOffset()
        {
            // Imported models often keep where they were modelled (here 1 m to the side) in their root,
            // plus an axis-conversion rotation that must be kept so the model is not tipped over.
            var modelRotation = Quaternion.Euler(270f, 0f, 0f);
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.transform.SetPositionAndRotation(new Vector3(-1f, 0.003f, -0.5f), modelRotation);
            var anchor = new GameObject("Image Anchor").transform;
            try
            {
                var item = new ImageTargetItem("Offset Model", null, 0.2f, prefab);
                var content = ToolkitObjectFactory.SpawnImageContent(item, anchor, new Vector2(0.2f, 0.2f), null);

                Assert.AreEqual(Vector3.zero, content.transform.localPosition);
                Assert.AreEqual(0f, Quaternion.Angle(modelRotation, content.transform.localRotation), 0.01f);

                content.Manipulator.Raise(0.1f);
                content.Manipulator.ResetPose();
                Assert.AreEqual(Vector3.zero, content.transform.localPosition, "Reset returns the model to the image center.");
            }
            finally
            {
                Object.DestroyImmediate(anchor.gameObject);
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
