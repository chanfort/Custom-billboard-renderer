using System.Collections.Generic;
using UnityEngine;

public static class Sort
{
    public static Vector3[] SortByDistance(Vector3[] input, Vector3 center)
    {
        if(input == null || input.Length == 0)
        {
            return null;
        }

        float[] distances = new float[input.Length];
        for(int i=0; i<distances.Length; i++)
        {
            distances[i] = 1f / (input[i] - center).sqrMagnitude;
        }

        int[] sorted = HeapSortFloat(distances);
        
        Vector3[] result = new Vector3[sorted.Length];
        for(int i=0; i<sorted.Length; i++)
        {
            result[i] = input[sorted[i]];
        }

        return result;
    }

    public static int[] HeapSortFloat(float[] input)
    {
        //Build-Max-Heap
        int heapSize = input.Length;

        int[] iorig = new int[heapSize];
        for (int i = 0; i < heapSize; i++)
        {
            iorig[i] = i;
        }

        for (int p = (heapSize - 1) / 2; p >= 0; p--)
            MaxHeapifyFloat(input, iorig, heapSize, p);

        for (int i = input.Length - 1; i > 0; i--)
        {
            //Swap
            float temp = input[i];
            input[i] = input[0];
            input[0] = temp;

            int itemp = iorig[i];
            iorig[i] = iorig[0];
            iorig[0] = itemp;

            heapSize--;
            MaxHeapifyFloat(input, iorig, heapSize, 0);
        }

        return iorig;
    }


    private static void MaxHeapifyFloat(float[] input, int[] iorig, int heapSize, int index)
    {
        int left = (index + 1) * 2 - 1;
        int right = (index + 1) * 2;
        int largest = 0;

        if (left < heapSize && input[left] > input[index])
            largest = left;
        else
            largest = index;

        if (right < heapSize && input[right] > input[largest])
            largest = right;

        if (largest != index)
        {
            float temp = input[index];
            input[index] = input[largest];
            input[largest] = temp;

            int itemp = iorig[index];
            iorig[index] = iorig[largest];
            iorig[largest] = itemp;

            MaxHeapifyFloat(input, iorig, heapSize, largest);
        }
    }
}
