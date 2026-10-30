using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    public Transform target;
    public float smooth = 8f;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 p = target.position;
        p.z = transform.position.z; 
        transform.position = Vector3.Lerp(transform.position, p, smooth * Time.deltaTime);
    }
}