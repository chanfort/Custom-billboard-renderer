using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SpaceSpawner : MonoBehaviour
{
    public List<SpawnableType> spawnables = new List<SpawnableType>();

    void Start()
    {
        for(int i=0; i<spawnables.Count; i++)
        {
            SetInstances(i);
        }
    }

    void SetInstances(int i)
    {
        SpawnableType spawnable = spawnables[i];
        UnityEngine.Random.InitState(spawnable.seed);
        float3[] positions = new float3[spawnable.n];

        for(int j=0; j<spawnable.n; j++)
        {
            positions[j] = UnityEngine.Random.insideUnitSphere * spawnable.spawnRadius;
        }

        BillboardsManager.active.UpdatePositions(spawnable.billboardId, positions);
    }

    [System.Serializable]
    public class SpawnableType
    {
        public int billboardId;
        public float spawnRadius;
        public int n;
        public int seed;
    }
}
