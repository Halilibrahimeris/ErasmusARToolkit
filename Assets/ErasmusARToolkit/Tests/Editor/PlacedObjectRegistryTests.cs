using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class PlacedObjectRegistryTests
    {
        static ToolkitObject CreateObject(string name)
        {
            return new GameObject(name).AddComponent<ToolkitObject>();
        }

        [Test]
        public void CanPlace_RespectsMaximum()
        {
            var registry = new PlacedObjectRegistry(2);
            Assert.IsTrue(registry.CanPlace);
            registry.Add(CreateObject("A"));
            registry.Add(CreateObject("B"));
            Assert.IsFalse(registry.CanPlace);
            Assert.AreEqual(2, registry.Count);
            registry.Clear();
        }

        [Test]
        public void Add_IgnoresNullAndDuplicates()
        {
            var registry = new PlacedObjectRegistry(5);
            var item = CreateObject("A");
            registry.Add(item);
            registry.Add(item);
            registry.Add(null);
            Assert.AreEqual(1, registry.Count);
            registry.Clear();
        }

        [Test]
        public void Remove_DestroysObjectAndRaisesChanged()
        {
            var registry = new PlacedObjectRegistry(5);
            var changes = 0;
            registry.Changed += () => changes++;
            var item = CreateObject("A");
            registry.Add(item);
            registry.Remove(item);

            Assert.AreEqual(0, registry.Count);
            Assert.IsTrue(item == null, "Removed object should be destroyed.");
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void Clear_DestroysEverything()
        {
            var registry = new PlacedObjectRegistry(5);
            var a = CreateObject("A");
            var b = CreateObject("B");
            registry.Add(a);
            registry.Add(b);
            registry.Clear();

            Assert.AreEqual(0, registry.Count);
            Assert.IsTrue(a == null && b == null);
        }

        [Test]
        public void Count_SkipsObjectsDestroyedElsewhere()
        {
            var registry = new PlacedObjectRegistry(5);
            var a = CreateObject("A");
            registry.Add(a);
            Object.DestroyImmediate(a.gameObject);
            Assert.AreEqual(0, registry.Count);
            Assert.IsTrue(registry.CanPlace);
        }
    }

    public class ObjectUtilityTests
    {
        [Test]
        public void EnsureCollider_KeepsExistingCollider()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var existing = cube.GetComponent<Collider>();
                Assert.AreSame(existing, ObjectUtility.EnsureCollider(cube));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void EnsureCollider_AddsBoxFittedToChildRenderers()
        {
            var root = new GameObject("Root");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Object.DestroyImmediate(child.GetComponent<Collider>());
                child.transform.SetParent(root.transform);
                child.transform.localPosition = new Vector3(0f, 0.5f, 0f);

                var collider = ObjectUtility.EnsureCollider(root) as BoxCollider;
                Assert.IsNotNull(collider);
                Assert.AreEqual(0.5f, collider.center.y, 0.001f);
                Assert.AreEqual(1f, collider.size.x, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
