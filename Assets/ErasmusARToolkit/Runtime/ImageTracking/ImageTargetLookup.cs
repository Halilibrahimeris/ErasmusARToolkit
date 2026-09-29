using System;
using System.Collections.Generic;

namespace Erasmus.ARToolkit
{
    /// <summary>Maps reference image names to their configured image targets.</summary>
    public sealed class ImageTargetLookup
    {
        readonly Dictionary<string, ImageTargetItem> itemsByName;

        public ImageTargetLookup(IEnumerable<ImageTargetItem> items)
        {
            itemsByName = new Dictionary<string, ImageTargetItem>(StringComparer.Ordinal);
            foreach (var item in items ?? Array.Empty<ImageTargetItem>())
            {
                if (item == null)
                {
                    continue;
                }

                var name = item.ResolvedName;
                if (!string.IsNullOrEmpty(name) && !itemsByName.ContainsKey(name))
                {
                    itemsByName.Add(name, item);
                }
            }
        }

        public int Count => itemsByName.Count;

        public bool TryGet(string name, out ImageTargetItem item)
        {
            if (string.IsNullOrEmpty(name))
            {
                item = null;
                return false;
            }

            return itemsByName.TryGetValue(name, out item);
        }
    }
}
