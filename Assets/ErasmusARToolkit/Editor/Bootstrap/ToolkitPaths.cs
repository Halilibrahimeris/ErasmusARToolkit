namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Shared constants for the editor tooling. Lives in the bootstrap assembly so it
    /// is available even before AR Foundation is installed.
    /// </summary>
    public static class ToolkitPaths
    {
        public const string DisplayName = "Erasmus AR Toolkit";
        public const string Version = "1.1.0";

        public const string MenuRoot = "Tools/Erasmus AR Toolkit/";
        public const string OpenMenuPath = MenuRoot + "Open Toolkit";
        public const string InstallMenuPath = MenuRoot + "Install Dependencies";

        /// <summary>Folder that is exported as the .unitypackage.</summary>
        public const string ToolkitRoot = "Assets/ErasmusARToolkit";

        /// <summary>Per-project user data. Never exported, never overwritten by package updates.</summary>
        public const string ContentRoot = "Assets/ARToolkitContent";
        public const string GeneratedFolder = ContentRoot + "/Generated";
        public const string ScenesFolder = ContentRoot + "/Scenes";
        public const string ConfigAssetPath = ContentRoot + "/ARToolkitConfig.asset";
        public const string ImageLibraryAssetPath = GeneratedFolder + "/ARToolkitImageLibrary.asset";
        public const string DefaultScenePath = ScenesFolder + "/ARScene.unity";

        public const string PrefabsFolder = ToolkitRoot + "/Prefabs";
        public const string SamplesFolder = ToolkitRoot + "/Samples";
        public const string TestsFolder = ToolkitRoot + "/Tests";

        public const string ArFoundationPackage = "com.unity.xr.arfoundation";
        public const string ArCorePackage = "com.unity.xr.arcore";
        public const string InputSystemPackage = "com.unity.inputsystem";
    }
}
