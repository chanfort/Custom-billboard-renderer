﻿using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class GenerateTerrain : MonoBehaviour
{
    public Texture2D diffuseMap;
    public Texture2D normalMap;
    public Material terrainMaterial;
    public int[] treeManualPrefabs;
    public GameObject[] treeBuildInPrefabs;
    public bool manualBillboards = true;
    public int numberOfTrees = 2000000;

    float size;
    float maxHeight;
    TerrainData terrainData;
    Terrain terrain;

    void Start()
    {
        Generate();
    }

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.P))
        {
            SwitchTreeRenderer();
        }
    }

    void SwitchTreeRenderer()
    {
        // SetManualTrees(100);
        // SetManualTrees(2000000);

        manualBillboards = !manualBillboards;
        
        if(manualBillboards)
        {
            SetBuildInTrees(0);
            SetManualTrees(numberOfTrees);
        }
        else
        {
            SetManualTrees(0);
            SetBuildInTrees(numberOfTrees);
        }
    }

    void Generate()
    {
        // Settings
        int res = 512;
        size = 10000f;
        maxHeight = size / 10f;
        float perlinSize = 500f;

        // TerrainData
        terrainData = new TerrainData();
        terrainData.name = "Terrain";
        terrainData.heightmapResolution = res;
        terrainData.alphamapResolution = res;
        terrainData.size = new Vector3(size, maxHeight, size);

        // Terrain GameObject
        GameObject terrainGameObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainGameObject.transform.position = new Vector3(-size / 2f, 0f, -size / 2f);
        terrain = terrainGameObject.GetComponent<Terrain>();
        terrainGameObject.name = "Terrain";

        // Heightmap
        terrainData.SetHeights(0, 0, GetHeightmap(res + 1, perlinSize));

        // Textures
        TerrainLayer[] layerPrototypes = new TerrainLayer[1]
        {
            new TerrainLayer()
            {
                diffuseTexture = diffuseMap,
                normalMapTexture = normalMap
            }
        };
        terrainData.terrainLayers = layerPrototypes;

        // Materials
        terrain.materialTemplate = terrainMaterial;

        // Trees
        if(manualBillboards)
        {
            SetManualTrees(2000000);
        }
        else
        {
            SetBuildInTrees(2000000);
        }
        
        // Terrain properties
        terrain.basemapDistance = size / 2f;

        if (CameraController.active != null)
        {
            CameraController.active.terrain = terrain;
        }
    }

    void SetBuildInTrees(int nTrees)
    {
        // Tree prototypes
        TreePrototype[] treePrototypes = new TreePrototype[treeBuildInPrefabs.Length];
        for (int i = 0; i < treePrototypes.Length; i++)
        {
            treePrototypes[i] = new TreePrototype()
            {
                prefab = treeBuildInPrefabs[i],
                bendFactor = 0.1f
            };
        }
        terrainData.treePrototypes = treePrototypes;

        // Tree instances
        List<TreeInstance> treeInstances = new List<TreeInstance>();
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(1);

        for (int i = 0; i < treeBuildInPrefabs.Length; i++)
        {
            for (int j = 0; j < nTrees; j++)
            {
                float2 pos = random.NextFloat2();
                float treeBiome = NoiseExtensions.SNoise(pos, 1f, 2f, 0.5f, 1, 5f, new float2(10000, 0));

                if (treeBiome < 0.8f)
                {
                    float height = terrain.SampleHeight(new float3((pos.x - 0.5f) * size, 0f, (pos.y - 0.5f) * size));
                    if (height > 337f)
                    {
                        treeInstances.Add(
                            new TreeInstance()
                            {
                                color = Color.white,
                                heightScale = 1f,
                                lightmapColor = Color.white,
                                position = new float3(pos.x, height / maxHeight, pos.y),
                                prototypeIndex = i,
                                widthScale = 1f
                            }
                        );
                    }
                }
            }
        }
        terrainData.treeInstances = treeInstances.ToArray();

        
        terrain.treeDistance = size;
    }

    void SetManualTrees(int nTrees)
    {
        // int nTrees = 2000000;
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(1);

        for (int i = 0; i < treeManualPrefabs.Length; i++)
        {
            List<float3> treeInstances = new List<float3>();
            for (int j = 0; j < nTrees; j++)
            {
                float2 pos = random.NextFloat2();
                float treeBiome = NoiseExtensions.SNoise(pos, 1f, 2f, 0.5f, 1, 5f, new float2(10000, 0));

                if (treeBiome < 0.8f)
                {
                    float height = terrain.SampleHeight(new float3((pos.x - 0.5f) * size, 0f, (pos.y - 0.5f) * size));
                    if (height > 337f)
                    {
                        treeInstances.Add(
                            new float3(
                                (pos.x - 0.5f) * size,
                                height,
                                (pos.y - 0.5f) * size
                            )
                        );
                    }
                }
            }

            BillboardsManager.active.UpdatePositions(treeManualPrefabs[i], treeInstances.ToArray());
        }
    }

    float[,] GetHeightmap(int res, float size)
    {
        float[,] heghts = new float[res, res];

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                float2 pos = new float2(1f * i / size, 1f * j / size);
                heghts[i, j] = NoiseExtensions.SNoise(pos, 1f, 2f, 0.5f, 6, 1f, new float2(0, 0));
            }
        }

        return heghts;
    }
}
