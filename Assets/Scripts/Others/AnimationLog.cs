using UnityEngine;

public static class AnimationLog
{
    private const float VectorEpsilonSqr = 0.0001f;
    private const float RotationEpsilon = 0.01f;

    public static bool ShouldAnimate(Vector3 current, Vector3 target)
    {
        return Vector3.SqrMagnitude(current - target) > VectorEpsilonSqr;
    }

    public static bool ShouldAnimate(Quaternion current, Quaternion target)
    {
        return Quaternion.Angle(current, target) > RotationEpsilon;
    }

    public static void Tween(Transform target, string context, string property, Vector3 from, Vector3 to)
    {
        Debug.Log($"[Animation] {context} {GetTargetName(target)}.{property}: {Format(from)} -> {Format(to)}");
    }

    public static void Tween(Transform target, string context, string property, Quaternion from, Quaternion to)
    {
        Debug.Log($"[Animation] {context} {GetTargetName(target)}.{property}: {Format(from.eulerAngles)} -> {Format(to.eulerAngles)}");
    }

    public static void State(string context, string message)
    {
        Debug.Log($"[Animation] {context} {message}");
    }

    private static string GetTargetName(Transform target)
    {
        return target != null ? target.name : "null";
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }
}
