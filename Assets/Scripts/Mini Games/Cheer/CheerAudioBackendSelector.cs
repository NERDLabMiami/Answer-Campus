using UnityEngine;

public class CheerAudioBackendSelector : MonoBehaviour
{
    [Header("Selection")]
    public CheerAudioBackendMode mode = CheerAudioBackendMode.Auto;

    [Header("Unity Audio")]
    public CheerAudioConfig unityConfig;

    [Tooltip("If left null, we will create AudioSources on this GameObject.")]
    public AudioSource crowdSource;

    public AudioSource countdownSource;
    public AudioSource cheerSource;

    // Resolved backend instance
    public ICheerAudioBackend Backend { get; private set; }
    private void ConfigureSource(AudioSource s, bool loop)
    {
        s.playOnAwake = false;
        s.loop = loop;

        s.mute = false;
        s.volume = 1f;

        s.spatialBlend = 0f;                 // 2D audio
        s.dopplerLevel = 0f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;

        s.outputAudioMixerGroup = null;      // bypass any mixer routing
    }

    public void DebugDump(string tag)
    {
        Debug.Log(
            $"[CHEER-AUDIO] {tag}\n" +
            $" selectorActive={gameObject.activeInHierarchy} enabled={enabled}\n" +
            $" listenerCount={FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude).Length} " +
            $" AudioListener.pause={AudioListener.pause} vol={AudioListener.volume}\n" +
            $" crowd:     goActive={crowdSource && crowdSource.gameObject.activeInHierarchy} enabled={crowdSource && crowdSource.enabled} " +
            $" mute={crowdSource && crowdSource.mute} vol={(crowdSource ? crowdSource.volume : -1)} clip={(crowdSource && crowdSource.clip ? crowdSource.clip.name : "null")}\n" +
            $" countdown: goActive={countdownSource && countdownSource.gameObject.activeInHierarchy} enabled={countdownSource && countdownSource.enabled} " +
            $" mute={countdownSource && countdownSource.mute} vol={(countdownSource ? countdownSource.volume : -1)} clip={(countdownSource && countdownSource.clip ? countdownSource.clip.name : "null")}\n" +
            $" cheer:     goActive={cheerSource && cheerSource.gameObject.activeInHierarchy} enabled={cheerSource && cheerSource.enabled} " +
            $" mute={cheerSource && cheerSource.mute} vol={(cheerSource ? cheerSource.volume : -1)} clip={(cheerSource && cheerSource.clip ? cheerSource.clip.name : "null")} " +
            $" isPlaying={(cheerSource ? cheerSource.isPlaying : false)}"
        );
    }
    void Awake()
    {
        EnsureSources();
        if (unityConfig != null)
        {
            ForceLoad(unityConfig.crowdLoop);
            ForceLoad(unityConfig.countdownOneShot);
            if (unityConfig.cheers != null)
                foreach (var t in unityConfig.cheers) ForceLoad(t.clip);
        }


#if UNITY_WEBGL
        // WebGL build: must be Unity backend
        Backend = new CheerAudioUnityBackend(this, unityConfig);
        if (Backend is CheerAudioUnityBackend unity && unityConfig != null)
            unity.PreloadAll();

#else
        // Non-WebGL: choose based on mode & availability
        if (mode == CheerAudioBackendMode.ForceUnity)
        {
            Backend = new CheerAudioUnityBackend(this, unityConfig);
        }
        else if (mode == CheerAudioBackendMode.ForceFMOD)
        {
            Backend = TryCreateFmodBackendOrFallbackToUnity();
        }
        else // Auto
        {
            Backend = TryCreateFmodBackendOrFallbackToUnity();
        }
#endif
    }
    
    [ContextMenu("Smoke Test Unity Audio")]
    public void SmokeTest()
    {
        EnsureSources();

        if (unityConfig == null)
        {
            Debug.LogError("[CHEER-AUDIO] Missing unityConfig");
            return;
        }

        crowdSource.clip = unityConfig.crowdLoop;
        countdownSource.clip = unityConfig.countdownOneShot;
        cheerSource.clip = (unityConfig.cheers != null && unityConfig.cheers.Length > 0) ? unityConfig.cheers[0].clip : null;

        Debug.Log($"[CHEER-AUDIO] SmokeTest crowd={crowdSource.clip?.name} countdown={countdownSource.clip?.name} cheer={cheerSource.clip?.name}");

        if (crowdSource.clip) crowdSource.Play();
        if (countdownSource.clip) countdownSource.PlayDelayed(0.25f);
        if (cheerSource.clip) cheerSource.PlayDelayed(1.5f);
    }

    static void ForceLoad(AudioClip c)
    {
        if (c == null) return;
        if (c.loadState == AudioDataLoadState.Unloaded)
            c.LoadAudioData();
    }
    private ICheerAudioBackend TryCreateFmodBackendOrFallbackToUnity()
    {
        // If your FMOD backend component exists + FMOD is compiled, use it.
        // Otherwise fallback to Unity.
        var fmod = GetComponent<CheerAudioFmodBackendComponent>();
        if (fmod != null && fmod.IsReady)
            return fmod.CreateBackend();

        return new CheerAudioUnityBackend(this, unityConfig);
    }

    private void EnsureSources()
    {
        if (!crowdSource)     crowdSource     = gameObject.AddComponent<AudioSource>();
        if (!countdownSource) countdownSource = gameObject.AddComponent<AudioSource>();
        if (!cheerSource)     cheerSource     = gameObject.AddComponent<AudioSource>();
        ConfigureSource(crowdSource, true);
        ConfigureSource(cheerSource, false);
        ConfigureSource(countdownSource, false);
    }
}
