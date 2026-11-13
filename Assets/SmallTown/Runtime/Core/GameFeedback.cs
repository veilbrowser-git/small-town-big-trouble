using System.Collections.Generic;
using UnityEngine;

namespace SmallTown
{
    public enum FeedbackSound
    {
        HandTap,
        Grab,
        Release,
        Store,
        Take,
        Reject,
        Success,
        Click,
        Whoosh,
        Gunshot,
        Alarm,
        Cash,
        Ding,
        Siren,
    }

    /// <summary>
    /// Small pooled one-shot audio player with procedurally generated placeholder
    /// sounds, so the prototype has clear audio feedback without imported assets.
    /// </summary>
    public sealed class GameFeedback : MonoBehaviour
    {
        const int PoolSize = 10;
        const int SampleRate = 22050;

        static GameFeedback instance;
        readonly Dictionary<FeedbackSound, AudioClip> clips = new Dictionary<FeedbackSound, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        int next;

        /// <summary>Counts plays per sound; tests use it to assert that feedback fired.</summary>
        public static readonly Dictionary<FeedbackSound, int> PlayCounts = new Dictionary<FeedbackSound, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            PlayCounts.Clear();
        }

        public static void Play(FeedbackSound sound, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            PlayCounts.TryGetValue(sound, out int count);
            PlayCounts[sound] = count + 1;
            if (!Application.isPlaying) return;

            if (instance == null)
            {
                var host = new GameObject("GameFeedback");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<GameFeedback>();
            }
            instance.PlayInternal(sound, position, volume, pitch);
        }

        void Awake()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("Voice" + i);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.minDistance = 0.5f;
                source.maxDistance = 20f;
                source.rolloffMode = AudioRolloffMode.Linear;
                pool.Add(source);
            }
        }

        void PlayInternal(FeedbackSound sound, Vector3 position, float volume, float pitch)
        {
            if (!clips.TryGetValue(sound, out AudioClip clip))
            {
                clip = Build(sound);
                clips[sound] = clip;
            }
            AudioSource source = pool[next];
            next = (next + 1) % pool.Count;
            source.transform.position = position;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(volume);
            source.clip = clip;
            source.Play();
        }

        static AudioClip Build(FeedbackSound sound)
        {
            switch (sound)
            {
                case FeedbackSound.HandTap: return Tone("tap", 0.06f, 140f, 90f, 0.9f, 0.5f);
                case FeedbackSound.Grab: return Tone("grab", 0.07f, 520f, 700f, 0.5f, 0.05f);
                case FeedbackSound.Release: return Tone("release", 0.07f, 600f, 380f, 0.4f, 0.05f);
                case FeedbackSound.Store: return Tone("store", 0.16f, 440f, 880f, 0.6f, 0f);
                case FeedbackSound.Take: return Tone("take", 0.14f, 880f, 520f, 0.5f, 0f);
                case FeedbackSound.Reject: return Tone("reject", 0.22f, 170f, 120f, 0.7f, 0.15f);
                case FeedbackSound.Success: return Tone("success", 0.35f, 660f, 1320f, 0.6f, 0f);
                case FeedbackSound.Click: return Tone("click", 0.03f, 1200f, 900f, 0.5f, 0.1f);
                case FeedbackSound.Gunshot: return Tone("gunshot", 0.16f, 220f, 60f, 1f, 0.85f);
                case FeedbackSound.Alarm: return Tone("alarm", 0.5f, 900f, 1300f, 0.7f, 0f);
                case FeedbackSound.Cash: return Tone("cash", 0.22f, 1100f, 1900f, 0.6f, 0.05f);
                case FeedbackSound.Ding: return Tone("ding", 0.6f, 1560f, 1560f, 0.6f, 0f);
                case FeedbackSound.Siren: return Tone("siren", 0.7f, 600f, 950f, 0.6f, 0f);
                default: return Tone("whoosh", 0.2f, 300f, 200f, 0.2f, 1f);
            }
        }

        static AudioClip Tone(string name, float seconds, float startHz, float endHz, float gain, float noise)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            float phase = 0f;
            var random = new System.Random(name.GetHashCode());
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float hz = Mathf.Lerp(startHz, endHz, t);
                phase += 2f * Mathf.PI * hz / SampleRate;
                float envelope = Mathf.Min(1f, t * 30f) * (1f - t) * (1f - t);
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                data[i] = (Mathf.Sin(phase) * (1f - noise) + white * noise) * envelope * gain;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
