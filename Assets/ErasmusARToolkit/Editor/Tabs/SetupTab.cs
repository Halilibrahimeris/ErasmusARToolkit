using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Checklist of everything the project needs, with one-click fixes (SetupTab.uxml).</summary>
    public sealed class SetupTab : IToolkitTab
    {
        readonly StatusRow arFoundationRow;
        readonly StatusRow arCoreRow;
        readonly StatusRow inputRow;
        readonly StatusRow tmpRow;
        readonly StatusRow androidModuleRow;
        readonly StatusRow windowsModuleRow;
        readonly StatusRow urpRow;
        readonly StatusRow arCoreEnabledRow;
        readonly StatusRow androidPlayerRow;
        readonly StatusRow urpFeaturesRow;
        readonly StatusRow urpStencilRow;
        readonly StatusRow depthOptionalRow;
        readonly StatusRow phoneRenderingRow;
        readonly StatusRow validationRow;
        readonly Button openValidationButton;
        readonly StatusRow configRow;
        readonly StatusRow scanAssetsRow;
        readonly StatusRow sceneUpgradeRow;
        readonly TextField productName;
        readonly TextField companyName;
        readonly TextField packageName;

        public SetupTab()
        {
            Root = EditorUi.Clone("SetupTab");
            arFoundationRow = EditorUi.Q<StatusRow>(Root, "row-arfoundation");
            arCoreRow = EditorUi.Q<StatusRow>(Root, "row-arcore");
            inputRow = EditorUi.Q<StatusRow>(Root, "row-input");
            tmpRow = EditorUi.Q<StatusRow>(Root, "row-tmp");
            androidModuleRow = EditorUi.Q<StatusRow>(Root, "row-android-module");
            windowsModuleRow = EditorUi.Q<StatusRow>(Root, "row-windows-module");
            urpRow = EditorUi.Q<StatusRow>(Root, "row-urp");
            arCoreEnabledRow = EditorUi.Q<StatusRow>(Root, "row-arcore-enabled");
            androidPlayerRow = EditorUi.Q<StatusRow>(Root, "row-android-player");
            urpFeaturesRow = EditorUi.Q<StatusRow>(Root, "row-urp-features");
            urpStencilRow = EditorUi.Q<StatusRow>(Root, "row-urp-stencil");
            depthOptionalRow = EditorUi.Q<StatusRow>(Root, "row-depth-optional");
            phoneRenderingRow = EditorUi.Q<StatusRow>(Root, "row-phone-rendering");
            validationRow = EditorUi.Q<StatusRow>(Root, "row-validation");
            openValidationButton = EditorUi.Q<Button>(Root, "open-validation");
            configRow = EditorUi.Q<StatusRow>(Root, "row-config");
            scanAssetsRow = EditorUi.Q<StatusRow>(Root, "row-scan-assets");
            sceneUpgradeRow = EditorUi.Q<StatusRow>(Root, "row-scene-upgrade");
            productName = EditorUi.Q<TextField>(Root, "product-name");
            companyName = EditorUi.Q<TextField>(Root, "company-name");
            packageName = EditorUi.Q<TextField>(Root, "package-name");

            EditorUi.OnClick(Root, "run-all", () => EditorApplication.delayCall += RunAll);
            openValidationButton.clicked += ValidationFixer.OpenValidationWindow;
            productName.RegisterValueChangedCallback(evt => SetProductName(evt.newValue));
            companyName.RegisterValueChangedCallback(evt => SetCompanyName(evt.newValue));
        }

        public string Title => "Setup";
        public VisualElement Root { get; }

        public void Refresh()
        {
            var arCoreInstalled = DependencyInstaller.IsPackageInstalled(ToolkitPaths.ArCorePackage);
            var arCoreEnabled = arCoreInstalled && ProjectConfigurator.IsArCoreEnabled;

            arFoundationRow.SetState(true);
            arCoreRow.SetState(arCoreInstalled, DependencyInstaller.InstallMissingPackages);
            inputRow.SetState(ProjectConfigurator.IsInputSystemActive, ProjectConfigurator.OpenPlayerSettings);
            tmpRow.SetState(ProjectConfigurator.AreTmpEssentialsImported, Do(ProjectConfigurator.ImportTmpEssentials));
            androidModuleRow.SetState(ProjectConfigurator.IsAndroidModuleInstalled, ShowModuleHelp);
            windowsModuleRow.SetState(ProjectConfigurator.IsWindowsModuleInstalled, ShowModuleHelp);
            urpRow.SetState(ProjectConfigurator.IsUrpActive, () => SettingsService.OpenProjectSettings("Project/Graphics"));
            arCoreEnabledRow.SetState(arCoreEnabled, Do(() => ProjectConfigurator.ConfigureAndroid()));
            androidPlayerRow.SetState(ProjectConfigurator.IsAndroidPlayerReady, Do(() => ProjectConfigurator.ConfigureAndroid()));
            urpFeaturesRow.SetState(UrpRendererConfigurator.IsConfigured, Do(() => UrpRendererConfigurator.AddMissingFeatures()));
            urpStencilRow.SetState(UrpRendererConfigurator.IsStencilSafe, Do(() => UrpRendererConfigurator.MakeStencilSafe()));
            depthOptionalRow.SetState(ProjectConfigurator.IsDepthOptional,
                arCoreEnabled ? Do(() => ProjectConfigurator.SetDepthOptional()) : null);
            phoneRenderingRow.SetState(UrpRendererConfigurator.IsPhoneRenderingTuned,
                Do(UrpRendererConfigurator.TunePhoneRendering));
            RefreshValidation(arCoreEnabled);
            configRow.SetState(ToolkitAssets.FindConfig() != null,
                Do(() => Selection.activeObject = ToolkitAssets.GetOrCreateConfig()));
            scanAssetsRow.SetState(ToolkitAssets.HasRoomScanAssets(),
                Do(() => ToolkitAssets.EnsureRoomScanAssets(ToolkitAssets.GetOrCreateTheme())));
            sceneUpgradeRow.SetState(!SceneUpgrader.AnyOpenSceneNeedsUpgrade(),
                Do(() => SceneUpgrader.UpgradeOpenScenes()));

            productName.SetValueWithoutNotify(PlayerSettings.productName);
            companyName.SetValueWithoutNotify(PlayerSettings.companyName);
            packageName.SetValueWithoutNotify(
                PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android));
        }

        void RefreshValidation(bool arCoreEnabled)
        {
            EditorUi.SetShown(openValidationButton, false);
            if (!arCoreEnabled)
            {
                validationRow.SetState(false, null, "XR Project Validation (enable ARCore first)");
                return;
            }

            var issues = ValidationFixer.GetIssues(BuildTargetGroup.Android);
            if (issues == null)
            {
                validationRow.SetState(false, ValidationFixer.OpenValidationWindow, "XR Project Validation");
                return;
            }

            var errors = issues.Count(issue => issue.Error);
            var warnings = issues.Count - errors;
            var ok = issues.Count == 0;
            validationRow.SetState(ok, Do(() => ValidationFixer.FixAll(BuildTargetGroup.Android)),
                ok ? "XR Project Validation: no issues" : $"XR Project Validation: {errors} error(s), {warnings} warning(s)");
            EditorUi.SetShown(openValidationButton, !ok);
        }

        /// <summary>Wraps a fix so the checklist refreshes afterwards.</summary>
        Action Do(Action fix)
        {
            return () =>
            {
                fix();
                Refresh();
            };
        }

        void RunAll()
        {
            if (DependencyInstaller.GetMissingPackages().Count > 0)
            {
                DependencyInstaller.InstallMissingPackages();
                EditorUtility.DisplayDialog(ToolkitPaths.DisplayName,
                    "Installing packages. When Unity finishes compiling, press 'Run All Setup Steps' again.", "OK");
                return;
            }

            ToolkitSetup.RunAll();
            Refresh();
        }

        static void SetProductName(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != PlayerSettings.productName)
            {
                PlayerSettings.productName = value.Trim();
            }
        }

        static void SetCompanyName(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != PlayerSettings.companyName)
            {
                PlayerSettings.companyName = value.Trim();
            }
        }

        static void ShowModuleHelp()
        {
            EditorUtility.DisplayDialog("Install Build Support",
                "Open Unity Hub > Installs, click the gear next to Unity " + Application.unityVersion +
                ", choose 'Add modules' and tick:\n\n" +
                "• Android Build Support (with OpenJDK and Android SDK & NDK Tools)\n" +
                "• Windows Build Support (IL2CPP) - optional, Mono works too\n\n" +
                "Restart the Editor afterwards.", "OK");
        }
    }
}
