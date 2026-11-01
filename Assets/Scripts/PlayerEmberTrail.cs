using UnityEngine;

/// <summary>
/// След из огоньков за тыквой. Вешается на объект игрока (где PlayerMazeController).
/// Ничего настраивать вручную не нужно: система частиц и материал создаются в коде.
///  - при ходьбе искры сыплются чаще, когда игрок стоит - редкие "тлеющие" искры;
///  - частицы живут в мировых координатах, поэтому остаются позади и медленно поднимаются вверх;
///  - квадратные пиксельные искорки: оранжевые -> красные, уменьшаются и гаснут.
/// </summary>
public class PlayerEmberTrail : MonoBehaviour
{
    [Header("Ссылки (если пусто - берутся с этого объекта)")]
    public PlayerMazeController controller;
    public SpriteRenderer playerSprite;

    [Header("Количество")]
    public float movingRate = 35f;        // искр в секунду при ходьбе
    public float idleRate = 3f;           // искр в секунду, пока стоит

    [Header("Вид")]
    public Vector2 lifetime = new Vector2(0.45f, 0.85f);
    public Vector2 size = new Vector2(0.06f, 0.13f);   // в юнитах (1 клетка = 1 юнит)
    public Color colorA = new Color(1f, 0.55f, 0.1f, 1f);
    public Color colorB = new Color(1f, 0.85f, 0.3f, 1f);
    public float riseSpeed = 0.5f;        // дрейф вверх
    public float spread = 0.12f;          // дрейф в стороны
    public float emitRadius = 0.12f;      // искры появляются в небольшом круге вокруг центра

    [Header("Порядок отрисовки")]
    public int orderOffset = 0;           // относительно спрайта игрока; 0 = тот же порядок, но чуть дальше по Z
    public float zOffset = 0.05f;         // искры позади игрока

    ParticleSystem ps;
    Material mat;
    Vector3 lastPos;

    void Awake()
    {
        if (controller == null) controller = GetComponent<PlayerMazeController>();
        if (playerSprite == null) playerSprite = GetComponentInChildren<SpriteRenderer>();
        lastPos = transform.position;
        Build();
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    void Build()
    {
        var go = new GameObject("EmberTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, zOffset);

        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // настраиваем на остановленной системе

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = idleRate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = emitRadius;

        // дрейф: чуть в стороны и вверх по экрану
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-spread, spread);
        vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.4f, riseSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // цвет: стартовый -> красный -> тёмный, прозрачность к нулю
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.35f, 0.1f), 0.6f),
                new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // искры уменьшаются к концу жизни
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        // квадратные пиксельные частицы: стандартный спрайтовый шейдер, без текстуры
        var r = go.GetComponent<ParticleSystemRenderer>();
        mat = new Material(Shader.Find("Sprites/Default"));
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        if (playerSprite != null)
        {
            r.sortingLayerID = playerSprite.sortingLayerID;
            r.sortingOrder = playerSprite.sortingOrder + orderOffset;
        }

        ps.Play();
    }

    void Update()
    {
        if (ps == null) return;

        bool moving;
        if (controller != null)
        {
            moving = controller.IsMoving;
        }
        else
        {
            moving = (transform.position - lastPos).sqrMagnitude > 0.000001f;
            lastPos = transform.position;
        }

        var emission = ps.emission;
        emission.rateOverTime = moving ? movingRate : idleRate;
    }
}
