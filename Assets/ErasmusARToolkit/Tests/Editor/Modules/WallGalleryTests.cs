using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class WallRaycastTests
    {
        // A wall along x from -2 to 2 at z = 3, from the floor (0) up to 1 m (seen by the scan).
        static readonly WallSegment Wall = new WallSegment(new Vector2(-2f, 3f), new Vector2(2f, 3f), 0f, 1f, 1f, true);

        [Test]
        public void TryHit_RayTowardTheWall_HitsIt_FacingTheViewer()
        {
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.forward);
            Assert.IsTrue(WallRaycast.TryHit(ray, new[] { Wall }, out var pose));
            Assert.AreEqual(3f, pose.position.z, 0.0001f);
            Assert.AreEqual(1.5f, pose.position.y, 0.0001f, "Pictures hang above what the scan saw of the wall.");
            var forward = pose.rotation * Vector3.forward;
            Assert.AreEqual(-1f, forward.z, 0.0001f, "The picture faces the room.");
            Assert.AreEqual(1f, (pose.rotation * Vector3.up).y, 0.0001f, "Pictures hang upright.");
        }

        [Test]
        public void TryHit_PastTheEndOrTooHigh_Misses()
        {
            Assert.IsFalse(WallRaycast.TryHit(new Ray(new Vector3(3f, 1f, 0f), Vector3.forward), new[] { Wall }, out _));
            Assert.IsFalse(WallRaycast.TryHit(new Ray(new Vector3(0f, 3f, 0f), Vector3.forward), new[] { Wall }, out _));
            Assert.IsFalse(WallRaycast.TryHit(new Ray(new Vector3(0f, 1f, 0f), Vector3.back), new[] { Wall }, out _));
        }

        [Test]
        public void TryHit_TakesTheNearestWall()
        {
            var near = new WallSegment(new Vector2(-2f, 2f), new Vector2(2f, 2f), 0f, 2.5f, 1f, true);
            Assert.IsTrue(WallRaycast.TryHit(new Ray(new Vector3(0f, 1f, 0f), Vector3.forward), new[] { Wall, near }, out var pose));
            Assert.AreEqual(2f, pose.position.z, 0.0001f);
        }

        [Test]
        public void WallPose_TurnsTheNormalToTheViewer()
        {
            var pose = WallRaycast.WallPose(new Vector3(0f, 1f, 3f), Vector3.forward, Vector3.zero);
            Assert.AreEqual(-1f, (pose.rotation * Vector3.forward).z, 0.0001f);
        }

        [Test]
        public void TryIntersectRectangle_HitsInside_MissesOutside()
        {
            var normal = Vector3.back;
            Assert.IsTrue(WallRaycast.TryIntersectRectangle(new Ray(new Vector3(1f, 1f, 0f), Vector3.forward),
                new Vector3(0f, 1.25f, 2f), normal, 2.5f, 0f, 2.5f, out var point));
            Assert.AreEqual(new Vector3(1f, 1f, 2f), point);
            Assert.IsFalse(WallRaycast.TryIntersectRectangle(new Ray(new Vector3(3f, 1f, 0f), Vector3.forward),
                new Vector3(0f, 1.25f, 2f), normal, 2.5f, 0f, 2.5f, out _));
            Assert.IsFalse(WallRaycast.TryIntersectRectangle(new Ray(new Vector3(0f, 1f, 0f), Vector3.right),
                new Vector3(0f, 1.25f, 2f), normal, 2.5f, 0f, 2.5f, out _));
        }

        [Test]
        public void RotateYaw_OnAWall_TurnsAroundTheWallNormal()
        {
            var go = new GameObject("Picture");
            try
            {
                go.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                var manipulator = go.AddComponent<ObjectManipulator>();
                manipulator.OnWall = true;
                manipulator.RotateYaw(30f);
                Assert.AreEqual(-1f, go.transform.forward.z, 0.0001f, "It keeps facing the room.");
                Assert.Less(go.transform.up.y, 0.99f, "It is tilted on the wall.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Config_HasWallPlaceables_OnlyWithAWallItem()
        {
            var config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            var prefab = new GameObject("Prefab");
            try
            {
                config.SetContent(new[] { new PlaceableItem("Box", prefab) }, new ImageTargetItem[0]);
                Assert.IsFalse(config.HasWallPlaceables);
                config.SetContent(new[] { new PlaceableItem("Painting", prefab, 1f, null, null, PlaceOn.Wall) }, new ImageTargetItem[0]);
                Assert.IsTrue(config.HasWallPlaceables);
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(prefab);
            }
        }
    }

    public class ExhibitPrefabBuilderTests
    {
        [Test]
        public void SizeOf_KeepsThePictureShape()
        {
            var image = new Texture2D(400, 300);
            try
            {
                var size = ExhibitPrefabBuilder.SizeOf(image, null, 0.8f);
                Assert.AreEqual(0.8f, size.x, 0.0001f);
                Assert.AreEqual(0.6f, size.y, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void BuildContent_PictureFacesTheRoom_CenteredOnThePivot_InAFrame()
        {
            var root = new GameObject("Exhibit");
            try
            {
                ExhibitPrefabBuilder.BuildContent(root, new Vector2(0.8f, 0.6f), null, null, null, null);
                var picture = root.transform.Find(ExhibitPrefabBuilder.PictureName);
                Assert.IsNotNull(picture);
                var face = -picture.forward;
                Assert.AreEqual(1f, face.z, 0.0001f, "A quad shows its -Z side: turned around, that side faces the room (+Z).");
                Assert.AreEqual(0f, picture.localPosition.y, 0.0001f, "The tapped point is the middle of the picture.");
                Assert.Greater(picture.localPosition.z, 0f, "In front of the wall.");
                Assert.AreEqual(2, root.transform.childCount, "Board and picture, no frame.");

                var framed = new GameObject("Framed");
                try
                {
                    ExhibitPrefabBuilder.BuildContent(framed, new Vector2(0.8f, 0.6f), null, new Material(Shader.Find("Hidden/InternalErrorShader")), null, null);
                    Assert.AreEqual(6, framed.transform.childCount, "Board, picture and four frame bars.");
                    Assert.IsNotNull(framed.GetComponentInChildren<Collider>(), "It can be tapped.");
                }
                finally
                {
                    Object.DestroyImmediate(framed);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
