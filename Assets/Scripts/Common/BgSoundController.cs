using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BgSoundController : MonoBehaviour
{

    public void StopMusic()
    {
        GetComponent<AudioSource>().Stop();
    }
    public void PlayMusic()
    {
        if (GetComponent<AudioSource>().isPlaying == false)
        GetComponent<AudioSource>().Play();
    }
}
