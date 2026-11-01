using TMPro;
using UnityEngine;

/// <summary>
/// Небольшая подсказка в мире: слева иконка (например, клавиша пробела), справа подпись, сзади тёмный фон.
/// Ничего не требует от других скриптов: просто повесь на пустой объект и перетащи его в AltarUse (поле Altar Hint).
/// AltarUse сам переносит подсказку к нужному алтарю и вызывает Show() / Hide().
/// Всё строится в коде, Canvas не нужен.
/// </summary>
public class AltarHint : MonoBehaviour
{
    [Header("Content")]
    public string text = "Use";
    public Sprite icon;                       // картинка клавиши пробела; можно оставить пустой

    [Header("Look")]
    public Vector2 offset = new Vector2(0f, 0.9f);   // смещение над центром клетки (мировые единицы)
    public float iconSize = 0.4f;                    // высота иконки
    public float spacing = 0.12f;                    // расстояние между иконкой и текстом
    public float fontSize = 3f;
    public Color textColor = Color.white;
    public TMP_FontAsset font;                       // если пусто - шрифт TMP по умолчанию

    [Header("Background")]
    public bool useBackground = true;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.7f);
    public Vector2 padding = new Vector2(0.12f, 0.08f);

    [Header("Sorting")]
    public int sortingOrder = 100;                   // поверх стен, игрока и алтарей

    Transform root;
    SpriteRenderer iconRenderer, bgRenderer;
    TextMeshPro tmp;
    static Sprite whiteSprite;

    void Awake()
    {
        Build();
        Refresh();
        root.gameObject.SetActive(false);   // стартует скрытой
    }

    void OnValidate()
    {
        // в Play Mode изменения в инспекторе сразу видны
        if (Application.isPlaying && root != null) Refresh();
    }

    // ---------- публичные методы ----------
    public void Show() { if (root != null) root.gameObject.SetActive(true); }
    public void Hide() { if (root != null) root.gameObject.SetActive(false); }

    public void SetText(string newText) { text = newText; Refresh(); }
    public void SetIcon(Sprite newIcon) { icon = newIcon; Refresh(); }

    // ---------- построение ----------
    void Build()
    {
        root = new GameObject("Hint").transform;
        root.SetParent(transform, false);

        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(root, false);
        bgRenderer = bgGo.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = GetWhiteSprite();
        bgRenderer.sortingOrder = sortingOrder;

        var iconGo = new GameObject("Icon");
        iconGo.transform.SetParent(root, false);
        iconRenderer = iconGo.AddComponent<SpriteRenderer>();
        iconRenderer.sortingOrder = sortingOrder + 1;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root, false);
        tmp = textGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.rectTransform.pivot = new Vector2(0f, 0.5f);
        tmp.sortingOrder = sortingOrder + 1;
    }

    void Refresh()
    {
        if (root == null) return;

        root.localPosition = new Vector3(offset.x, offset.y, 0f);

        // --- текст ---
        if (font != null) tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.text = text;
        Vector2 textSize = tmp.GetPreferredValues(text);
        tmp.rectTransform.sizeDelta = textSize;

        // --- иконка ---
        bool hasIcon = icon != null;
        iconRenderer.gameObject.SetActive(hasIcon);
        float iconW = 0f;
        if (hasIcon)
        {
            iconRenderer.sprite = icon;
            float h = Mathf.Max(0.0001f, icon.bounds.size.y);
            float k = iconSize / h;
            iconRenderer.transform.localScale = new Vector3(k, k, 1f);
            iconW = icon.bounds.size.x * k;
        }

        bool hasText = !string.IsNullOrEmpty(text);
        tmp.gameObject.SetActive(hasText);
        float textW = hasText ? textSize.x : 0f;
        float gap = (hasIcon && hasText) ? spacing : 0f;

        // --- раскладка: [иконка][отступ][текст], всё по центру над алтарём ---
        float total = iconW + gap + textW;
        float left = -total * 0.5f;

        if (hasIcon)
        {
            float pivotShift = icon.bounds.center.x * iconRenderer.transform.localScale.x;
            float cy = -icon.bounds.center.y * iconRenderer.transform.localScale.y;
            iconRenderer.transform.localPosition = new Vector3(left + iconW * 0.5f - pivotShift, cy, 0f);
        }
        tmp.rectTransform.localPosition = new Vector3(left + iconW + gap, 0f, 0f);

        // --- фон ---
        float contentH = Mathf.Max(hasIcon ? iconSize : 0f, hasText ? textSize.y : 0f);
        bgRenderer.gameObject.SetActive(useBackground && total > 0f);
        bgRenderer.color = backgroundColor;
        bgRenderer.transform.localScale = new Vector3(total + padding.x * 2f, contentH + padding.y * 2f, 1f);
        bgRenderer.transform.localPosition = Vector3.zero;
    }

    // белый квадрат для фона
    static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        return whiteSprite;
    }
}
