using System;
using UnityEngine;

public class GridRenderer : MonoBehaviour
{
    public GameObject squarePrefab;
    public MapGenerator MapGen;
    public GridDisplay GridDisplay;
    public int iterationsPerUpdate;
    public int iterations;

    private bool _generating = false;
    private int _currentIteration = 0;

    void Start()
    {
        // generate map
        MapGen = new MapGenerator(42, 200, 50);
        GridDisplay = new GridDisplay(squarePrefab);
        GridDisplay.DisplayGrid(MapGen.Map);
        StartGeneration();
    }


    private void Update()
    {
        if (Input.GetKeyDown("r") && !_generating)
            StartGeneration();

        if (_generating)
        {
            // do n update steps (n = iterationsPerUpdate)
            for (int i = 0; i < iterationsPerUpdate; i++)
            {
                MapGen.Step();
                _currentIteration++;

                if (_currentIteration > iterations)
                {
                    _generating = false;
                    GridDisplay.DisplayGrid(MapGen.Map);
                    break;
                }
            }

            // update display
            GridDisplay.DisplayGrid(MapGen.Map);
        }
    }

    private void StartGeneration()
    {
        MapGen.Initialize();
        _generating = true;
        _currentIteration = 0;
    }
}