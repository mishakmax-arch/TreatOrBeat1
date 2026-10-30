using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// ¬ешаетс€ на игрока. ѕосле генерации лабиринта телепортирует игрока
/// в случайный waypoint (отличный от текущего). ƒвижени€ между точками нет.
/// ¬ MazeGenerator перетащите этого же игрока в поле Player.
/// </summary>
public class PlayerWaypointMover : MonoBehaviour
{
    public MazeGenerator maze;
    public bool autoStart = true;     // спавнитьс€ сразу после генерации

    /// <summary>Waypoint, в котором заспавнилс€ игрок.</summary>
    public Transform FinalWaypoint { get; private set; }
    public event Action<Transform> OnArrived;

    void OnEnable()
    {
        if (maze != null) maze.OnMazeGenerated += HandleGenerated;
    }

    void OnDisable()
    {
        if (maze != null) maze.OnMazeGenerated -= HandleGenerated;
    }

    void HandleGenerated()
    {
        if (autoStart) SpawnAtRandomWaypoint();
    }

    /// <summary>ћожно вызывать вручную (например, по кнопке) - игрок по€витс€ в новом случайном waypoint'е.</summary>
    public void SpawnAtRandomWaypoint()
    {
        FinalWaypoint = null;

        if (maze == null || maze.WaypointCells == null || maze.WaypointCells.Count == 0) return;

        Vector2Int from = maze.WorldToGrid(transform.position);

        // кандидаты - все waypoint'ы, кроме того, на котором стоим
        var candidates = new List<int>();
        for (int i = 0; i < maze.WaypointCells.Count; i++)
            if (maze.WaypointCells[i] != from) candidates.Add(i);

        // если waypoint всего один и мы на нЄм - остаЄмс€ на месте
        if (candidates.Count == 0)
        {
            for (int i = 0; i < maze.WaypointCells.Count; i++) candidates.Add(i);
        }

        int idx = candidates[Random.Range(0, candidates.Count)];
        Transform wp = maze.Waypoints[idx];

        // телепорт, Z сохран€ем как был
        transform.position = new Vector3(wp.position.x, wp.position.y, transform.position.z);

        FinalWaypoint = wp;
        Debug.Log("»грок заспавнилс€ на " + FinalWaypoint.name);
        if (OnArrived != null) OnArrived(FinalWaypoint);
    }
}