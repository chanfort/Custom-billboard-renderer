using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public static class NoiseExtensions
{
    public static float SNoise(
        float2 v,
        float frequency,
        float lacunarity,
        float persistence,
        int octaveCount,
        float scale,
        float2 offset
    )
    {
        v *= scale;
        v += offset;

        float total = 0;
        float amplitude = 1;
        float totalAmplitude = 0;

        for (float i = 0; i < octaveCount; i++)
        {
            float n = noise.snoise(new float2(v.x * frequency, v.y * frequency));
            total += n * amplitude;
            totalAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return (total / totalAmplitude) * 0.5f + 0.5f;
    }
}
