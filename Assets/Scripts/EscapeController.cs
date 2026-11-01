using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Побег и финал.
///
/// 1) Когда активирован ПОСЛЕДНИЙ алтарь (и карта перестроилась), начинается побег:
///    весь экран плавно краснеет (полупрозрачный красный фильтр, слегка пульсирует).
/// 2) "Ключ" собран, пока все алтари активированы. Игрок встаёт на клетку выхода (жёлтая дверь)
///    и нажимает клавишу (по умолчанию Пробел) - запускается финал.
///    Если дверь у вас уже открывается своей системой, вызовите TriggerEscape() оттуда
///    и выключите галочку Use Built In Key.
/// 3) Финал: экран затемняется -> чуть проявляется, играет музыка, висит картинка "сбежал" ->
///    по окончании трека (около 16 с) игра сама переходит в главное меню. Кнопок нет.
///
/// Вешается на любой объект сцены. UI создаётся в коде. AltarUse/MazeGenerator ищутся автоматически.
/// </summary>
public class EscapeController : MonoBehaviour
{
    [Header("Ссылки (если пусто - ищутся в сцене)")]
    public AltarUse altars;
    public MazeGenerator maze;
    public PlayerMazeController player;
    public MonsterChaser monster;

    [Header("Интерфейс ключа (Key Interface)")]
    public GameObject keyInterface;             // если пусто - берётся объект с компонентом KeyChange
    public bool hideKeyOnDeath = false;         // true - прятать ключ и при смерти (чтобы не висел поверх Game Over)

    [Header("Красный фильтр побега")]
    public Color tintColor = new Color(0.85f, 0.05f, 0.05f, 0.22f); // alpha = сила фильтра
    public float tintFadeIn = 2f;
    [Range(0f, 0.5f)] public float tintPulse = 0.2f;                // 0 = без пульсации
    public float tintPulseSpeed = 2f;

    [Header("Открытие двери выхода")]
    public bool useBuiltInKey = true;
    public KeyCode openKey = KeyCode.Space;

    [Header("Финал")]
    public Sprite escapeSprite;                 // картинка "ты сбежал" (для моего собственного экрана или для Win Screen Image)

    [Header("Готовый экран победы (Screens/Win)")]
    public GameObject winScreen;                // ваш существующий экран победы; если назначен - показывается он вместо моей картинки
    public Image winScreenImage;                // необязательно: Image на нём, куда подставится Escape Sprite
    public bool hideButtonsOnWin = true;        // кнопки на экране победы прячутся: в меню игра переходит сама
    [Range(0.1f, 1f)] public float imageScreenFraction = 0.7f; // какую часть экрана занимает картинка
    public AudioClip escapeMusic;               // трек (~16 с)
    [Range(0f, 1f)] public float musicVolume = 1f;
    public float fallbackSeconds = 16f;         // длительность, если трек не назначен
    public float fadeToBlackTime = 1.5f;
    public float revealTime = 2f;
    [Range(0f, 1f)] public float revealAlpha = 0.55f;  // насколько остаётся чёрный экран после "проявления"
    public float imageFadeInTime = 2f;
    public float endFadeTime = 1.5f;            // затемнение в конце перед меню

    [Header("Главное меню")]
    public string mainMenuScene = "MainMenu";   // имя сцены; если такой нет в Build Settings - берётся сцена с индексом 0

    public bool EscapePhase { get; private set; }
    public bool Finished { get; private set; }
    public event Action OnEscapeStarted;
    public event Action OnEscaped;

    Image tint, black, picture;
    AudioSource music;
    float tintTime;
    bool tintHidden;
    bool dead;   // игрока поймал монстр

    void Awake()
    {
        if (altars == null)
        {
#if UNITY_2023_1_OR_NEWER
            altars = FindFirstObjectByType<AltarUse>();
#else
            altars = FindObjectOfType<AltarUse>();
#endif
        }
        if (maze == null && altars != null) maze = altars.maze;
        if (maze == null)
        {
#if UNITY_2023_1_OR_NEWER
            maze = FindFirstObjectByType<MazeGenerator>();
#else
            maze = FindObjectOfType<MazeGenerator>();
#endif
        }
        if (player == null && altars != null) player = altars.playerController;
        if (player == null)
        {
#if UNITY_2023_1_OR_NEWER
            player = FindFirstObjectByType<PlayerMazeController>();
#else
            player = FindObjectOfType<PlayerMazeController>();
#endif
        }
        if (monster == null && altars != null) monster = altars.monster;
        if (monster == null)
        {
#if UNITY_2023_1_OR_NEWER
            monster = FindFirstObjectByType<MonsterChaser>();
#else
            monster = FindObjectOfType<MonsterChaser>();
#endif
        }

        BuildUI();
    }

    void OnEnable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated += HandleGenerated;
        maze.OnMazeRebuilt += HandleRebuilt;
    }

    void OnDisable()
    {
        if (maze == null) return;
        maze.OnMazeGenerated -= HandleGenerated;
        maze.OnMazeRebuilt -= HandleRebuilt;
    }

    void Start()
    {
        if (monster != null) monster.OnCaught += HandleCaught;
    }

    void OnDestroy()
    {
        if (monster != null) monster.OnCaught -= HandleCaught;
    }

    // игрока поймали: красный фильтр пропадает (в этот момент экран закрыт скримером), побег больше невозможен
    void HandleCaught()
    {
        dead = true;
        tintHidden = true;
        SetAlpha(tint, 0f);
        if (hideKeyOnDeath) SetKeyVisible(false);
    }

    /// Можно вызвать из своего GameManager, если смерть определяется не через MonsterChaser.
    public void SetDead(bool value)
    {
        dead = value;
        if (value) { tintHidden = true; SetAlpha(tint, 0f); if (hideKeyOnDeath) SetKeyVisible(false); }
    }

    // новая игра: всё сбрасывается
    void HandleGenerated()
    {
        EscapePhase = false;
        dead = false;
        tintHidden = false;
        SetKeyVisible(true);
        tintTime = 0f;
        SetAlpha(tint, 0f);
    }

    // после перестройки карты: если активирован последний алтарь - начинается побег
    void HandleRebuilt()
    {
        if (EscapePhase || Finished || altars == null) return;
        int total = TotalAltars();
        if (total > 0 && altars.NumberOfActivatedAltars >= total)
        {
            EscapePhase = true;
            tintTime = 0f;
            if (OnEscapeStarted != null) OnEscapeStarted();
        }
    }

    int TotalAltars()
    {
        if (maze == null || maze.WaypointCells == null) return 0;
        int n = maze.WaypointCells.Count;
        if (maze.EntranceIndex >= 0) n--;
        if (maze.ExitIndex >= 0 && maze.ExitIndex != maze.EntranceIndex) n--;
        return Mathf.Max(0, n);
    }

    // ------------------------------------------------------------------
    void Update()
    {
        UpdateTint();

        if (!useBuiltInKey || !EscapePhase || Finished || dead) return;
        if (!Input.GetKeyDown(openKey)) return;
        if (maze == null || player == null || player.IsMoving) return;
        if (maze.ExitIndex < 0 || maze.ExitIndex >= maze.WaypointCells.Count) return;

        // игрок стоит на клетке выхода
        if (maze.WaypointCells[maze.ExitIndex] == maze.WorldToGrid(player.transform.position))
            TriggerEscape();
    }

    void UpdateTint()
    {
        if (tint == null) return;
        if (!EscapePhase) return;
        if (tintHidden) { SetAlpha(tint, 0f); return; }   // в финале красный фильтр не нужен

        tintTime += Time.unscaledDeltaTime;
        float k = tintFadeIn > 0f ? Mathf.Clamp01(tintTime / tintFadeIn) : 1f;
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * tintPulseSpeed) * tintPulse;
        SetAlpha(tint, tintColor.a * k * pulse);
    }

    /// Запускает финал. Можно вызвать из своей системы двери. Работает только в фазе побега.
    public void TriggerEscape()
    {
        if (!EscapePhase || Finished || dead) return;
        Finished = true;
        StartCoroutine(Finale());
    }

    // ------------------------------------------------------------------
    IEnumerator Finale()
    {
        // игрок и монстр замирают, прочие звуки глушатся
        if (player != null) player.InputLocked = true;
        if (monster != null) monster.Freeze(true);
        AudioListener.pause = true;     // музыка финала это игнорирует (ignoreListenerPause)

        // 1. затемнение
        yield return FadeTo(black, 1f, fadeToBlackTime);

        SetKeyVisible(false);   // экран чёрный: убираем интерфейс ключа незаметно

        // 2. экран чуть проявляется, играет музыка, появляется картинка
        float length = fallbackSeconds;
        if (escapeMusic != null)
        {
            music.clip = escapeMusic;
            music.volume = musicVolume;
            music.Play();
            length = escapeMusic.length;
        }
        float start = Time.unscaledTime;

        tintHidden = true;   // экран сейчас полностью чёрный: красный фильтр можно убрать незаметно

        bool useWin = winScreen != null;
        if (useWin)
        {
            // показываем ваш готовый экран победы (пока он закрыт чёрным), потом чёрный уходит
            if (winScreenImage != null && escapeSprite != null) winScreenImage.sprite = escapeSprite;
            if (hideButtonsOnWin)
                foreach (var b in winScreen.GetComponentsInChildren<Button>(true)) b.gameObject.SetActive(false);
            winScreen.SetActive(true);
        }
        else if (picture != null && escapeSprite != null)
        {
            picture.sprite = escapeSprite;
            picture.gameObject.SetActive(true);
            StartCoroutine(FadeTo(picture, 1f, imageFadeInTime));
        }
        // с готовым экраном победы чёрный убирается полностью, со своей картинкой остаётся Reveal Alpha
        yield return FadeTo(black, useWin ? 0f : revealAlpha, revealTime);

        // 3. ждём конец трека, перед ним плавно гасим экран
        float fade = Mathf.Min(endFadeTime, Mathf.Max(0.1f, length * 0.5f));
        float fadeStart = start + length - fade;
        while (Time.unscaledTime < fadeStart) yield return null;

        if (picture != null) StartCoroutine(FadeTo(picture, 0f, fade));
        yield return FadeTo(black, 1f, fade);

        while (Time.unscaledTime < start + length) yield return null; // добираем до конца трека

        if (OnEscaped != null) OnEscaped();

        AudioListener.pause = false;   // сбрасываем, иначе в меню не будет звука
        LoadMenu();
    }

    void LoadMenu()
    {
        if (!string.IsNullOrEmpty(mainMenuScene) && Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            SceneManager.LoadScene(mainMenuScene);
            return;
        }
        Debug.LogWarning("[EscapeController] Сцена '" + mainMenuScene + "' не найдена в Build Settings. Загружается сцена с индексом 0.");
        SceneManager.LoadScene(0);
    }

    IEnumerator FadeTo(Image img, float to, float time)
    {
        if (img == null) yield break;
        float from = img.color.a;
        float t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(img, Mathf.Lerp(from, to, Mathf.Clamp01(t / time)));
            yield return null;
        }
        SetAlpha(img, to);
    }

    static void SetAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // ------------------------------------------------------------------
    GameObject keyObj;
    bool keySearched;

    GameObject KeyObject()
    {
        if (keyInterface != null) return keyInterface;
        if (keyObj == null && !keySearched)
        {
            keySearched = true;
            KeyChange kc = altars != null ? altars.keyChanger : null;
            if (kc == null)
            {
#if UNITY_2023_1_OR_NEWER
                kc = FindFirstObjectByType<KeyChange>();
#else
                kc = FindObjectOfType<KeyChange>();
#endif
            }
            if (kc != null) keyObj = kc.gameObject;
        }
        return keyObj;
    }

    void SetKeyVisible(bool visible)
    {
        GameObject go = KeyObject();
        if (go != null && go.activeSelf != visible) go.SetActive(visible);
    }

    // ------------------------------------------------------------------
    void BuildUI()
    {
        // красный фильтр: под HUD (у TaskHUD sortingOrder = 20)
        var tintCanvas = MakeCanvas("EscapeTint_Canvas", 5);
        tint = MakeFullscreenImage("Tint", tintCanvas.transform, new Color(tintColor.r, tintColor.g, tintColor.b, 0f));

        // финал: поверх всего
        var finaleCanvas = MakeCanvas("EscapeFinale_Canvas", 100);
        black = MakeFullscreenImage("Black", finaleCanvas.transform, new Color(0f, 0f, 0f, 0f));

        var pic = new GameObject("EscapePicture", typeof(RectTransform), typeof(Image));
        pic.transform.SetParent(finaleCanvas.transform, false);
        var rt = (RectTransform)pic.transform;
        float h = imageScreenFraction * 0.5f;
        rt.anchorMin = new Vector2(0.5f - h, 0.5f - h);
        rt.anchorMax = new Vector2(0.5f + h, 0.5f + h);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        picture = pic.GetComponent<Image>();
        picture.preserveAspect = true;
        picture.raycastTarget = false;
        picture.color = new Color(1f, 1f, 1f, 0f);
        pic.SetActive(false);

        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = false;
        music.spatialBlend = 0f;
        music.ignoreListenerPause = true;
    }

    Canvas MakeCanvas(string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        return c;
    }

    Image MakeFullscreenImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
}