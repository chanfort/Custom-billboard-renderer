using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class CustomBillboardRenderer
{
    public Material billboardMaterial;

    public int n = 10000;
    public NativeArray<float3> positions;

    NativeArray<float3> nearPositions;
    NativeArray<int> nearIndices;

    NativeArray<float3> farPositions;
    NativeArray<quaternion> farRotations;
    NativeArray<int> farIndices;

    NativeArray<float3> transitioningPositions;
    NativeArray<int> transitioningIndices;

    NativeArray<short> calculationPhase;
    // 0 - not initialised
    // 1 - draw as mesh
    // 2 - draw as billboard
    // 3 - mesh to billboard
    // 4 - billboard to mesh

    Mesh[] combinedMeshes = new Mesh[0];

    ModelBounds bounds;
    Texture2D texture;

    public Mesh mesh;
    public Material[] materials;
    Camera mainCamera;
    Transform mainCameraTransform;
    public Transform lightTransform;
    float3 previousCameraPosition;
    quaternion previousCameraRotation;
    quaternion previousLightRotation;

    bool initialized;
    bool calculating;

    public float lodDistance = 10f;
    float3 meshOffset;

    public bool treeMode = false;

    public int resolution = 128;
    public bool castShadows;
    public bool receiveShadows;
    public bool useJobifiedPassToMesh;

    public void Start()
    {
        ResetMesh();

        mainCamera = Camera.main;
        mainCameraTransform = mainCamera.transform;
        Vector3 cameraPos = mainCameraTransform.position;

        InitialiseModel();

        calculationPhase = new NativeArray<short>(n, Allocator.Persistent);

        for (int i = 0; i < n; i++)
        {
            positions[i] += meshOffset;
        }

        SplitLod();
        UpdateInstanceRotations(cameraPos);
        BatchStart();
        renderTexture = new RenderTexture(resolution, resolution, 24);
        Capture();
        Capture();

        previousCameraPosition = cameraPos;
        previousCameraRotation = mainCameraTransform.rotation;

        if (lightTransform != null)
        {
            previousLightRotation = lightTransform.rotation;
        }
    }

    public void RefreshPositions(NativeArray<float3> newPositions)
    {
        positions.Dispose();
        FinishSchedules();

        positions = newPositions;

        if (calculationPhase.IsCreated)
        {
            calculationPhase.Dispose();
        }

        n = newPositions.Length;

        for (int i = 0; i < combinedFarIndicesListPrev.Count; i++)
        {
            if (combinedFarIndicesListPrev[i].IsCreated)
            {
                combinedFarIndicesListPrev[i].Dispose();
            }
        }
        combinedFarIndicesListPrev.Clear();
        for (int i = 0; i < n; i++)
        {
            combinedFarIndicesListPrev.Add(new NativeArray<int>());
        }

        for (int i = 0; i < n; i++)
        {
            positions[i] += meshOffset;
        }
        calculationPhase = new NativeArray<short>(n, Allocator.Persistent);

        SplitLod();
        UpdateInstanceRotations(mainCameraTransform.position);
        BatchStart();
        Capture();
    }

    void ResetMesh()
    {
        Mesh copyMesh = MonoBehaviour.Instantiate(mesh);
        Vector3[] copyVertices = copyMesh.vertices;

        Vector3 center = mesh.bounds.center;

        for (int i = 0; i < copyVertices.Length; i++)
        {
            copyVertices[i] -= center;
        }

        copyMesh.vertices = copyVertices;
        copyMesh.RecalculateBounds();

        mesh = copyMesh;
        meshOffset = center;
    }

    void InitialiseModel()
    {
        GetModelBounds();
        texture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
        billboardMaterial.mainTexture = texture;
    }

    public void Update()
    {
        if (calculating)
        {
            if (batchHandleComplete)
            {
                BatchProceed();
            }
            else
            {
                BatchJobCheck();
            }
        }

        if (!initialized)
        {
            return;
        }

        UpdateCamera();

        for (int i = 0; i < combinedMeshes.Length; i++)
        {
            Graphics.DrawMesh(combinedMeshes[i], Vector3.zero, Quaternion.identity, billboardMaterial, 0, mainCamera, 0, null, castShadows, false);
        }

        for (int i = 0; i < nearPositions.Length; i++)
        {
            int globalId = nearIndices[i];
            short cPhase = calculationPhase[globalId];

            for (int j = 0; j < materials.Length; j++)
            {
                Graphics.DrawMesh(mesh, nearPositions[i], Quaternion.identity, materials[j], 0, mainCamera, j, null, castShadows, receiveShadows);
            }
        }

        for (int i = 0; i < transitioningPositions.Length; i++)
        {
            int globalId = transitioningIndices[i];
            short cPhase = calculationPhase[globalId];
            if (cPhase == 1 || cPhase == 3)
            {
                for (int j = 0; j < materials.Length; j++)
                {
                    Graphics.DrawMesh(mesh, transitioningPositions[i], Quaternion.identity, materials[j], 0, mainCamera, j, null, castShadows, receiveShadows);
                }
            }
        }
    }

    void UpdateCamera()
    {
        float3 currentCameraPosition = mainCameraTransform.position;
        if (math.lengthsq(currentCameraPosition - previousCameraPosition) > 1f && !calculating)
        {
            previousCameraPosition = currentCameraPosition;

            SplitLod();
            UpdateInstanceRotations(currentCameraPosition);
            BatchStart();
        }

        quaternion currentCameraRotation = mainCameraTransform.rotation;
        if (Quaternion.Angle(currentCameraRotation, previousCameraRotation) > 2f)
        {
            previousCameraRotation = currentCameraRotation;
            Capture();
        }
        else if (lightTransform != null)
        {
            quaternion currentLightRotation = lightTransform.rotation;
            if (Quaternion.Angle(lightTransform.rotation, previousLightRotation) > 5f)
            {
                previousLightRotation = currentLightRotation;
                Capture();
            }
        }
    }

    void SplitLod()
    {
        NativeArray<bool> isNear = new NativeArray<bool>(n, Allocator.Persistent);
        NativeArray<int> nearCount = new NativeArray<int>(1, Allocator.Persistent);

        NativeArray<bool> isFar = new NativeArray<bool>(n, Allocator.Persistent);
        NativeArray<int> farCount = new NativeArray<int>(1, Allocator.Persistent);

        NativeArray<bool> isTransitioning = new NativeArray<bool>(n, Allocator.Persistent);
        NativeArray<int> transitioningCount = new NativeArray<int>(1, Allocator.Persistent);

        JobHandle handle = new NearCountJob
        {
            maxDistanceSqr = lodDistance * lodDistance,
            cameraPos = mainCameraTransform.position,
            n = n,
            positions = positions,
            calculationPhase = calculationPhase,
            isNear = isNear,
            nearCount = nearCount,
            isFar = isFar,
            farCount = farCount,
            isTransitioning = isTransitioning,
            transitioningCount = transitioningCount
        }.Schedule();
        handle.Complete();

        nearPositions = Resize(nearPositions, nearCount[0]);
        nearIndices = Resize(nearIndices, nearCount[0]);
        farPositions = Resize(farPositions, farCount[0]);
        farIndices = Resize(farIndices, farCount[0]);

        transitioningPositions = Resize(transitioningPositions, transitioningCount[0]);
        transitioningIndices = Resize(transitioningIndices, transitioningCount[0]);

        float3 offset = float3.zero;
        if (treeMode)
        {
            offset = new float3(0f, -bounds.maxSize, 0f);
        }

        handle = new SetNearFarPositionsJob
        {
            n = n,
            isNear = isNear,
            isFar = isFar,
            positions = positions,
            nearPositions = nearPositions,
            nearIndices = nearIndices,
            farPositions = farPositions,
            farIndices = farIndices,
            isTransitioning = isTransitioning,
            offset = offset,
            transitioningPositions = transitioningPositions,
            transitioningIndices = transitioningIndices,
            calculationPhase = calculationPhase
        }.Schedule();
        handle.Complete();

        isNear.Dispose();
        nearCount.Dispose();
        isFar.Dispose();
        farCount.Dispose();
        isTransitioning.Dispose();
        transitioningCount.Dispose();
    }

    [BurstCompile]
    public struct NearCountJob : IJob
    {
        [ReadOnly] public float maxDistanceSqr;
        [ReadOnly] public float3 cameraPos;
        [ReadOnly] public int n;
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<short> calculationPhase;
        [WriteOnly] public NativeArray<bool> isNear;
        [WriteOnly] public NativeArray<int> nearCount;

        [WriteOnly] public NativeArray<bool> isFar;
        [WriteOnly] public NativeArray<int> farCount;

        [WriteOnly] public NativeArray<bool> isTransitioning;
        [WriteOnly] public NativeArray<int> transitioningCount;

        public void Execute()
        {
            int ncount = 0;
            int fcount = 0;
            int tcount = 0;
            for (int i = 0; i < n; i++)
            {
                float cameraDist = math.lengthsq(cameraPos - positions[i]);
                if (cameraDist < maxDistanceSqr)
                {
                    if (calculationPhase[i] == 2)
                    {
                        isTransitioning[i] = true;
                        tcount++;
                    }
                    else
                    {
                        isTransitioning[i] = false;
                    }

                    if (calculationPhase[i] == 1 || calculationPhase[i] == 0)
                    {
                        isNear[i] = true;
                        ncount++;
                    }
                    else
                    {
                        isNear[i] = false;
                    }
                }
                else
                {
                    isFar[i] = true;
                    fcount++;

                    if (calculationPhase[i] == 1)
                    {
                        isTransitioning[i] = true;
                        tcount++;
                    }
                    else
                    {
                        isTransitioning[i] = false;
                    }
                }
            }
            nearCount[0] = ncount;
            farCount[0] = fcount;
            transitioningCount[0] = tcount;
        }
    }

    [BurstCompile]
    public struct SetNearFarPositionsJob : IJob
    {
        [ReadOnly] public int n;
        [ReadOnly] public NativeArray<bool> isNear;
        [ReadOnly] public NativeArray<bool> isFar;
        [ReadOnly] public NativeArray<float3> positions;
        [WriteOnly] public NativeArray<float3> nearPositions;
        [WriteOnly] public NativeArray<int> nearIndices;
        [WriteOnly] public NativeArray<float3> farPositions;
        [WriteOnly] public NativeArray<int> farIndices;
        [ReadOnly] public NativeArray<bool> isTransitioning;
        [ReadOnly] public float3 offset;
        [WriteOnly] public NativeArray<float3> transitioningPositions;
        [WriteOnly] public NativeArray<int> transitioningIndices;
        public NativeArray<short> calculationPhase;

        public void Execute()
        {
            int iNear = 0;
            int iFar = 0;
            int iTransitioning = 0;
            for (int i = 0; i < n; i++)
            {
                short currentCalculationPhase = calculationPhase[i];

                if (isNear[i])
                {
                    nearPositions[iNear] = positions[i];
                    nearIndices[iNear] = i;
                    iNear++;

                    if (currentCalculationPhase == 0)
                    {
                        calculationPhase[i] = 1;
                    }
                    else if (currentCalculationPhase == 2)
                    {
                        calculationPhase[i] = 4;
                    }
                    else
                    {
                        calculationPhase[i] = 1;
                    }
                }
                else if (isFar[i])
                {
                    farPositions[iFar] = positions[i] + offset;
                    farIndices[iFar] = i;
                    iFar++;

                    if (currentCalculationPhase == 0)
                    {
                        calculationPhase[i] = 2;
                    }
                    else if (currentCalculationPhase == 1)
                    {
                        calculationPhase[i] = 3;
                    }
                    else
                    {
                        calculationPhase[i] = 2;
                    }
                }

                if (isTransitioning[i])
                {
                    transitioningPositions[iTransitioning] = positions[i];
                    transitioningIndices[iTransitioning] = i;
                    if (currentCalculationPhase == 2)
                    {
                        calculationPhase[i] = 4;
                    }
                    iTransitioning++;
                }
            }
        }
    }

    NativeMesh billboardNativeMesh;

    List<NativeArray<int>> combinedFarIndicesListPrev = new List<NativeArray<int>>();
    List<NativeArray<int>> combinedFarIndicesList = new List<NativeArray<int>>();

    List<NativeMesh> combinedNativeMeshes = new List<NativeMesh>();
    bool recreate;
    int nSplits;
    bool batchHandleComplete = true;
    JobHandle batchJobHandle;

    void BatchStart()
    {
        calculating = true;

        billboardNativeMesh = GetBillboardMesh();

        int nVerticesBillboardMesh = billboardNativeMesh.verticesData.Length;
        int nTrianglesBillboardMesh = billboardNativeMesh.triangles.Length;

        int positionsPerSplit = 15000;
        nSplits = farPositions.Length / positionsPerSplit + 1;

        for (int i = 0; i < billboardNativeMesh.verticesData.Length; i++)
        {
            VertexData vertexData = billboardNativeMesh.verticesData[i];

            if (treeMode)
            {
                vertexData.pos += new float3(0f, 0.5f, 0f);
            }
            vertexData.pos *= 2f * bounds.maxSize;

            billboardNativeMesh.verticesData[i] = vertexData;
        }

        combinedNativeMeshes.Clear();

        combinedFarIndicesList = new List<NativeArray<int>>();

        batchJobHandle = default;

        for (int l = 0; l < nSplits; l++)
        {
            int imin = l * positionsPerSplit;
            int imax = (l + 1) * positionsPerSplit;
            if (imax > farPositions.Length)
            {
                imax = farPositions.Length;
            }

            int nGroup = imax - imin;

            NativeArray<VertexData> combinedVerticesData = new NativeArray<VertexData>(nGroup * nVerticesBillboardMesh, Allocator.Persistent);

            NativeArray<int> combinedTriangles = new NativeArray<int>(nGroup * nTrianglesBillboardMesh, Allocator.Persistent);

            NativeArray<float3> groupPositions = new NativeArray<float3>(nGroup, Allocator.Persistent);
            NativeArray<quaternion> groupRotations = new NativeArray<quaternion>(nGroup, Allocator.Persistent);
            NativeArray<int> groupFarIndices = new NativeArray<int>(nGroup, Allocator.Persistent);

            batchJobHandle = new CopyGroupPositionsJob
            {
                imin = imin,
                farPositions = farPositions,
                farRotations = farRotations,
                farIndices = farIndices,
                groupPositions = groupPositions,
                groupRotations = groupRotations,
                groupFarIndices = groupFarIndices
            }.Schedule(nGroup, 4, batchJobHandle);

            batchJobHandle = new CalculateCombinedMeshJob
            {
                verticesData = billboardNativeMesh.verticesData,
                triangles = billboardNativeMesh.triangles,

                nVerticesBillboardMesh = nVerticesBillboardMesh,
                nTrianglesBillboardMesh = nTrianglesBillboardMesh,

                groupPositions = groupPositions,
                groupRotations = groupRotations,

                combinedVerticesData = combinedVerticesData,

                combinedTriangles = combinedTriangles
            }.Schedule(nGroup, 4, batchJobHandle);

            groupPositions.Dispose(batchJobHandle);
            groupRotations.Dispose(batchJobHandle);

            combinedNativeMeshes.Add(
                new NativeMesh(
                    combinedVerticesData,
                    combinedTriangles
                )
            );

            combinedFarIndicesList.Add(groupFarIndices);
        }

        batchHandleComplete = false;
        BatchJobCheck();
    }

    void BatchJobCheck()
    {
        if (!batchJobHandle.IsCompleted)
        {
            return;
        }

        batchJobHandle.Complete();
        batchHandleComplete = true;

        recreate = false;
        if (combinedMeshes == null || combinedMeshes.Length != nSplits)
        {
            if (combinedMeshes != null)
            {
                for (int l = 0; l < combinedMeshes.Length; l++)
                {
                    MonoBehaviour.Destroy(combinedMeshes[l]);
                }

                for (int l = 0; l < combinedFarIndicesListPrev.Count; l++)
                {
                    for (int j = 0; j < combinedFarIndicesListPrev[l].Length; j++)
                    {

                        int phase = calculationPhase[combinedFarIndicesListPrev[l][j]];

                        if (phase == 4)
                        {
                            calculationPhase[combinedFarIndicesListPrev[l][j]] = 1;
                        }
                    }
                }
            }
            combinedMeshes = new Mesh[nSplits];
            recreate = true;

        }

        currentBatch = 0;
        BatchProceed();
    }

    int currentBatch;
    void BatchProceed()
    {
        if (recreate)
        {
            combinedMeshes[currentBatch] = new Mesh();
        }
        else
        {
            combinedMeshes[currentBatch].Clear();

            for (int j = 0; j < combinedFarIndicesListPrev[currentBatch].Length; j++)
            {
                int phase = calculationPhase[combinedFarIndicesListPrev[currentBatch][j]];
                if (phase == 4)
                {
                    calculationPhase[combinedFarIndicesListPrev[currentBatch][j]] = 1;
                }
            }
        }

        if (useJobifiedPassToMesh)
        {
            combinedNativeMeshes[currentBatch].PassToMeshJobified(combinedMeshes[currentBatch]);
        }
        else
        {
            combinedNativeMeshes[currentBatch].PassToMesh(combinedMeshes[currentBatch]);
        }


        for (int i = 0; i < combinedFarIndicesList[currentBatch].Length; i++)
        {
            calculationPhase[combinedFarIndicesList[currentBatch][i]] = 2;
        }

        combinedNativeMeshes[currentBatch].Dispose();
        currentBatch++;

        if (currentBatch >= nSplits)
        {
            BatchFinish();
            currentBatch = 0;
        }
    }

    void BatchFinish()
    {
        billboardNativeMesh.Dispose();

        for (int i = 0; i < combinedFarIndicesListPrev.Count; i++)
        {
            CheckAndDispose(combinedFarIndicesListPrev[i]);
        }
        combinedFarIndicesListPrev = combinedFarIndicesList;

        initialized = true;
        calculating = false;
    }

    NativeMesh GetBillboardMesh()
    {
        NativeArray<VertexData> verticesData = new NativeArray<VertexData>(4, Allocator.Persistent);

        verticesData[0] = GetVertexData(
            new float3(-0.5f, -0.5f, 0f),
            new float3(0f, 0f, -1f),
            new float2(0f, 0f)
        );

        verticesData[1] = GetVertexData(
            new float3(0.5f, -0.5f, 0f),
            new float3(0f, 0f, -1f),
            new float2(1f, 0f)
        );

        verticesData[2] = GetVertexData(
            new float3(-0.5f, 0.5f, 0f),
            new float3(0f, 0f, -1f),
            new float2(0f, 1f)
        );

        verticesData[3] = GetVertexData(
            new float3(0.5f, 0.5f, 0f),
            new float3(0f, 0f, -1f),
            new float2(1f, 1f)
        );

        NativeArray<int> triangles = new NativeArray<int>(6, Allocator.Persistent);

        triangles[0] = 0;
        triangles[1] = 3;
        triangles[2] = 1;
        triangles[3] = 3;
        triangles[4] = 0;
        triangles[5] = 2;

        return new NativeMesh(verticesData, triangles);
    }

    VertexData GetVertexData(float3 pos, float3 normal, float2 uv)
    {
        return new VertexData
        {
            pos = pos,
            normal = normal,
            uv = uv
        };
    }

    Vector3[] ToVector3Array(NativeArray<float3> input)
    {
        Vector3[] output = new Vector3[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = input[i];
        }
        return output;
    }

    Vector2[] ToVector2Array(NativeArray<float2> input)
    {
        Vector2[] output = new Vector2[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = input[i];
        }
        return output;
    }

    [BurstCompile]
    public struct CopyGroupPositionsJob : IJobParallelFor
    {
        public int imin;
        [ReadOnly] public NativeArray<float3> farPositions;
        [ReadOnly] public NativeArray<quaternion> farRotations;
        [ReadOnly] public NativeArray<int> farIndices;

        [WriteOnly] public NativeArray<float3> groupPositions;
        [WriteOnly] public NativeArray<quaternion> groupRotations;
        [WriteOnly] public NativeArray<int> groupFarIndices;

        public void Execute(int i)
        {
            groupPositions[i] = farPositions[i + imin];
            groupRotations[i] = farRotations[i + imin];
            groupFarIndices[i] = farIndices[i + imin];
        }
    }

    [BurstCompile]
    public struct CalculateCombinedMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VertexData> verticesData;
        [ReadOnly] public NativeArray<int> triangles;

        [ReadOnly] public int nVerticesBillboardMesh;
        [ReadOnly] public int nTrianglesBillboardMesh;

        [ReadOnly] public NativeArray<float3> groupPositions;
        [ReadOnly] public NativeArray<quaternion> groupRotations;

        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<VertexData> combinedVerticesData;
        [NativeDisableParallelForRestriction] [WriteOnly] public NativeArray<int> combinedTriangles;

        public void Execute(int i)
        {
            float3 pos = groupPositions[i];
            quaternion rot = groupRotations[i];

            for (int j = 0; j < nVerticesBillboardMesh; j++)
            {
                int k = i * nVerticesBillboardMesh + j;

                VertexData vertexData = verticesData[j];
                VertexData combinedVertexData = vertexData;

                combinedVertexData.pos = pos + math.mul(rot, vertexData.pos);
                combinedVerticesData[k] = combinedVertexData;
            }

            for (int j = 0; j < nTrianglesBillboardMesh; j++)
            {
                int iTriangle = triangles[j];
                int k = i * nTrianglesBillboardMesh + j;
                combinedTriangles[k] = i * nVerticesBillboardMesh + iTriangle;
            }
        }
    }

    bool AllHandlesCompete(List<JobHandle> handles)
    {
        for (int i = 0; i < handles.Count; i++)
        {
            if (!handles[i].IsCompleted)
            {
                return false;
            }
        }
        return true;
    }

    void UpdateInstanceRotations(float3 cameraPos)
    {
        farRotations = Resize(farRotations, farPositions.Length);

        JobHandle handle = new UpdateInstanceRotationsJob
        {
            cameraPos = cameraPos,
            up = new float3(0f, 1f, 0f),
            farPositions = farPositions,
            farRotations = farRotations
        }.Schedule(farPositions.Length, 4);
        handle.Complete();
    }

    NativeArray<T> Resize<T>(NativeArray<T> input, int length) where T : struct
    {
        if (input != null)
        {
            if (input.IsCreated)
            {
                if (input.Length != length)
                {
                    input.Dispose();
                    input = new NativeArray<T>(length, Allocator.Persistent);
                }
            }
            else
            {
                input = new NativeArray<T>(length, Allocator.Persistent);
            }
        }
        else
        {
            input = new NativeArray<T>(length, Allocator.Persistent);
        }

        return input;
    }

    void CheckAndDispose<T>(NativeArray<T> input) where T : struct
    {
        if (input != null && input.IsCreated)
        {
            input.Dispose();
        }
    }

    void CheckAndDispose<T>(List<NativeArray<T>> inputList) where T : struct
    {
        if (inputList != null)
        {
            for (int i = 0; i < inputList.Count; i++)
            {
                NativeArray<T> input = inputList[i];
                if (input.IsCreated)
                {
                    input.Dispose();
                }
            }
        }
    }

    [BurstCompile]
    public struct UpdateInstanceRotationsJob : IJobParallelFor
    {
        [ReadOnly] public float3 cameraPos;
        [ReadOnly] public float3 up;
        [ReadOnly] public NativeArray<float3> farPositions;
        [WriteOnly] public NativeArray<quaternion> farRotations;

        public void Execute(int i)
        {
            farRotations[i] = quaternion.LookRotationSafe(farPositions[i] - cameraPos, up);
        }
    }

    void GetModelBounds()
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        float heightPositive = -float.MaxValue;
        float heightNegative = float.MaxValue;
        float sideMax = 0f;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 pos = vertices[i];
            heightPositive = Mathf.Max(heightPositive, pos.y);
            heightNegative = Mathf.Min(heightNegative, pos.y);

            sideMax = Mathf.Max(sideMax, pos.sqrMagnitude);
        }
        sideMax = Mathf.Sqrt(sideMax);

        Vector3 center = new Vector3(0f, (heightPositive + heightNegative) / 2f, 0f);
        float maxSize = Mathf.Max(sideMax, heightPositive - center.y, center.y - heightNegative);

        bounds = new ModelBounds
        {
            heightPositive = heightPositive,
            heightNegative = heightNegative,
            sideMax = sideMax,
            center = new Vector3(0f, (heightPositive + heightNegative) / 2f, 0f),
            maxSize = maxSize
        };
    }

    RenderTexture renderTexture;
    public void Capture()
    {
        float3 mainCameraDirection = mainCameraTransform.forward;

        Camera camera = new GameObject().AddComponent<Camera>();
        camera.transform.position = bounds.center - mainCameraDirection * (bounds.maxSize + camera.nearClipPlane);
        camera.transform.rotation = mainCameraTransform.rotation;

        camera.aspect = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.orthographicSize = bounds.maxSize;
        camera.cullingMask = 29 << 30;

        camera.targetTexture = renderTexture;
        for (int i = 0; i < materials.Length; i++)
        {
            Graphics.DrawMesh(mesh, Vector3.zero, Quaternion.identity, materials[i], 30, camera, i, null, castShadows, receiveShadows);
        }
        camera.Render();

        RenderTexture.active = renderTexture;

        texture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        texture.Apply();

        RenderTexture.active = null;
        camera.targetTexture = null;

        renderTexture.Release();

        MonoBehaviour.Destroy(camera.gameObject);
    }

    NativeArray<float2> ToNative2(Vector2[] input)
    {
        NativeArray<float2> output = new NativeArray<float2>(input.Length, Allocator.Persistent);
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = input[i];
        }
        return output;
    }

    NativeArray<float3> ToNative3(Vector3[] input)
    {
        NativeArray<float3> output = new NativeArray<float3>(input.Length, Allocator.Persistent);
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = input[i];
        }
        return output;
    }

    public void Dispose()
    {
        FinishSchedules();

        CheckAndDispose(positions);
        CheckAndDispose(farPositions);
        CheckAndDispose(farRotations);
        CheckAndDispose(farIndices);
        CheckAndDispose(nearPositions);
        CheckAndDispose(nearIndices);
        CheckAndDispose(transitioningPositions);
        CheckAndDispose(transitioningIndices);
        CheckAndDispose(calculationPhase);

        CheckAndDispose(combinedFarIndicesListPrev);
        if (combinedFarIndicesList != combinedFarIndicesListPrev)
        {
            CheckAndDispose(combinedFarIndicesList);
        }
        billboardNativeMesh.Dispose();
    }

    public void FinishSchedules()
    {
        if (!batchHandleComplete)
        {
            batchHandleComplete = true;
            batchJobHandle.Complete();
            BatchJobCheck();
        }

        if (calculating)
        {
            int nRemaining = nSplits - currentBatch;
            for (int i = 0; i < nRemaining; i++)
            {
                BatchProceed();
            }
        }
    }

    public class ModelBounds
    {
        public float heightPositive;
        public float heightNegative;
        public float sideMax;
        public float3 center;
        public float maxSize;
    }

    public struct NativeMesh
    {
        public NativeArray<VertexData> verticesData;
        public NativeArray<int> triangles;
        public bool isCreated;
        public bool meshCreated;

        public NativeMesh(NativeArray<VertexData> v, NativeArray<int> t)
        {
            verticesData = v;
            triangles = t;
            isCreated = true;
            meshCreated = false;
        }

        public void PassToMesh(Mesh mesh)
        {
            // var sw = new System.Diagnostics.Stopwatch();
            // sw.Start();
            VertexAttributeDescriptor[] layout = new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            };

            int vertexCount = verticesData.Length;
            int trianglesCount = triangles.Length;

            mesh.SetVertexBufferParams(vertexCount, layout);
            mesh.SetIndexBufferParams(trianglesCount, IndexFormat.UInt32);

            MeshUpdateFlags flags =
                MeshUpdateFlags.DontRecalculateBounds |
                MeshUpdateFlags.DontResetBoneBounds |
                MeshUpdateFlags.DontValidateIndices |
                MeshUpdateFlags.DontNotifyMeshUsers;

            mesh.SetVertexBufferData(verticesData, 0, 0, vertexCount, 0, flags);
            mesh.SetIndexBufferData(triangles, 0, 0, trianglesCount, flags);

            mesh.subMeshCount = 1;
            SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor
            {
                indexCount = triangles.Length,
                topology = MeshTopology.Triangles,
                bounds = new Bounds
                {
                    center = float3.zero,
                    extents = new float3(float.MaxValue, float.MaxValue, float.MaxValue)
                }
            };

            mesh.SetSubMesh(0, subMeshDescriptor, flags);

            mesh.bounds = subMeshDescriptor.bounds;
            meshCreated = true;

            // Debug.Log($"{sw.Elapsed.TotalMilliseconds}");
        }

        public void PassToMeshJobified(Mesh mesh)
        {
            // var sw = new System.Diagnostics.Stopwatch();
            // sw.Start();

            NativeArray<VertexAttributeDescriptor> layout = new NativeArray<VertexAttributeDescriptor>(3, Allocator.TempJob);

            Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);

            new ApplyMeshDataJob
            {
                verticesData = verticesData,
                triangles = triangles,
                meshDataArray = meshDataArray,
                layout = layout
            }.Schedule().Complete();

            mesh.bounds = new Bounds
            {
                center = float3.zero,
                extents = new float3(float.MaxValue, float.MaxValue, float.MaxValue)
            };

            // double t1 = sw.Elapsed.TotalMilliseconds;

            MeshUpdateFlags flags =
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontResetBoneBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers;

            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh, flags);

            meshCreated = true;
            layout.Dispose();

            // Debug.Log($"{t1} {sw.Elapsed.TotalMilliseconds - t1}");
        }

        [BurstCompile]
        struct ApplyMeshDataJob : IJob
        {
            public NativeArray<VertexData> verticesData;
            public NativeArray<int> triangles;

            public Mesh.MeshDataArray meshDataArray;
            public NativeArray<VertexAttributeDescriptor> layout;

            public void Execute()
            {
                layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
                layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
                layout[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

                Mesh.MeshData meshData = meshDataArray[0];

                int vertexCount = verticesData.Length;
                int trianglesCount = triangles.Length;

                meshData.SetVertexBufferParams(vertexCount, layout);
                meshData.SetIndexBufferParams(trianglesCount, IndexFormat.UInt32);

                MeshUpdateFlags flags =
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontResetBoneBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers;

                var meshDataVertices = meshData.GetVertexData<VertexData>();

                for (int i = 0; i < vertexCount; i++)
                {
                    meshDataVertices[i] = verticesData[i];
                }

                var meshDataTriangles = meshData.GetIndexData<int>();

                for (int i = 0; i < trianglesCount; i++)
                {
                    meshDataTriangles[i] = triangles[i];
                }

                meshData.subMeshCount = 1;
                SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor
                {
                    indexCount = triangles.Length,
                    topology = MeshTopology.Triangles,
                    bounds = new Bounds
                    {
                        center = float3.zero,
                        extents = new float3(float.MaxValue, float.MaxValue, float.MaxValue)
                    }
                };

                meshData.SetSubMesh(0, subMeshDescriptor, flags);
            }
        }

        public void Dispose()
        {
            if (!isCreated || meshCreated)
            {
                return;
            }

            isCreated = false;

            if (verticesData != null && verticesData.IsCreated)
            {
                verticesData.Dispose();
            }
            if (triangles != null && triangles.IsCreated)
            {
                triangles.Dispose();
            }
        }
    }
}
