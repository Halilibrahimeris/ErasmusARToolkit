using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Shows the configured content when ARCore recognises an image target, hides it
    /// when tracking is lost and removes it when the image is removed.
    /// </summary>
    [RequireComponent(typeof(ARTrackedImageManager))]
    public sealed class ImageTargetSpawner : MonoBehaviour
    {
        [SerializeField] ARToolkitManager manager;

        readonly Dictionary<TrackableId, ToolkitObject> spawned = new Dictionary<TrackableId, ToolkitObject>();
        ARTrackedImageManager imageManager;
        ImageTargetLookup lookup;

        internal void Initialize(ARToolkitManager toolkitManager)
        {
            manager = toolkitManager;
        }

        void Awake()
        {
            imageManager = GetComponent<ARTrackedImageManager>();
            var config = manager != null ? manager.Config : null;
            lookup = new ImageTargetLookup(config != null ? config.ImageTargets : null);

            if (config == null || config.ImageLibrary == null || lookup.Count == 0)
            {
                // Nothing to track: keep the camera free for plane detection.
                imageManager.enabled = false;
                return;
            }

            // The config is the source of truth; a scene may still reference an older library.
            if (!ReferenceEquals(imageManager.referenceLibrary, config.ImageLibrary))
            {
                imageManager.referenceLibrary = config.ImageLibrary;
            }

            Debug.Log($"[Erasmus AR Toolkit] Image tracking is on for {lookup.Count} image(s): " +
                      string.Join(", ", config.ImageTargets.Where(t => t != null).Select(t => t.ResolvedName)));
        }

        void OnEnable()
        {
            imageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        void OnDisable()
        {
            imageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
        {
            foreach (var image in changes.added)
            {
                Spawn(image);
            }

            foreach (var image in changes.updated)
            {
                if (spawned.TryGetValue(image.trackableId, out var content) && content != null)
                {
                    var visible = image.trackingState == TrackingState.Tracking;
                    if (content.gameObject.activeSelf != visible)
                    {
                        content.gameObject.SetActive(visible);
                    }
                }
            }

            foreach (var pair in changes.removed)
            {
                if (spawned.TryGetValue(pair.Key, out var content))
                {
                    ObjectUtility.SafeDestroy(content != null ? content.gameObject : null);
                    spawned.Remove(pair.Key);
                }
            }
        }

        void Spawn(ARTrackedImage image)
        {
            var imageName = image.referenceImage.name;
            if (!lookup.TryGet(imageName, out var item))
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] Image '{imageName}' is tracked but not in the config.", this);
                return;
            }

            var size = image.size.x > 0f ? image.size : item.PhysicalSize;
            spawned[image.trackableId] = ToolkitObjectFactory.SpawnImageContent(item, image.transform, size,
                manager.Theme, manager.Config.ScaleLimits);
        }
    }
}
