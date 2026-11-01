using UnityEngine;

/// <summary>
/// Снимает "стену" с объекта из списка RoomGenerator.objects: ставит ему blocking = false,
/// после чего игрок может на него наступить.
/// По умолчанию работает с объектом под номером 1 (второй в списке).
///
/// Вызывай Unblock() из кода или из любого UnityEvent
/// (например, On End у диалога или On Finished у OrbitingSound).
/// </summary>
public class ObjectUnblocker : MonoBehaviour
{
    public RoomGenerator room;              // если пусто - ищется в сцене
    public int objectIndex = 1;             // номер в списке objects, начиная с 0 (1 = второй объект)
    public bool runOnStart = false;         // для проверки: снять блокировку сразу при старте

    [Header("Rotation")]
    public bool setRotationZ = true;        // при Unblock() повернуть объект по Z
    public float rotationZ = 0f;            // какое значение Rotation Z поставить

    void Awake()
    {
        if (room == null)
        {
#if UNITY_2023_1_OR_NEWER
            room = FindFirstObjectByType<RoomGenerator>();
#else
            room = FindObjectOfType<RoomGenerator>();
#endif
        }
        if (room == null) Debug.LogWarning("[ObjectUnblocker] RoomGenerator не найден в сцене.");
    }

    void Start()
    {
        if (runOnStart) Unblock();
    }

    /// <summary>Делает объект проходимым (blocking = false).</summary>
    [ContextMenu("Unblock")]
    public void Unblock()
    {
        if (room == null) return;
        room.SetBlocking(objectIndex, false);

        if (setRotationZ) SetRotationZ(rotationZ);
    }

    /// <summary>Ставит объекту Rotation Z (как в инспекторе), X и Y не трогает.</summary>
    public void SetRotationZ(float z)
    {
        if (room == null || room.SpawnedObjects == null ||
            objectIndex < 0 || objectIndex >= room.SpawnedObjects.Count) return;

        var go = room.SpawnedObjects[objectIndex];
        if (go == null) return;

        Vector3 e = go.transform.localEulerAngles;
        go.transform.localEulerAngles = new Vector3(e.x, e.y, z);
    }

    /// <summary>Снова делает объект непроходимым (blocking = true).</summary>
    [ContextMenu("Block")]
    public void Block()
    {
        if (room != null) room.SetBlocking(objectIndex, true);
    }
}