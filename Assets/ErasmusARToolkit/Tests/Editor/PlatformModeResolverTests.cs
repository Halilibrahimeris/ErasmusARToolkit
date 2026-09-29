using NUnit.Framework;
using UnityEngine.XR.ARFoundation;

namespace Erasmus.ARToolkit.Tests
{
    public class PlatformModeResolverTests
    {
        [Test]
        public void Auto_OnDesktop_ReturnsDesktop_RegardlessOfSessionState()
        {
            Assert.AreEqual(ToolkitMode.Desktop,
                PlatformModeResolver.Resolve(ModeOverride.Auto, false, ARSessionState.Ready));
            Assert.AreEqual(ToolkitMode.Desktop,
                PlatformModeResolver.Resolve(ModeOverride.Auto, false, ARSessionState.None));
        }

        [TestCase(ARSessionState.Ready)]
        [TestCase(ARSessionState.SessionInitializing)]
        [TestCase(ARSessionState.SessionTracking)]
        [TestCase(ARSessionState.NeedsInstall)]
        [TestCase(ARSessionState.Installing)]
        public void Auto_OnMobile_WithUsableSession_ReturnsAR(ARSessionState state)
        {
            Assert.AreEqual(ToolkitMode.AR, PlatformModeResolver.Resolve(ModeOverride.Auto, true, state));
        }

        [TestCase(ARSessionState.Unsupported)]
        [TestCase(ARSessionState.None)]
        public void Auto_OnMobile_WithoutSupport_FallsBackToDesktop(ARSessionState state)
        {
            Assert.AreEqual(ToolkitMode.Desktop, PlatformModeResolver.Resolve(ModeOverride.Auto, true, state));
        }

        [Test]
        public void Auto_OnMobile_WhileChecking_IsUndetermined()
        {
            Assert.AreEqual(ToolkitMode.Undetermined,
                PlatformModeResolver.Resolve(ModeOverride.Auto, true, ARSessionState.CheckingAvailability));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Overrides_AlwaysWin(bool isMobile)
        {
            Assert.AreEqual(ToolkitMode.AR,
                PlatformModeResolver.Resolve(ModeOverride.ForceAR, isMobile, ARSessionState.Unsupported));
            Assert.AreEqual(ToolkitMode.Desktop,
                PlatformModeResolver.Resolve(ModeOverride.ForceDesktop, isMobile, ARSessionState.Ready));
        }

        [Test]
        public void NeedsAvailabilityCheck_OnlyForAutoOnMobile()
        {
            Assert.IsTrue(PlatformModeResolver.NeedsAvailabilityCheck(ModeOverride.Auto, true));
            Assert.IsFalse(PlatformModeResolver.NeedsAvailabilityCheck(ModeOverride.Auto, false));
            Assert.IsFalse(PlatformModeResolver.NeedsAvailabilityCheck(ModeOverride.ForceAR, true));
            Assert.IsFalse(PlatformModeResolver.NeedsAvailabilityCheck(ModeOverride.ForceDesktop, true));
        }

        [Test]
        public void IsFallback_TrueOnlyWhenMobileEndsUpInDesktop()
        {
            Assert.IsTrue(PlatformModeResolver.IsFallback(ModeOverride.Auto, true, ToolkitMode.Desktop));
            Assert.IsFalse(PlatformModeResolver.IsFallback(ModeOverride.Auto, false, ToolkitMode.Desktop));
            Assert.IsFalse(PlatformModeResolver.IsFallback(ModeOverride.ForceDesktop, true, ToolkitMode.Desktop));
            Assert.IsFalse(PlatformModeResolver.IsFallback(ModeOverride.Auto, true, ToolkitMode.AR));
        }
    }
}
