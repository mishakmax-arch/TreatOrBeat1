using System;
using UnityEngine;

/// <summary>
/// Put on the player. After the maze is generated, places the player on the entrance waypoint.
/// </summary>
public class PlayerWaypointMover : MonoBehaviour
{
    public MazeGenerator maze;
    public bool autoStart = true;

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
        if (autoStart) SpawnAtEntrance();
    }

    public void SpawnAtEntrance()
    {
        FinalWaypoint = null;
        if (maze == null) return;

        Transform wp = maze.Entrance;
        if (wp == null) return;

        transform.position = new Vector3(wp.position.x, wp.position.y, transform.position.z);

        FinalWaypoint = wp;
        if (OnArrived != null) OnArrived(FinalWaypoint);
    }
}