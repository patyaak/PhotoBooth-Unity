using UnityEngine;
using UnityEngine.SceneManagement;


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
    public AudioClip gatchaRevealSound;
    public AudioClip shutterSound;
    public AudioClip frameSelectionSound;

    [Header("Timer Sounds")]
    public AudioClip timer3SecSound;
    public AudioClip timer5SecSound;
    public AudioClip timer7SecSound;
    public AudioClip timer10SecSound;
    public AudioClip printingDoneSound;

    public bool IsAudioEnabled { get; private set; }

    private AudioSource sfxAudioSource;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            sfxAudioSource = gameObject.AddComponent<AudioSource>();

            IsAudioEnabled = PlayerPrefs.GetInt("AudioEnabled", 1) == 1;
            
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }


    private void OnDestroy()
    {
       
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyAudioState();
    }

    public void SetAudioEnabled(bool enabled)
    {
        IsAudioEnabled = enabled;
        PlayerPrefs.SetInt("AudioEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyAudioState();
        
        Debug.Log($"🔊 [AudioManager] Audio state set to: {(enabled ? "ON" : "OFF")}");
    }

    public void ApplyAudioState()
    {
        // Find the main camera and its audio source
        if (Camera.main != null)
        {
            AudioSource cameraAudio = Camera.main.GetComponent<AudioSource>();
            if (cameraAudio != null)
            {
                cameraAudio.mute = !IsAudioEnabled;
            }
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
    
    public void PlayGatchaReveal()
    {
        PlaySound(gatchaRevealSound);
    }

    public void PlayShutter()
    {
        PlaySound(shutterSound);
    }

    public void PlayFrameSelection()
    {
        PlaySound(frameSelectionSound);
    }

    public void PlayTimerSound(int seconds)
    {
        AudioClip clipToPlay = null;
        switch (seconds)
        {
            case 3: clipToPlay = timer3SecSound; break;
            case 5: clipToPlay = timer5SecSound; break;
            case 7: clipToPlay = timer7SecSound; break;
            case 10: clipToPlay = timer10SecSound; break;
        }

        if (clipToPlay != null)
        {
            PlaySound(clipToPlay);
        }
    }

    public void PlayPrintingDone()
    {
        PlaySound(printingDoneSound);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(clip);
        }
    }
}
