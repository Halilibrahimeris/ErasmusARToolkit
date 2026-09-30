using System;
using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>What a story step does.</summary>
    public enum StoryAction
    {
        /// <summary>The character says the text (bubble, subtitle, optional sound).</summary>
        Say,

        /// <summary>The character walks to the point, around what stands in the room.</summary>
        WalkTo,

        /// <summary>The character turns to the user.</summary>
        TurnToUser,

        /// <summary>The character's Animator gets the trigger (models with an Animator).</summary>
        PlayAnimation,

        /// <summary>Nothing happens for a while.</summary>
        Wait,

        /// <summary>The target object appears.</summary>
        Show,

        /// <summary>The target object disappears.</summary>
        Hide,
    }

    /// <summary>When a story goes on to the next step.</summary>
    public enum StoryAdvance
    {
        /// <summary>As soon as the step is done.</summary>
        Auto,

        /// <summary>When the user taps (or presses Next).</summary>
        Tap,

        /// <summary>When the user comes close to the character.</summary>
        UserNear,
    }

    /// <summary>One step of a story, edited in the Inspector of the Story.</summary>
    [Serializable]
    public sealed class StoryStep
    {
        [SerializeField] StoryCharacter character;
        [SerializeField] StoryAction action = StoryAction.Say;
        [SerializeField, TextArea(1, 4), Tooltip("Say: what the character says.")]
        string text = string.Empty;
        [SerializeField, Tooltip("Say: a recorded voice (optional).")]
        AudioClip audio;
        [SerializeField, Tooltip("Walk To: where the character goes.")]
        StoryPoint point;
        [SerializeField, Tooltip("Play Animation: the trigger of the character's Animator.")]
        string animationTrigger = string.Empty;
        [SerializeField, Min(0f), Tooltip("Say: how long the text stays at least (0: by its length). Wait / Play Animation: seconds.")]
        float seconds;
        [SerializeField] StoryAdvance advance = StoryAdvance.Auto;
        [SerializeField, Min(0.3f), Tooltip("User Near: how close (meters) the user comes.")]
        float nearMeters = 1.5f;
        [SerializeField, Tooltip("Show / Hide: the object.")]
        GameObject target;

        public StoryStep()
        {
        }

        public StoryStep(StoryCharacter character, StoryAction action, string text = "", StoryAdvance advance = StoryAdvance.Auto,
            StoryPoint point = null, float seconds = 0f, GameObject target = null, string animationTrigger = "", float nearMeters = 1.5f)
        {
            this.character = character;
            this.action = action;
            this.text = text ?? string.Empty;
            this.advance = advance;
            this.point = point;
            this.seconds = Mathf.Max(0f, seconds);
            this.target = target;
            this.animationTrigger = animationTrigger ?? string.Empty;
            this.nearMeters = Mathf.Max(0.3f, nearMeters);
        }

        public StoryCharacter Character => character;
        public StoryAction Action => action;
        public string Text => text ?? string.Empty;
        public AudioClip Audio => audio;
        public StoryPoint Point => point;
        public string AnimationTrigger => animationTrigger ?? string.Empty;
        public float Seconds => seconds;
        public StoryAdvance Advance => advance;
        public float NearMeters => nearMeters;
        public GameObject Target => target;

        /// <summary>The step has what it needs (a character for character steps, a point to walk to ...).</summary>
        public bool IsPlayable
        {
            get
            {
                switch (action)
                {
                    case StoryAction.Wait: return true;
                    case StoryAction.Show:
                    case StoryAction.Hide: return target != null;
                    case StoryAction.WalkTo: return character != null && point != null;
                    default: return character != null;
                }
            }
        }

        /// <summary>The shortest time the step lasts: reading time for Say, the seconds for Wait and animations.</summary>
        public float MinimumSeconds
        {
            get
            {
                switch (action)
                {
                    case StoryAction.Say: return seconds > 0f ? seconds : StoryTiming.ReadingSeconds(text);
                    case StoryAction.Wait:
                    case StoryAction.PlayAnimation: return seconds;
                    default: return 0f;
                }
            }
        }
    }

    public static class StoryTiming
    {
        /// <summary>Time to read a line: 1.5 s plus a little per letter, at most 8 s.</summary>
        public static float ReadingSeconds(string text)
        {
            return Mathf.Min(8f, 1.5f + 0.05f * (text ?? string.Empty).Length);
        }
    }

    /// <summary>Where a story is.</summary>
    public enum StoryPhase
    {
        NotStarted,
        Acting,
        WaitingForTap,
        WaitingForUser,
        Finished,
    }

    /// <summary>
    /// Runs the steps of a story one after the other. The stage tells it each frame whether the current
    /// step's action is done (a walk arrived), whether the user tapped and how far the user is from the
    /// step's character. Pure logic, so it can be tested.
    /// </summary>
    public sealed class StoryRunner
    {
        readonly IReadOnlyList<StoryStep> steps;
        readonly List<int> skipped = new List<int>();

        public StoryRunner(IReadOnlyList<StoryStep> storySteps)
        {
            steps = storySteps ?? Array.Empty<StoryStep>();
        }

        public int Index { get; private set; } = -1;

        public StoryPhase Phase { get; private set; } = StoryPhase.NotStarted;

        public float Elapsed { get; private set; }

        public StoryStep Current => Index >= 0 && Index < steps.Count ? steps[Index] : null;

        /// <summary>Steps that were left out because they miss a character, a point or a target.</summary>
        public IReadOnlyList<int> Skipped => skipped;

        public event Action<int, StoryStep> StepStarted;
        public event Action<int, StoryStep> StepEnded;
        public event Action Finished;

        public bool WantsTap => Phase == StoryPhase.WaitingForTap
                                || (Phase == StoryPhase.Acting && Current != null && Current.Advance == StoryAdvance.Tap
                                    && Current.Action == StoryAction.Say);

        public void Start()
        {
            Index = -1;
            skipped.Clear();
            Phase = StoryPhase.Acting;
            MoveNext();
        }

        /// <param name="actionDone">The current step's action is over (always true for steps that only take time).</param>
        /// <param name="tapped">The user tapped (or pressed Next) since the last tick.</param>
        /// <param name="userDistance">Meters from the user to the step's character.</param>
        public void Tick(float deltaSeconds, bool actionDone, bool tapped, float userDistance)
        {
            var step = Current;
            if (step == null)
            {
                return;
            }

            switch (Phase)
            {
                case StoryPhase.Acting:
                    Elapsed += deltaSeconds;
                    if (tapped && WantsTap)
                    {
                        // A tap while a line is shown finishes it.
                        MoveNext();
                        return;
                    }

                    if (!actionDone || Elapsed < step.MinimumSeconds)
                    {
                        return;
                    }

                    switch (step.Advance)
                    {
                        case StoryAdvance.Tap:
                            Phase = StoryPhase.WaitingForTap;
                            break;
                        case StoryAdvance.UserNear:
                            Phase = StoryPhase.WaitingForUser;
                            Tick(0f, true, false, userDistance);
                            break;
                        default:
                            MoveNext();
                            break;
                    }

                    break;
                case StoryPhase.WaitingForTap:
                    if (tapped)
                    {
                        MoveNext();
                    }

                    break;
                case StoryPhase.WaitingForUser:
                    if (userDistance <= step.NearMeters)
                    {
                        MoveNext();
                    }

                    break;
            }
        }

        void MoveNext()
        {
            if (Current != null)
            {
                StepEnded?.Invoke(Index, Current);
            }

            while (true)
            {
                Index++;
                Elapsed = 0f;
                if (Index >= steps.Count)
                {
                    Index = steps.Count;
                    Phase = StoryPhase.Finished;
                    Finished?.Invoke();
                    return;
                }

                var step = steps[Index];
                if (step != null && step.IsPlayable)
                {
                    Phase = StoryPhase.Acting;
                    StepStarted?.Invoke(Index, step);
                    return;
                }

                skipped.Add(Index);
            }
        }
    }
}
