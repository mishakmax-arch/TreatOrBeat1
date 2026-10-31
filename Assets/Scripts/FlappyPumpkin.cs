using UnityEngine;
using UnityEngine.InputSystem;

public class FlappyPumpkin : MonoBehaviour
{
    [Header("References")]
    public RectTransform pumpkin;
    public RectTransform obstacles;
    public RectTransform gameArea;

    [Header("Movement")]
    public float gravity = 600f;
    public float jumpForce = 250f;
    public float obstacleSpeed = 150f;

    [Header("Obstacles")]
    public float gapSize = 130f;
    public float resetPositionX = 400f;
    public float spawnPositionX = 400f;

    [Header("Input")]
    public InputAction jump;

    private RectTransform obstacleTop;
    private RectTransform obstacleBottom;

    private float verticalVelocity;
    private bool isDead;

    private Vector2 pumpkinStartPosition;

    private void Awake()
    {
        obstacleTop = obstacles.Find("ObstaclesTop")
            .GetComponent<RectTransform>();

        obstacleBottom = obstacles.Find("ObstaclesBottom")
            .GetComponent<RectTransform>();

        pumpkinStartPosition = pumpkin.anchoredPosition;
    }

    private void OnEnable()
    {
        jump.Enable();
        Restart();
    }

    private void OnDisable()
    {
        jump.Disable();
    }

    private void Update()
    {
        if (jump.WasPressedThisFrame())
        {
            if (isDead)
            {
                Restart();
                return;
            }

            verticalVelocity = jumpForce;
        }

        if (isDead)
            return;

        float deltaTime = Time.unscaledDeltaTime;

        MovePumpkin(deltaTime);
        MoveObstacles(deltaTime);
        CheckCollision();
    }

    private void MovePumpkin(float deltaTime)
    {
        verticalVelocity -= gravity * deltaTime;

        pumpkin.anchoredPosition +=
            Vector2.up * verticalVelocity * deltaTime;

        float halfHeight = gameArea.rect.height / 2f;
        float pumpkinHalfHeight = pumpkin.rect.height / 2f;

        if (pumpkin.anchoredPosition.y + pumpkinHalfHeight > halfHeight)
        {
            pumpkin.anchoredPosition = new Vector2(
                pumpkin.anchoredPosition.x,
                halfHeight - pumpkinHalfHeight
            );

            verticalVelocity = 0f;
        }
    }

    private void MoveObstacles(float deltaTime)
    {
        obstacles.anchoredPosition +=
            Vector2.left * obstacleSpeed * deltaTime;

        if (obstacles.anchoredPosition.x < -resetPositionX)
        {
            Vector2 position = obstacles.anchoredPosition;

            position.x = spawnPositionX;
            position.y = Random.Range(-80f, 80f);

            obstacles.anchoredPosition = position;
        }
    }

    private void CheckCollision()
    {
        if (IsOverlapping(pumpkin, obstacleTop) ||
            IsOverlapping(pumpkin, obstacleBottom))
        {
            isDead = true;
        }

        float halfHeight = gameArea.rect.height / 2f;
        float pumpkinY = pumpkin.anchoredPosition.y;
        float pumpkinHalfHeight = pumpkin.rect.height / 2f;

        if (pumpkinY + pumpkinHalfHeight > halfHeight ||
            pumpkinY - pumpkinHalfHeight < -halfHeight)
        {
            isDead = true;
        }
    }

    private bool IsOverlapping(RectTransform first, RectTransform second)
    {
        Vector3[] firstCorners = new Vector3[4];
        Vector3[] secondCorners = new Vector3[4];

        first.GetWorldCorners(firstCorners);
        second.GetWorldCorners(secondCorners);

        Rect firstRect = new Rect(
            firstCorners[0].x,
            firstCorners[0].y,
            firstCorners[2].x - firstCorners[0].x,
            firstCorners[2].y - firstCorners[0].y
        );

        Rect secondRect = new Rect(
            secondCorners[0].x,
            secondCorners[0].y,
            secondCorners[2].x - secondCorners[0].x,
            secondCorners[2].y - secondCorners[0].y
        );

        return firstRect.Overlaps(secondRect);
    }

    private void Restart()
    {
        isDead = false;
        verticalVelocity = 0f;

        pumpkin.anchoredPosition = pumpkinStartPosition;

        obstacles.anchoredPosition =
            new Vector2(spawnPositionX, 0f);
    }
}