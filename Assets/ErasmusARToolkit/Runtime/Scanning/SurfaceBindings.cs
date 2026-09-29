using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The holders of the objects that stand on scanned surfaces (see <see cref="SurfaceHolder"/>). Keeps
    /// them on their surfaces when heights change and when two surfaces turn out to be one.
    /// </summary>
    internal sealed class SurfaceBindings
    {
        const string HolderName = "Surface Holder";

        readonly List<SurfaceHolder> holders = new List<SurfaceHolder>();

        public int Count
        {
            get
            {
                Prune();
                return holders.Count;
            }
        }

        /// <param name="mapPose">Where the holder lies, in the space of <paramref name="root"/>.</param>
        public SurfaceHolder Create(Transform root, Pose mapPose, int levelId)
        {
            var holderObject = new GameObject(HolderName);
            holderObject.transform.SetParent(root, false);
            holderObject.transform.localPosition = mapPose.position;
            holderObject.transform.localRotation = mapPose.rotation;
            var holder = holderObject.AddComponent<SurfaceHolder>();
            holder.LevelId = levelId;
            holders.Add(holder);
            return holder;
        }

        /// <summary>Moves a holder to another point, which may lie on another surface.</summary>
        public void Move(SurfaceHolder holder, Vector3 mapPosition, int levelId)
        {
            if (holder == null)
            {
                return;
            }

            holder.transform.localPosition = mapPosition;
            holder.LevelId = levelId;
            if (!holders.Contains(holder))
            {
                holders.Add(holder);
            }
        }

        public void OnLevelMerged(int from, int into)
        {
            foreach (var holder in holders)
            {
                if (holder != null && holder.LevelId == from)
                {
                    holder.LevelId = into;
                }
            }
        }

        /// <summary>The objects on a surface that is gone stay where they are.</summary>
        public void OnLevelRemoved(int id)
        {
            foreach (var holder in holders)
            {
                if (holder != null && holder.LevelId == id)
                {
                    holder.LevelId = -1;
                }
            }
        }

        public void UnbindAll()
        {
            foreach (var holder in holders)
            {
                if (holder != null)
                {
                    holder.LevelId = -1;
                }
            }
        }

        /// <summary>Puts every holder at the height its surface is drawn at.</summary>
        public void FollowHeights(RoomSurfaceMap map)
        {
            Prune();
            foreach (var holder in holders)
            {
                if (!holder.HasSurface)
                {
                    continue;
                }

                var level = map.FindLevel(holder.LevelId);
                if (level == null)
                {
                    holder.LevelId = -1;
                    continue;
                }

                var position = holder.transform.localPosition;
                if (!Mathf.Approximately(position.y, level.DrawnHeight))
                {
                    position.y = level.DrawnHeight;
                    holder.transform.localPosition = position;
                }
            }
        }

        void Prune()
        {
            holders.RemoveAll(holder => holder == null);
        }
    }
}
