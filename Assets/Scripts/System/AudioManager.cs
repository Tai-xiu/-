using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局音频管理器（RuntimeInitializeOnLoadMethod自举DDOL，无需挂场景物体）：
/// - BGM：按活动场景自动切换——Camp=营地酒馆曲 / Dungeon=地牢氛围曲 / 其他场景（主菜单）静音，循环播放；
/// - SFX：PlaySfx一键播放（内置8路AudioSource对象池），clip经Resources加载
///   （Assets/Resources/Audio/ 下 SFX_*.wav 与 BGM_*.wav，文件名即资源名）。
/// 自举时机AfterSceneLoad：每次场景加载检查补建（Instance判空防重复），DDOL跨场景天然存活。
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Tooltip("BGM音量（0~1）")]
    [SerializeField] private float bgmVolume = 0.1f;

    private AudioSource _bgmSource;
    private readonly AudioSource[] _sfxPool = new AudioSource[8];
    private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= BootstrapOnSceneLoaded;
        SceneManager.sceneLoaded += BootstrapOnSceneLoaded;
        BootstrapOnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void BootstrapOnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // UI.scene是纯制作场景；离开该场景后再按需要补建游戏实例
        if (scene.name == "UI" || Instance != null) return;
        var go = new GameObject("AudioManager");
        go.AddComponent<AudioManager>();
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.loop = true;
        _bgmSource.playOnAwake = false;
        _bgmSource.volume = bgmVolume;
        for (int i = 0; i < _sfxPool.Length; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            _sfxPool[i] = src;
        }
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplySceneBgm(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneBgm(scene.name);
    }

    /// <summary>按场景名切换BGM（营地/地牢各一曲，主菜单等场景静音）</summary>
    private void ApplySceneBgm(string sceneName)
    {
        string clipName = null;
        if (sceneName == "Camp") clipName = "BGM_Camp";
        else if (sceneName == "Dungeon") clipName = "BGM_Dungeon";

        if (clipName == null)
        {
            _bgmSource.Stop();
            return;
        }
        var clip = LoadClip(clipName);
        if (clip == null) return;
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return; // 同曲不重启（重进同场景保留播放进度）
        _bgmSource.clip = clip;
        _bgmSource.Play();
    }

    /// <summary>
    /// 播放一次性音效：position为空按2D播（UI类），否则在指定世界坐标3D播放（空间感）。
    /// clipName为Resources/Audio/下文件名（不含扩展名），如"SFX_SwordSwing"。
    /// </summary>
    public static void PlaySfx(string clipName, Vector3? position = null, float volume = 1f)
    {
        if (Instance == null) return;
        var clip = Instance.LoadClip(clipName);
        if (clip == null) return;
        foreach (var src in Instance._sfxPool)
        {
            if (src.isPlaying) continue;
            PlayOn(src, clip, position, volume);
            return;
        }
        PlayOn(Instance._sfxPool[0], clip, position, volume); // 池满复用最旧一路
    }

    private static void PlayOn(AudioSource src, AudioClip clip, Vector3? position, float volume)
    {
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = position.HasValue ? 1f : 0f;
        src.transform.position = position ?? Vector3.zero;
        src.Play();
    }

    private AudioClip LoadClip(string name)
    {
        if (_clipCache.TryGetValue(name, out var c) && c != null) return c;
        c = Resources.Load<AudioClip>("Audio/" + name);
        _clipCache[name] = c;
        return c;
    }
}
