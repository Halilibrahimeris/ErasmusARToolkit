using System;
using System.Collections.Generic;

namespace Erasmus.ARToolkit
{
    /// <summary>Keeps track of objects the user placed and enforces the maximum count.</summary>
    public sealed class PlacedObjectRegistry
    {
        readonly List<ToolkitObject> objects = new List<ToolkitObject>();

        public PlacedObjectRegistry(int maxObjects)
        {
            MaxObjects = Math.Max(1, maxObjects);
        }

        public event Action Changed;

        public int MaxObjects { get; }

        public int Count
        {
            get
            {
                PruneDestroyed();
                return objects.Count;
            }
        }

        public bool CanPlace => Count < MaxObjects;

        public void Add(ToolkitObject item)
        {
            if (item == null || objects.Contains(item))
            {
                return;
            }

            objects.Add(item);
            Changed?.Invoke();
        }

        public void Remove(ToolkitObject item)
        {
            if (item == null || !objects.Remove(item))
            {
                return;
            }

            ObjectUtility.SafeDestroy(item.gameObject);
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var item in objects)
            {
                if (item != null)
                {
                    ObjectUtility.SafeDestroy(item.gameObject);
                }
            }

            objects.Clear();
            Changed?.Invoke();
        }

        void PruneDestroyed()
        {
            objects.RemoveAll(item => item == null);
        }
    }
}
