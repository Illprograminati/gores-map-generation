﻿using System;
using System.Collections.Generic;
using System.Linq;
using IO;
using MonoBehaviour;
using UnityEngine;
using Util;

namespace Generator
{
    public class Map
    {
        private BlockType[,] grid;
        public int Width;
        public int Height;

        public Map(int width, int height)
        {
            grid = new BlockType[width, height];
            this.Width = width;
            this.Height = height;
        }

        public Map(BlockType[,] grid)
        {
            this.grid = grid;
            Width = grid.GetLength(0);
            Height = grid.GetLength(1);
        }

        public BlockType this[int x, int y]
        {
            get => grid[x, y];
            set => grid[x, y] = value;
        }

        public static bool CheckSameDimension(Map map1, Map map2)
        {
            return map1.Height == map2.Height && map1.Width == map2.Width;
        }

        public void ExportMap(string name)
        {
            MapSerializer.ExportMap(this, name);
        }

        public void SetBlocks(int xPos, int yPos, bool[,] kernel, BlockType type)
        {
            var kernelOffset = (kernel.GetLength(0) - 1) / 2;
            var kernelSize = kernel.GetLength(0);

            for (var xKernel = 0; xKernel < kernelSize; xKernel++)
            {
                for (var yKernel = 0; yKernel < kernelSize; yKernel++)
                {
                    int x = xPos + (xKernel - kernelOffset);
                    int y = yPos + (yKernel - kernelOffset);
                    if (kernel[xKernel, yKernel] && x > 0 && x < Width && y > 0 && y < Height)
                        grid[x, y] = type;
                }
            }
        }

        public Map Clone()
        {
            return new Map((BlockType[,])grid.Clone());
        }

        public bool CheckTypeInArea(int x1, int y1, int x2, int y2, BlockType type)
        {
            // returns True if type is at least present once in the area
            for (var x = x1; x <= x2; x++)
            {
                for (var y = y1; y <= y2; y++)
                {
                    if (grid[x, y] == type)
                        return true;
                }
            }

            return false;
        }

        public BlockType[,] GetCellNeighbors(int xPos, int yPos)
        {
            var neighbors = new BlockType[3, 3];
            for (var xOffset = 0; xOffset <= 2; xOffset++)
            {
                for (var yOffset = 0; yOffset <= 2; yOffset++)
                {
                    neighbors[xOffset, yOffset] = grid[xPos - xOffset - 1, yPos - yOffset - 1];
                }
            }

            return neighbors;
        }
    }

    public class MoveArray
    {
        public Vector2Int[] moves;
        public float[] probabilities;
        public readonly int size;

        public MoveArray(bool allowDiagonal)
        {
            size = allowDiagonal ? 8 : 4;
            moves = new Vector2Int[size];
            probabilities = new float[size];

            int index = 0;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) // skip (0,0) move
                        continue;

                    if (!allowDiagonal && x != 0 && y != 0) // skip diagonal moves, if disallowed
                        continue;

                    moves[index++] = new Vector2Int(x, y);
                }
            }
        }

        public void Normalize()
        {
            float sum = Sum();
            for (int i = 0; i < size; i++)
            {
                probabilities[i] /= sum;
            }
        }

        public float Sum()
        {
            return probabilities.Sum();
        }

        public override String ToString()
        {
            return $"{moves}, {probabilities}";
        }
    }

    public class MapGenerator
    {
        // config
        public MapGenerationConfig config;

        // data structures
        public Map Map { get; }
        private RandomGenerator _rndGen;
        private List<Vector2Int> _positions;
        private KernelGenerator _kernelGenerator;

        // walker state
        public Vector2Int WalkerPos;
        private bool[,] _kernel;
        private int _walkerTargetPosIndex = 0;
        private MapGeneratorMode _walkerMode;
        private int stepCount;

        // tunnel mode state
        private int _tunnelRemainingSteps = 0;
        private Vector2Int _tunnelDir;

        public MapGenerator(MapGenerationConfig config)
        {
            this.config = config;

            Map = new Map(config.mapWidth, config.mapHeight);
            _rndGen = new RandomGenerator(config.seed);
            _positions = new List<Vector2Int>();
            _positions.Add(new Vector2Int(WalkerPos.x, WalkerPos.y));
            _kernelGenerator =
                new KernelGenerator(config.kernelConfig, config.initKernelSize, config.initKernelCircularity);

            WalkerPos = config.initPosition;
            _kernel = _kernelGenerator.GetCurrentKernel();
            _walkerMode = MapGeneratorMode.DistanceProbability; // start default mode 
        }

        public int GetSeed()
        {
            return config.seed;
        }

        private Vector2Int StepTunnel()
        {
            if (_tunnelRemainingSteps <= 0)
            {
                _walkerMode = MapGeneratorMode.DistanceProbability;
            }

            _tunnelRemainingSteps--;
            return _tunnelDir;
        }

        private Vector2Int StepDistanceProbabilities()
        {
            var distanceProbabilities = GetDistanceProbabilities();
            var pickedMove = _rndGen.PickRandomMove(distanceProbabilities);
            _kernelGenerator.Mutate(config.kernelSizeChangeProb, config.kernelCircularityChangeProb, _rndGen);

            // switch to tunnel mode with a certain probability TODO: state pattern?
            if (config.enableTunnelMode && _rndGen.RandomBool(config.tunnelProbability))
            {
                _walkerMode = MapGeneratorMode.Tunnel;
                _tunnelRemainingSteps = _rndGen.RandomChoice(config.tunnelLengths);
                _kernelGenerator.ForceKernelConfig(size: _rndGen.RandomChoice(config.tunnelWidths), circularity: 0.0f);
                _tunnelDir = GetBestMove();
            }

            return pickedMove;
        }

        public void Step()
        {
            // calculate next move depending on current _walkerMode
            Vector2Int pickedMove = _walkerMode switch
            {
                MapGeneratorMode.DistanceProbability => StepDistanceProbabilities(),
                MapGeneratorMode.Tunnel => StepTunnel(),
                _ => Vector2Int.zero
            };

            // move walker by picked move and remove tiles using a given kernel
            WalkerPos += pickedMove;
            _positions.Add(new Vector2Int(WalkerPos.x, WalkerPos.y));
            Map.SetBlocks(WalkerPos.x, WalkerPos.y, _kernelGenerator.GetCurrentKernel(), BlockType.Empty);

            // update targetPosition if current one was reached
            if (WalkerPos.Equals(GetCurrentTargetPos()) && _walkerTargetPosIndex < config.targetPositions.Length - 1)
                _walkerTargetPosIndex++;

            stepCount++;
        }

        public void OnFinish()
        {
            FillSpaceWithObstacles(config.distanceTransformMethod, config.distanceThreshold, config.preDistanceNoise,
                config.gridDistance);
            GenerateFreeze();
            if (config.generatePlatforms)
                GeneratePlatforms();
        }

        public Vector2Int GetCurrentTargetPos()
        {
            return config.targetPositions[_walkerTargetPosIndex];
        }

        private MoveArray GetDistanceProbabilities()
        {
            var moveArray = new MoveArray(allowDiagonal: false);

            // calculate distances for each possible move
            var moveDistances = new float[moveArray.size];
            for (var moveIndex = 0; moveIndex < moveArray.size; moveIndex++)
                moveDistances[moveIndex] =
                    Vector2Int.Distance(GetCurrentTargetPos(), WalkerPos + moveArray.moves[moveIndex]);

            // sort moves by their respective distance to the goal
            Array.Sort(moveDistances, moveArray.moves);

            // assign each move a probability based on their index in the sorted order
            for (var i = 0; i < moveArray.size; i++)
                moveArray.probabilities[i] = MathUtil.GeometricDistribution(i + 1, config.bestMoveProbability);

            moveArray.Normalize(); // normalize the probabilities so that they sum up to 1

            return moveArray;
        }

        private Vector2Int GetBestMove()
        {
            var moveArray = new MoveArray(allowDiagonal: false);

            // calculate distances for each possible move
            Vector2Int bestMove = Vector2Int.zero;
            float bestDistance = float.MaxValue;
            for (var moveIndex = 0; moveIndex < moveArray.size; moveIndex++)
            {
                float distance = Vector2Int.Distance(GetCurrentTargetPos(), WalkerPos + moveArray.moves[moveIndex]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMove = moveArray.moves[moveIndex];
                }
            }

            return bestMove;
        }


        private void FillSpaceWithObstacles(DistanceTransformMethod distanceTransformMethod, float distanceThreshold,
            float preDistanceNoise, int gridDistance)
        {
            float[,] distances =
                MathUtil.DistanceTransform(Map, distanceTransformMethod, _rndGen, preDistanceNoise, gridDistance);
            int width = distances.GetLength(0);
            int height = distances.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (distances[x, y] >= distanceThreshold)
                    {
                        Map[x, y] = BlockType.Hookable;
                    }
                }
            }
        }


        private void GenerateFreeze()
        {
            // iterate over every cell of the map
            for (var x = 0; x < config.mapWidth; x++)
            {
                for (var y = 0; y < config.mapHeight; y++)
                {
                    // if a hookable tile is nearby -> set freeze
                    if (Map[x, y] == BlockType.Empty &&
                        Map.CheckTypeInArea(x - 1, y - 1, x + 1, y + 1, BlockType.Hookable))
                        Map[x, y] = BlockType.Freeze;
                }
            }
        }

        private void GeneratePlatforms()
        {
            // very WIP, but kinda works? TODO: add parameters to config 
            int minPlatformDistance = 1000; // an average distance might allow for better platform placement
            int safeTop = 4;
            int safeRight = 4;
            int safeDown = 0;
            int safeLeft = 4;

            int lastPlatformIndex = 0;
            int currentPositionIndex = 0;
            int positionsCount = _positions.Count;

            while (currentPositionIndex < positionsCount)
            {
                if (currentPositionIndex > lastPlatformIndex + minPlatformDistance)
                {
                    int x = _positions[currentPositionIndex].x;
                    int y = _positions[currentPositionIndex].y;
                    if (!CheckPlatformArea(x, y, safeLeft, safeTop, safeRight, safeDown))
                    {
                        currentPositionIndex++;
                        continue;
                    }

                    Map[x - safeLeft, y - safeDown] = BlockType.Debug;
                    Map[x + safeRight, y + safeTop] = BlockType.Debug;

                    // move platform area down until it hits a wall
                    bool movedDown = false;
                    while (CheckPlatformArea(x, y, safeLeft, safeTop, safeRight, safeDown))
                    {
                        y--;
                        movedDown = true;
                    }

                    Map[x - safeLeft, (movedDown ? y + 1 : y) - safeDown] = BlockType.Unhookable;
                    Map[x + safeRight, (movedDown ? y + 1 : y) + safeTop] = BlockType.Unhookable;

                    // place platform at last safe position
                    PlacePlatform(x, movedDown ? y + 1 : y);
                    lastPlatformIndex = currentPositionIndex;
                }

                currentPositionIndex++;
            }
        }

        private bool CheckPlatformArea(int x, int y, int safeLeft, int safeTop, int safeRight, int safeDown)
        {
            return !Map.CheckTypeInArea(x - safeLeft, y - safeDown, x + safeRight, y + safeTop, BlockType.Hookable) &&
                   !Map.CheckTypeInArea(x - safeLeft, y - safeDown, x + safeRight, y + safeTop, BlockType.Freeze);
        }

        private void PlacePlatform(int x, int y)
        {
            Map[x, y] = BlockType.Platform;
            Map[x - 1, y] = BlockType.Platform;
            Map[x - 2, y] = BlockType.Platform;
            Map[x + 1, y] = BlockType.Platform;
            Map[x + 2, y] = BlockType.Platform;
        }
    }
}