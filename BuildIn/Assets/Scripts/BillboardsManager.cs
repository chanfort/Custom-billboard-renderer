﻿using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

public class BillboardsManager : MonoBehaviour
{
    public Transform lightTransform;
    public static BillboardsManager active;
    public Material billboardMaterial;
    public List<BillboardType> billboardTypes = new List<BillboardType>();

    List<ShiftedBillboardRenderer> billboardRenderers = new List<ShiftedBillboardRenderer>();

    public KeyCode useJobifiedPassToMeshSwitchKey;
    bool useJobifiedPassToMesh;

    void Awake()
    {
        active = this;

        for (int i = 0; i < billboardTypes.Count; i++)
        {
            BillboardType billboardType = billboardTypes[i];
            ShiftedBillboardRenderer billboardRenderer = new ShiftedBillboardRenderer
            {
                billboardMaterial = Instantiate(billboardMaterial),
                n = 0,
                mesh = billboardType.prefab.GetComponent<MeshFilter>().sharedMesh,
                materials = billboardType.prefab.GetComponent<MeshRenderer>().sharedMaterials,
                lodDistance = billboardType.lodDistance,
                treeMode = billboardType.treeMode,
                castShadows = billboardType.castShadows,
                receiveShadows = billboardType.receiveShadows,
                resolution = billboardType.resolution,
                positions = new NativeArray<float3>(0, Allocator.Persistent),
                lightTransform = lightTransform,
                useJobifiedPassToMesh = useJobifiedPassToMesh
            };
            billboardRenderer.Start();
            billboardRenderers.Add(billboardRenderer);
        }
    }

    public void UpdatePositions(GameObject prefab, float3[] positions)
    {
        int i = GetPrefabIndex(prefab);
        if (i != -1)
        {
            billboardRenderers[i].RefreshPositions(new NativeArray<float3>(positions, Allocator.Persistent));
        }
    }

    public void UpdatePositions(int i, float3[] positions)
    {
        if (i > -1 && i < billboardRenderers.Count)
        {
            billboardRenderers[i].RefreshPositions(new NativeArray<float3>(positions, Allocator.Persistent));
        }
    }

    int GetPrefabIndex(GameObject prefab)
    {
        for (int i = 0; i < billboardTypes.Count; i++)
        {
            if (billboardTypes[i].prefab == prefab)
            {
                return i;
            }
        }
        return -1;
    }

    void Update()
    {
        if (Input.GetKeyDown(useJobifiedPassToMeshSwitchKey))
        {
            useJobifiedPassToMesh = !useJobifiedPassToMesh;

            if(useJobifiedPassToMesh)
            {
                Debug.Log("Using JobifiedPassToMesh");
            }
            else
            {
                Debug.Log("Using regular PassToMesh");
            }

            for (int i = 0; i < billboardRenderers.Count; i++)
            {
                billboardRenderers[i].useJobifiedPassToMesh = useJobifiedPassToMesh;
            }
        }

        for (int i = 0; i < billboardRenderers.Count; i++)
        {
            billboardRenderers[i].Update();
        }
    }

    void OnApplicationQuit()
    {
        Dispose();
    }

    public void Dispose()
    {
        for (int i = 0; i < billboardRenderers.Count; i++)
        {
            billboardRenderers[i].Dispose();
        }
        billboardRenderers.Clear();
    }

    [System.Serializable]
    public class BillboardType
    {
        public GameObject prefab;
        public float lodDistance = 10f;
        public bool treeMode = false;
        public bool castShadows;
        public bool receiveShadows;
        public int resolution = 128;
    }
}
