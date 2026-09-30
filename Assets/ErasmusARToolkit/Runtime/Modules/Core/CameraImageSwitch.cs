using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Hides the camera picture behind a plain sky color and shows it again; tracking keeps running.
    /// While the picture is hidden, real-world occlusion and the room scan's stand-ins for the real world
    /// are paused too: what is not shown must not hide virtual content. Used by virtual views (design
    /// walkthrough) and by portals. Reach it through <see cref="ARToolkitManager.CameraImage"/>.
    /// </summary>
    public sealed class CameraImageSwitch
    {
        readonly ARToolkitManager manager;
        readonly HashSet<object> occlusionPausers = new HashSet<object>();
        Camera hiddenOn;
        CameraClearFlags savedFlags;
        Color savedColor;
        bool occlusionPaused;
        bool occlusionWasEnabled;
        bool warnedUndetermined;

        internal CameraImageSwitch(ARToolkitManager toolkitManager)
        {
            manager = toolkitManager;
            if (manager != null && manager.RoomScanner != null)
            {
                manager.RoomScanner.ScanningChanged += _ => ApplyOcclusion();
            }
        }

        /// <summary>True while the camera picture is hidden.</summary>
        public bool IsHidden => hiddenOn != null;

        /// <summary>The color shown instead of the camera picture.</summary>
        public Color SkyColor { get; private set; } = Color.black;

        /// <summary>True while real-world occlusion is paused (camera hidden, inside a portal).</summary>
        public bool IsOcclusionPaused => occlusionPausers.Count > 0;

        /// <summary>Raised with true when the picture is hidden, false when it is shown again.</summary>
        public event Action<bool> Changed;

        /// <summary>Hides the camera picture and shows <paramref name="skyColor"/> instead.</summary>
        public void Hide(Color skyColor)
        {
            SkyColor = skyColor;
            if (hiddenOn != null)
            {
                hiddenOn.backgroundColor = skyColor;
                return;
            }

            var camera = manager != null ? manager.ActiveCamera : null;
            if (camera == null)
            {
                if (!warnedUndetermined)
                {
                    warnedUndetermined = true;
                    Debug.LogWarning("[Erasmus AR Toolkit] The camera picture cannot be hidden before the mode is known.");
                }

                return;
            }

            if (manager.Mode == ToolkitMode.AR)
            {
                var background = camera.GetComponent<ARCameraBackground>();
                if (background != null && background.enabled)
                {
                    // Switching the background off resets the projection: keep the one that matches the phone camera.
                    var projection = camera.projectionMatrix;
                    background.enabled = false;
                    camera.projectionMatrix = projection;
                }
            }
            else if (manager.CameraFeed != null)
            {
                manager.CameraFeed.SetImageHidden(true);
            }

            savedFlags = camera.clearFlags;
            savedColor = camera.backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = skyColor;
            hiddenOn = camera;
            PauseOcclusion(this);
            Changed?.Invoke(true);
        }

        /// <summary>Shows the camera picture again.</summary>
        public void Show()
        {
            if (hiddenOn == null)
            {
                return;
            }

            var camera = hiddenOn;
            hiddenOn = null;
            camera.clearFlags = savedFlags;
            camera.backgroundColor = savedColor;
            ResumeOcclusion(this);
            if (manager.Mode == ToolkitMode.AR)
            {
                var background = camera.GetComponent<ARCameraBackground>();
                if (background != null)
                {
                    background.enabled = true;
                }
            }
            else if (manager.CameraFeed != null)
            {
                manager.CameraFeed.SetImageHidden(false);
            }

            Changed?.Invoke(false);
        }

        public void Toggle(Color skyColor)
        {
            if (IsHidden)
            {
                Show();
            }
            else
            {
                Hide(skyColor);
            }
        }

        /// <summary>
        /// Pauses real-world occlusion and the room scan's occluders for as long as any <paramref name="by"/>
        /// wants it (a portal while the user is inside it). Never during a room scan: the scan reads depth.
        /// </summary>
        public void PauseOcclusion(object by)
        {
            if (by != null && occlusionPausers.Add(by))
            {
                ApplyOcclusion();
            }
        }

        public void ResumeOcclusion(object by)
        {
            if (by != null && occlusionPausers.Remove(by))
            {
                ApplyOcclusion();
            }
        }

        void ApplyOcclusion()
        {
            if (manager == null)
            {
                return;
            }

            var scanner = manager.RoomScanner;
            var scanning = scanner != null && scanner.IsScanning;
            var pause = occlusionPausers.Count > 0 && !scanning;
            if (scanner != null)
            {
                scanner.SetOccludersEnabled(!pause);
            }

            var occlusion = manager.OcclusionManager;
            if (occlusion == null || pause == occlusionPaused)
            {
                return;
            }

            occlusionPaused = pause;
            if (pause)
            {
                occlusionWasEnabled = occlusion.enabled;
                occlusion.enabled = false;
                return;
            }

            if (occlusionWasEnabled)
            {
                occlusion.enabled = true;
                if (manager.Mode == ToolkitMode.AR && manager.isActiveAndEnabled)
                {
                    // The check for depth support may have been cut short while occlusion was paused.
                    manager.StartCoroutine(RealWorldOcclusion.TurnOffIfUnsupported(occlusion));
                }
            }
        }
    }
}
