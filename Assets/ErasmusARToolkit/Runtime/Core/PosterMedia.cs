using System;
using UnityEngine;
using UnityEngine.Video;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Video and sound of a living poster: when the camera sees the printed image, the video plays on top
    /// of it and the sound starts; they pause when the image is lost and go on when it is seen again.
    /// Use H.264 MP4 video: other formats may not play on Android.
    /// </summary>
    [Serializable]
    public sealed class PosterMedia
    {
        [SerializeField, Tooltip("Plays on top of the printed image, at its size. H.264 MP4 works on every phone.")]
        VideoClip video;
        [SerializeField, Tooltip("Plays when the image is seen (a narration, music).")]
        AudioClip audio;
        [SerializeField] bool loop = true;
        [SerializeField, Range(0f, 1f)] float volume = 1f;

        public PosterMedia()
        {
        }

        public PosterMedia(VideoClip video, AudioClip audio, bool loop = true, float volume = 1f)
        {
            this.video = video;
            this.audio = audio;
            this.loop = loop;
            this.volume = Mathf.Clamp01(volume);
        }

        public VideoClip Video => video;
        public AudioClip Audio => audio;
        public bool Loop => loop;
        public float Volume => Mathf.Clamp01(volume);

        public bool HasVideo => video != null;
        public bool HasAudio => audio != null;
        public bool HasAny => HasVideo || HasAudio;
    }
}
