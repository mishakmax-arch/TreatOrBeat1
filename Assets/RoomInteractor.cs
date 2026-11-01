using UnityEngine;

/// <summary>
/// Реализуй этот интерфейс в любом скрипте на объекте (или его детях),
/// чтобы объект сам реагировал на игрока. Удобно для префабов.
/// </summary>
public interface IRoomInteractable
{
    void OnPlayerNear();    // игрок подошёл
    void OnPlayerLeave();   // игрок отошёл
    void OnInteract();      // игрок нажал клавишу взаимодействия
}

/// <summary>
/// Следит, стоит ли игрок рядом с объектом из RoomGenerator.objects, у которого включено Interactable,
/// и по нажатию клавиши вызывает взаимодействие.
/// Рядом = любая из 8 соседних клеток (вверх/вниз/влево/вправо и по диагонали).
/// Повесь на игрока, рядом с RoomPlayerController.
///
/// Реакцию можно задать тремя способами (работают одновременно):
///   1) UnityEvent-поля у объекта в списке Objects (onPlayerNear / onPlayerLeave / onInteract);
///   2) скрипт на самом объекте, реализующий IRoomInteractable (например, InteractMenu);
///   3) подписка из кода на события OnNear / OnLeave / OnInteract этого компонента.
/// </summary>
public class RoomInteractor : MonoBehaviour
{
    public RoomGenerator room;                    // если пусто - ищется в сцене
    public RoomPlayerController controller;       // если пусто - берётся с этого объекта

    public KeyCode interactKey = KeyCode.E;

    /// <summary>Индекс объекта в списке objects, рядом с которым стоит игрок, или -1.</summary>
    public int CurrentIndex { get; private set; } = -1;

    /// <summary>Объект на сцене, рядом с которым стоит игрок, или null.</summary>
    public GameObject Current
    {
        get
        {
            if (room == null || CurrentIndex < 0 || CurrentIndex >= room.SpawnedObjects.Count) return null;
            return room.SpawnedObjects[CurrentIndex];
        }
    }

    public event System.Action<int, GameObject> OnNear;
    public event System.Action<int, GameObject> OnLeave;
    public event System.Action<int, GameObject> OnInteract;

    void Awake()
    {
        if (controller == null) controller = GetComponent<RoomPlayerController>();
        if (room == null)
        {
#if UNITY_2023_1_OR_NEWER
            room = FindFirstObjectByType<RoomGenerator>();
#else
            room = FindObjectOfType<RoomGenerator>();
#endif
        }
        if (room == null) Debug.LogWarning("[RoomInteractor] RoomGenerator не найден в сцене.");
    }

    void OnEnable()
    {
        if (room != null) room.OnRoomGenerated += ResetCurrent;
    }

    void OnDisable()
    {
        if (room != null) room.OnRoomGenerated -= ResetCurrent;
    }

    // после перегенерации старые объекты могли быть удалены
    void ResetCurrent()
    {
        CurrentIndex = -1;
    }

    void Update()
    {
        if (room == null || room.Grid == null || room.SpawnedObjects == null) return;

        // пока игрок идёт между клетками, состояние не пересчитываем (чтобы подсказки не мигали)
        if (controller == null || !controller.IsMoving)
        {
            Vector2Int cell = controller != null ? controller.Cell : room.WorldToGrid(transform.position);
            int found = FindNear(cell);
            if (found != CurrentIndex)
            {
                if (CurrentIndex >= 0) Fire(CurrentIndex, Kind.Leave);
                CurrentIndex = found;
                if (CurrentIndex >= 0) Fire(CurrentIndex, Kind.Near);
            }
        }

        // взаимодействие работает и при InputLocked, чтобы тем же E можно было закрыть меню
        if (CurrentIndex >= 0 && Input.GetKeyDown(interactKey))
            Fire(CurrentIndex, Kind.Interact);
    }

    int FindNear(Vector2Int cell)
    {
        // если текущий объект всё ещё рядом - оставляем его, чтобы не перескакивать между соседними
        if (CurrentIndex >= 0 && IsUsable(CurrentIndex) && room.IsNextTo(room.objects[CurrentIndex], cell))
            return CurrentIndex;

        for (int i = 0; i < room.objects.Count; i++)
            if (IsUsable(i) && room.IsNextTo(room.objects[i], cell))
                return i;
        return -1;
    }

    bool IsUsable(int i)
    {
        if (i >= room.objects.Count || i >= room.SpawnedObjects.Count) return false;
        var o = room.objects[i];
        return o != null && o.interactable && room.SpawnedObjects[i] != null;
    }

    enum Kind { Near, Leave, Interact }

    void Fire(int i, Kind kind)
    {
        var o = i < room.objects.Count ? room.objects[i] : null;
        var go = i < room.SpawnedObjects.Count ? room.SpawnedObjects[i] : null;

        if (o != null)
        {
            if (kind == Kind.Near && o.onPlayerNear != null) o.onPlayerNear.Invoke();
            if (kind == Kind.Leave && o.onPlayerLeave != null) o.onPlayerLeave.Invoke();
            if (kind == Kind.Interact && o.onInteract != null) o.onInteract.Invoke();
        }

        if (go != null)
            foreach (var it in go.GetComponentsInChildren<IRoomInteractable>(true))
            {
                if (kind == Kind.Near) it.OnPlayerNear();
                else if (kind == Kind.Leave) it.OnPlayerLeave();
                else it.OnInteract();
            }

        if (kind == Kind.Near && OnNear != null) OnNear(i, go);
        if (kind == Kind.Leave && OnLeave != null) OnLeave(i, go);
        if (kind == Kind.Interact && OnInteract != null) OnInteract(i, go);
    }
}