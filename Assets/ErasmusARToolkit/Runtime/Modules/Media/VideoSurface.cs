using UnityEngine;
using UnityEngine.Video;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Plays a video on the object's own renderer (a quad) and/or a sound. It pauses when the object is
    /// switched off (the image was lost) and goes on where it stopped when it is switched on again. Tap the
    /// object and use Pause / Play in its panel. Living posters and video exhibits use it.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("Erasmus AR Toolkit/Video Surface")]
    public sealed class VideoSurface : MonoBehaviour
    {
        const string BaseMapProperty = "_BaseMap";

        [SerializeField] VideoClip video;
        [SerializeField, Tooltip("Plays together with the video, or alone.")]
        AudioClip audioClip;
        [SerializeField] bool loop = true;
        [SerializeField, Range(0f, 1f)] float volume = 1f;
        [SerializeField, Tooltip("Starts playing when the object appears.")]
        bool playOnEnable = true;

        VideoPlayer player;
        AudioSource videoSound;
        AudioSource sound;
        bool wantsPlaying;

        public VideoClip Video => video;

        public AudioClip AudioClip => audioClip;

        /// <summary>True while it plays (false while paused by the user or while the object is off).</summary>
        public bool IsPlaying => (player != null && player.isPlaying) || (sound != null && sound.isPlaying);

        /// <summary>The user wants it to play (it plays whenever the object is on).</summary>
        public bool WantsPlaying => wantsPlaying;

        public void Configure(VideoClip videoClip, AudioClip audio, bool loopPlayback, float soundVolume)
        {
            video = videoClip;
            audioClip = audio;
            loop = loopPlayback;
            volume = Mathf.Clamp01(soundVolume);
            player = null;
            sound = null;
            videoSound = null;
        }

        void OnEnable()
        {
            if (playOnEnable || wantsPlaying)
            {
                Play();
            }
        }

        void OnDisable()
        {
            // Keep what the user wanted: it goes on when the image is seen again.
            var wanted = wantsPlaying;
            PauseOutputs();
            wantsPlaying = wanted;
            playOnEnable = false;
        }

        public void Play()
        {
            wantsPlaying = true;
            EnsureOutputs();
            if (player != null)
            {
                player.Play();
            }

            if (sound != null && !sound.isPlaying)
            {
                if (sound.time > 0f)
                {
                    sound.UnPause();
                }
                else
                {
                    sound.Play();
                }
            }
        }

        public void Pause()
        {
            PauseOutputs();
        }

        public void TogglePlay()
        {
            if (wantsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        void PauseOutputs()
        {
            wantsPlaying = false;
            if (player != null && player.isPlaying)
            {
                player.Pause();
            }

            if (sound != null && sound.isPlaying)
            {
                sound.Pause();
            }
        }

        void EnsureOutputs()
        {
            if (video != null && player == null)
            {
                player = gameObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.clip = video;
                player.isLooping = loop;
                player.renderMode = VideoRenderMode.MaterialOverride;
                player.targetMaterialRenderer = GetComponent<Renderer>();
                player.targetMaterialProperty = BaseMapProperty;
                player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                videoSound = gameObject.AddComponent<AudioSource>();
                videoSound.playOnAwake = false;
                videoSound.volume = volume;
                player.SetTargetAudioSource(0, videoSound);
            }

            if (audioClip != null && sound == null)
            {
                sound = gameObject.AddComponent<AudioSource>();
                sound.playOnAwake = false;
                sound.clip = audioClip;
                sound.loop = loop;
                sound.volume = volume;
            }
        }
    }
}
