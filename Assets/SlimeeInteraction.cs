using UnityEngine;

/// <summary>
/// Пример взаимодействия: повесь на объект (или его префаб).
/// Когда игрок рядом - показывает подсказку, по E открывает/закрывает меню (панель UI).
/// Пока меню открыто, игрок не двигается.
/// </summary>
public class InteractMenu : MonoBehaviour, IRoomInteractable
{
    public GameObject hint;          // подсказка над объектом, например "E"
    public GameObject menuPanel;     // панель интерфейса; если на префабе - назначь через код или держи панель внутри префаба
    public bool lockPlayerWhileOpen = true;

    RoomPlayerController player;

    void Start()
    {
#if UNITY_2023_1_OR_NEWER
        player = FindFirstObjectByType<RoomPlayerController>();
#else
        player = FindObjectOfType<RoomPlayerController>();
#endif
        if (hint != null) hint.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(false);
    }

    public void OnPlayerNear()
    {
        if (hint != null) hint.SetActive(true);
    }

    public void OnPlayerLeave()
    {
        SetMenu(false);
        if (hint != null) hint.SetActive(false);   // после SetMenu, который включает подсказку обратно
    }

    public void OnInteract()
    {
        if (menuPanel != null) SetMenu(!menuPanel.activeSelf);
    }

    void SetMenu(bool open)
    {
        if (menuPanel == null) return;
        menuPanel.SetActive(open);
        if (hint != null) hint.SetActive(!open);
        if (lockPlayerWhileOpen && player != null) player.InputLocked = open;
    }
}