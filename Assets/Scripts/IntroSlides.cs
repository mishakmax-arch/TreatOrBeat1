using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Предыстория: слайды в начале игровой сцены (после кнопки "Играть" в главном меню).
///
/// Пока идут слайды, игра стоит: игрок не двигается, монстр заморожен (и появится только после слайдов),
/// таскбар скрыт, остальные звуки заглушены. Время (Time.timeScale) не трогается, поэтому музыка паузы
/// не включается. После последнего слайда экран плавно открывается и игра начинается.
///
/// Слайд: картинка + необязательный текст (печатается по буквам). Переход: Пробел / Enter / клик мыши
/// либо автоматически через заданное число секунд.
///
/// Вешается на пустой объект в ИГРОВОЙ сцене. UI создаётся в коде.
/// </summary>
public class IntroSlides : MonoBehaviour
{
    [Serializable]
    public class Slide
    {
        public Sprite image;                       // картинка слайда
        [TextArea(2, 6)] public string caption;    // текст внизу (необязательно)
        public float seconds = 6f;                 // сколько держать слайд после того, как текст напечатан
    }

    /// true, пока идёт предыстория (используется TaskHUD, чтобы скрыть таскбар).
    public static bool IsPlaying { get; private set; }
    static bool shownThisSession;

    [Header("Слайды (3 штуки)")]
    public Slide[] slides = new Slide[3];

    [Header("Показ")]
    public bool autoAdvance = true;                // переходить сами через Seconds
    public bool showOnlyOnceAfterLaunch = false;   // true - показывать только при первом запуске игры (не после рестарта сцены)
    public float fadeTime = 0.8f;                  // появление/исчезновение слайда и открытие игры
    public float inputDelay = 0.5f;                // сколько слайд не реагирует на нажатия (чтобы не пролистать случайно)

    [Header("Текст")]
    public bool typewriter = true;                 // печатать текст по буквам
    public float charsPerSecond = 40f;
    public int captionFontSize = 34;
    public Color captionColor = Color.white;
    public string hintText = "SPACE / click";      // подсказка справа внизу
    public Font font;

    [Header("Звук")]
    public AudioClip introMusic;
    [Range(0f, 1f)] public float musicVolume = 0.8f;
    public bool silenceOtherAudio = true;          // на время слайдов глушить остальные звуки (их музыка начнётся после)

    [Header("Ссылки (если пусто - ищутся в сцене)")]
    public PlayerMazeController player;
    public MonsterChaser monster;

    public event Action OnFinished;

    Canvas canvas;
    Image black, picture, band;
    Text caption, hint;
    AudioSource music;
    bool weSetListenerPause;
    bool active;

    void Awake()
    {
        if (!HasContent() || (showOnlyOnceAfterLaunch && shownThisSession))
        {
            enabled = false;
            return;
        }

        active = true;
        IsPlaying = true;

        if (player == null)
        {
#if UNITY_2023_1_OR_NEWER
            player = FindFirstObjectByType<PlayerMazeController>();
#else
            player = FindObjectOfType<PlayerMazeController>();
#endif
        }
        if (monster == null)
        {
#if UNITY_2023_1_OR_NEWER
            monster = FindFirstObjectByType<MonsterChaser>();
#else
            monster = FindObjectOfType<MonsterChaser>();
#endif
        }

        // игра стоит, пока идут слайды
        if (player != null) player.InputLocked = true;
        if (monster != null) monster.Freeze(true);
        if (silenceOtherAudio)
        {
            AudioListener.pause = true;
            weSetListenerPause = true;
        }

        BuildUI();
    }

    void Start()
    {
        if (active) StartCoroutine(Run());
    }

    void OnDestroy()
    {
        Finish(false);
    }

    bool HasContent()
    {
        if (slides == null) return false;
        foreach (var s in slides)
            if (s != null && (s.image != null || !string.IsNullOrEmpty(s.caption))) return true;
        return false;
    }

    // ------------------------------------------------------------------
    IEnumerator Run()
    {
        if (introMusic != null)
        {
            music.clip = introMusic;
            music.volume = musicVolume;
            music.Play();
        }

        for (int i = 0; i < slides.Length; i++)
        {
            Slide s = slides[i];
            if (s == null || (s.image == null && string.IsNullOrEmpty(s.caption))) continue;

            picture.sprite = s.image;
            picture.enabled = s.image != null;
            string text = s.caption ?? "";
            caption.text = typewriter ? "" : text;
            band.enabled = !string.IsNullOrEmpty(text);

            SetSlideAlpha(0f);
            yield return FadeSlide(0f, 1f, fadeTime);

            float t = 0f, hold = 0f, typed = 0f;
            bool done = !typewriter || text.Length == 0;
            bool advance = false;

            while (!advance)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;

                if (!done)
                {
                    typed += dt * Mathf.Max(1f, charsPerSecond);
                    int n = Mathf.Min(text.Length, (int)typed);
                    caption.text = text.Substring(0, n);
                    done = n >= text.Length;
                }
                else
                {
                    hold += dt;
                }

                bool pressed = t >= inputDelay &&
                               (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                                Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0));

                if (pressed)
                {
                    if (!done) { caption.text = text; done = true; }   // первое нажатие дописывает текст
                    else advance = true;
                }
                else if (autoAdvance && done && hold >= s.seconds)
                {
                    advance = true;
                }

                // подсказка: когда можно листать, слегка мигает
                if (hint != null)
                {
                    float a = (done && t >= inputDelay) ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f) * 0.5f + 0.25f : 0f;
                    SetAlpha(hint, a * picture.color.a);
                }

                yield return null;
            }

            yield return FadeSlide(1f, 0f, fadeTime);
        }

        // конец: музыка стихает, чёрный экран открывает игру
        StartCoroutine(FadeMusic(fadeTime));
        yield return FadeImage(black, 1f, 0f, fadeTime);

        Finish(true);
    }

    // разблокируем игру и убираем UI; вызывается и из OnDestroy на случай выгрузки сцены
    void Finish(bool normal)
    {
        if (!active) return;
        active = false;
        IsPlaying = false;
        shownThisSession = true;

        if (weSetListenerPause)
        {
            weSetListenerPause = false;
            AudioListener.pause = false;
        }

        if (normal)
        {
            if (player != null) player.InputLocked = false;
            if (monster != null) monster.Freeze(false);   // монстр появится как обычно, с задержкой спавна
            if (canvas != null) Destroy(canvas.gameObject);
            if (OnFinished != null) OnFinished();
        }
    }

    // ------------------------------------------------------------------
    void SetSlideAlpha(float a)
    {
        SetAlpha(picture, a);
        SetAlpha(caption, a);
        Color c = band.color; c.a = 0.55f * a; band.color = c;
    }

    IEnumerator FadeSlide(float from, float to, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            SetSlideAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / time)));
            yield return null;
        }
        SetSlideAlpha(to);
    }

    IEnumerator FadeImage(Image img, float from, float to, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(img, Mathf.Lerp(from, to, Mathf.Clamp01(t / time)));
            yield return null;
        }
        SetAlpha(img, to);
    }

    IEnumerator FadeMusic(float time)
    {
        if (music == null || !music.isPlaying) yield break;
        float from = music.volume, t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            music.volume = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / time));
            yield return null;
        }
        music.Stop();
    }

    static void SetAlpha(Graphic g, float a)
    {
        if (g == null) return;
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    // ------------------------------------------------------------------
    void BuildUI()
    {
        var cgo = new GameObject("IntroSlides_Canvas");
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;   // выше HUD (20) и финала (100), ниже скримера (200)
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // чёрный фон: закрывает игру с первого кадра
        black = MakeFull("Black", cgo.transform, Color.black);

        // картинка слайда (вписывается в экран с сохранением пропорций)
        picture = MakeFull("Picture", cgo.transform, new Color(1f, 1f, 1f, 0f));
        picture.preserveAspect = true;

        // затемнённая полоса под текстом
        var bgo = new GameObject("CaptionBand", typeof(RectTransform), typeof(Image));
        bgo.transform.SetParent(cgo.transform, false);
        var brt = (RectTransform)bgo.transform;
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 0.24f);
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        band = bgo.GetComponent<Image>();
        band.color = new Color(0f, 0f, 0f, 0f);
        band.raycastTarget = false;

        // текст слайда
        caption = MakeText("Caption", bgo.transform, captionFontSize, FontStyle.Normal, TextAnchor.MiddleCenter, captionColor);
        var crt = caption.rectTransform;
        crt.anchorMin = new Vector2(0.08f, 0f);
        crt.anchorMax = new Vector2(0.92f, 1f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        caption.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = caption.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);

        // подсказка
        hint = MakeText("Hint", cgo.transform, Mathf.Max(14, captionFontSize / 2), FontStyle.Normal, TextAnchor.LowerRight, Color.white);
        hint.text = hintText;
        var hrt = hint.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(1f, 0f);
        hrt.pivot = new Vector2(1f, 0f);
        hrt.sizeDelta = new Vector2(500f, 60f);
        hrt.anchoredPosition = new Vector2(-30f, 20f);
        SetAlpha(hint, 0f);

        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.spatialBlend = 0f;
        music.ignoreListenerPause = true;   // слышна, даже когда остальные звуки заглушены
    }

    Image MakeFull(string name, Transform parent, Color color)
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

    Text MakeText(string name, Transform parent, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : DefaultFont();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        return t;
    }

    static Font DefaultFont()
    {
#if UNITY_2022_2_OR_NEWER
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
    }
}
