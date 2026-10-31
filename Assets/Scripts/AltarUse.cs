using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Алтарь активируется нажатием клавиши (по умолчанию Пробел), только если игрок стоит на его клетке.
/// Пока идёт активация (экран загрузки и перестройка карты), игрок не может двигаться.
/// </summary>
public class AltarUse : MonoBehaviour
{
    public ChangeLoadScreen changeLoadScreen;
    public KeyChange keyChanger;                     // если пусто - ищется в сцене
    public GameObject exitObject;   // становится видимым, когда игрок стоит на exit-алтаре
    public Transform Player;
    public PlayerMazeController playerController; // если пусто - берётся с объекта Player
    public MazeGenerator maze;
    public PlayerSpriteChanger changer;
    public MonsterChaser monster;                 // замирает на время активации

    public PlayerLightMask lightMask;             // если пусто, ищется в сцене
    public float softnessStep = 0.18f;             // на сколько уменьшать softness за алтарь
    public DetectionWarFog detectionWarFog; 
    public float detectionStep = 1f;   // на сколько уменьшать softness за алтарь

    public KeyCode activateKey = KeyCode.Space;

    public List<bool> ActivatedAltars = new List<bool>();
    public int NumberOfActivatedAltars = 0;
    public Image LoadScreen;

    bool busy; // идёт активация, повторные нажатия игнорируются

    void Start()
    {
        if (exitObject != null) exitObject.SetActive(false);
        if (playerController == null && Player != null)
            playerController = Player.GetComponent<PlayerMazeController>();

        if (monster == null)
        {
#if UNITY_2023_1_OR_NEWER
            monster = FindFirstObjectByType<MonsterChaser>();
#else
            monster = FindObjectOfType<MonsterChaser>();
#endif
        }

        if (playerController == null) Debug.LogWarning("[AltarUse] PlayerMazeController не найден на Player: игрок не будет блокироваться.");
        if (monster == null) Debug.LogWarning("[AltarUse] MonsterChaser не найден в сцене: монстр не будет замирать.");


        if (lightMask == null)
        {
#if UNITY_2023_1_OR_NEWER
            lightMask = FindFirstObjectByType<PlayerLightMask>();
#else
    lightMask = FindObjectOfType<PlayerLightMask>();
#endif
        }

        if (lightMask == null) Debug.LogWarning("[AltarUse] PlayerLightMask не найден в сцене: softness не будет меняться.");
    }

    void OnEnable()
    {
        if (maze != null) maze.OnMazeGenerated += ResetAltars;
    }

    void CheckExitAltar()
    {
        if (exitObject == null) return;
        if (maze.ExitIndex < 0 || maze.ExitIndex >= maze.WaypointCells.Count) return;

        Vector2Int playerCell = maze.WorldToGrid(Player.position);
        bool onExit = maze.WaypointCells[maze.ExitIndex] == playerCell;

        if (exitObject.activeSelf != onExit)
            exitObject.SetActive(onExit);
    }

    void OnDisable()
    {
        if (maze != null) maze.OnMazeGenerated -= ResetAltars;
    }

    // only a full Generate() (a brand new set of altars) resets progress
    void ResetAltars()
    {
        ActivatedAltars.Clear();
        for (int i = 0; i < maze.WaypointCells.Count; i++) ActivatedAltars.Add(false);
        NumberOfActivatedAltars = 0;
    }

    void Update()
    {
        if (busy || maze == null || maze.WaypointCells == null || Player == null) return;
        CheckExitAltar();
        if (!Input.GetKeyDown(activateKey)) return;

        // "наступил" = стоит в клетке алтаря, а не пробегает мимо
        if (playerController != null && playerController.IsMoving) return;

        Vector2Int playerCell = maze.WorldToGrid(Player.position);

        for (int i = 0; i < maze.WaypointCells.Count; i++)
        {
            // entrance and exit are not altars
            if (i == maze.EntranceIndex || i == maze.ExitIndex) continue;

            if (maze.WaypointCells[i] == playerCell)
            {
                AltarActivation(i);
                break;
            }
        }
    }

    void AltarActivation(int altarNumber)
    {
        if (altarNumber >= ActivatedAltars.Count) return;
        if (ActivatedAltars[altarNumber]) return; // already activated

        ActivatedAltars[altarNumber] = true;
        NumberOfActivatedAltars++;

        maze.ActivateWaypoint(altarNumber);
        changer.SetSprite(NumberOfActivatedAltars + 1);
        DecreaseLightSoftness();

        StartCoroutine(ActivationSequence());
    }

    IEnumerator ActivationSequence()
    {
        changeLoadScreen.SwitchSprites(NumberOfActivatedAltars + 1);
        busy = true;
        if (playerController != null) playerController.InputLocked = true;
        if (monster != null) monster.Freeze(true);

        if (LoadScreen != null)
        {
            LoadScreen.fillAmount = 0f;
            while (LoadScreen.fillAmount < 1f)
            {
                LoadScreen.fillAmount += 1f * Time.deltaTime;
                yield return null;
            }
            LoadScreen.fillAmount = 1f;
        }

        maze.RebuildMazeKeepWaypoints();
        yield return new WaitForSeconds(0.5f); // wait a bit for the maze to rebuild1

        if (LoadScreen != null)
        {
            while (LoadScreen.fillAmount > 0f)
            {
                LoadScreen.fillAmount -= 1f * Time.deltaTime;
                yield return null;
            }
            LoadScreen.fillAmount = 0f;
        }

        if (playerController != null) playerController.InputLocked = false;
        if (monster != null) monster.Freeze(false); // после перестройки монстр появится заново
        busy = false;
    }

    // уменьшает мягкость края света при активации алтаря
    void DecreaseLightSoftness()
    {
        detectionWarFog.detectionCells -= 1;
        keyChanger.SwitchSprites(NumberOfActivatedAltars + 1);
        if (lightMask == null) return;
        lightMask.softness = Mathf.Max(0f, lightMask.softness - softnessStep);
    }
}