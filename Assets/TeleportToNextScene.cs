using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Переход на другую сцену, когда игрок встаёт на клетку объекта из RoomGenerator.objects.
///
/// Вариант 1: повесь на сам объект (или его префаб) и оставь Object Index = -1 -
///            скрипт сам найдёт, какой это номер в списке objects.
/// Вариант 2: повесь на любой объект и укажи Object Index вручную (0 - первый в списке).
///
/// Чтобы игрок мог наступить на объект, у него должно быть Blocking = false
/// (галочка в списке Objects или ObjectUnblocker.Unblock()).
/// Сцена должна быть добавлена в File -> Build Profiles (Build Settings) -> Scene List.
/// </summary>
public class SceneExitTrigger : MonoBehaviour
{
    public string sceneName = "Menu";       // имя сцены точь-в-точь как в проекте
    public int objectIndex = -1;            // -1 - определить автоматически по этому объекту
    public float delay = 0f;                // пауза перед переходом (сек), игрок в это время стоит

    public RoomGenerator room;              // если пусто - ищется в сцене
    public RoomPlayerController player;     // если пусто - ищется в сцене

    bool triggered;

    void Start()
    {
#if UNITY_2023_1_OR_NEWER
        if (room == null) room = FindFirstObjectByType<RoomGenerator>();
        if (player == null) player = FindFirstObjectByType<RoomPlayerController>();
#else
        if (room == null) room = FindObjectOfType<RoomGenerator>();
        if (player == null) player = FindObjectOfType<RoomPlayerController>();
#endif
        if (room == null) Debug.LogWarning("[SceneExitTrigger] RoomGenerator не найден в сцене.");
        if (player == null) Debug.LogWarning("[SceneExitTrigger] RoomPlayerController не найден в сцене.");
    }

    void Update()
    {
        if (triggered || room == null || player == null || player.IsMoving) return;

        int i = ResolveIndex();
        if (i < 0) return;

        if (IsOnObject(room.objects[i], player.Cell))
        {
            triggered = true;
            player.InputLocked = true;
            if (delay > 0f) Invoke(nameof(LoadScene), delay);
            else LoadScene();
        }
    }

    int ResolveIndex()
    {
        if (objectIndex >= 0)
            return objectIndex < room.objects.Count ? objectIndex : -1;

        // ищем себя в списке: среди созданных объектов или среди исходных
        for (int i = 0; i < room.objects.Count; i++)
        {
            bool isSpawned = room.SpawnedObjects != null && i < room.SpawnedObjects.Count &&
                             room.SpawnedObjects[i] == gameObject;
            bool isSource = room.objects[i] != null && room.objects[i].obj == gameObject;
            if (isSpawned || isSource) return i;
        }
        return -1;
    }

    static bool IsOnObject(RoomGenerator.RoomObject o, Vector2Int c)
    {
        if (o == null) return false;
        int w = Mathf.Max(1, o.size.x), h = Mathf.Max(1, o.size.y);
        return c.x >= o.cell.x && c.y >= o.cell.y && c.x < o.cell.x + w && c.y < o.cell.y + h;
    }

    void LoadScene()
    {
        SceneManager.LoadScene(sceneName);
    }
}