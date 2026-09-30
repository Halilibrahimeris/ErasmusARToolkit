using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>What rises in the room.</summary>
    public enum FillKind
    {
        Water,
        Smoke,
        Objects,
    }

    /// <summary>
    /// A simulation that fills the room: water, smoke or piled-up objects rise to a level ("a year of plastic
    /// bottles", "a flood of 80 cm"). After a room scan it covers the scanned floor, else a rectangle. Real
    /// furniture higher than the water stays dry: AR occlusion hides the water behind and under it. HUD: Start /
    /// Pause, a slider and the level under the status line.
    /// </summary>
    [RequireComponent(typeof(ModulePlacement))]
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Room Fill")]
    public sealed class RoomFill : ToolkitModule
    {
        const float SmokeLayerSpacing = 0.25f;
        const int MaxSmokeLayers = 12;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmokeId = Shader.PropertyToID("_Smoke");

        [SerializeField] FillKind kind = FillKind.Water;
        [SerializeField, Min(0.05f), Tooltip("The highest level, in meters above the floor.")]
        float maxLevel = 1f;
        [SerializeField, Min(0f), Tooltip("Seconds from empty to full after Start. 0: only the slider moves it.")]
        float durationSeconds = 20f;
        [SerializeField] Color color = new Color(0.2f, 0.55f, 0.85f, 0.55f);
        [SerializeField, Tooltip("Objects: what piles up (a bottle, a bag). Empty: small capsules.")]
        GameObject objectPrefab;
        [SerializeField, Min(0.03f), Tooltip("Objects: distance between them, in meters.")]
        float objectSpacing = 0.15f;
        [SerializeField, Tooltip("The area when the room was not scanned, in meters (width, length).")]
        Vector2 areaSize = new Vector2(4f, 4f);
        [SerializeField, Tooltip("After a room scan, fill the scanned floor instead of the rectangle.")]
        bool useScannedFloor = true;
        [SerializeField, Tooltip("Shown in front of the level: \"Water · 45 cm\".")]
        string label = "Water";
        [SerializeField, TextArea(1, 3), Tooltip("Shown when the fill is full (\"A year of plastic bottles from our school\").")]
        string fullMessage = string.Empty;
        [SerializeField, Tooltip("Start rising as soon as it is placed.")]
        bool startWhenPlaced;
        [SerializeField, Tooltip("Objects: another number gives another random pile.")]
        int pileSeed = 2026;

        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<float> yaws = new List<float>();
        readonly List<GameObject> smokeLayers = new List<GameObject>();
        readonly List<List<Vector2>> worldLoops = new List<List<Vector2>>();
        ModulePlacement placement;
        GameObject surface;
        Mesh surfaceMesh;
        Material material;
        ObjectPileRenderer pile;
        Button startButton;
        Slider slider;
        bool announcedFull;
        float appliedLevel = -1f;

        public FillKind Kind => kind;

        public float MaxLevel => maxLevel;

        /// <summary>The level now, in meters above the floor.</summary>
        public float Level { get; private set; }

        public bool IsRunning { get; private set; }

        public FillArea Area { get; private set; }

        /// <summary>Objects drawn now (Objects kind).</summary>
        public int ObjectCount => kind == FillKind.Objects ? positions.Count : 0;

        public ModulePlacement Placement => placement != null ? placement : placement = GetComponent<ModulePlacement>();

        internal void Configure(FillKind fillKind, float max, float duration, Color fillColor, GameObject prefab, Vector2 size,
            string levelLabel, string message)
        {
            kind = fillKind;
            maxLevel = Mathf.Max(0.05f, max);
            durationSeconds = Mathf.Max(0f, duration);
            color = fillColor;
            objectPrefab = prefab;
            areaSize = size;
            label = levelLabel;
            fullMessage = message;
        }

        void Awake()
        {
            Placement.Placed += OnPlaced;
        }

        void OnDestroy()
        {
            if (placement != null)
            {
                placement.Placed -= OnPlaced;
            }

            ObjectUtility.SafeDestroy(surfaceMesh);
            ObjectUtility.SafeDestroy(material);
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            startButton = AddHudButton(FillText.StartButton(false), Toggle);
            slider = new Slider(0f, 1f) { value = FillLevel.Fraction(Level, maxLevel) };
            slider.RegisterValueChangedCallback(e =>
            {
                Pause();
                SetLevel(FillLevel.FromFraction(e.newValue, maxLevel));
            });
            AddHudElement(slider);
            if (Placement.IsPlaced)
            {
                OnPlaced(Placement.PlacedPose);
            }

            RefreshHud();
        }

        protected override void OnDisable()
        {
            IsRunning = false;
            startButton = null;
            slider = null;
            base.OnDisable();
        }

        void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            SetLevel(FillLevel.Advance(Level, maxLevel, durationSeconds, Time.deltaTime));
            if (Level >= maxLevel)
            {
                IsRunning = false;
                RefreshHud();
            }
        }

        public void Play()
        {
            if (Level >= maxLevel)
            {
                SetLevel(0f);
            }

            IsRunning = durationSeconds > 0f;
            RefreshHud();
        }

        public void Pause()
        {
            IsRunning = false;
            RefreshHud();
        }

        public void Toggle()
        {
            if (IsRunning)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>Sets the level (meters above the floor) and redraws.</summary>
        public void SetLevel(float level)
        {
            Level = FillLevel.Clamp(level, maxLevel);
            if (Level < maxLevel)
            {
                announcedFull = false;
            }
            else if (!announcedFull && !string.IsNullOrWhiteSpace(fullMessage))
            {
                announcedFull = true;
                Toast(fullMessage.Trim());
            }

            Redraw();
            RefreshHud();
        }

        void OnPlaced(Pose pose)
        {
            BuildArea();
            appliedLevel = -1f;
            Redraw();
            if (startWhenPlaced && IsModeReady)
            {
                Play();
            }
        }

        /// <summary>The scanned floor after a room scan, else the rectangle.</summary>
        void BuildArea()
        {
            var scanner = IsAR && useScannedFloor && Manager != null ? Manager.RoomScanner : null;
            Area = scanner != null && scanner.TryGetFloorOutline(worldLoops)
                ? FillArea.FromWorld(worldLoops, transform)
                : FillArea.Rectangle(areaSize);
            surfaceMesh = FillSurface.Build(Area, surfaceMesh);
            foreach (var layer in smokeLayers)
            {
                ObjectUtility.SafeDestroy(layer);
            }

            smokeLayers.Clear();
        }

        void Redraw()
        {
            if (Area == null || Mathf.Approximately(appliedLevel, Level))
            {
                return;
            }

            appliedLevel = Level;
            switch (kind)
            {
                case FillKind.Water:
                    DrawWater();
                    break;
                case FillKind.Smoke:
                    DrawSmoke();
                    break;
                default:
                    DrawObjects();
                    break;
            }
        }

        void DrawWater()
        {
            if (surface == null)
            {
                surface = Layer("Water Surface", 0f);
            }

            surface.SetActive(Level > 0.001f);
            surface.transform.localPosition = new Vector3(0f, Level, 0f);
        }

        void DrawSmoke()
        {
            var count = Level <= 0.001f ? 0 : Mathf.Min(MaxSmokeLayers, Mathf.CeilToInt(Level / SmokeLayerSpacing));
            while (smokeLayers.Count < count)
            {
                smokeLayers.Add(Layer("Smoke Layer " + (smokeLayers.Count + 1), 1f));
            }

            for (var i = 0; i < smokeLayers.Count; i++)
            {
                var shown = i < count;
                smokeLayers[i].SetActive(shown);
                if (shown)
                {
                    smokeLayers[i].transform.localPosition = new Vector3(0f, Level * (i + 1) / count, 0f);
                }
            }
        }

        void DrawObjects()
        {
            if (pile == null)
            {
                pile = ObjectPileRenderer.Create(transform, objectPrefab, color, Manager != null ? Manager.Theme : null);
            }

            var layerHeight = Mathf.Max(0.02f, pile.Height);
            ObjectPile.Layout(Area, Mathf.Max(objectSpacing, pile.Footprint), layerHeight, Level, IsBlocked, pileSeed, positions, yaws);
            pile.Show(positions, yaws);
        }

        /// <summary>Nothing piles up inside what the room scan found standing in the room.</summary>
        bool IsBlocked(Vector2 local)
        {
            var scanner = IsAR && Manager != null ? Manager.RoomScanner : null;
            if (scanner == null || !scanner.HasModel)
            {
                return false;
            }

            var world = transform.TransformPoint(new Vector3(local.x, 0f, local.y));
            foreach (var obstacle in scanner.Obstacles)
            {
                if (obstacle.ContainsXZ(world, 0.05f))
                {
                    return true;
                }
            }

            return false;
        }

        GameObject Layer(string name, float smoke)
        {
            if (material == null)
            {
                var theme = Manager != null ? Manager.Theme : null;
                var source = theme != null ? theme.FillMaterial : null;
                material = source != null ? new Material(source) : new Material(Shader.Find("Erasmus AR Toolkit/Fill"));
                material.name = "Room Fill";
                material.SetColor(BaseColorId, color);
                material.SetFloat(SmokeId, kind == FillKind.Smoke ? 1f : 0f);
            }

            var layer = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(transform, false);
            layer.GetComponent<MeshFilter>().sharedMesh = surfaceMesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return layer;
        }

        void RefreshHud()
        {
            if (!IsModeReady)
            {
                return;
            }

            if (startButton != null)
            {
                startButton.text = FillText.StartButton(IsRunning);
                startButton.EnableInClassList(ARToolkitHUD.ActiveButtonClass, IsRunning);
            }

            slider?.SetValueWithoutNotify(FillLevel.Fraction(Level, maxLevel));
            SetInfoLine(Placement.IsPlaced ? FillText.Line(label, Level) : null);
        }
    }

    /// <summary>Texts of the room fill.</summary>
    public static class FillText
    {
        public static string StartButton(bool running) => running ? "Pause" : "Start";

        public static string Line(string label, float level)
        {
            var name = string.IsNullOrWhiteSpace(label) ? "Level" : label.Trim();
            return $"{name} · {Mathf.RoundToInt(level * 100f)} cm";
        }
    }

    /// <summary>Draws the objects of a room fill with GPU instancing (up to <see cref="ObjectPile.MaxCount"/>).</summary>
    public sealed class ObjectPileRenderer : MonoBehaviour
    {
        const int BatchSize = 1023;

        readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        readonly Matrix4x4[] batch = new Matrix4x4[BatchSize];
        Mesh mesh;
        Material material;
        Matrix4x4 meshMatrix = Matrix4x4.identity;
        bool ownsMesh;

        /// <summary>Height of one object (one layer), in meters.</summary>
        public float Height { get; private set; } = 0.2f;

        /// <summary>Width of one object on the floor, in meters.</summary>
        public float Footprint { get; private set; } = 0.1f;

        public int Count => matrices.Count;

        public static ObjectPileRenderer Create(Transform parent, GameObject prefab, Color color, ToolkitTheme theme)
        {
            var go = new GameObject("Object Pile");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ObjectPileRenderer>();
            view.Build(prefab, color, theme);
            return view;
        }

        public void Show(List<Vector3> positions, List<float> yaws)
        {
            matrices.Clear();
            for (var i = 0; i < positions.Count; i++)
            {
                var yaw = i < yaws.Count ? yaws[i] : 0f;
                matrices.Add(Matrix4x4.TRS(positions[i], Quaternion.Euler(0f, yaw, 0f), Vector3.one) * meshMatrix);
            }
        }

        void Build(GameObject prefab, Color color, ToolkitTheme theme)
        {
            var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            var source = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter != null && filter.sharedMesh != null && source != null && source.sharedMaterial != null)
            {
                mesh = filter.sharedMesh;
                material = new Material(source.sharedMaterial);
                meshMatrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            }
            else
            {
                // Small capsules (bottles) in the fill color.
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                mesh = capsule.GetComponent<MeshFilter>().sharedMesh;
                ObjectUtility.SafeDestroy(capsule);
                var baseMaterial = theme != null ? theme.ImageTargetMaterial : null;
                material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.mainTexture = null;
                material.color = new Color(color.r, color.g, color.b, 1f);
                meshMatrix = Matrix4x4.Scale(new Vector3(0.07f, 0.09f, 0.07f));
            }

            material.enableInstancing = true;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            var bounds = mesh.bounds;
            var size = meshMatrix.MultiplyVector(bounds.size);
            Height = Mathf.Max(0.02f, Mathf.Abs(size.y));
            Footprint = Mathf.Max(0.02f, Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)));
            meshMatrix = Matrix4x4.Translate(new Vector3(0f, -meshMatrix.MultiplyPoint3x4(bounds.center).y, 0f)) * meshMatrix;
        }

        void Update()
        {
            if (mesh == null || material == null || matrices.Count == 0)
            {
                return;
            }

            var parameters = new RenderParams(material)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
                worldBounds = new Bounds(transform.position, Vector3.one * 50f),
            };
            var toWorld = transform.localToWorldMatrix;
            for (var start = 0; start < matrices.Count; start += BatchSize)
            {
                var count = Mathf.Min(BatchSize, matrices.Count - start);
                for (var i = 0; i < count; i++)
                {
                    batch[i] = toWorld * matrices[start + i];
                }

                Graphics.RenderMeshInstanced(parameters, mesh, 0, batch, count);
            }
        }

        void OnDestroy()
        {
            ObjectUtility.SafeDestroy(material);
            if (ownsMesh)
            {
                ObjectUtility.SafeDestroy(mesh);
            }
        }
    }
}
