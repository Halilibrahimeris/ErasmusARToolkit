using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Installs the packages the toolkit depends on. A .unitypackage cannot declare
    /// dependencies, so this class (which has no AR references and always compiles)
    /// takes care of it.
    /// </summary>
    [InitializeOnLoad]
    public static class DependencyInstaller
    {
        const string PromptShownKey = "ErasmusARToolkit.SetupWindowShown";

        /// <summary>
        /// ARCore pulls in AR Foundation, XR Management and XR Core Utils as dependencies.
        /// Names without versions let Unity pick the version verified for this editor. The video and AI
        /// modules are built into Unity; some project templates switch them off.
        /// </summary>
        static readonly string[] RequiredPackages =
        {
            ToolkitPaths.ArCorePackage,
            ToolkitPaths.ArFoundationPackage,
            ToolkitPaths.InputSystemPackage,
            ToolkitPaths.VideoModule,
            ToolkitPaths.AiModule,
        };

        static AddAndRemoveRequest activeRequest;

        public static bool IsInstalling => activeRequest != null && !activeRequest.IsCompleted;

        static DependencyInstaller()
        {
            EditorApplication.delayCall += ShowSetupWindowIfNeeded;
        }

        public static IReadOnlyList<string> GetMissingPackages()
        {
            var installed = GetInstalledPackageNames();
            return RequiredPackages.Where(name => !installed.Contains(name)).ToArray();
        }

        public static bool IsPackageInstalled(string packageName)
        {
            return GetInstalledPackageNames().Contains(packageName);
        }

        static HashSet<string> GetInstalledPackageNames()
        {
            return new HashSet<string>(
                UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p => p.name));
        }

        [MenuItem(ToolkitPaths.InstallMenuPath, priority = 100)]
        public static void InstallMissingPackages()
        {
            if (IsInstalling)
            {
                Debug.Log($"[{ToolkitPaths.DisplayName}] Package installation is already running.");
                return;
            }

            var missing = GetMissingPackages();
            if (missing.Count == 0)
            {
                Debug.Log($"[{ToolkitPaths.DisplayName}] All required packages are already installed.");
                return;
            }

            Debug.Log($"[{ToolkitPaths.DisplayName}] Installing: {string.Join(", ", missing)}");
            try
            {
                activeRequest = Client.AddAndRemove(packagesToAdd: missing.ToArray());
                EditorApplication.update += PollRequest;
            }
            catch (Exception e)
            {
                activeRequest = null;
                Debug.LogError($"[{ToolkitPaths.DisplayName}] Could not start package installation: {e.Message}");
            }
        }

        static void PollRequest()
        {
            if (activeRequest == null || !activeRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= PollRequest;
            if (activeRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[{ToolkitPaths.DisplayName}] Packages installed. Open {ToolkitPaths.OpenMenuPath} to continue setup.");
            }
            else
            {
                var message = activeRequest.Error != null ? activeRequest.Error.message : "Unknown error";
                Debug.LogError($"[{ToolkitPaths.DisplayName}] Package installation failed: {message}");
            }

            activeRequest = null;
        }

        static void ShowSetupWindowIfNeeded()
        {
            if (Application.isBatchMode || SessionState.GetBool(PromptShownKey, false))
            {
                return;
            }

            SessionState.SetBool(PromptShownKey, true);
            if (GetMissingPackages().Count > 0)
            {
                SetupRequiredWindow.Open();
            }
        }
    }
}
