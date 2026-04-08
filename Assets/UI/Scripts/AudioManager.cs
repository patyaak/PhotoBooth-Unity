using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("UI Sounds")]
    public AudioClip clickSound;
    public AudioClip backBtnSound;
    public AudioClip loginSuccessSound;
    public AudioClip errorSound;

    [Header("Special Sounds")]
    public AudioClip gatchaAnimSound;
    public AudioClip shutterSound;
    public AudioClip frameSelectionSound;

    private AudioSource sfxAudioSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayClick()
    {
        PlaySound(clickSound);
    }

    public void PlayBackBtnSound()
    {
        PlaySound(backBtnSound);
    }

    public void PlayLoginSuccess()
    {
        PlaySound(loginSuccessSound);
    }

    public void PlayError()
    {
        PlaySound(errorSound);
    }

    public void PlayGatchaAnim()
    {
        PlaySound(gatchaAnimSound);
    }

    public void PlayShutter()
    {
        PlaySound(shutterSound);
    }

    public void PlayFrameSelection()
    {
        PlaySound(frameSelectionSound);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(clip);
        }
    }
}
