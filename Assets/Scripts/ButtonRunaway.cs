using UnityEngine;

public class RunawayButton : MonoBehaviour
{
    public float escapeDistance = 150f;
    public float escapeSpeed = 500f;

    private RectTransform rectTransform;
    private Canvas canvas;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    void Update()
    {
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            Input.mousePosition,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera,
            out Vector2 mousePosition
        );

        Vector2 buttonPosition = canvasRect.InverseTransformPoint(
            rectTransform.position
        );

        Vector2 direction = buttonPosition - mousePosition;
        float distance = direction.magnitude;

        if (distance < escapeDistance && distance > 0.01f)
        {
            Vector2 movement = direction.normalized *
                               escapeSpeed * Time.deltaTime;

            rectTransform.anchoredPosition += movement;
        }

        KeepButtonInsideCanvas();
    }

    private void KeepButtonInsideCanvas()
    {
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();

        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Vector2 bottomLeft = canvasRect.InverseTransformPoint(corners[0]);
        Vector2 topRight = canvasRect.InverseTransformPoint(corners[2]);

        Rect bounds = canvasRect.rect;

        Vector2 correction = Vector2.zero;

        if (bottomLeft.x < bounds.xMin)
            correction.x = bounds.xMin - bottomLeft.x;
        else if (topRight.x > bounds.xMax)
            correction.x = bounds.xMax - topRight.x;

        if (bottomLeft.y < bounds.yMin)
            correction.y = bounds.yMin - bottomLeft.y;
        else if (topRight.y > bounds.yMax)
            correction.y = bounds.yMax - topRight.y;

        rectTransform.position += canvasRect.TransformVector(correction);
    }
}