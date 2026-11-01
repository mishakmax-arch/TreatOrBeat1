using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Скример: когда монстр ловит игрока, на весь экран вспыхивает картинка со звуком,
/// слегка трясётся и увеличивается; когда он закончился, вызывается GameOver (затемнение как было).
///
/// Вешается на любой объект сцены. MonsterChaser находит его сам (или назначьте в его поле Jumpscare).
/// UI создаётся в коде.
/// </summary>
public class Jumpscare : MonoBehaviour
{
    [Header("Ссылки (если пусто - ищутся в сцене)")]
    public PlayerMazeController player;

    [Header("Скример")]
    public Sprite scareSprite;                    // картинка (если пусто - будет только звук)
    public AudioClip scareSound;                  // звук
    [Range(0f, 1f)] public float volume = 1f;
    public bool preserveAspect = false;           // false - растянуть на весь экран

    [Header("Длительность")]
    public bool useClipLength = true;             // длительность = длина звука (но не больше Max Duration)
    public float duration = 1.2f;                 // если звука нет или Use Clip Length выключен
    public float maxDuration = 3f;

    [Header("Эффект")]
    public float shake = 25f;                     // дрожание в пикселях (0 = нет)
    public float zoomFrom = 1f;
    public float zoomTo = 1.2f;

    [Header("Звук")]
    public bool silenceOtherAudio = true;         // на время скримера глушить остальные звуки

    public bool IsPlaying { get; private set; }

    Canvas canvas;
    Image image;
    RectTransform imageRect;
    AudioSource src;

    void Awake()
    {
        if (player == null)
        {
#if UNITY_2023_1_OR_NEWER
            player = FindFirstObjectByType<PlayerMazeController>();
#else
            player = FindObjectOfType<PlayerMazeController>();
#endif
        }

        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        src.ignoreListenerPause = true;   // скример слышен, даже когда остальные звуки заглушены

        BuildUI();
    }

    void BuildUI()
    {
        var go = new GameObject("Jumpscare_Canvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // выше всего: HUD (20), финал (100)

        var img = new GameObject("Picture", typeof(RectTransform), typeof(Image));
        img.transform.SetParent(go.transform, false);
        imageRect = (RectTransform)img.transform;
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        float m = shake * 2f;                 // запас, чтобы при дрожании не показывались края
        imageRect.offsetMin = new Vector2(-m, -m);
        imageRect.offsetMax = new Vector2(m, m);

        image = img.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = preserveAspect;
        image.sprite = scareSprite;
        image.color = Color.white;

        go.SetActive(false);
    }

    /// Запускает скример; по окончании вызывает onDone.
    public void Play(Action onDone)
    {
        if (IsPlaying) return;
        StartCoroutine(Run(onDone));
    }

    IEnumerator Run(Action onDone)
    {
        IsPlaying = true;
        if (player != null) player.InputLocked = true;
        if (silenceOtherAudio) AudioListener.pause = true;

        float len = duration;
        if (scareSound != null)
        {
            src.clip = scareSound;
            src.volume = volume;
            src.Play();
            if (useClipLength) len = scareSound.length;
        }
        len = Mathf.Clamp(len, 0.1f, maxDuration);

        bool showImage = scareSprite != null;
        if (showImage)
        {
            image.sprite = scareSprite;
            image.preserveAspect = preserveAspect;
            canvas.gameObject.SetActive(true);
        }

        float t = 0f;
        while (t < len)
        {
            t += Time.unscaledDeltaTime;
            if (showImage)
            {
                float k = Mathf.Clamp01(t / len);
                float z = Mathf.Lerp(zoomFrom, zoomTo, k);
                imageRect.localScale = new Vector3(z, z, 1f);
                imageRect.anchoredPosition = shake > 0f ? UnityEngine.Random.insideUnitCircle * shake : Vector2.zero;
            }
            yield return null;
        }

        if (src.isPlaying) src.Stop();
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (silenceOtherAudio) AudioListener.pause = false;   // вернуть звук, чтобы GameOver играл как раньше

        IsPlaying = false;
        if (onDone != null) onDone();
    }

    void OnDestroy()
    {
        if (IsPlaying && silenceOtherAudio) AudioListener.pause = false;
    }
}
