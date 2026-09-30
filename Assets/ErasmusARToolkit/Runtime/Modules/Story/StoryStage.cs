using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// A character of a story. The toolkit's simple character (capsule and head) or any model; a model with an
    /// Animator gets its "Speed" (float) and "Talk" (bool) parameters set when it has them. Walks on the NavMesh
    /// the story builds, around what the room scan found.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Story Character")]
    public sealed class StoryCharacter : MonoBehaviour
    {
        const float ArriveDistance = 0.15f;
        const float TurnDegreesPerSecond = 240f;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int TalkId = Animator.StringToHash("Talk");

        [SerializeField] string displayName = "Character";
        [SerializeField, Min(0.2f)] float walkSpeed = 1.1f;
        [SerializeField, Tooltip("Optional. Found on the character when empty.")]
        Animator animator;

        NavMeshAgent agent;
        AudioSource voice;
        Vector3? destination;
        Vector3? lookAt;
        float bob;
        Transform body;
        Vector3 bodyRest;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();

        public float WalkSpeed => walkSpeed;

        public bool IsWalking => destination.HasValue;

        /// <summary>True when the last walk arrived (or no walk was asked for).</summary>
        public bool HasArrived => !destination.HasValue;

        public bool IsTurning => lookAt.HasValue;

        internal void Configure(string characterName, float speed)
        {
            displayName = characterName;
            walkSpeed = Mathf.Max(0.2f, speed);
        }

        void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            // The toolkit's simple character bobs its body while it walks.
            body = transform.Find("Body");
            if (body != null)
            {
                bodyRest = body.localPosition;
            }
        }

        /// <summary>Puts the character on the NavMesh (after the story built it).</summary>
        public void UseNavMesh()
        {
            if (!NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
            {
                return;
            }

            transform.position = hit.position;
            if (agent == null)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
            }

            agent.enabled = true;
            agent.Warp(hit.position);
            agent.speed = walkSpeed;
            agent.angularSpeed = 360f;
            agent.acceleration = 8f;
            agent.radius = 0.25f;
            agent.height = 1.7f;
            agent.stoppingDistance = 0.05f;
            agent.autoBraking = true;
        }

        public void WalkTo(Vector3 position)
        {
            lookAt = null;
            destination = position;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(position, out var hit, 2f, NavMesh.AllAreas))
                {
                    destination = hit.position;
                }

                agent.isStopped = false;
                agent.SetDestination(destination.Value);
            }
        }

        public void TurnTowards(Vector3 position)
        {
            lookAt = position;
        }

        public void Stop()
        {
            destination = null;
            lookAt = null;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
        }

        public void SetTalking(bool talking)
        {
            SetBool(TalkId, talking);
        }

        public void PlayTrigger(string trigger)
        {
            if (animator != null && !string.IsNullOrWhiteSpace(trigger))
            {
                animator.SetTrigger(trigger.Trim());
            }
        }

        public void Say(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0.8f;
            }

            voice.clip = clip;
            voice.Play();
        }

        void Update()
        {
            var speed = 0f;
            if (destination.HasValue)
            {
                speed = UpdateWalk();
            }

            if (lookAt.HasValue)
            {
                var direction = lookAt.Value - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    lookAt = null;
                }
                else
                {
                    var wanted = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, TurnDegreesPerSecond * Time.deltaTime);
                    if (Quaternion.Angle(transform.rotation, wanted) < 2f)
                    {
                        lookAt = null;
                    }
                }
            }

            SetFloat(SpeedId, speed);
            Bob(speed);
        }

        float UpdateWalk()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(ArriveDistance, agent.stoppingDistance))
                {
                    destination = null;
                    return 0f;
                }

                return agent.velocity.magnitude;
            }

            // No NavMesh: walk straight, on the ground.
            var target = destination.Value;
            var flat = new Vector3(target.x, transform.position.y, target.z);
            var toTarget = flat - transform.position;
            if (toTarget.magnitude <= ArriveDistance)
            {
                transform.position = flat;
                destination = null;
                return 0f;
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget.normalized, Vector3.up),
                TurnDegreesPerSecond * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, flat, walkSpeed * Time.deltaTime);
            return walkSpeed;
        }

        void Bob(float speed)
        {
            if (body == null)
            {
                return;
            }

            if (speed > 0.05f)
            {
                bob += Time.deltaTime * 10f;
                body.localPosition = bodyRest + Vector3.up * (Mathf.Abs(Mathf.Sin(bob)) * 0.05f);
            }
            else
            {
                bob = 0f;
                body.localPosition = bodyRest;
            }
        }

        void SetFloat(int id, float value)
        {
            if (animator != null && HasParameter(id, AnimatorControllerParameterType.Float))
            {
                animator.SetFloat(id, value);
            }
        }

        void SetBool(int id, bool value)
        {
            if (animator != null && HasParameter(id, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(id, value);
            }
        }

        bool HasParameter(int id, AnimatorControllerParameterType type)
        {
            if (animator.runtimeAnimatorController == null)
            {
                return false;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == id && parameter.type == type)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A named place of a story ("Door", "Table"). Characters walk to it.</summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Story Point")]
    public sealed class StoryPoint : MonoBehaviour
    {
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.2f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.3f, name);
        }
#endif
    }

    /// <summary>
    /// The walkable area of a story: the floor (the scanned floor, else a square around the story) with what
    /// stands in the room and the walls cut out. Pure setup of NavMesh sources, so it can be tested.
    /// </summary>
    public static class StoryNavMesh
    {
        public const int Walkable = 0;
        public const int NotWalkable = 1;
        const float WallThickness = 0.15f;

        /// <param name="floor">The floor area: center at floor height, size in x and z.</param>
        public static List<NavMeshBuildSource> Sources(Bounds floor, IEnumerable<ObstacleVolume> obstacles, IEnumerable<WallSegment> walls)
        {
            var sources = new List<NavMeshBuildSource>
            {
                Box(Matrix4x4.TRS(floor.center + Vector3.down * 0.01f, Quaternion.identity, Vector3.one),
                    new Vector3(floor.size.x, 0.02f, floor.size.z), Walkable),
            };

            if (obstacles != null)
            {
                foreach (var obstacle in obstacles)
                {
                    var pose = obstacle.Pose;
                    sources.Add(Box(Matrix4x4.TRS(pose.position + Vector3.up * (obstacle.Size.y * 0.5f), pose.rotation, Vector3.one),
                        obstacle.Size, NotWalkable));
                }
            }

            if (walls != null)
            {
                foreach (var wall in walls)
                {
                    var along = wall.B - wall.A;
                    if (along.sqrMagnitude < 1e-4f)
                    {
                        continue;
                    }

                    var middle = (wall.A + wall.B) * 0.5f;
                    var height = Mathf.Max(0.5f, wall.Top - wall.Bottom);
                    var rotation = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y).normalized, Vector3.up);
                    sources.Add(Box(Matrix4x4.TRS(new Vector3(middle.x, wall.Bottom + height * 0.5f, middle.y), rotation, Vector3.one),
                        new Vector3(WallThickness, height, along.magnitude), NotWalkable));
                }
            }

            return sources;
        }

        public static NavMeshDataInstance Build(List<NavMeshBuildSource> sources, Bounds floor)
        {
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.25f;
            settings.agentHeight = 1.7f;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 30f;
            var bounds = new Bounds(floor.center, new Vector3(floor.size.x + 1f, 4f, floor.size.z + 1f));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            return NavMesh.AddNavMeshData(data);
        }

        static NavMeshBuildSource Box(Matrix4x4 transform, Vector3 size, int area)
        {
            return new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = transform,
                size = size,
                area = area,
            };
        }
    }

    /// <summary>
    /// A story played by characters in the room. Its steps (in the Inspector) say who does what: say a line,
    /// walk to a point, turn to the user, wait ... and when the story goes on (at once, on a tap, when the user
    /// comes close). The characters walk around what the room scan found. HUD: Next, Restart, Pause; what is
    /// said appears as a bubble and a subtitle.
    /// </summary>
    [RequireComponent(typeof(ModulePlacement))]
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Story Stage")]
    public sealed class StoryStage : ToolkitModule, ITapHandler
    {
        [SerializeField] List<StoryStep> steps = new List<StoryStep>();
        [SerializeField, Tooltip("Start again after the last step.")]
        bool loop;
        [SerializeField, Min(2f), Tooltip("The walkable square around the story when the room was not scanned (meters).")]
        float areaSize = 10f;
        [SerializeField, Tooltip("Build a NavMesh so characters walk around obstacles (else they walk straight).")]
        bool buildNavMesh = true;

        StoryRunner runner;
        ModulePlacement placement;
        NavMeshDataInstance navMesh;
        InfoLabel bubble;
        Button nextButton;
        Button pauseButton;
        bool tapped;
        bool paused;
        bool actionStarted;

        public IReadOnlyList<StoryStep> Steps => steps;

        public StoryRunner Runner => runner;

        public bool IsPaused => paused;

        public ModulePlacement Placement => placement != null ? placement : placement = GetComponent<ModulePlacement>();

        bool ITapHandler.WantsTap => runner != null && !paused && runner.WantsTap;

        internal void SetSteps(IEnumerable<StoryStep> storySteps)
        {
            steps = new List<StoryStep>(storySteps);
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

            RemoveNavMesh();
        }

        protected override void OnModeReady(ToolkitMode mode)
        {
            nextButton = AddHudButton(StoryText.Next, Next);
            AddHudButton(StoryText.Restart, Restart);
            pauseButton = AddHudButton(StoryText.PauseButton(false), TogglePause);
            if (Interaction != null)
            {
                Interaction.PushTapHandler(this);
            }

            if (Placement.IsPlaced && runner == null)
            {
                OnPlaced(Placement.PlacedPose);
            }

            RefreshHud();
        }

        protected override void OnDisable()
        {
            if (Interaction != null)
            {
                Interaction.RemoveTapHandler(this);
            }

            EndLine();
            nextButton = null;
            pauseButton = null;
            base.OnDisable();
        }

        /// <summary>Goes on when the story waits for a tap (the Next button, a tap on the scene).</summary>
        public void Next()
        {
            tapped = true;
        }

        public void Restart()
        {
            EndLine();
            foreach (var character in GetComponentsInChildren<StoryCharacter>())
            {
                character.Stop();
            }

            paused = false;
            runner?.Start();
            RefreshHud();
        }

        public void TogglePause()
        {
            paused = !paused;
            RefreshHud();
        }

        void ITapHandler.OnTap(Vector2 screenPosition)
        {
            Next();
        }

        void OnPlaced(Pose pose)
        {
            if (buildNavMesh)
            {
                BuildWalkableArea();
            }

            foreach (var character in GetComponentsInChildren<StoryCharacter>())
            {
                if (buildNavMesh)
                {
                    character.UseNavMesh();
                }
            }

            runner = new StoryRunner(steps);
            runner.StepStarted += OnStepStarted;
            runner.StepEnded += OnStepEnded;
            runner.Finished += OnFinished;
            runner.Start();
            foreach (var index in runner.Skipped)
            {
                Debug.LogWarning($"[Erasmus AR Toolkit] Story step {index + 1} of '{name}' misses its character, point or target and is left out.", this);
            }

            RefreshHud();
        }

        void Update()
        {
            if (runner == null || paused || runner.Phase == StoryPhase.Finished || runner.Phase == StoryPhase.NotStarted)
            {
                tapped = false;
                return;
            }

            var step = runner.Current;
            var wasTapped = tapped;
            tapped = false;
            runner.Tick(Time.deltaTime, IsActionDone(step), wasTapped, UserDistance(step));
            RefreshHud();
        }

        bool IsActionDone(StoryStep step)
        {
            if (step == null)
            {
                return true;
            }

            switch (step.Action)
            {
                case StoryAction.WalkTo: return actionStarted && step.Character != null && step.Character.HasArrived;
                case StoryAction.TurnToUser: return step.Character == null || !step.Character.IsTurning;
                default: return true;
            }
        }

        float UserDistance(StoryStep step)
        {
            var camera = ViewCamera;
            var character = step != null ? step.Character : null;
            if (camera == null || character == null)
            {
                return float.MaxValue;
            }

            var a = camera.transform.position;
            var b = character.transform.position;
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        void OnStepStarted(int index, StoryStep step)
        {
            actionStarted = true;
            var character = step.Character;
            switch (step.Action)
            {
                case StoryAction.Say:
                    if (character != null)
                    {
                        character.SetTalking(true);
                        character.Say(step.Audio);
                        var theme = Manager != null ? Manager.Theme : null;
                        bubble = InfoLabel.Create(character.transform, new InfoLabelData(character.DisplayName, step.Text, true), theme);
                    }

                    SetSubtitle(StoryText.Subtitle(character != null ? character.DisplayName : null, step.Text));
                    break;
                case StoryAction.WalkTo:
                    character.WalkTo(step.Point.transform.position);
                    break;
                case StoryAction.TurnToUser:
                    var camera = ViewCamera;
                    if (camera != null)
                    {
                        character.TurnTowards(camera.transform.position);
                    }

                    break;
                case StoryAction.PlayAnimation:
                    character.PlayTrigger(step.AnimationTrigger);
                    break;
                case StoryAction.Show:
                    step.Target.SetActive(true);
                    break;
                case StoryAction.Hide:
                    step.Target.SetActive(false);
                    break;
            }
        }

        void OnStepEnded(int index, StoryStep step)
        {
            actionStarted = false;
            if (step.Action == StoryAction.Say)
            {
                if (step.Character != null)
                {
                    step.Character.SetTalking(false);
                }

                EndLine();
            }
        }

        void OnFinished()
        {
            EndLine();
            if (loop)
            {
                runner.Start();
                return;
            }

            Toast(StoryText.TheEnd);
            RefreshHud();
        }

        void EndLine()
        {
            if (bubble != null)
            {
                ObjectUtility.SafeDestroy(bubble.gameObject);
                bubble = null;
            }

            SetSubtitle(null);
        }

        /// <summary>The floor to walk on, without what stands in the room: from the room scan when there is one.</summary>
        void BuildWalkableArea()
        {
            RemoveNavMesh();
            var scanner = IsAR && Manager != null ? Manager.RoomScanner : null;
            var floor = new Bounds(transform.position, new Vector3(areaSize, 0f, areaSize));
            IEnumerable<ObstacleVolume> obstacles = null;
            IEnumerable<WallSegment> walls = null;
            if (scanner != null && scanner.HasModel)
            {
                var loops = new List<List<Vector2>>();
                if (scanner.TryGetFloorOutline(loops))
                {
                    var points = loops.SelectMany(l => l).ToList();
                    var min = new Vector2(points.Min(p => p.x), points.Min(p => p.y));
                    var max = new Vector2(points.Max(p => p.x), points.Max(p => p.y));
                    var height = float.IsNaN(scanner.FloorHeight) ? transform.position.y : scanner.FloorHeight;
                    floor = new Bounds(new Vector3((min.x + max.x) * 0.5f, height, (min.y + max.y) * 0.5f),
                        new Vector3(max.x - min.x, 0f, max.y - min.y));
                }

                obstacles = scanner.Obstacles;
                walls = scanner.Walls;
            }

            navMesh = StoryNavMesh.Build(StoryNavMesh.Sources(floor, obstacles, walls), floor);
        }

        void RemoveNavMesh()
        {
            if (navMesh.valid)
            {
                NavMesh.RemoveNavMeshData(navMesh);
            }

            navMesh = default;
        }

        void RefreshHud()
        {
            if (!IsModeReady)
            {
                return;
            }

            if (nextButton != null)
            {
                nextButton.SetEnabled(runner != null && runner.WantsTap && !paused);
                nextButton.EnableInClassList(ARToolkitHUD.ActiveButtonClass, runner != null && runner.Phase == StoryPhase.WaitingForTap);
            }

            if (pauseButton != null)
            {
                pauseButton.text = StoryText.PauseButton(paused);
            }
        }
    }

    /// <summary>Texts of the story.</summary>
    public static class StoryText
    {
        public const string Next = "Next";
        public const string Restart = "Restart";
        public const string TheEnd = "The end.";

        public static string PauseButton(bool paused) => paused ? "Resume" : "Pause";

        public static string Subtitle(string speaker, string text)
        {
            text = (text ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(speaker) ? text : $"{speaker.Trim()}: {text}";
        }
    }
}
