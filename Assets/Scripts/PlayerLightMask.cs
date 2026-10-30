
using UnityEngine;
using UnityEngine.UI;

public class PlayerLightMask : MonoBehaviour
{
    public Transform player;
    public Image darknessImage;
    public Camera mainCamera;

    public float radius = 0.15f;
    public float softness = 0.02f;

    private Material maskMaterial;

    void Start()
    {
        maskMaterial = new Material(darknessImage.material);
        darknessImage.material = maskMaterial;

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    void LateUpdate()
    {
        Vector3 screenPosition =
            mainCamera.WorldToScreenPoint(player.position);

        RectTransform rect = darknessImage.rectTransform;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect,
            screenPosition,
            null,
            out Vector2 localPoint
        );

        Rect imageRect = rect.rect;

        Vector2 center = new Vector2(
            (localPoint.x - imageRect.xMin) / imageRect.width,
            (localPoint.y - imageRect.yMin) / imageRect.height
        );

        maskMaterial.SetVector("_Center", center);
        maskMaterial.SetFloat("_Radius", radius);
        maskMaterial.SetFloat("_Softness", softness);
        maskMaterial.SetFloat(
            "_Aspect",
            imageRect.width / imageRect.height
        );
    }

    void OnDestroy()
    {
        if (maskMaterial != null)
            Destroy(maskMaterial);
    }
}
