using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Меню Credits для главного меню: кнопка открывает небольшое окно со списком авторов
/// и (необязательно) ссылками на GitHub. Ссылки открываются в браузере.
///
/// Как подключить: повесить на любой объект сцены главного меню.
///  - Своя кнопка: перетащите её в Open Button (удобно продублировать существующую кнопку меню,
///    чтобы стиль совпал). Если поле пустое и на этом объекте нет Button - кнопка CREDITS создаётся сама
///    в левом нижнем углу.
///  - Авторов и ссылки заполните в списке Authors. Поле Url можно оставить пустым - тогда кнопки GitHub не будет.
/// Закрыть окно: кнопка CLOSE, клик по тёмному фону или Escape.
/// Нужен EventSystem в сцене (он есть, раз кнопки меню работают).
/// </summary>
public class CreditsMenu : MonoBehaviour
{
    [Serializable]
    public class Author
    {
        public string name = "Name Surname";
        public string role = "Role";
        public string url = "";               // например https://github.com/username (пусто = без ссылки)
    }

    [Header("Кнопка открытия")]
    public Button openButton;                  // ваша кнопка; если пусто - Button с этого объекта, иначе создаётся новая
    public string openButtonLabel = "CREDITS";

    [Header("Содержимое")]
    public string title = "CREDITS";
    public string gameTitle = "";              // название игры под заголовком (можно пусто)
    public Author[] authors = new Author[]
    {
        new Author { name = "Name Surname", role = "Programming", url = "" },
        new Author { name = "Name Surname", role = "Art", url = "" },
        new Author { name = "Name Surname", role = "Music & Sound", url = "" },
    };
    public string projectUrl = "";             // ссылка на репозиторий игры (пусто = без кнопки)
    public string projectLinkLabel = "Project on GitHub";
    public string linkLabel = "GitHub";        // подпись кнопки рядом с автором
    [TextArea(1, 3)] public string footer = "Thanks for playing!";
    public string closeLabel = "CLOSE";

    [Header("Оформление")]
    public Font font;
    public int titleSize = 52;
    public int textSize = 28;
    public float panelWidth = 760f;
    public Color dimColor = new Color(0f, 0f, 0f, 0.75f);
    public Color panelColor = new Color(0.08f, 0.07f, 0.1f, 0.97f);
    public Color accentColor = new Color(0.675f, 0.576f, 0.757f, 1f);   // сиреневый, как линии стен
    public Color textColor = Color.white;
    public Color roleColor = new Color(0.7f, 0.7f, 0.75f, 1f);
    public Color buttonColor = new Color(0.17f, 0.14f, 0.22f, 1f);
    public bool closeOnEscape = true;

    GameObject panelRoot;
    Canvas rootCanvas;

    void Start()
    {
        rootCanvas = ResolveCanvas();

        if (openButton == null) openButton = GetComponent<Button>();
        if (openButton == null) openButton = CreateOpenButton();
        if (openButton != null) openButton.onClick.AddListener(Toggle);

        BuildPanel();
        panelRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(Toggle);
    }

    void Update()
    {
        if (closeOnEscape && panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    // ------------------------------------------------------------------
    public void Open()
    {
        if (panelRoot == null) return;
        panelRoot.transform.SetAsLastSibling();   // поверх остального меню
        panelRoot.SetActive(true);
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Toggle()
    {
        if (panelRoot == null) return;
        if (panelRoot.activeSelf) Close(); else Open();
    }

    static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        Application.OpenURL(url);
    }

    // ------------------------------------------------------------------
    // Canvas: тот, где лежит кнопка, иначе любой в сцене, иначе создаём свой
    Canvas ResolveCanvas()
    {
        Canvas c = null;
        if (openButton != null) c = openButton.GetComponentInParent<Canvas>();
        if (c == null) c = GetComponentInParent<Canvas>();
        if (c == null)
        {
#if UNITY_2023_1_OR_NEWER
            c = FindFirstObjectByType<Canvas>();
#else
            c = FindObjectOfType<Canvas>();
#endif
        }
        if (c == null)
        {
            var go = new GameObject("Credits_Canvas");
            c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }
        return c.rootCanvas;
    }

    Button CreateOpenButton()
    {
        var btn = MakeButton(rootCanvas.transform, openButtonLabel, textSize, 240f, 64f, null);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);   // левый нижний угол
        rt.anchoredPosition = new Vector2(30f, 30f);
        rt.sizeDelta = new Vector2(240f, 64f);
        return btn;
    }

    // ------------------------------------------------------------------
    void BuildPanel()
    {
        // корень без графики: внутри два соседа - тёмный фон (по клику закрывает) и окно
        panelRoot = new GameObject("CreditsPanel", typeof(RectTransform));
        panelRoot.transform.SetParent(rootCanvas.transform, false);
        Stretch((RectTransform)panelRoot.transform);

        var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
        dim.transform.SetParent(panelRoot.transform, false);
        Stretch((RectTransform)dim.transform);
        var dimImg = dim.GetComponent<Image>();
        dimImg.color = dimColor;
        var dimBtn = dim.GetComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.targetGraphic = dimImg;
        dimBtn.onClick.AddListener(Close);

        // окно: фиксированная ширина, высота по содержимому
        var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(panelRoot.transform, false);
        var brt = (RectTransform)box.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(panelWidth, 100f);
        brt.anchoredPosition = Vector2.zero;
        var boxImg = box.GetComponent<Image>();
        boxImg.color = panelColor;
        var outline = box.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(3f, -3f);

        var vl = box.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(36, 36, 32, 32);
        vl.spacing = 16f;
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        var fit = box.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // заголовок
        MakeText(box.transform, title, titleSize, FontStyle.Bold, accentColor);
        if (!string.IsNullOrEmpty(gameTitle))
            MakeText(box.transform, gameTitle, textSize, FontStyle.Italic, roleColor);

        Spacer(box.transform, 6f);

        // авторы
        string roleHex = ColorUtility.ToHtmlStringRGB(roleColor);
        if (authors != null)
        {
            foreach (var a in authors)
            {
                if (a == null || string.IsNullOrEmpty(a.name)) continue;

                var row = MakeRow(box.transform);
                string line = "<b>" + a.name + "</b>";
                if (!string.IsNullOrEmpty(a.role)) line += "  <color=#" + roleHex + ">" + a.role + "</color>";
                MakeText(row.transform, line, textSize, FontStyle.Normal, textColor);

                if (!string.IsNullOrEmpty(a.url))
                {
                    string url = a.url;
                    MakeButton(row.transform, linkLabel, textSize - 6, 140f, 40f, () => OpenUrl(url));
                }
            }
        }

        // ссылка на проект
        if (!string.IsNullOrEmpty(projectUrl))
        {
            Spacer(box.transform, 4f);
            var prow = MakeRow(box.transform);
            string purl = projectUrl;
            MakeButton(prow.transform, projectLinkLabel, textSize - 4, 360f, 48f, () => OpenUrl(purl));
        }

        if (!string.IsNullOrEmpty(footer))
        {
            Spacer(box.transform, 4f);
            MakeText(box.transform, footer, textSize - 6, FontStyle.Italic, roleColor);
        }

        Spacer(box.transform, 6f);

        // закрыть
        var crow = MakeRow(box.transform);
        MakeButton(crow.transform, closeLabel, textSize, 220f, 56f, Close);
    }

    // ------------------------------------------------------------------
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    GameObject MakeRow(Transform parent)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 20f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        return go;
    }

    void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    Text MakeText(Transform parent, string text, int size, FontStyle style, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font != null ? font : DefaultFont();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    Button MakeButton(Transform parent, string label, int size, float width, float height, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label + "_Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = buttonColor;

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = cb;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = accentColor;
        outline.effectDistance = new Vector2(2f, -2f);

        var txt = MakeText(go.transform, label, size, FontStyle.Bold, textColor);
        Stretch(txt.rectTransform);
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        return btn;
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
