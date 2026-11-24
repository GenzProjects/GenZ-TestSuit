// GenzMonetization-v1.33
using System.Collections.Generic;
using UnityEngine;

// Keeps every AudioSource in the loaded scenes playing correctly after an ad
// (or any audio device reset) by restoring the state they had when the app paused.
// iOS only: on other platforms the component compiles but does nothing, so scenes
// and prefabs that use it never show a missing script.
public class AudioSourceStatePreserver : MonoBehaviour
{
#if UNITY_IOS
    private struct AudioState
    {
        public AudioSource source;
        public bool wasPlaying;
        public float savedTime;
    }

    private readonly List<AudioState> _states = new List<AudioState>();

    void OnEnable()
    {
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;
    }

    void OnDisable()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged;
    }

    // This only runs exactly when the game pauses (Ad opens) or unpauses (Ad closes).
    // Zero CPU cost during normal gameplay!
    void OnApplicationPause(bool isPaused)
    {
        if (!isPaused) return;

        // New cycle: this object lives in DontDestroyOnLoad, so the old list may hold
        // sources from a previous scene. Reset it before taking the new snapshot.
        _states.Clear();

        // The ad just opened! Take a snapshot of every active AudioSource right now.
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (sources == null) return;

        foreach (AudioSource source in sources)
        {
            if (source == null) continue; // destroyed or missing: never touch it

            bool playing = source.isPlaying;
            _states.Add(new AudioState
            {
                source = source,
                wasPlaying = playing,
                savedTime = (playing && source.loop) ? source.time : 0f
            });
        }
    }

    private void OnAudioConfigChanged(bool deviceWasChanged)
    {
        foreach (AudioState state in _states)
        {
            AudioSource source = state.source;
            // Null check first (e.g. destroyed by a scene change), then skip switched-off ones.
            if (source == null) continue;
            if (!source.isActiveAndEnabled) continue;

            if (state.wasPlaying)
            {
                if (source.loop && source.clip != null && state.savedTime < source.clip.length)
                {
                    source.time = state.savedTime;
                }

                source.Play();
            }
            else
            {
                source.Stop();
            }
        }
    }
#endif
}
