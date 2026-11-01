using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Облачко с диалогом над объектом (тёмная плашка, хвостик снизу слева, стрелка "дальше" справа снизу).
/// Каждый элемент списка lines - отдельная реплика. Клавиша взаимодействия (Interact Key в RoomInteractor):
///   - первое нажатие открывает облачко с первой репликой;
///   - пока текст печатается - нажатие сразу допечатывает его;
///   - когда реплика показана целиком - нажатие листает на следующую;
///   - после последней реплики облачко закрывается.
/// Диалогов может быть несколько: lines - диалог 1, moreDialogues - диалоги 2, 3...
/// Следующий запускается из кода или UnityEvent через StartDialogueAt(номер) / StartNextDialogue().
/// Повесь на объект (или префаб) с галочкой Interactable в RoomGenerator.objects.
/// Всё создаётся само в мире, без Canvas. Для текста нужен TextMeshPro.
/// </summary>
public class DialogueBubble : MonoBehaviour, IRoomInteractable
{
    [Header("Dialogue 1")]
    [TextArea(2, 4)]
    public List<string> lines = new List<string> { "What's the matter?" };
    [Tooltip("Срабатывает, когда диалог 1 дочитан до конца (только в первый раз). Сюда - OrbitingSound.Play.")]
    public UnityEvent onFirstDialogueEnd;

    [System.Serializable]
    public class DialogueSet
    {
        [TextArea(2, 4)]
        public List<string> lines = new List<string>();
        [Tooltip("Срабатывает, когда этот диалог дочитан до конца (только в первый раз).")]
        public UnityEvent onEnd;
        [HideInInspector] public bool endFired;
    }

    [Header("Dialogue 2, 3, ...")]
    [Tooltip("Следующие диалоги. Element 0 - это диалог 2, Element 1 - диалог 3 и т.д.")]
    public List<DialogueSet> moreDialogues = new List<DialogueSet>();

    [Header("Text")]
    public TMP_FontAsset font;                       // свой шрифт (например, рукописный); пусто - по умолчанию
    public float fontSize = 3f;
    public bool upperCase = true;
    public Color textColor = Color.white;
    public float maxWidth = 3.5f;                    // ширина, после которой текст переносится

    [Header("Bubble")]
    public Vector2 offset = new Vector2(0.2f, 0.55f); // где кончик хвостика относительно центра объекта
    public Sprite boxSprite;                          // свой фон (лучше 9-slice); пусто - сгенерируется
    public Color boxColor = new Color(0.04f, 0.04f, 0.04f, 0.95f);
    public Vector2 padding = new Vector2(0.18f, 0.12f);
    public bool showTail = true;
    public float tailX = 0.3f;                        // расстояние от левого края плашки до хвостика
    public Color arrowColor = new Color(0.75f, 0.75f, 0.75f);
    public float arrowSize = 0.12f;
    public int sortingOrder = 60;

    [Header("Screen")]
    public bool keepOnScreen = true;                 // не давать облачку выходить за край камеры
    public float screenMargin = 0.15f;               // отступ от края экрана
    public bool hideTailWhenShifted = true;          // прятать хвостик, если облачко пришлось сдвинуть

    [Header("Animation")]
    public float charsPerSecond = 30f;               // скорость печати
    public float charFade = 3f;                      // сколько символов одновременно "проявляются"
    public float charRise = 0.08f;                   // буква всплывает снизу на эту высоту
    public float popTime = 0.15f;                    // появление/исчезновение облачка

    [Header("Behaviour")]
    public bool lockPlayer = true;                   // игрок не ходит, пока идёт диалог
    public bool hideHintWhileOpen = true;            // прятать ObjectHint на этом объекте
    public UnityEvent onDialogueStart;
    public UnityEvent onDialogueEnd;

    public bool IsOpen { get; private set; }
    public int LineIndex { get; private set; } = -1;

    /// <summary>Номер текущего диалога (1 - первый). Его же откроет клавиша взаимодействия.</summary>
    public int CurrentDialogue { get; private set; } = 1;

    bool firstEndFired;
    public bool IsTyping { get { return IsOpen && revealed < charCount + charFade; } }

    Transform root;
    SpriteRenderer boxRenderer, tailRenderer, arrowRenderer;
    TextMeshPro tmp;
    ObjectHint hint;
    RoomPlayerController player;

    float revealed;
    int charCount;
    Vector3 arrowBasePos;
    Vector3 boxCenterLocal;
    Vector2 boxSize;
    Coroutine popRoutine;

    static Sprite boxGen, tailGen, arrowGen;

    void Awake()
    {
        Build();
        root.gameObject.SetActive(false);
        hint = GetComponent<ObjectHint>();
    }

    void Start()
    {
#if UNITY_2023_1_OR_NEWER
        player = FindFirstObjectByType<RoomPlayerController>();
#else
        player = FindObjectOfType<RoomPlayerController>();
#endif
    }

    // ---------- реакция на игрока (вызывает RoomInteractor) ----------
    public void OnPlayerNear() { }

    public void OnPlayerLeave()
    {
        if (IsOpen) Close();
    }

    public void OnInteract()
    {
        if (!IsOpen) StartDialogue();
        else Next();
    }

    // ---------- управление диалогом ----------
    List<string> CurrentLines()
    {
        if (CurrentDialogue <= 1) return lines;
        int i = CurrentDialogue - 2;
        return (i < moreDialogues.Count && moreDialogues[i] != null) ? moreDialogues[i].lines : null;
    }

    /// <summary>Открывает текущий диалог (по умолчанию первый, после StartDialogueAt - последний запущенный).</summary>
    public void StartDialogue()
    {
        StartDialogueAt(CurrentDialogue);
    }

    /// <summary>
    /// Открывает диалог по номеру: 1 - первый (lines), 2 - moreDialogues[0], 3 - moreDialogues[1]...
    /// Можно вызывать из UnityEvent (например, OrbitingSound.onFinished), игроку ничего нажимать не нужно.
    /// Если облачко уже открыто, реплики сразу меняются на новый диалог.
    /// </summary>
    public void StartDialogueAt(int number)
    {
        int prev = CurrentDialogue;
        CurrentDialogue = Mathf.Max(1, number);
        var l = CurrentLines();
        if (l == null || l.Count == 0)
        {
            Debug.LogWarning("[DialogueBubble] Диалог " + number + " пустой или не существует.");
            CurrentDialogue = prev;
            return;
        }

        if (IsOpen)
        {
            ShowLine(0);
            return;
        }

        IsOpen = true;
        root.gameObject.SetActive(true);
        if (lockPlayer && player != null) player.InputLocked = true;
        if (hideHintWhileOpen && hint != null) hint.Hide();

        ShowLine(0);
        ClampToCamera();
        Pop(true);
        if (onDialogueStart != null) onDialogueStart.Invoke();
    }

    /// <summary>Открывает диалог со следующим номером.</summary>
    public void StartNextDialogue()
    {
        StartDialogueAt(CurrentDialogue + 1);
    }

    /// <summary>Допечатывает текущую реплику, либо листает дальше, либо закрывает облачко.</summary>
    public void Next()
    {
        if (!IsOpen) return;

        if (IsTyping)
        {
            revealed = charCount + charFade;
            ApplyReveal();
            return;
        }

        var l = CurrentLines();
        if (l != null && LineIndex + 1 < l.Count) ShowLine(LineIndex + 1);
        else Close(true);
    }

    /// <summary>Закрывает облачко, не засчитывая диалог как дочитанный.</summary>
    public void Close()
    {
        Close(false);
    }

    void Close(bool completed)
    {
        if (!IsOpen) return;
        IsOpen = false;
        LineIndex = -1;

        if (lockPlayer && player != null) player.InputLocked = false;
        if (hideHintWhileOpen && hint != null) hint.Show();

        Pop(false);
        if (onDialogueEnd != null) onDialogueEnd.Invoke();

        if (completed) FireDialogueEnd(CurrentDialogue);
    }

    // событие конца конкретного диалога - только один раз, чтобы повторный разговор не запускал всё заново
    void FireDialogueEnd(int number)
    {
        if (number <= 1)
        {
            if (firstEndFired) return;
            firstEndFired = true;
            if (onFirstDialogueEnd != null) onFirstDialogueEnd.Invoke();
            return;
        }

        int i = number - 2;
        if (i >= moreDialogues.Count || moreDialogues[i] == null || moreDialogues[i].endFired) return;
        moreDialogues[i].endFired = true;
        if (moreDialogues[i].onEnd != null) moreDialogues[i].onEnd.Invoke();
    }

    void ShowLine(int i)
    {
        LineIndex = i;
        tmp.text = CurrentLines()[i];
        Layout();
        if (IsOpen) ClampToCamera();

        revealed = 0f;
        tmp.ForceMeshUpdate();
        charCount = tmp.textInfo.characterCount;
        ApplyReveal();
    }

    void Update()
    {
        if (!IsOpen) return;

        if (IsTyping)
        {
            revealed = Mathf.Min(revealed + charsPerSecond * Time.deltaTime, charCount + charFade);
            ApplyReveal();
            arrowRenderer.gameObject.SetActive(false);
        }
        else
        {
            // стрелка "дальше" покачивается, когда реплика показана целиком
            arrowRenderer.gameObject.SetActive(true);
            float bob = Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.03f;
            arrowRenderer.transform.localPosition = arrowBasePos + new Vector3(bob, 0f, 0f);
        }
    }

    // ---------- удержание в пределах экрана ----------
    void LateUpdate()
    {
        if (root == null || !root.gameObject.activeSelf) return;
        ClampToCamera();
    }

    void ClampToCamera()
    {
        Vector3 basePos = transform.TransformPoint(new Vector3(offset.x, offset.y, 0f));
        root.position = basePos;

        var cam = Camera.main;
        if (!keepOnScreen || cam == null || !cam.orthographic)
        {
            tailRenderer.gameObject.SetActive(showTail);
            return;
        }

        // размеры плашки в мире (без учёта анимации появления)
        float sc = root.lossyScale.x / Mathf.Max(0.0001f, root.localScale.x);
        Vector3 c = basePos + boxCenterLocal * sc;
        float hw = boxSize.x * 0.5f * sc, hh = boxSize.y * 0.5f * sc;

        float camH = cam.orthographicSize, camW = camH * cam.aspect;
        Vector3 cp = cam.transform.position;
        float top = cp.y + camH - screenMargin, bottom = cp.y - camH + screenMargin;
        float right = cp.x + camW - screenMargin, left = cp.x - camW + screenMargin;

        Vector3 shift = Vector3.zero;
        if (c.x + hw > right) shift.x = right - (c.x + hw);
        if (c.x - hw + shift.x < left) shift.x = left - (c.x - hw);
        if (c.y - hh < bottom) shift.y = bottom - (c.y - hh);
        if (c.y + hh + shift.y > top) shift.y = top - (c.y + hh);   // верх важнее: длинный текст уходит вниз

        root.position = basePos + shift;
        bool shifted = shift.sqrMagnitude > 0.0001f;
        tailRenderer.gameObject.SetActive(showTail && !(hideTailWhenShifted && shifted));
    }

    // ---------- анимация печати: каждая буква проявляется и всплывает ----------
    void ApplyReveal()
    {
        tmp.ForceMeshUpdate();
        var ti = tmp.textInfo;
        float fade = Mathf.Max(0.01f, charFade);

        for (int i = 0; i < ti.characterCount; i++)
        {
            var ci = ti.characterInfo[i];
            if (!ci.isVisible) continue;

            float p = Mathf.Clamp01((revealed - i) / fade);
            float e = 1f - (1f - p) * (1f - p);         // плавное замедление в конце

            int m = ci.materialReferenceIndex, v = ci.vertexIndex;
            var verts = ti.meshInfo[m].vertices;
            var cols = ti.meshInfo[m].colors32;
            Vector3 shift = new Vector3(0f, -(1f - e) * charRise, 0f);
            byte a = (byte)(textColor.a * 255f * p);

            for (int j = 0; j < 4; j++)
            {
                verts[v + j] += shift;
                cols[v + j].a = a;
            }
        }
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.All);
    }

    // ---------- появление / исчезновение облачка ----------
    void Pop(bool show)
    {
        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(PopRoutine(show));
    }

    IEnumerator PopRoutine(bool show)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, popTime);
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.Clamp01(t);
            // появление: растёт от хвостика с лёгким отскоком; исчезновение: сжимается в хвостик
            float s = show
                ? Mathf.Lerp(0.3f, 1f, k) + 0.12f * Mathf.Sin(k * Mathf.PI)
                : 1f - k;
            root.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        root.localScale = Vector3.one;
        if (!show) root.gameObject.SetActive(false);
        popRoutine = null;
    }

    // ---------- построение ----------
    void Build()
    {
        root = new GameObject("DialogueBubble").transform;
        root.SetParent(transform, false);

        var boxGo = new GameObject("Box");
        boxGo.transform.SetParent(root, false);
        boxRenderer = boxGo.AddComponent<SpriteRenderer>();
        boxRenderer.sprite = boxSprite != null ? boxSprite : GetBoxSprite();
        boxRenderer.drawMode = SpriteDrawMode.Sliced;
        boxRenderer.sortingOrder = sortingOrder;

        var tailGo = new GameObject("Tail");
        tailGo.transform.SetParent(root, false);
        tailRenderer = tailGo.AddComponent<SpriteRenderer>();
        tailRenderer.sprite = GetTailSprite();
        tailRenderer.sortingOrder = sortingOrder;

        var arrowGo = new GameObject("NextArrow");
        arrowGo.transform.SetParent(root, false);
        arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = GetArrowSprite();
        arrowRenderer.sortingOrder = sortingOrder + 1;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root, false);
        tmp = textGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.sortingOrder = sortingOrder + 1;
    }

    void Layout()
    {
        root.localPosition = new Vector3(offset.x, offset.y, 0f);

        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.fontStyle = upperCase ? FontStyles.UpperCase : FontStyles.Normal;

        Vector2 pref = tmp.GetPreferredValues(tmp.text, maxWidth, 0f);
        float tw = Mathf.Min(pref.x, maxWidth) + 0.01f;
        tmp.rectTransform.sizeDelta = new Vector2(tw, pref.y);

        float arrowSpace = arrowSize + 0.06f;
        float boxW = tw + padding.x * 2f + arrowSpace;
        float boxH = pref.y + padding.y * 2f;

        boxSize = new Vector2(boxW, boxH);

        float tailH = showTail ? 0.16f : 0f;
        tailRenderer.gameObject.SetActive(showTail);
        tailRenderer.color = boxColor;
        tailRenderer.transform.localPosition = Vector3.zero;              // кончик хвостика = точка offset

        // плашка над хвостиком; хвостик на tailX от её левого края
        Vector3 boxCenter = new Vector3(boxW * 0.5f - tailX, tailH - 0.01f + boxH * 0.5f, 0f);
        boxCenterLocal = boxCenter;
        boxRenderer.color = boxColor;
        boxRenderer.size = new Vector2(boxW, boxH);
        boxRenderer.transform.localPosition = boxCenter;

        tmp.rectTransform.localPosition = boxCenter + new Vector3(-arrowSpace * 0.5f, 0f, 0f);

        arrowRenderer.color = arrowColor;
        float k = arrowSize / 0.1f;                                       // спрайт стрелки 0.1 единицы
        arrowRenderer.transform.localScale = new Vector3(k, k, 1f);
        arrowBasePos = boxCenter + new Vector3(boxW * 0.5f - padding.x * 0.6f - arrowSize * 0.5f,
                                               -boxH * 0.5f + padding.y * 0.6f + arrowSize * 0.5f, 0f);
        arrowRenderer.transform.localPosition = arrowBasePos;
    }

    // ---------- сгенерированные спрайты (белые, цвет задаётся через SpriteRenderer.color) ----------

    // скруглённый прямоугольник 32x32 с 9-slice границей
    static Sprite GetBoxSprite()
    {
        if (boxGen != null) return boxGen;
        const int n = 32; const float r = 10f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, n - r);
                float cy = Mathf.Clamp(y + 0.5f, r, n - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(r - d + 0.5f);                    // сглаженный край
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        boxGen = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
        return boxGen;
    }

    // хвостик: прямоугольный треугольник, кончик снизу слева (pivot в кончике)
    static Sprite GetTailSprite()
    {
        if (tailGen != null) return tailGen;
        const int n = 16;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float width = (y + 0.5f) / n * n * 0.9f;                  // ширина растёт снизу вверх
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(width - x));
            }
        tex.SetPixels(px);
        tex.Apply();
        tailGen = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0f, 0f), 100f);
        return tailGen;
    }

    // стрелка "дальше": треугольник вправо, 10x10 пикселей = 0.1 единицы
    static Sprite GetArrowSprite()
    {
        if (arrowGen != null) return arrowGen;
        const int n = 10;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float half = (1f - (x + 0.5f) / n) * n * 0.5f;
                bool inside = Mathf.Abs(y + 0.5f - n * 0.5f) <= half;
                px[y * n + x] = inside ? Color.white : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        arrowGen = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return arrowGen;
    }
}