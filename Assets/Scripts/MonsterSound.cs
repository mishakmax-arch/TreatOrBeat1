using UnityEngine;

/// <summary>
/// Звуки монстра, зависящие от положения относительно ИГРОКА:
///  - лево/право: panStereo по направлению на монстра,
///  - громкость: растёт по мере приближения (с учётом пути по лабиринту),
///  - верх/низ: звук сверху ярче и чуть выше по тону, снизу глуше и ниже.
/// Звук играется в 2D-режиме и считается вручную, потому что камера стоит в центре карты,
/// а не на игроке, и встроенная 3D-панорама Unity считала бы направление от камеры.
/// Повесь на объект монстра рядом с MonsterChaser.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(AudioLowPassFilter))]
public class MonsterSound : MonoBehaviour
{
    [Header("Ссылки")]
    public MonsterChaser monster;   // если пусто - берётся с этого же объекта
    public Transform player;        // кто слушает (игрок)

    [Header("Звуки (3-5 штук, лучше моно)")]
    public AudioClip[] clips;

    [Header("Интервал между звуками (сек, считается после конца предыдущего)")]
    public Vector2 intervalRange = new Vector2(4f, 9f);

    [Header("Громкость и расстояние")]
    [Range(0f, 1f)] public float maxVolume = 1f;
    public float minHearDistance = 2f;    // ближе этого - полная громкость (в юнитах)
    public float maxHearDistance = 30f;   // дальше этого - тишина
    public float rolloffPower = 2f;       // 1 = линейно, 2 = быстрее нарастает вблизи
    [Range(0f, 1f)]
    public float pathInfluence = 0.5f;    // 0 = по прямой, 1 = только по пути в лабиринте

    [Header("Направление")]
    public float panFullDistance = 6f;    // на таком смещении по X звук полностью в одном канале

    [Header("Верх / низ")]
    public bool verticalCue = true;
    public float downCutoff = 700f;       // Гц: насколько глушится звук, идущий снизу
    [Range(0f, 0.3f)]
    public float verticalPitch = 0.08f;   // сдвиг тона: сверху выше, снизу ниже

    [Header("Разнообразие")]
    [Range(0f, 0.2f)]
    public float pitchVariation = 0.05f;
    public float smoothing = 12f;         // сглаживание громкости и панорамы

    const float OPEN_CUTOFF = 22000f;

    AudioSource src;
    AudioLowPassFilter lowpass;
    float timer;
    bool wasActive;
    int lastClip = -1;
    float clipPitch = 1f;

    float curVol, curPan, curVert;

    void Awake()
    {
        src = GetComponent<AudioSource>();
        lowpass = GetComponent<AudioLowPassFilter>();

        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;   // чистый 2D: направление и громкость считаем сами

        if (monster == null) monster = GetComponent<MonsterChaser>();
    }

    void Update()
    {
        if (monster == null || player == null || clips == null || clips.Length == 0) return;

        // монстр не появился / пересоздаётся карта / поймал игрока - тишина
        if (!monster.IsActive)
        {
            if (wasActive) { src.Stop(); wasActive = false; }
            return;
        }

        if (!wasActive)
        {
            wasActive = true;
            ScheduleNext();
        }

        if (src.isPlaying)
        {
            UpdateSpatial(false);
            return;
        }

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            PlayRandom();
            ScheduleNext();
        }
    }

    void ScheduleNext()
    {
        float a = Mathf.Min(intervalRange.x, intervalRange.y);
        float b = Mathf.Max(intervalRange.x, intervalRange.y);
        timer = Random.Range(a, b);
    }

    void PlayRandom()
    {
        // случайный звук, но не тот же самый подряд
        int idx = Random.Range(0, clips.Length);
        if (clips.Length > 1 && idx == lastClip) idx = (idx + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        lastClip = idx;
        if (clips[idx] == null) return;

        clipPitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        src.clip = clips[idx];
        UpdateSpatial(true); // сразу правильные громкость и панорама, без "наезда"
        src.Play();
    }

    void UpdateSpatial(bool snap)
    {
        Vector3 delta = monster.transform.position - player.position;
        float dx = delta.x, dy = delta.y;
        float straight = Mathf.Sqrt(dx * dx + dy * dy);

        // расстояние для громкости: смесь прямой и реального пути (звук "через стену" тише)
        float d = straight;
        int path = monster.PathDistanceToPlayer;
        if (path >= 0 && pathInfluence > 0f)
        {
            float tile = monster.maze != null ? monster.maze.TileSize : 1f;
            d = Mathf.Lerp(straight, path * tile, pathInfluence);
        }

        float t = Mathf.InverseLerp(minHearDistance, maxHearDistance, d);
        float targetVol = maxVolume * Mathf.Pow(1f - t, Mathf.Max(0.1f, rolloffPower));

        // направление: -1..1. Вблизи (меньше panFullDistance) смещается к центру, чтобы не метаться
        float den = Mathf.Max(straight, panFullDistance);
        float targetPan = Mathf.Clamp(dx / den, -1f, 1f);
        float targetVert = Mathf.Clamp(dy / den, -1f, 1f);   // +1 = монстр выше игрока на экране

        float k = snap ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        curVol = Mathf.Lerp(curVol, targetVol, k);
        curPan = Mathf.Lerp(curPan, targetPan, k);
        curVert = Mathf.Lerp(curVert, targetVert, k);

        src.volume = curVol;
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
}
