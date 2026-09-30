using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// How often the room scan takes a camera frame. A scan costs time on the main thread, and an AR app
    /// that drops below 30 frames a second is unpleasant to use, so the scan slows down when the phone
    /// does not keep up, and when it gets warm. A phone that gets hot ends the scan: it would throttle
    /// everything, the camera included.
    /// </summary>
    internal sealed class ScanPacing
    {
        /// <summary>Seconds between frames when all is well (8 per second).</summary>
        public const float NormalInterval = 0.125f;

        /// <summary>Seconds between frames when the phone is busy or warm (4 per second).</summary>
        public const float SlowInterval = 0.25f;

        /// <summary>The scan slows down when a frame of it costs more than this (milliseconds)...</summary>
        public const float SlowAboveCostMs = 6f;

        /// <summary>...or when the app needs longer than this (milliseconds) for a frame.</summary>
        public const float SlowAboveFrameMs = 40f;

        /// <summary>It speeds up again when a scan frame costs less than this...</summary>
        public const float FastBelowCostMs = 3f;

        /// <summary>...and the app needs less than this for a frame...</summary>
        public const float FastBelowFrameMs = 36f;

        /// <summary>...for this long (seconds).</summary>
        public const float RecoverySeconds = 5f;

        /// <summary>Android thermal status from which on the scan is slow (THERMAL_STATUS_MODERATE).</summary>
        public const int WarmStatus = 2;

        /// <summary>Android thermal status from which on the scan ends (THERMAL_STATUS_SEVERE).</summary>
        public const int HotStatus = 3;

        /// <summary>The scan looks complete when nothing new was checked for this long (seconds).</summary>
        public const float CompleteAfterSeconds = 20f;

        /// <summary>Less new floor than this (square meters) is nothing new.</summary>
        public const float NewArea = 0.25f;

        const float Smoothing = 0.1f;

        float frameMs;
        float costMs;
        float wellSince = -1f;
        float areaSeen;
        float areaSeenAt = -1f;
        bool saidComplete;

        /// <summary>The scan takes fewer frames: the phone is busy or warm.</summary>
        public bool IsSlow { get; private set; }

        /// <summary>The phone is warm (the scan is slow because of it).</summary>
        public bool IsWarm { get; private set; }

        /// <summary>The phone is hot: the scan should end.</summary>
        public bool IsHot { get; private set; }

        /// <summary>Seconds to the next frame of the scan.</summary>
        public float Interval => IsSlow || IsWarm ? SlowInterval : NormalInterval;

        /// <summary>Time the app needs for a frame, smoothed, in milliseconds.</summary>
        public float FrameMs => frameMs;

        public void Reset()
        {
            frameMs = 0f;
            costMs = 0f;
            wellSince = -1f;
            areaSeen = 0f;
            areaSeenAt = -1f;
            saidComplete = false;
            IsSlow = false;
            IsWarm = false;
            IsHot = false;
        }

        /// <summary>Call every frame of the app while scanning.</summary>
        /// <param name="deltaSeconds">Time the last frame took.</param>
        /// <param name="scanCostMs">What a frame of the scan costs, in milliseconds.</param>
        /// <param name="now">Time in seconds.</param>
        public void NoteFrame(float deltaSeconds, float scanCostMs, float now)
        {
            var milliseconds = deltaSeconds * 1000f;
            frameMs = frameMs <= 0f ? milliseconds : Mathf.Lerp(frameMs, milliseconds, Smoothing);
            costMs = scanCostMs;
            if (!IsSlow)
            {
                IsSlow = costMs > SlowAboveCostMs || frameMs > SlowAboveFrameMs;
                wellSince = -1f;
                return;
            }

            if (costMs >= FastBelowCostMs || frameMs > FastBelowFrameMs)
            {
                wellSince = -1f;
                return;
            }

            if (wellSince < 0f)
            {
                wellSince = now;
            }
            else if (now - wellSince >= RecoverySeconds)
            {
                IsSlow = false;
                wellSince = -1f;
            }
        }

        /// <param name="status">Android thermal status (0 = none ... 6 = shutdown), or -1 when it is not known.</param>
        public void NoteThermalStatus(int status)
        {
            IsWarm = status >= WarmStatus;
            IsHot = status >= HotStatus;
        }

        /// <summary>
        /// True once, when the scan has found nothing new for a while: the user can be told that the scan
        /// looks complete. After something new was found, it can become true again.
        /// </summary>
        /// <param name="checkedArea">Floor area the camera has checked, in square meters.</param>
        public bool LooksComplete(float checkedArea, float now)
        {
            if (areaSeenAt < 0f || checkedArea >= areaSeen + NewArea || checkedArea < areaSeen)
            {
                areaSeen = checkedArea;
                areaSeenAt = now;
                saidComplete = false;
                return false;
            }

            if (saidComplete || checkedArea < NewArea || now - areaSeenAt < CompleteAfterSeconds)
            {
                return false;
            }

            saidComplete = true;
            return true;
        }
    }

    /// <summary>How warm the phone is, as Android reports it.</summary>
    internal static class ThermalStatus
    {
        /// <summary>The status is not known (not on Android, or an Android before 10).</summary>
        public const int Unknown = -1;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject powerManager;
        static bool failed;
#endif

        /// <summary>Android thermal status: 0 none, 1 light, 2 moderate, 3 severe, 4 critical, 5 emergency, 6 shutdown.</summary>
        public static int Read()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (failed)
            {
                return Unknown;
            }

            try
            {
                if (powerManager == null)
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        if (version.GetStatic<int>("SDK_INT") < 29)
                        {
                            failed = true;
                            return Unknown;
                        }
                    }

                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        powerManager = activity.Call<AndroidJavaObject>("getSystemService", "power");
                    }
                }

                return powerManager != null ? powerManager.Call<int>("getCurrentThermalStatus") : Unknown;
            }
            catch (System.Exception exception)
            {
                failed = true;
                Debug.LogWarning("[Erasmus AR Toolkit] The thermal status of the phone cannot be read: " + exception.Message);
                return Unknown;
            }
#else
            return Unknown;
#endif
        }
    }
}
