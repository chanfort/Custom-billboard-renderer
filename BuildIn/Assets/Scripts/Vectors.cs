using UnityEngine;

public static class Vectors
{
    public static Vector3 RotateAround(float rotAngle, Vector3 original, Vector3 direction)
    {
        return Quaternion.AngleAxis(-rotAngle, direction.normalized) * original;
    }
}
