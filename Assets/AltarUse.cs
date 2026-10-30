using NUnit.Framework;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class AltarUse : MonoBehaviour
{

    public List<bool> ActivatedAltars;
    MazeGenerator maze;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var list = maze.Waypoints;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
