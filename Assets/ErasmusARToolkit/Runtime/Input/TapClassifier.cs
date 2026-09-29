using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Tells a quick tap apart from a drag or a long press.</summary>
    public static class TapClassifier
    {
        public static bool IsTap(Vector2 start, Vector2 end, float durationSeconds,
            float maxMovePixels, float maxDurationSeconds)
        {
            return (end - start).sqrMagnitude <= maxMovePixels * maxMovePixels
                   && durationSeconds <= maxDurationSeconds;
        }
    }
}
