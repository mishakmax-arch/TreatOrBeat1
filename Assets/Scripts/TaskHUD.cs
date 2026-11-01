using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Маленький таскбар сбоку: "TASKS" + задания и счётчик активированных алтарей.
/// После активации ПОСЛЕДНЕГО алтаря (когда карта перестроилась) на экране на несколько секунд
/// появляется большой жирный "RUN!", а задание меняется на "добраться до выхода и открыть дверь".
///
/// Как подключить: повесить на любой объект сцены. Всё остальное (UI, Canvas) создаётся само.
/// AltarUse и MazeGenerator ищутся в сцене автоматически, если не назначены.
/// AltarUse менять не нужно: счётчик берётся из NumberOfActivatedAltars.
/// </summary>
public class TaskHUD : MonoBehaviour
{
    [Header("Ссылки (если пусто - ищутся в сцене)")]
    public AltarUse altars;
    public MazeGenerator maze;

    [Header("Тексты")]
    public string title = "TASKS";
    public string collectTask = "Collect key pieces: activate altars";
    public string exitTask = "Reach the exit and unlock the door";
    public string runWord = "RUN!";

    [Header("RUN")]
    public float runShowSeconds = 5f;
    public int runFontSize = 120;
    public Color runColor = new Color(0.95f, 0.1f, 0.1f, 1f);
    public float pulseSpeed = 9f;
    public float pulseAmount = 0.08f;

    [Header("Панель")]
    public bool rightSide = false;                  // false - слева сверху, true - справа сверху
    public Vector2 margin = new Vector2(20f, 20f);
    public int fontSize = 20;
    public Color panelColor = new Color(0f, 0f, 0f, 0.5f);
    public Color doneColor = new Color(0.55f, 0.9f, 0.55f, 1f);

    [Header("Скрывать таскбар")]
    public bool hideOnPause = true;                 // пауза = Time.timeScale == 0 (или PauseMusic.SetPaused(true))
    public bool hideOnDeath = true;                 // монстр поймал игрока
    public MonsterChaser monster;                   // если пусто - ищется в сцене

    [Header("Свой UI (необязательно: если пусто - создаётся автоматически)")]
    public Text taskText;
    public Text runText;
    public Font font;

    int lastActivated = -1, lastTotal = -1;
    bool runShown;
    float runTimer;
    bool dead;
    EscapeController escape;
    bool hudVisible = true;
    CanvasGroup group;

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
#if UNITY_2023_1_OR_NEWER
        escape = FindFirstObjectByType<EscapeController>();
#else
        escape = FindObjectOfType<EscapeController>();
#endif
        if (monster == null && altars != null) monster = altars.monster;
        if (monster == null)
        {
#if UNITY_2023_1_OR_NEWER
            monster = FindFirstObjectByType<MonsterChaser>();
#else
            monster = FindObjectOfType<MonsterChaser>();
#endif
        }
        if (altars == null) Debug.LogWarning("[TaskHUD] AltarUse не найден в сцене: счётчик работать не будет.");

        BuildUI();
        if (runText != null) runText.gameObject.SetActive(false);
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

    // монстр поймал игрока: таскбар пропадает до новой игры
    void HandleCaught() { dead = true; }

    /// Можно вызвать из своего GameManager, если смерть определяется не через MonsterChaser.
    public void SetDead(bool value) { dead = value; }

    // новая игра: сбрасываем RUN и обновляем текст
    void HandleGenerated()
    {
        dead = false;
        runShown = false;
        runTimer = 0f;
        if (runText != null) runText.gameObject.SetActive(false);
        lastActivated = -1;
    }

    // карта перестроилась после активации алтаря: если он был последним - показываем RUN
    void HandleRebuilt()
    {
        if (runShown || altars == null) return;
        int total = TotalAltars();
        if (total > 0 && altars.NumberOfActivatedAltars >= total)
        {
            runShown = true;
            runTimer = runShowSeconds;
            if (runText != null) runText.gameObject.SetActive(true);
        }
    }

    // алтари = все точки, кроме входа и выхода
    int TotalAltars()
    {
        if (maze == null || maze.WaypointCells == null) return 0;
        int n = maze.WaypointCells.Count;
        if (maze.EntranceIndex >= 0) n--;
        if (maze.ExitIndex >= 0 && maze.ExitIndex != maze.EntranceIndex) n--;
        return Mathf.Max(0, n);
    }

    void Update()
    {
        bool paused = hideOnPause && (Time.timeScale <= 0.0001f || PauseMusic.IsPaused);
        bool escaped = escape != null && escape.Finished;   // в финале побега таскбар не нужен
        bool hide = paused || escaped || IntroSlides.IsPlaying || (hideOnDeath && dead);   // во время предыстории таскбар скрыт
        ApplyVisible(!hide);
        if (hide) return;   // пока скрыт, таймер RUN не идёт

        UpdateTasks();
        UpdateRun();
    }

    void ApplyVisible(bool visible)
    {
        if (visible == hudVisible) return;
        hudVisible = visible;

        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
        }
        else
        {
            // свой UI без нашего Canvas: просто выключаем компоненты текста
            if (taskText != null) taskText.enabled = visible;
            if (runText != null) runText.enabled = visible;
        }
    }

    void UpdateTasks()
    {
        if (taskText == null || altars == null) return;

        int total = TotalAltars();
        int done = Mathf.Min(altars.NumberOfActivatedAltars, total);
        if (done == lastActivated && total == lastTotal) return;
        lastActivated = done;
        lastTotal = total;

        string head = "<b>" + title + "</b>\n";
        string doneHex = ColorUtility.ToHtmlStringRGB(doneColor);

        if (total > 0 && done >= total)
        {
            taskText.text = head +
                "<color=#" + doneHex + ">[x] Collect key pieces " + done + "/" + total + "</color>\n" +
                "[ ] " + exitTask;
        }
        else
        {
            taskText.text = head + "[ ] " + collectTask + " " + done + "/" + total;
        }
    }

    void UpdateRun()
    {
        if (runText == null || runTimer <= 0f) return;

        runTimer -= Time.unscaledDeltaTime;
        if (runTimer <= 0f)
        {
            runText.gameObject.SetActive(false);
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
        runText.rectTransform.localScale = new Vector3(pulse, pulse, 1f);

        // последняя секунда - плавно гаснет
        Color c = runColor;
        c.a = Mathf.Clamp01(runTimer / 1f);
        runText.color = c;
    }

    // ------------------------------------------------------------------
    // UI создаётся в коде, чтобы не настраивать его руками
    void BuildUI()
    {
        if (taskText != null && runText != null) return;

        var canvasGo = new GameObject("TaskHUD_Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasGo.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        if (taskText == null)
        {
            var panel = new GameObject("TaskPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            float ax = rightSide ? 1f : 0f;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(ax, 1f);
            prt.anchoredPosition = new Vector2(rightSide ? -margin.x : margin.x, -margin.y);
            var img = panel.GetComponent<Image>();
            img.color = panelColor;
            img.raycastTarget = false;

            var vl = panel.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(14, 14, 10, 10);
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = false;
            vl.childForceExpandHeight = false;

            var fit = panel.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            taskText = MakeText("TaskText", panel.transform, fontSize, FontStyle.Normal,
                                rightSide ? TextAnchor.UpperRight : TextAnchor.UpperLeft, Color.white);
        }

        if (runText == null)
        {
            runText = MakeText("RunText", canvasGo.transform, runFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, runColor);
            runText.text = runWord;
            var rt = runText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.78f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1400f, 300f);
            rt.anchoredPosition = Vector2.zero;
            var outline = runText.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4f, -4f);
        }
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
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
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
