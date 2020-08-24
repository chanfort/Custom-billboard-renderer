using UnityEngine;

public class TimeOfDay : MonoBehaviour
{
    public float speed = 600f;
    public Vector3 northPoleVector;
    float secondsInDay;

    void Start()
    {
        secondsInDay = 24f * 60f * 60f;
    }

    void Update()
    {
        Vector3 curentDirection = transform.rotation * new Vector3(0f, 0f, 1f);
        float da = 360f * Time.deltaTime / secondsInDay;
        curentDirection = Vectors.RotateAround(-speed * da, curentDirection, northPoleVector);
        transform.rotation = Quaternion.LookRotation(curentDirection);
    }
}
