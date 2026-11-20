// GenzMonetization-v1.33
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioSourceStatePreserver : MonoBehaviour
{
    private AudioSource _audioSource;
    private bool _wasPlayingBeforeReset;
    private float _savedTimeSample;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }

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
        if (_audioSource == null) return;

        if (isPaused)
        {
            // The ad just opened! Take a snapshot of what is happening right now.
            _wasPlayingBeforeReset = _audioSource.isPlaying;
            
            if (_wasPlayingBeforeReset && _audioSource.loop)
            {
                _savedTimeSample = _audioSource.time;
            }
        }
    }

    private void OnAudioConfigChanged(bool deviceWasChanged)
    {
        if (_audioSource == null) return;

     
        
        if (_wasPlayingBeforeReset)
        {
            if (_audioSource.loop && _audioSource.clip != null)
            {
                _audioSource.time = _savedTimeSample;
            }

            _audioSource.Play();
        }
        else
        {
            _audioSource.Stop();
        }
    }
}