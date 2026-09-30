using System;
using System.Collections;
using System.Linq;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Shows a device camera behind the scene whenever real AR is not running: the PC webcam,
    /// or the phone's back camera on devices without ARCore. The image is a quad just inside the
    /// camera's far plane, sized to cover the view. The HUD's Camera button cycles cameras / off.
    /// (With ARCore, AR Foundation owns the camera and this component stays inactive.)
    /// </summary>
    public sealed class WebcamBackground : MonoBehaviour
    {
        const float FarPlaneFraction = 0.9f;
        const int RequestedWidth = 1280;
        const int RequestedHeight = 720;
        const int RequestedFps = 30;
        const float PermissionTimeoutSeconds = 20f;
        const float StartTimeoutSeconds = 4f;
        const int MinimumValidSize = 32;

        [SerializeField] Camera viewCamera;
        [SerializeField] ARToolkitManager manager;

        WebCamDevice[] devices = Array.Empty<WebCamDevice>();
        WebCamTexture webcam;
        Transform quad;
        Material material;
        int currentIndex = -1;
        bool busy;
        bool imageHidden;

        /// <summary>Short user-facing messages (camera name, errors) for the HUD.</summary>
        public event Action<string> Notification;

        /// <summary>Raised when the camera starts or stops.</summary>
        public event Action StateChanged;

        public bool IsRunning => webcam != null && webcam.isPlaying && webcam.width >= MinimumValidSize;
        public bool IsEnabledInConfig => manager != null && manager.Config != null && manager.Config.UseDeviceCamera;

        /// <summary>The live camera texture (null while the camera is off).</summary>
        public WebCamTexture Texture => IsRunning ? webcam : null;

        /// <summary>The camera may run, but its picture is not drawn (a project module shows a virtual world).</summary>
        public bool IsImageHidden => imageHidden;

        /// <summary>
        /// Hides the camera picture without stopping the camera (image recognition keeps working), or shows
        /// it again. The HUD's Camera button still switches cameras.
        /// </summary>
        public void SetImageHidden(bool hidden)
        {
            imageHidden = hidden;
            if (quad != null)
            {
                quad.gameObject.SetActive(!hidden && IsRunning);
            }
        }

        /// <summary>True when the displayed image is flipped upside down (see <see cref="CameraFeedLayout"/>).</summary>
        public bool IsFlippedVertically => webcam != null && webcam.videoVerticallyMirrored;

        /// <summary>
        /// Camera parameters that match how the image is displayed behind the scene, so content
        /// posed with them lines up with the image. Not available for rotated (phone) cameras.
        /// </summary>
        public bool TryGetIntrinsics(out CameraIntrinsics intrinsics)
        {
            intrinsics = default;
            if (!IsRunning || quad == null || webcam.videoRotationAngle % 180 != 0)
            {
                return false;
            }

            var quadHeight = Mathf.Abs(quad.localScale.y);
            if (quadHeight <= 0f)
            {
                return false;
            }

            intrinsics = CameraIntrinsics.FromDisplayedImage(webcam.width, webcam.height, quad.localPosition.z, quadHeight);
            return true;
        }

        internal void Initialize(Camera camera, ARToolkitManager toolkitManager)
        {
            viewCamera = camera;
            manager = toolkitManager;
        }

        void Start()
        {
            if (IsEnabledInConfig && viewCamera != null)
            {
                StartCoroutine(StartPreferredCamera());
            }
        }

        void LateUpdate()
        {
            if (webcam == null || quad == null || webcam.width < MinimumValidSize)
            {
                return;
            }

            var distance = viewCamera.farClipPlane * FarPlaneFraction;
            var viewHeight = 2f * distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var placement = CameraFeedLayout.Compute(viewHeight, viewCamera.aspect, webcam.width, webcam.height,
                webcam.videoRotationAngle, webcam.videoVerticallyMirrored);

            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localRotation = Quaternion.Euler(0f, 0f, placement.RotationDegrees);
            quad.localScale = placement.Scale;
            material.mainTextureScale = new Vector2(1f, placement.FlipVertical ? -1f : 1f);
            material.mainTextureOffset = new Vector2(0f, placement.FlipVertical ? 1f : 0f);
        }

        void OnDestroy()
        {
            StopCamera();
            if (quad != null)
            {
                ObjectUtility.SafeDestroy(quad.gameObject);
            }

            ObjectUtility.SafeDestroy(material);
        }

        /// <summary>Switches to the next camera; after the last one the camera is turned off.</summary>
        public void CycleCamera()
        {
            if (busy)
            {
                return;
            }

            if (devices.Length == 0)
            {
                StartCoroutine(StartPreferredCamera());
                return;
            }

            StartCoroutine(Open(CameraDeviceSelector.Next(currentIndex, devices.Length)));
        }

        IEnumerator StartPreferredCamera()
        {
            busy = true;
            yield return RequestPermission();
            if (!HasPermission())
            {
                busy = false;
                Notify("Camera permission was denied. Allow camera access in the device settings.");
                yield break;
            }

            devices = WebCamTexture.devices;
            Debug.Log("[Erasmus AR Toolkit] Cameras found: " +
                      (devices.Length == 0 ? "none" : string.Join(", ", devices.Select(d => d.name))));

            var infos = devices.Select(d => new CameraDeviceInfo(d.name, d.isFrontFacing)).ToArray();
            var index = CameraDeviceSelector.Pick(infos, manager.Config.PreferredCameraName, Application.isMobilePlatform);
            busy = false;
            if (index < 0)
            {
                Notify("No camera found. Showing the virtual scene only.");
                yield break;
            }

            yield return Open(index);
        }

        IEnumerator Open(int index)
        {
            busy = true;
            StopCamera();
            currentIndex = index;
            if (index < 0)
            {
                busy = false;
                Notify("Camera off.");
                StateChanged?.Invoke();
                yield break;
            }

            var baseMaterial = manager.Theme != null ? manager.Theme.ImageTargetMaterial : null;
            if (baseMaterial == null)
            {
                busy = false;
                Debug.LogWarning("[Erasmus AR Toolkit] The theme has no image target material; cannot show the camera.", this);
                yield break;
            }

            var deviceName = devices[index].name;
            try
            {
                webcam = new WebCamTexture(deviceName, RequestedWidth, RequestedHeight, RequestedFps);
                webcam.Play();
                if (material == null)
                {
                    material = new Material(baseMaterial);
                    material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                }

                material.mainTexture = webcam;
                EnsureQuad();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] Could not start camera '" + deviceName + "': " + exception.Message, this);
                StopCamera();
            }

            // A camera that is busy in another app "plays" but never delivers a real frame.
            var timeout = Time.realtimeSinceStartup + StartTimeoutSeconds;
            while (webcam != null && webcam.width < MinimumValidSize && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            busy = false;
            if (IsRunning)
            {
                quad.gameObject.SetActive(!imageHidden);
                Debug.Log($"[Erasmus AR Toolkit] Camera image {webcam.width}x{webcam.height}, " +
                          $"rotation {webcam.videoRotationAngle}, mirrored {webcam.videoVerticallyMirrored}.");
                Notify("Camera: " + deviceName);
            }
            else
            {
                StopCamera();
                Notify("Camera '" + deviceName + "' did not start. Is another app using it?");
            }

            StateChanged?.Invoke();
        }

        void StopCamera()
        {
            if (webcam != null)
            {
                webcam.Stop();
                ObjectUtility.SafeDestroy(webcam);
                webcam = null;
            }

            if (quad != null)
            {
                quad.gameObject.SetActive(false);
            }
        }

        void EnsureQuad()
        {
            if (quad != null)
            {
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Camera Image";
            ObjectUtility.SafeDestroy(go.GetComponent<Collider>());
            go.transform.SetParent(viewCamera.transform, false);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            quad = go.transform;
        }

        /// <summary>Lets camera-related features (such as webcam image recognition) show a HUD message.</summary>
        internal void Announce(string message)
        {
            Notify(message);
        }

        void Notify(string message)
        {
            Debug.Log("[Erasmus AR Toolkit] " + message);
            Notification?.Invoke(message);
        }

        static IEnumerator RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                yield break;
            }

            var answered = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            Permission.RequestUserPermission(Permission.Camera, callbacks);
            var timeout = Time.realtimeSinceStartup + PermissionTimeoutSeconds;
            while (!answered && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
#else
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }
#endif
        }

        static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
            return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
        }
    }
}
