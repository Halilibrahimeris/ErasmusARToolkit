using NUnit.Framework;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    public class ManipulationMathTests
    {
        static readonly Vector2 Limits = new Vector2(0.5f, 2f);

        [Test]
        public void TwistDelta_CounterClockwiseQuarterTurn_IsPositive90()
        {
            var delta = ManipulationMath.TwistDeltaDegrees(
                Vector2.zero, Vector2.right, Vector2.zero, Vector2.up);
            Assert.AreEqual(90f, delta, 0.01f);
        }

        [Test]
        public void TwistDelta_ClockwiseQuarterTurn_IsNegative90()
        {
            var delta = ManipulationMath.TwistDeltaDegrees(
                Vector2.zero, Vector2.up, Vector2.zero, Vector2.right);
            Assert.AreEqual(-90f, delta, 0.01f);
        }

        [Test]
        public void TwistDelta_DegenerateFingers_IsZero()
        {
            Assert.AreEqual(0f, ManipulationMath.TwistDeltaDegrees(Vector2.one, Vector2.one, Vector2.zero, Vector2.up));
        }

        [Test]
        public void PinchScale_SpreadingFingers_ScalesUpProportionally()
        {
            Assert.AreEqual(1.5f, ManipulationMath.PinchScale(1f, 100f, 150f, Limits), 0.0001f);
        }

        [Test]
        public void PinchScale_IsClampedToLimits()
        {
            Assert.AreEqual(2f, ManipulationMath.PinchScale(1.5f, 100f, 400f, Limits), 0.0001f);
            Assert.AreEqual(0.5f, ManipulationMath.PinchScale(1f, 400f, 10f, Limits), 0.0001f);
        }

        [Test]
        public void PinchScale_ZeroPreviousDistance_KeepsScale()
        {
            Assert.AreEqual(1.2f, ManipulationMath.PinchScale(1.2f, 0f, 100f, Limits), 0.0001f);
        }

        [Test]
        public void StepScale_PositiveAndNegativeSteps()
        {
            Assert.AreEqual(1.1f, ManipulationMath.StepScale(1f, 1f, 0.1f, Limits), 0.0001f);
            Assert.AreEqual(1f / 1.1f, ManipulationMath.StepScale(1f, -1f, 0.1f, Limits), 0.0001f);
            Assert.AreEqual(1f, ManipulationMath.StepScale(1f, 0f, 0.1f, Limits), 0.0001f);
        }

        [Test]
        public void ClampScale_HandlesInvertedLimits()
        {
            Assert.AreEqual(2f, ManipulationMath.ClampScale(10f, new Vector2(2f, 0.5f)), 0.0001f);
            Assert.AreEqual(0.5f, ManipulationMath.ClampScale(0.1f, new Vector2(2f, 0.5f)), 0.0001f);
        }

        [Test]
        public void YawTowards_FacesTargetOnHorizontalPlaneOnly()
        {
            var rotation = ManipulationMath.YawTowards(Vector3.zero, new Vector3(0f, 5f, 3f));
            var forward = rotation * Vector3.forward;
            Assert.AreEqual(0f, forward.y, 0.0001f);
            Assert.AreEqual(1f, forward.z, 0.0001f);
        }

        [Test]
        public void YawTowards_TargetDirectlyAbove_ReturnsIdentity()
        {
            Assert.AreEqual(Quaternion.identity, ManipulationMath.YawTowards(Vector3.zero, Vector3.up * 2f));
        }
    }
}
