using UnityEngine;

/// <summary>
/// Музыка паузы и музыка смерти (у каждой свой трек).
///  - Пауза (Time.timeScale == 0 или SetPaused(true)): играет Pause Clip.
///  - Смерть (монстр поймал игрока, после окончания скримера): играет Game Over Clip.
///    Смерть главнее паузы: если GameOver() замораживает время, трек паузы не включится.
/// Остальные звуки на это время замирают (Silence Other Audio) и продолжаются после паузы.
///
/// Если Game Over Clip не назначен, при смерти этот скрипт ничего не делает и ничего не глушит
/// (можно оставить свою музыку на экране Game Over).
/// Во время финала побега (EscapeController) не вмешивается.
/// Вешается на любой объект сцены.
/// </summary>
public class PauseMusic : MonoBehaviour
{
    /// Игра на паузе (для других скриптов, например TaskHUD).
    public static bool IsPaused { get; private set; }

    [Header("Пауза")]
    public AudioClip pauseClip;
    [Range(0f, 1f)] public float pauseVolume = 0.8f;

    [Header("Смерть")]
    public AudioClip gameOverClip;
    [Range(0f, 1f)] public float gameOverVolume = 0.9f;
    public bool loopGameOver = true;

    [Header("Поведение")]
    public float fadeTime = 0.3f;              // плавное появление/затухание (в реальном времени)
    public bool autoDetectPause = true;        // пауза = Time.timeScale == 0
    public bool silenceOtherAudio = true;      // замораживать остальные звуки на время паузы/смерти

    [Header("Ссылки (если пусто - ищутся в сцене)")]
    public MonsterChaser monster;
    public Jumpscare jumpscare;
    public MazeGenerator maze;

    enum Mode { None, Pause, Death }

    AudioSource src;
    EscapeController escape;
    Mode mode = Mode.None;
    bool manual;            // пауза, заданная через SetPaused
    bool caught;            // монстр поймал игрока
    bool weSetListenerPause;

    /// Ручное управление паузой (если timeScale не используется).
    public void SetPaused(bool value) { manual = value; }

    /// Можно вызвать из своего GameManager, если смерть определяется не через MonsterChaser.
    public void SetDead(bool value) { caught = value; }

    void Awake()
    {
        IsPaused = false;

        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.ignoreListenerPause = true;   // сама музыка не должна глохнуть от AudioListener.pause
        src.volume = 0f;

#if UNITY_2023_1_OR_NEWER
        if (monster == null) monster = FindFirstObjectByType<MonsterChaser>();
        if (jumpscare == null) jumpscare = FindFirstObjectByType<Jumpscare>();
        if (maze == null) maze = FindFirstObjectByType<MazeGenerator>();
        escape = FindFirstObjectByType<EscapeController>();
#else
        if (monster == null) monster = FindObjectOfType<MonsterChaser>();
        if (jumpscare == null) jumpscare = FindObjectOfType<Jumpscare>();
        if (maze == null) maze = FindObjectOfType<MazeGenerator>();
        escape = FindObjectOfType<EscapeController>();
#endif
    }

    void OnEnable()
    {
        if (monster != null) monster.OnCaught += HandleCaught;
        if (maze != null) maze.OnMazeGenerated += HandleGenerated;
    }

    void OnDisable()
    {
        if (monster != null) monster.OnCaught -= HandleCaught;
        if (maze != null) maze.OnMazeGenerated -= HandleGenerated;
        ReleaseListener();   // выход со сцены не должен оставить звук замороженным
        IsPaused = false;
    }

    void HandleCaught() { caught = true; }
    void HandleGenerated() { caught = false; }   // новая игра

    void Update()
    {
        // финал побега сам управляет звуком
        if (escape != null && escape.Finished) return;

        // смерть наступает после скримера, чтобы музыка не перекрывала крик
        bool dead = caught && (jumpscare == null || !jumpscare.IsPlaying);
        bool pause = manual || (autoDetectPause && Time.timeScale <= 0.0001f);

        Mode now = dead ? Mode.Death : (pause ? Mode.Pause : Mode.None);
        if (now != mode)
        {
            mode = now;
            IsPaused = (mode == Mode.Pause);
            Apply();
        }

        // плавная громкость в реальном времени (при timeScale = 0 deltaTime равен 0)
        float targetVol = mode == Mode.Pause ? pauseVolume : (mode == Mode.Death ? gameOverVolume : 0f);
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        src.volume = Mathf.MoveTowards(src.volume, targetVol, step * Mathf.Max(0.0001f, Mathf.Max(pauseVolume, gameOverVolume)));

        if (mode == Mode.None && src.isPlaying && src.volume <= 0.001f) src.Stop();
    }

    void Apply()
    {
        src.Stop();

        AudioClip clip = null;
        bool loop = true;
        if (mode == Mode.Pause) { clip = pauseClip; loop = true; }
        else if (mode == Mode.Death) { clip = gameOverClip; loop = loopGameOver; }

        if (clip != null)
        {
            src.clip = clip;
            src.loop = loop;
            src.volume = 0f;
            src.Play();
        }

        // глушим остальное: на паузе всегда, при смерти - только если есть своя музыка смерти
        bool silence = silenceOtherAudio && (mode == Mode.Pause || (mode == Mode.Death && gameOverClip != null));
        if (silence)
        {
            AudioListener.pause = true;
            weSetListenerPause = true;
        }
        else
        {
            ReleaseListener();
        }
    }

    void ReleaseListener()
    {
        if (!weSetListenerPause) return;
        weSetListenerPause = false;
        AudioListener.pause = false;
    }
}