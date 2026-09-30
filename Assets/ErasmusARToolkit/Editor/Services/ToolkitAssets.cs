using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Finds or creates the assets the toolkit needs: config, theme, materials, plane prefab.</summary>
    public static class ToolkitAssets
    {
        const string SettingsFolder = ToolkitPaths.ToolkitRoot + "/Settings";
        const string MaterialsFolder = ToolkitPaths.ToolkitRoot + "/Materials";
        const string ThemePath = SettingsFolder + "/ARToolkitTheme.asset";
        const string PlanePrefabPath = ToolkitPaths.PrefabsFolder + "/ARToolkitPlane.prefab";
        const string UiFolder = ToolkitPaths.ToolkitRoot + "/UI";
        const string PanelSettingsName = "ARToolkitPanelSettings";
        public const string HudLayoutName = "ARToolkitHUD";

        const string UnlitShader = "Universal Render Pipeline/Unlit";
        const string LitShader = "Universal Render Pipeline/Lit";
        const string SurfaceShader = "Erasmus AR Toolkit/AR Surface";
        const string OccluderShader = "Erasmus AR Toolkit/Occluder";
        const string SurfaceDepthShader = "Erasmus AR Toolkit/Surface Depth";
        const string ObstacleShader = "Erasmus AR Toolkit/Obstacle";

        /// <summary>How see-through the boxes around obstacles are, and how strong their edges.</summary>
        const float ObstacleFillAlpha = 0.18f;
        const float ObstacleEdgeAlpha = 0.9f;

        const float PlaneEdgeWidth = 0.01f;

        public static ARToolkitConfig FindConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ARToolkitConfig>(ToolkitPaths.ConfigAssetPath);
            if (config != null || !AssetDatabase.IsValidFolder(ToolkitPaths.ContentRoot))
            {
                return config;
            }

            var guids = AssetDatabase.FindAssets("t:" + nameof(ARToolkitConfig), new[] { ToolkitPaths.ContentRoot });
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<ARToolkitConfig>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        public static ARToolkitConfig GetOrCreateConfig()
        {
            var config = FindConfig();
            if (config != null)
            {
                return config;
            }

            EnsureFolder(ToolkitPaths.ContentRoot);
            config = ScriptableObject.CreateInstance<ARToolkitConfig>();
            AssetDatabase.CreateAsset(config, ToolkitPaths.ConfigAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Erasmus AR Toolkit] Created config at " + ToolkitPaths.ConfigAssetPath, config);
            return config;
        }

        /// <summary>The theme asset, or null when the setup never ran. Only reads.</summary>
        public static ToolkitTheme FindTheme()
        {
            return AssetDatabase.LoadAssetAtPath<ToolkitTheme>(ThemePath);
        }

        public static ToolkitTheme GetOrCreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ToolkitTheme>(ThemePath);
            if (theme == null)
            {
                EnsureFolder(SettingsFolder);
                theme = ScriptableObject.CreateInstance<ToolkitTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            if (!theme.HasAllMaterials)
            {
                theme.SetMaterials(
                    GetOrCreateUnlit("ARToolkitGround", Color.white, true),
                    GetOrCreateUnlit("ARToolkitHighlight", theme.AccentColor, false),
                    GetOrCreateUnlit("ARToolkitImageTarget", Color.white, false),
                    GetOrCreateUnlit("ARToolkitLabelBackground", new Color(0f, 0.08f, 0.25f, 0.82f), true));
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
            }

            // The highlight draws the placement ring, the selection ring and image frames: marks on real surfaces.
            UseSurfaceShader(theme.HighlightMaterial, RenderOrder.SurfaceMark);

            // The image target material shows stand-ins for the real world in Desktop mode (webcam picture,
            // simulated images), so it draws before the surface marks, like the AR camera image does.
            SetOpaqueRenderQueue(theme.ImageTargetMaterial, RenderOrder.CameraImage);
            EnsureRoomScanAssets(theme);
            return theme;
        }

        /// <summary>The theme exists and has the room scan materials and shaders. Only reads.</summary>
        public static bool HasRoomScanAssets()
        {
            var theme = FindTheme();
            return theme != null && theme.HasRoomScanAssets;
        }

        /// <summary>
        /// Depth-only materials of the room scan: table occluders and the depth correction of scanned
        /// surfaces. They live on the theme together with their shaders. Every toolkit scene refers to the
        /// theme, so the shaders are part of every build. Returns false when something is still missing.
        /// </summary>
        public static bool EnsureRoomScanAssets(ToolkitTheme theme)
        {
            if (theme == null)
            {
                return false;
            }

            var occluderShader = Shader.Find(OccluderShader);
            var surfaceDepthShader = Shader.Find(SurfaceDepthShader);
            var occluder = GetOrKeepShaderMaterial(theme.OccluderMaterial, occluderShader,
                "ARToolkitOccluder", OccluderShader, RenderOrder.RoomOccluder);
            var surfaceDepth = GetOrKeepShaderMaterial(theme.SurfaceDepthMaterial, surfaceDepthShader,
                "ARToolkitSurfaceDepth", SurfaceDepthShader, RenderOrder.SurfaceDepth);
            var obstacleShader = Shader.Find(ObstacleShader);
            var obstacle = GetOrKeepObstacleMaterial(theme.ObstacleMaterial, obstacleShader, "ARToolkitObstacle",
                RenderOrder.ObstacleFill, theme.AccentColor, ObstacleFillAlpha, CullMode.Back);
            var obstacleEdge = GetOrKeepObstacleMaterial(theme.ObstacleEdgeMaterial, obstacleShader,
                "ARToolkitObstacleEdge", RenderOrder.ObstacleOutline, theme.AccentColor, ObstacleEdgeAlpha, CullMode.Off);
            if (theme.OccluderMaterial != occluder || theme.SurfaceDepthMaterial != surfaceDepth
                || theme.OccluderShader != occluderShader || theme.SurfaceDepthShader != surfaceDepthShader
                || theme.ObstacleMaterial != obstacle || theme.ObstacleEdgeMaterial != obstacleEdge
                || theme.ObstacleShader != obstacleShader)
            {
                theme.SetOccluderMaterial(occluder);
                theme.SetSurfaceDepthMaterial(surfaceDepth);
                theme.SetRoomScanShaders(occluderShader, surfaceDepthShader);
                theme.SetObstacleAssets(obstacle, obstacleEdge, obstacleShader);
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
            }

            return theme.HasRoomScanAssets;
        }

        /// <summary>
        /// A material for the boxes around obstacles. A new one gets the accent color of the theme; one
        /// that exists keeps the color it was given.
        /// </summary>
        static Material GetOrKeepObstacleMaterial(Material current, Shader shader, string name, int renderQueue,
            Color color, float alpha, CullMode cull)
        {
            var isNew = (current == null || shader == null || current.shader != shader)
                        && AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + name + ".mat") == null;
            var material = GetOrKeepShaderMaterial(current, shader, name, ObstacleShader, renderQueue);
            if (material == null)
            {
                return null;
            }

            if (isNew)
            {
                material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
            }

            if (!Mathf.Approximately(material.GetFloat("_Cull"), (float)cull))
            {
                material.SetFloat("_Cull", (float)cull);
                isNew = true;
            }

            if (isNew)
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>The theme's own material when it uses the shader (queue corrected), else the toolkit's.</summary>
        static Material GetOrKeepShaderMaterial(Material current, Shader shader, string name, string shaderName,
            int renderQueue)
        {
            if (current == null || shader == null || current.shader != shader)
            {
                return GetOrCreateShaderMaterial(name, shaderName, renderQueue);
            }

            if (current.renderQueue != renderQueue)
            {
                current.renderQueue = renderQueue;
                EditorUtility.SetDirty(current);
            }

            return current;
        }

        /// <summary>A material asset with the given shader and render queue, made if missing. Null if the shader is missing.</summary>
        static Material GetOrCreateShaderMaterial(string name, string shaderName, int renderQueue)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[Erasmus AR Toolkit] Shader '{shaderName}' not found. Re-import the toolkit package.");
                    return null;
                }

                EnsureFolder(MaterialsFolder);
                material = new Material(shader) { name = name, renderQueue = renderQueue };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.renderQueue != renderQueue)
            {
                material.renderQueue = renderQueue;
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        /// <summary>Runtime UI Toolkit panel settings for the HUD (scales like a 1080x1920 screen).</summary>
        public static PanelSettings GetOrCreatePanelSettings()
        {
            var settings = FindAsset<PanelSettings>(PanelSettingsName);
            if (settings != null)
            {
                return settings;
            }

            EnsureFolder(UiFolder);
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = FindAsset<ThemeStyleSheet>("ARToolkitRuntimeTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1080, 1920);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 10;
            AssetDatabase.CreateAsset(settings, UiFolder + "/" + PanelSettingsName + ".asset");
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>
        /// Loads a toolkit asset by file name. Checks the default toolkit folders first and then
        /// searches the project, so the toolkit keeps working if its folder is moved.
        /// </summary>
        public static T FindAsset<T>(string fileNameWithoutExtension) where T : Object
        {
            var guids = AssetDatabase.FindAssets($"{fileNameWithoutExtension} t:{typeof(T).Name}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == fileNameWithoutExtension)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// The AR plane prefab: grid fill, surface outline and a collider. Prefabs and materials made by
        /// older toolkit versions are upgraded in place (same assets, so scenes keep their references).
        /// </summary>
        public static GameObject GetOrCreatePlanePrefab(ToolkitTheme theme)
        {
            var planeMaterial = GetOrCreateUnlit("ARToolkitPlane", Color.white, true);
            EnsurePlaneGrid(planeMaterial, theme);
            UseSurfaceShader(planeMaterial, RenderOrder.PlaneFill);
            var edgeMaterial = GetOrCreatePlaneEdge(theme);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (prefab != null)
            {
                UpgradePlanePrefab(edgeMaterial);
                return AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            }

            EnsureFolder(ToolkitPaths.PrefabsFolder);

            var go = new GameObject("ARToolkit Plane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer),
                typeof(MeshFilter), typeof(MeshRenderer), typeof(LineRenderer));
            try
            {
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = planeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;

                var line = go.GetComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.shadowCastingMode = ShadowCastingMode.Off;
                ConfigurePlaneObject(go, edgeMaterial);

                prefab = PrefabUtility.SaveAsPrefabAsset(go, PlanePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            return prefab;
        }

        static void UpgradePlanePrefab(Material edgeMaterial)
        {
            var root = PrefabUtility.LoadPrefabContents(PlanePrefabPath);
            try
            {
                if (ConfigurePlaneObject(root, edgeMaterial))
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
                    Debug.Log("[Erasmus AR Toolkit] Updated the AR plane prefab (surface outline, collider).");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Adds a collider (ARPlaneMeshVisualizer fills it with the plane mesh) and the surface outline.
        /// The outline is drawn twice as wide because the stencil hides its inner half. Planes are drawn
        /// only while they are tracked: a plane with limited tracking has an unreliable pose. Returns true
        /// if anything changed.
        /// </summary>
        internal static bool ConfigurePlaneObject(GameObject plane, Material edgeMaterial)
        {
            var changed = false;
            if (plane.GetComponent<MeshCollider>() == null)
            {
                plane.AddComponent<MeshCollider>();
                changed = true;
            }

            changed |= ShowOnlyWhileTracked(plane.GetComponent<ARPlaneMeshVisualizer>());

            var line = plane.GetComponent<LineRenderer>();
            if (line != null && (line.sharedMaterial != edgeMaterial
                                 || !Mathf.Approximately(line.widthMultiplier, PlaneEdgeWidth)))
            {
                line.sharedMaterial = edgeMaterial;
                line.widthMultiplier = PlaneEdgeWidth;
                changed = true;
            }

            return changed;
        }

        /// <summary>Set on the serialized field: the property setter expects a running plane.</summary>
        static bool ShowOnlyWhileTracked(ARPlaneMeshVisualizer visualizer)
        {
            if (visualizer == null)
            {
                return false;
            }

            var serialized = new SerializedObject(visualizer);
            var threshold = serialized.FindProperty("m_TrackingStateVisibilityThreshold");
            if (threshold == null || threshold.intValue == (int)TrackingState.Tracking)
            {
                return false;
            }

            threshold.intValue = (int)TrackingState.Tracking;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        static Material GetOrCreatePlaneEdge(ToolkitTheme theme)
        {
            const string path = MaterialsFolder + "/ARToolkitPlaneEdge.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EnsureFolder(MaterialsFolder);
                material = new Material(FindShader(SurfaceShader, UnlitShader))
                {
                    name = "ARToolkitPlaneEdge",
                    color = theme.AccentColor,
                };
                AssetDatabase.CreateAsset(material, path);
            }

            UseSurfaceShader(material, RenderOrder.PlaneOutline);
            return material;
        }

        /// <summary>
        /// Moves an opaque URP material to another queue. URP's material editor recomputes the queue from
        /// the surface type plus _QueueOffset, so the offset is set too or the change would not stick.
        /// </summary>
        static void SetOpaqueRenderQueue(Material material, int renderQueue)
        {
            if (material == null || material.renderQueue == renderQueue)
            {
                return;
            }

            if (material.HasProperty("_QueueOffset"))
            {
                material.SetFloat("_QueueOffset", renderQueue - (int)RenderQueue.Geometry);
            }

            material.renderQueue = renderQueue;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Switches a material to the toolkit surface shader (texture and color carry over by name).</summary>
        static void UseSurfaceShader(Material material, int renderQueue)
        {
            if (material == null)
            {
                return;
            }

            var shader = Shader.Find(SurfaceShader);
            if (shader == null)
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] Shader '{SurfaceShader}' not found. Re-import the toolkit package.");
                return;
            }

            if (material.shader == shader && material.renderQueue == renderQueue)
            {
                return;
            }

            material.shader = shader;
            material.shaderKeywords = new string[0];
            material.renderQueue = renderQueue;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Gives detected AR planes a grid texture (like the floor of a VR guardian) so users see
        /// where objects can be placed. AR Foundation plane UVs are in meters, so tiling 4 = 25 cm cells.
        /// </summary>
        static void EnsurePlaneGrid(Material planeMaterial, ToolkitTheme theme)
        {
            if (planeMaterial.mainTexture != null)
            {
                return;
            }

            const string texturePath = MaterialsFolder + "/ARToolkitPlaneGrid.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                var fill = theme.AccentColor;
                fill.a = 0.14f;
                var generated = GridTextureFactory.Create(128, 4, new Color(1f, 1f, 1f, 0.75f), fill);
                try
                {
                    File.WriteAllBytes(texturePath, generated.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(generated);
                }

                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }

            planeMaterial.color = Color.white;
            planeMaterial.mainTexture = texture;
            planeMaterial.mainTextureScale = new Vector2(4f, 4f);
            EditorUtility.SetDirty(planeMaterial);
            AssetDatabase.SaveAssets();
        }

        public static Material GetOrCreateUnlit(string name, Color color, bool transparent)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            EnsureFolder(MaterialsFolder);
            material = new Material(FindShader(UnlitShader, "Unlit/Color")) { name = name };
            material.color = color;
            if (transparent)
            {
                MakeTransparent(material);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static Material GetOrCreateLit(string folder, string name, Color color)
        {
            var path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            EnsureFolder(folder);
            material = new Material(FindShader(LitShader, "Standard")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Configures a URP Unlit/Lit material for alpha blending, two-sided.</summary>
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        static Shader FindShader(string preferred, string fallback)
        {
            var shader = Shader.Find(preferred);
            if (shader == null)
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] Shader '{preferred}' not found (is URP installed?). Using '{fallback}'.");
                shader = Shader.Find(fallback);
            }

            return shader;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
