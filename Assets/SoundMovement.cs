using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Невидимый источник звука, который кружит вокруг игрока по часовой стрелке.
/// Повесь на пустой GameObject с AudioSource и вызови Play() из любого скрипта
/// (или из UnityEvent, например onDialogueEnd у DialogueBubble).
///
/// При вызове объект появляется на 2 клетки выше игрока и в течение duration секунд
/// летит по кругу по часовой стрелке, а звук всё это время идёт из его текущей точки.
///
/// Направление звука считается вручную относительно ИГРОКА (как в MonsterSound):
///  - лево/право: panStereo,
///  - громкость: по расстоянию до игрока,
///  - верх/низ: сверху ярче и чуть выше по тону, снизу глуше и ниже.
/// Звук играется в 2D-режиме, поэтому не важно, где стоит камера с AudioListener.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(AudioLowPassFilter))]
public class OrbitingSound : MonoBehaviour
{
    [Header("Target")]
    public Transform player;                 // если пусто - ищется RoomPlayerController в сцене
    public bool followPlayer = true;         // центр круга двигается вместе с игроком

    [Header("Orbit")]
    public float radiusInBlocks = 2f;        // на сколько клеток от игрока (старт - прямо сверху)
    public float duration = 8f;              // сколько секунд длится движение
    public float revolutions = 1f;           // сколько полных кругов за duration
    public float blockSize = 0f;             // размер клетки в мире; 0 - взять из RoomGenerator (или 1)

    [Header("Sound")]
    [Tooltip("Несколько звуков: играются друг за другом, пока объект летит. Пусто - используется клип из AudioSource.")]
    public AudioClip[] clips;
    public ClipOrder clipOrder = ClipOrder.InOrder;
    [Tooltip("Пауза между звуками (сек), случайная из диапазона.")]
    public Vector2 gapRange = new Vector2(0f, 0f);
    public bool loopWhileMoving = true;      // для одного клипа из AudioSource: зациклить, если он короче duration
    public float fadeOutTime = 0.5f;         // плавное затухание в конце; 0 - обрыв

    [Header("Громкость и расстояние")]
    [Range(0f, 1f)] public float maxVolume = 1f;
    public float minHearDistance = 2f;       // ближе этого - полная громкость (в юнитах)
    public float maxHearDistance = 30f;      // дальше этого - тишина
    public float rolloffPower = 2f;          // 1 = линейно, 2 = быстрее нарастает вблизи

    [Header("Направление")]
    [Tooltip("На таком смещении по X звук полностью в одном канале. 0 - равно радиусу круга (полная панорама).")]
    public float panFullDistance = 0f;

    [Header("Верх / низ")]
    public bool verticalCue = true;
    public float downCutoff = 700f;          // Гц: насколько глушится звук, идущий снизу
    [Range(0f, 0.3f)]
    public float verticalPitch = 0.08f;      // сдвиг тона: сверху выше, снизу ниже

    [Header("Разнообразие")]
    [Range(0f, 0.2f)]
    public float pitchVariation = 0f;        // случайный сдвиг тона при каждом запуске
    public float smoothing = 12f;            // сглаживание громкости и панорамы

    [Header("Testing")]
    public bool playOnStart = false;

    public UnityEvent onFinished;

    public enum ClipOrder
    {
        InOrder,          // 1, 2, 3, 1, 2, 3...
        Random,           // случайно, но не один и тот же подряд
        Shuffle           // все по разу в случайном порядке, потом новая перетасовка
    }

    public bool IsPlaying { get; private set; }

    const float OPEN_CUTOFF = 22000f;

    AudioSource src;
    AudioLowPassFilter lowpass;
    Coroutine routine;

    float radius;
    float clipPitch = 1f;
    float fade = 1f;
    float curVol, curPan, curVert;

    int clipIndex = -1;
    int lastClip = -1;
    int[] shuffleBag;
    int bagPos;
    float gapTimer;

    bool UseClipList { get { return clips != null && clips.Length > 0; } }

    void Awake()
    {
        src = GetComponent<AudioSource>();
        lowpass = GetComponent<AudioLowPassFilter>();

        src.playOnAwake = false;
        src.spatialBlend = 0f;   // чистый 2D: направление и громкость считаем сами

        // объект должен оставаться невидимым, даже если на нём случайно есть рендереры
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
    }

    void Start()
    {
        if (player == null)
        {
#if UNITY_2023_1_OR_NEWER
            var pc = FindFirstObjectByType<RoomPlayerController>();
#else
            var pc = FindObjectOfType<RoomPlayerController>();
#endif
            if (pc != null) player = pc.transform;
        }
        if (player == null) Debug.LogWarning("[OrbitingSound] Игрок не найден: назначь поле Player.");

        if (playOnStart) Play();
    }

    /// <summary>Запускает облёт вокруг игрока. Повторный вызов начинает заново.</summary>
    [ContextMenu("Play")]
    public void Play()
    {
        if (player == null) return;
        if (!UseClipList && src.clip == null) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(OrbitRoutine());
    }

    /// <summary>Останавливает облёт и звук сразу.</summary>
    public void Stop()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        src.Stop();
        IsPlaying = false;
    }

    IEnumerator OrbitRoutine()
    {
        IsPlaying = true;

        float block = blockSize > 0f ? blockSize : GetBlockSize();
        radius = radiusInBlocks * block;
        Vector3 center = player.position;
        float z = transform.position.z;

        // старт - прямо над игроком (угол 90°), по часовой стрелке угол уменьшается
        const float startAngle = Mathf.PI * 0.5f;
        float totalAngle = revolutions * Mathf.PI * 2f;

        transform.position = new Vector3(center.x, center.y + radius, z);

        fade = 1f;
        clipIndex = -1;
        shuffleBag = null;
        gapTimer = 0f;

        if (UseClipList)
        {
            src.loop = false;
            PlayNextClip(true);
        }
        else
        {
            clipPitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            src.loop = loopWhileMoving;
            UpdateSpatial(true);     // сразу правильные громкость и панорама, без "наезда"
            src.Play();
        }

        float t = 0f;
        float fadeStart = Mathf.Max(0f, duration - fadeOutTime);
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            if (followPlayer && player != null) center = player.position;

            float a = startAngle - totalAngle * k;
            transform.position = new Vector3(center.x + Mathf.Cos(a) * radius,
                                             center.y + Mathf.Sin(a) * radius, z);

            fade = (fadeOutTime > 0f && t > fadeStart)
                ? Mathf.Clamp01((duration - t) / fadeOutTime)
                : 1f;

            // список звуков: когда текущий закончился - выждать паузу и включить следующий
            if (UseClipList && !src.isPlaying)
            {
                gapTimer -= Time.deltaTime;
                if (gapTimer <= 0f) PlayNextClip(false);
            }

            UpdateSpatial(false);
            yield return null;
        }

        src.Stop();
        src.loop = false;
        IsPlaying = false;
        routine = null;

        if (onFinished != null) onFinished.Invoke();
    }

    // ---------- выбор следующего звука ----------
    void PlayNextClip(bool snap)
    {
        int idx = NextIndex();
        lastClip = idx;

        float a = Mathf.Min(gapRange.x, gapRange.y), b = Mathf.Max(gapRange.x, gapRange.y);
        gapTimer = Random.Range(a, b);

        if (clips[idx] == null) return;   // пустой слот - просто пропускаем после паузы

        clipPitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        src.clip = clips[idx];
        UpdateSpatial(snap);
        src.Play();
    }

    int NextIndex()
    {
        int n = clips.Length;
        if (n == 1) return 0;

        switch (clipOrder)
        {
            case ClipOrder.Random:
                {
                    int idx = Random.Range(0, n);
                    if (idx == lastClip) idx = (idx + 1 + Random.Range(0, n - 1)) % n;
                    return idx;
                }
            case ClipOrder.Shuffle:
                {
                    if (shuffleBag == null || shuffleBag.Length != n || bagPos >= n)
                    {
                        shuffleBag = new int[n];
                        for (int i = 0; i < n; i++) shuffleBag[i] = i;
                        for (int i = n - 1; i > 0; i--)
                        {
                            int j = Random.Range(0, i + 1);
                            int tmp = shuffleBag[i]; shuffleBag[i] = shuffleBag[j]; shuffleBag[j] = tmp;
                        }
                        // чтобы на стыке перетасовок не было повтора подряд
                        if (shuffleBag[0] == lastClip) { int tmp = shuffleBag[0]; shuffleBag[0] = shuffleBag[n - 1]; shuffleBag[n - 1] = tmp; }
                        bagPos = 0;
                    }
                    return shuffleBag[bagPos++];
                }
            default:
                clipIndex = (clipIndex + 1) % n;
                return clipIndex;
        }
    }

    // ---------- направление звука относительно игрока (из MonsterSound) ----------
    void UpdateSpatial(bool snap)
    {
        if (player == null) return;

        Vector3 delta = transform.position - player.position;
        float dx = delta.x, dy = delta.y;
        float straight = Mathf.Sqrt(dx * dx + dy * dy);

        float t = Mathf.InverseLerp(minHearDistance, maxHearDistance, straight);
        float targetVol = maxVolume * Mathf.Pow(1f - t, Mathf.Max(0.1f, rolloffPower));

        // направление: -1..1. Вблизи (меньше panFull) смещается к центру, чтобы не метаться
        float panFull = panFullDistance > 0f ? panFullDistance : Mathf.Max(0.01f, radius);
        float den = Mathf.Max(straight, panFull);
        float targetPan = Mathf.Clamp(dx / den, -1f, 1f);
        float targetVert = Mathf.Clamp(dy / den, -1f, 1f);   // +1 = источник выше игрока на экране

        float k = snap ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        curVol = Mathf.Lerp(curVol, targetVol, k);
        curPan = Mathf.Lerp(curPan, targetPan, k);
        curVert = Mathf.Lerp(curVert, targetVert, k);

        src.volume = curVol * fade;
        src.panStereo = curPan;

        lowpass.enabled = verticalCue;
        if (verticalCue)
        {
            // снизу - приглушаем (экспоненциально по частоте), сверху - оставляем яркий звук
            float cutoff = curVert < 0f
                ? OPEN_CUTOFF * Mathf.Pow(Mathf.Max(100f, downCutoff) / OPEN_CUTOFF, -curVert)
                : OPEN_CUTOFF;
            lowpass.cutoffFrequency = cutoff;
            src.pitch = clipPitch * (1f + curVert * verticalPitch);
        }
        else
        {
            src.pitch = clipPitch;
        }
    }

    float GetBlockSize()
    {
#if UNITY_2023_1_OR_NEWER
        var room = FindFirstObjectByType<RoomGenerator>();
#else
        var room = FindObjectOfType<RoomGenerator>();
#endif
        return room != null ? room.TileSize : 1f;
    }
}