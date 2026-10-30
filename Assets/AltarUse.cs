using System.Collections.Generic;
using UnityEngine;

public class AltarUse : MonoBehaviour
{
    public Transform Player;
    public MazeGenerator maze;
    public PlayerSpriteChanger changer;

    public List<bool> ActivatedAltars = new List<bool>();
    public int NumberOfActivatedAltars = 0;

    void OnEnable()
    {
        if (maze != null) maze.OnMazeGenerated += ResetAltars;
    }

    void OnDisable()
    {
        if (maze != null) maze.OnMazeGenerated -= ResetAltars;
    }

    void ResetAltars()
    {
        ActivatedAltars.Clear();
        for (int i = 0; i < maze.WaypointCells.Count; i++) ActivatedAltars.Add(false);
        NumberOfActivatedAltars = 0;
    }

    void Update()
    {
        if (maze == null || maze.WaypointCells == null || Player == null) return;

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

        maze.ActivateWaypoint(altarNumber);               // <-- switches the altar to the 2nd sprite
        changer.SetSprite(NumberOfActivatedAltars + 1);   // player sprite
    }
}