using System.Globalization;
using Erasmus.ARToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// What a build needs from the toolkit assets. These tests only read: a missing asset means that
    /// Tools > Erasmus AR Toolkit > Run Setup has to run (and its result has to be committed).
    /// </summary>
    public class ToolkitBuildChecksTests
    {
        const string SetupHint = " Run Tools > Erasmus AR Toolkit > Run Setup.";

        [Test]
        public void Theme_HasRoomScanAssets()
        {
            var theme = ToolkitAssets.FindTheme();

            Assert.IsNotNull(theme, "The theme asset is missing." + SetupHint);
            Assert.IsTrue(theme.HasRoomScanAssets, "The theme has no room scan materials or shaders." + SetupHint);
            Assert.IsTrue(ToolkitAssets.HasRoomScanAssets());
            Assert.AreEqual("Erasmus AR Toolkit/Surface Depth", theme.SurfaceDepthShader.name);
            Assert.AreEqual("Erasmus AR Toolkit/Occluder", theme.OccluderShader.name);
            Assert.AreSame(theme.SurfaceDepthShader, theme.SurfaceDepthMaterial.shader);
            Assert.AreSame(theme.OccluderShader, theme.OccluderMaterial.shader);
        }

        [Test]
        public void HasRoomScanAssets_NeedsMaterialsAndShaders()
        {
            var theme = ScriptableObject.CreateInstance<ToolkitTheme>();
            var shader = Shader.Find("Erasmus AR Toolkit/Occluder");
            var material = new Material(shader);
            try
            {
                Assert.IsFalse(theme.HasRoomScanAssets);

                theme.SetOccluderMaterial(material);
                theme.SetSurfaceDepthMaterial(material);
                Assert.IsFalse(theme.HasRoomScanAssets, "Materials alone are not enough: the shaders are referenced too.");

                theme.SetRoomScanShaders(shader, shader);
                Assert.IsTrue(theme.HasRoomScanAssets);
            }
            finally
            {
                Object.DestroyImmediate(theme);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void PlanePrefab_ShowsOnlyTrackedPlanes()
        {
            var prefab = ToolkitAssets.FindAsset<GameObject>("ARToolkitPlane");

            Assert.IsNotNull(prefab, "The AR plane prefab is missing." + SetupHint);
            Assert.AreEqual(TrackingState.Tracking,
                prefab.GetComponent<ARPlaneMeshVisualizer>().trackingStateVisibilityThreshold,
                "Planes with limited tracking have an unreliable pose and must not be drawn." + SetupHint);
        }

        [Test]
        public void ConfigurePlaneObject_SetsTheThreshold_AndReportsNoChangeTheSecondTime()
        {
            var plane = new GameObject("Plane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer),
                typeof(MeshFilter), typeof(MeshRenderer), typeof(LineRenderer));
            try
            {
                var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
                var serialized = new SerializedObject(visualizer);
                serialized.FindProperty("m_TrackingStateVisibilityThreshold").intValue = (int)TrackingState.Limited;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(ToolkitAssets.ConfigurePlaneObject(plane, null));

                Assert.AreEqual(TrackingState.Tracking, visualizer.trackingStateVisibilityThreshold);
                Assert.IsNotNull(plane.GetComponent<MeshCollider>());
                Assert.IsFalse(ToolkitAssets.ConfigurePlaneObject(plane, null));
            }
            finally
            {
                Object.DestroyImmediate(plane);
            }
        }
    }

    public class ScanDiagnosticsTests
    {
        [Test]
        public void ToLine_UsesDotDecimals_WhateverTheLanguageOfThePhone()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // Turkish writes decimals with a comma.
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                var diagnostics = new ScanDiagnostics
                {
                    Mapping = MappingState.Verified,
                    DepthLagMs = 12.4f,
                    DepthWidth = 160,
                    DepthHeight = 90,
                    ConfidentRatio = 0.625f,
                    CameraHeight = 0.031f,
                    FloorHeight = -1.19f,
                    PlaneFloorHeight = -1.172f,
                    Floor = FloorState.Candidate,
                    LevelCount = 4,
                    DepthSlotsUsed = 3,
                    DepthSlots = 8,
                    FramesIntegrated = 128,
                    IntegrateMs = 1.84f,
                    RebuildMs = 3.2f,
                    BackgroundMode = "BeforeOpaques",
                    ThermalStatus = -1,
                };

                var line = diagnostics.ToLine();

                StringAssert.Contains("map=Verified", line);
                StringAssert.Contains("depth=160x90", line);
                StringAssert.Contains("lag=12ms", line);
                StringAssert.Contains("conf=0.63", line);
                StringAssert.Contains("cam=0.03", line);
                StringAssert.Contains("floor=-1.190(Candidate)", line);
                StringAssert.Contains("plane=-1.172", line);
                StringAssert.Contains("levels=4", line);
                StringAssert.Contains("slots=3/8", line);
                StringAssert.Contains("frames=128", line);
                StringAssert.Contains("int=1.8", line);
                StringAssert.Contains("bg=BeforeOpaques", line);
                StringAssert.DoesNotContain(",", line.Replace("(Candidate,plane)", string.Empty));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ToLine_UnknownValues_AreDashes()
        {
            var diagnostics = new ScanDiagnostics
            {
                DepthLagMs = float.NaN,
                FloorHeight = float.NaN,
                PlaneFloorHeight = float.NaN,
                CameraHeight = float.NaN,
            };

            var line = diagnostics.ToLine();

            StringAssert.Contains("lag=-ms", line);
            StringAssert.Contains("floor=-(Unknown)", line);
            StringAssert.Contains("plane=-", line);
            StringAssert.Contains("bg=?", line);
        }
    }

    public class DepthResidualsTests
    {
        static ScanFrame FloorPoints(float distance, params float[] heights)
        {
            var frame = new ScanFrame();
            var origin = new Vector3(0f, 1.2f, 0f);
            frame.Reset(origin, true);
            foreach (var height in heights)
            {
                // A point at the given height and distance from the camera, straight ahead.
                var drop = origin.y - height;
                var forward = Mathf.Sqrt(Mathf.Max(0f, distance * distance - drop * drop));
                frame.Add(new Vector3(0f, height, forward), true);
            }

            return frame;
        }

        [Test]
        public void Add_SortsFloorPointsByDistance_AndMeasuresBiasAndSpread()
        {
            var residuals = new DepthResiduals();

            residuals.Add(FloorPoints(1.4f, 0.01f, -0.01f), 0f);
            residuals.Add(FloorPoints(3.0f, -0.02f, -0.06f), 0f);

            Assert.AreEqual(2, residuals.Count(0));
            Assert.AreEqual(0f, residuals.Mean(0), 1e-4f);
            Assert.AreEqual(0.01f, residuals.Spread(0), 1e-4f);
            Assert.AreEqual(0, residuals.Count(1));
            Assert.AreEqual(2, residuals.Count(2));
            Assert.AreEqual(-0.04f, residuals.Mean(2), 1e-4f, "Points below the floor show as a negative mean.");
            Assert.AreEqual(0.02f, residuals.Spread(2), 1e-4f);
        }

        [Test]
        public void Add_SkipsPointsFarFromTheFloor_AndPointsThatDoNotFaceUp()
        {
            var residuals = new DepthResiduals();
            var frame = FloorPoints(1.4f, 0.4f);
            frame.Add(new Vector3(0f, 0f, 0.8f), false);

            residuals.Add(frame, 0f);

            Assert.AreEqual(0, residuals.Count(0));
        }

        [Test]
        public void ToLine_WritesCentimeters_AndClearEmptiesIt()
        {
            var residuals = new DepthResiduals();
            residuals.Add(FloorPoints(1.4f, -0.01f, -0.03f), 0f);

            StringAssert.StartsWith("0.5-1.5m -2.0±1.0cm (2) | 1.5-2.5m -", residuals.ToLine());

            residuals.Clear();
            StringAssert.StartsWith("0.5-1.5m - | ", residuals.ToLine());
        }
    }

    public class FirstSceneHintTests
    {
        const string Sample = "Assets/ErasmusARToolkit/Samples/Scenes/ARToolkitDemo.unity";
        const string Own = "Assets/ARToolkitContent/Scenes/ARScene.unity";

        [Test]
        public void SampleFirst_WithAnOwnSceneLater_Warns()
        {
            var hint = BuildPaths.FirstSceneHint(new[] { Sample, Own });

            StringAssert.Contains("ARToolkitDemo", hint);
            StringAssert.Contains("ARScene", hint);
        }

        [Test]
        public void OwnSceneFirst_OrOnlyTheSample_IsFine()
        {
            Assert.IsNull(BuildPaths.FirstSceneHint(new[] { Own, Sample }));
            Assert.IsNull(BuildPaths.FirstSceneHint(new[] { Sample }));
            Assert.IsNull(BuildPaths.FirstSceneHint(new[] { Sample, "Assets/Scenes/SampleScene.unity" }));
            Assert.IsNull(BuildPaths.FirstSceneHint(new string[0]));
            Assert.IsNull(BuildPaths.FirstSceneHint(null));
        }
    }
}
