using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using STOP_MODE = FMOD.Studio.STOP_MODE;
using System.Collections.Concurrent;
public class FMODAudioManager : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PARAMETER_CALLBACK
    {
        public FMOD.Studio.PARAMETER_ID parameter;
        public float value;
        public float finalvalue;
    }
    public static FMODAudioManager Instance { get; private set; }
    private string currentEventPath = "";
    private AudioSource audioSource;
    public FMOD.Studio.EventInstance currentMusic;
    public FMOD.Studio.EventInstance currentAmbient;
    private Guid _currentMusicGuid;
    private Guid _currentAmbientGuid;

    private Coroutine currentFadeOut;
    private EventInstance nextMusicToPlay;

    private Coroutine currentMusicCoroutine;
    private List<EventInstance> allCreatedInstances = new();
    private readonly ConcurrentQueue<string> _paramLog = new ConcurrentQueue<string>();
    private readonly Dictionary<EventInstance, EVENT_CALLBACK> _markerCbs = new(new EventInstanceComparer());
    

    public void PrintActiveMusicInstances()
    {
        Debug.Log("----- Active FMOD Music Instances -----");
        foreach (var instance in allCreatedInstances)
        {
            if (instance.isValid())
            {
                instance.getDescription(out var desc);
                desc.getPath(out string path);
                instance.getPlaybackState(out var state);
                Debug.Log($"Playing: {path}, State: {state}");
            }
        }
    }
    private class EventInstanceComparer : IEqualityComparer<EventInstance>
    {
        public bool Equals(EventInstance a, EventInstance b) => a.handle == b.handle;
        public int GetHashCode(EventInstance e) => e.handle.GetHashCode();
    }
    public void PlayMusic(EventReference evt, float fadeDuration = 1f)
    {
        if (evt.IsNull) return;

        if (_currentMusicGuid == evt.Guid && currentMusic.isValid())
            return;

        _currentMusicGuid = evt.Guid;

        // IMPORTANT: if you adopted StopAllAudio() earlier, do NOT nuke ambient here
        // unless you explicitly want music to always kill ambient.
        if (currentMusicCoroutine != null)
            StopCoroutine(currentMusicCoroutine);

        currentMusicCoroutine = StartCoroutine(TransitionToNewMusic(evt, fadeDuration));
    }

    public void PlayAmbient(EventReference evt, float fadeDuration = 1f)
    {
        if (evt.IsNull) return;

        if (_currentAmbientGuid == evt.Guid && currentAmbient.isValid())
            return;

        _currentAmbientGuid = evt.Guid;

        if (currentAmbient.isValid())
            StartCoroutine(FadeOutAndStop(currentAmbient, fadeDuration));

        currentAmbient = RuntimeManager.CreateInstance(evt);
        allCreatedInstances.Add(currentAmbient);
        currentAmbient.start();
    }
    public EventInstance StartEventWithTimeline(EventReference evt, Action<string,int> onMarker = null, Action<TIMELINE_BEAT_PROPERTIES> onBeat = null, Action onStopped = null, string paramName = null, float? paramValue = null, Transform attachTo = null, bool ignoreSeekSpeed = true) {
        // Create instance
        var inst = FMODUnity.RuntimeManager.CreateInstance(evt);
        if (!inst.isValid())
        {
            Debug.LogError("[FMODAudioManager] StartEventWithTimeline: Failed to create instance.");
            return default;
        }
        inst.getDescription(out var desc);
        desc.getPath(out string path);
        Debug.Log($"[FMOD] CreateInstance OK → {path}  handle={inst.handle}");

// after setParameterByName(...)
        if (!string.IsNullOrEmpty(paramName) && paramValue.HasValue)
        {
            var r = inst.setParameterByName(paramName, paramValue.Value, ignoreSeekSpeed);
            Debug.Log($"[FMOD] setParameter '{paramName}'={paramValue.Value} → {r}");
        }

        // Optional 3D attach (Rigidbody can be null)
        if (attachTo != null)
        {
            var rb = attachTo.GetComponent<Rigidbody>();
            FMODUnity.RuntimeManager.AttachInstanceToGameObject(inst, attachTo, rb);
        }

        // Set parameter before start (so 0s transition regions see correct value)
        if (!string.IsNullOrEmpty(paramName) && paramValue.HasValue)
            inst.setParameterByName(paramName, paramValue.Value, ignoreSeekSpeed);

        // Copy delegates to locals to avoid closure surprises
        var _onMarker  = onMarker;
        var _onBeat    = onBeat;
        var _onStopped = onStopped;
// Create the delegate and store it so GC can't collect it
        EVENT_CALLBACK cb = (type, _inst, paramPtr) =>
        {
            // Local copies to guard against races
            var cbMarker  = _onMarker;
            var cbBeat    = _onBeat;
            var cbStopped = _onStopped;

            try
            {
                if ((type & EVENT_CALLBACK_TYPE.TIMELINE_BEAT) != 0 && cbBeat != null && paramPtr != IntPtr.Zero)
                {
                    var b = Marshal.PtrToStructure<FMOD.Studio.TIMELINE_BEAT_PROPERTIES>(paramPtr);
                    cbBeat(b);
                }
                else if ((type & EVENT_CALLBACK_TYPE.TIMELINE_MARKER) != 0 && cbMarker != null && paramPtr != IntPtr.Zero)
                {
                    var p = Marshal.PtrToStructure<FMOD.Studio.TIMELINE_MARKER_PROPERTIES>(paramPtr);
                    string marker = (string)p.name ?? string.Empty; // StringWrapper -> string
                    cbMarker(marker, p.position);
                }
                else if ((type & EVENT_CALLBACK_TYPE.STOPPED) != 0)
                {
                    // Fire stop, then unpin the callback so it can be GC'd
                    cbStopped?.Invoke();
                    _markerCbs.Remove(inst);   // <-- drop strong ref
                }
                
            }
            catch { /* never throw on FMOD thread */ }

            return FMOD.RESULT.OK;
        };

// Pin the delegate for this instance
        _markerCbs[inst] = cb;
        var mask = EVENT_CALLBACK_TYPE.TIMELINE_MARKER | EVENT_CALLBACK_TYPE.TIMELINE_BEAT | EVENT_CALLBACK_TYPE.STOPPED;
        inst.setCallback(cb, mask);
        // Start + release (release is safe; instance lives until STOPPED)
        var startResult = inst.start();
        Debug.Log($"[FMOD] start() → {startResult}");
        if (startResult != FMOD.RESULT.OK)
            Debug.LogError($"[FMODAudioManager] inst.start() failed: {startResult}");

        inst.release();

        return inst;
    }

    public void StopCurrent(bool allowFadeOut = true)
    {
        if (currentMusic.isValid())
        {
            currentMusic.stop(allowFadeOut ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
            currentMusic.release();
            currentMusic = default;
        }
    }
    private IEnumerator TransitionToNewMusic(EventReference newEventName, float fadeDuration)
    {
        // Fade out current music
        if (currentMusic.isValid())
        {
            currentMusic.getVolume(out float startVolume);
            float timer = 0f;

            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                float volume = Mathf.Lerp(startVolume, 0, timer / fadeDuration);
                currentMusic.setVolume(volume);
                yield return null;
            }

            currentMusic.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            currentMusic.release();
        }

        // Now start new music
        currentMusic = FMODUnity.RuntimeManager.CreateInstance(newEventName);
        allCreatedInstances.Add(currentMusic);
        currentMusic.setVolume(1f);
        currentMusic.start();
    }
    
    public void PlayAmbient(EventReference eventName)
    {
        if (currentAmbient.isValid())
        {
            currentAmbient.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            currentAmbient.release();
        }
        currentAmbient = FMODUnity.RuntimeManager.CreateInstance(eventName);
        currentAmbient.start();
        allCreatedInstances.Add(currentAmbient);
    }

    public void FadeOutMusic(float duration)
    {
        if (currentMusic.isValid())
        {
            StartCoroutine(FadeOutAndStop(currentMusic, duration));
        }
    }

    public void FadeOutAmbient(float duration)
    {
        if (currentAmbient.isValid())
        {
            StartCoroutine(FadeOutAndStop(currentAmbient, duration));
        }
    }
    private IEnumerator FadeOutAndStop(FMOD.Studio.EventInstance instance, float duration)
    {
        float timer = 0f;
        instance.getVolume(out float startVolume);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float newVol = Mathf.Lerp(startVolume, 0, timer / duration);
            instance.setVolume(newVol);
            yield return null;
        }

        instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        Debug.Log($"Releasing instance {instance}");
        instance.release();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("FMODAudioManager [Auto]");
        go.AddComponent<FMODAudioManager>();
        // Awake runs immediately, sets Instance and DontDestroyOnLoad
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);
    }
    public void StopAllAudio(float fadeDuration = 1f)
    {
        if (currentMusicCoroutine != null)
        {
            StopCoroutine(currentMusicCoroutine);
            currentMusicCoroutine = null;
        }

        if (currentMusic.isValid())
        {
            StartCoroutine(FadeOutAndStop(currentMusic, fadeDuration));
            currentMusic = default;
        }

        if (currentAmbient.isValid())
        {
            StartCoroutine(FadeOutAndStop(currentAmbient, fadeDuration));
            currentAmbient = default;
        }
    }

    public void SetDrums(int drumSet)
    {
        currentMusic.setParameterByName("CharacterDrumSelection", drumSet);

    }
   
    public void PlayOneShot(EventReference evt, string paramName = null, float paramValue = 0f)
    {
        if (evt.IsNull) { Debug.LogWarning("[FMOD] PlayOneShot NULL"); return; }

        var inst = RuntimeManager.CreateInstance(evt);
        AttachIf3D(inst, GetListenerTransform(), GetListenerRigidbody());

        if (!string.IsNullOrEmpty(paramName))
        {
            var r = inst.setParameterByName(paramName, paramValue, true);
            if (r != FMOD.RESULT.OK) Debug.LogWarning($"[FMOD] setParam '{paramName}'={paramValue} -> {r}");
        }

        var startRes = inst.start();
        if (startRes != FMOD.RESULT.OK) Debug.LogWarning($"[FMOD] start() -> {startRes}");
        inst.release();
    }
    public IEnumerator PlayOneShotAndWaitPrecise(EventReference evt, string paramName = null, float paramValue = 0f)
    {
        if (evt.IsNull) { Debug.LogWarning("[FMOD] WaitPrecise with NULL event"); yield break; }

        var inst = RuntimeManager.CreateInstance(evt);
        Debug.Log($"Creating Event Instance {inst}");
        AttachIf3D(inst, GetListenerTransform(), GetListenerRigidbody());

        if (!string.IsNullOrEmpty(paramName))
        {
            var r = inst.setParameterByName(paramName, paramValue, true);
            if (r != FMOD.RESULT.OK) Debug.LogWarning($"[FMOD] setParam '{paramName}'={paramValue} -> {r}");
        }

        var startRes = inst.start();
        if (startRes != FMOD.RESULT.OK) Debug.LogWarning($"[FMOD] start() -> {startRes}");

        var deadline = Time.realtimeSinceStartup + 15f; // safety
        PLAYBACK_STATE state;
        do
        {
            yield return null;
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[FMOD] WaitPrecise timed out; stopping instance.");
                break;
            }
            inst.getPlaybackState(out state);
        }
        while (state != PLAYBACK_STATE.STOPPED);

        inst.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT); // harmless if already stopped
        inst.release();
    }

    private static void AttachIf3D(EventInstance inst, Transform t, Rigidbody rb = null)
    {
        if (!inst.isValid() || t == null) return;
        inst.getDescription(out var desc);
        if (desc.isValid())
        {
            desc.is3D(out bool is3D);
            if (is3D)
                FMODUnity.RuntimeManager.AttachInstanceToGameObject(inst, t, rb);
        }
    }
    private static Transform GetListenerTransform()
    {
        var sl = UnityEngine.Object.FindObjectOfType<FMODUnity.StudioListener>();
        if (sl) return sl.transform;

        return Camera.main ? Camera.main.transform : null;
    }

    private static Rigidbody GetListenerRigidbody()
    {
        var t = GetListenerTransform();
        return t ? t.GetComponent<Rigidbody>() : null;
    }

    public void StopMusic(bool allowFadeOut = true)
    {
        if (currentMusic.isValid())
        {
            currentMusic.stop(allowFadeOut ? STOP_MODE.ALLOWFADEOUT : STOP_MODE.IMMEDIATE);
            currentMusic.release();
            currentMusic = default;  // <—
        }
        currentEventPath = "";
    }



}

