using System;
using PrimeTween;
using UnityEngine;

public sealed class AnimeSequence
{
    private Sequence sequence;

    private AnimeSequence(Sequence sequence)
    {
        this.sequence = sequence;
    }

    public bool IsAlive => sequence.isAlive;

    public static AnimeSequence Create()
    {
        return new AnimeSequence(Sequence.Create());
    }

    public void Stop()
    {
        if (sequence.isAlive)
        {
            sequence.Stop();
        }
    }

    public void Group(Tween tween)
    {
        sequence.Group(tween);
    }

    public void Chain(Tween tween)
    {
        sequence.Chain(tween);
    }

    public void OnComplete(Action onComplete)
    {
        sequence.OnComplete(onComplete);
    }
}

public static class AnimeManager
{
    public const float FieldRefreshDuration = 0.2f;

    /// <summary>GM/测试用：为 true 时所有动画瞬时完成，但回调仍会同步触发。</summary>
    public static bool Instant;

    public static AnimeSequence CreateSequence()
    {
        return AnimeSequence.Create();
    }

    public static void Delay(float duration, Action onComplete)
    {
        if (Instant)
        {
            onComplete?.Invoke();
            return;
        }

        Tween.Delay(duration, onComplete);
    }

    public static bool ShouldAnimate(Vector3 current, Vector3 target)
    {
        return AnimationLog.ShouldAnimate(current, target);
    }

    public static bool ShouldAnimate(Quaternion current, Quaternion target)
    {
        return AnimationLog.ShouldAnimate(current, target);
    }

    public static void State(string context, string message, bool useDebugLog = false)
    {
        if (useDebugLog)
        {
            AnimationLog.State(context, message);
        }
    }

    public static bool Scale(
        Transform target,
        string context,
        Vector3 targetScale,
        float duration,
        int cycles = 1,
        bool yoyo = false,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localScale, targetScale))
        {
            return false;
        }

        if (Instant)
        {
            target.localScale = targetScale;
            return true;
        }

        LogTween(useDebugLog, target, context, "scale", target.localScale, targetScale);
        Tween.Scale(target, targetScale, duration, cycles: cycles, cycleMode: GetCycleMode(yoyo));
        return true;
    }

    public static bool LocalPosition(Transform target, string context, Vector3 targetPosition, float duration, bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localPosition, targetPosition))
        {
            return false;
        }

        if (Instant)
        {
            target.localPosition = targetPosition;
            return true;
        }

        LogTween(useDebugLog, target, context, "localPosition", target.localPosition, targetPosition);
        Tween.LocalPosition(target, targetPosition, duration);
        return true;
    }

    public static bool LocalRotation(Transform target, string context, Quaternion targetRotation, float duration, bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localRotation, targetRotation))
        {
            return false;
        }

        if (Instant)
        {
            target.localRotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "localRotation", target.localRotation, targetRotation);
        Tween.LocalRotation(target, targetRotation, duration);
        return true;
    }

    public static bool GroupScale(
        AnimeSequence sequence,
        Transform target,
        string context,
        Vector3 targetScale,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localScale, targetScale))
        {
            return false;
        }

        if (Instant)
        {
            target.localScale = targetScale;
            return true;
        }

        LogTween(useDebugLog, target, context, "scale", target.localScale, targetScale);
        Tween tween = Tween.Scale(target, targetScale, duration);
        sequence?.Group(tween);
        return true;
    }

    public static bool GroupLocalPosition(
        AnimeSequence sequence,
        Transform target,
        string context,
        Vector3 targetPosition,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localPosition, targetPosition))
        {
            return false;
        }

        if (Instant)
        {
            target.localPosition = targetPosition;
            return true;
        }

        LogTween(useDebugLog, target, context, "localPosition", target.localPosition, targetPosition);
        Tween tween = Tween.LocalPosition(target, targetPosition, duration);
        sequence?.Group(tween);
        return true;
    }

    public static bool GroupLocalRotation(
        AnimeSequence sequence,
        Transform target,
        string context,
        Quaternion targetRotation,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localRotation, targetRotation))
        {
            return false;
        }

        if (Instant)
        {
            target.localRotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "localRotation", target.localRotation, targetRotation);
        Tween tween = Tween.LocalRotation(target, targetRotation, duration);
        sequence?.Group(tween);
        return true;
    }

    private static bool GroupLocalRotationForced(
        AnimeSequence sequence,
        Transform target,
        string context,
        Quaternion targetRotation,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null)
        {
            return false;
        }

        if (Instant)
        {
            target.localRotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "localRotation", target.localRotation, targetRotation);
        Tween tween = Tween.LocalRotation(target, targetRotation, duration);
        sequence?.Group(tween);
        return true;
    }

    public static bool GroupPosition(
        AnimeSequence sequence,
        Transform target,
        string context,
        Vector3 targetPosition,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.position, targetPosition))
        {
            return false;
        }

        if (Instant)
        {
            target.position = targetPosition;
            return true;
        }

        LogTween(useDebugLog, target, context, "position", target.position, targetPosition);
        Tween tween = Tween.Position(target, targetPosition, duration);
        sequence?.Group(tween);
        return true;
    }

    public static bool GroupRotation(
        AnimeSequence sequence,
        Transform target,
        string context,
        Quaternion targetRotation,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.rotation, targetRotation))
        {
            return false;
        }

        if (Instant)
        {
            target.rotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "rotation", target.rotation, targetRotation);
        Tween tween = Tween.Rotation(target, targetRotation, duration);
        sequence?.Group(tween);
        return true;
    }

    public static bool ChainLocalPosition(
        AnimeSequence sequence,
        Transform target,
        string context,
        Vector3 targetPosition,
        float duration,
        int cycles = 1,
        bool yoyo = false,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localPosition, targetPosition))
        {
            return false;
        }

        if (Instant)
        {
            target.localPosition = targetPosition;
            return true;
        }

        LogTween(useDebugLog, target, context, "localPosition", target.localPosition, targetPosition);
        Tween tween = Tween.LocalPosition(target, targetPosition, duration, cycles: cycles, cycleMode: GetCycleMode(yoyo));
        sequence?.Chain(tween);
        return true;
    }

    public static bool ChainLocalRotation(
        AnimeSequence sequence,
        Transform target,
        string context,
        Quaternion targetRotation,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null || !AnimationLog.ShouldAnimate(target.localRotation, targetRotation))
        {
            return false;
        }

        if (Instant)
        {
            target.localRotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "localRotation", target.localRotation, targetRotation);
        Tween tween = Tween.LocalRotation(target, targetRotation, duration);
        sequence?.Chain(tween);
        return true;
    }

    private static bool ChainLocalRotationForced(
        AnimeSequence sequence,
        Transform target,
        string context,
        Quaternion targetRotation,
        float duration,
        bool useDebugLog = false)
    {
        if (target == null)
        {
            return false;
        }

        if (Instant)
        {
            target.localRotation = targetRotation;
            return true;
        }

        LogTween(useDebugLog, target, context, "localRotation", target.localRotation, targetRotation);
        Tween tween = Tween.LocalRotation(target, targetRotation, duration);
        sequence?.Chain(tween);
        return true;
    }

    public static void PlayAttackAnimation(CardController attacker, Vector3 targetPosition, Action onComplete)
    {
        if (attacker == null)
        {
            onComplete?.Invoke();
            return;
        }

        Transform attackerTransform = attacker.transform;
        Transform parent = attackerTransform.parent;
        Vector3 originalLocalPosition = attackerTransform.localPosition;
        Quaternion originalLocalRotation = attackerTransform.localRotation;
        Vector3 targetLocalPosition = parent != null ? parent.InverseTransformPoint(targetPosition) : targetPosition;
        Vector3 localDirection = targetLocalPosition - originalLocalPosition;

        if (localDirection.sqrMagnitude < 0.0001f)
        {
            onComplete?.Invoke();
            return;
        }

        if (Instant)
        {
            attackerTransform.localPosition = originalLocalPosition;
            attackerTransform.localRotation = originalLocalRotation;
            onComplete?.Invoke();
            return;
        }

        Quaternion targetLocalRotation = GetAttackLocalRotation(originalLocalRotation, localDirection);
        AnimeSequence sequence = CreateSequence();
        bool hasAnimation = false;
        hasAnimation |= GroupLocalRotation(sequence, attackerTransform, "Attack", targetLocalRotation, 0.15f);
        hasAnimation |= ChainLocalPosition(sequence, attackerTransform, "Attack", targetLocalPosition, 0.15f, 2, true);
        hasAnimation |= ChainLocalRotationForced(sequence, attackerTransform, "AttackReturn", originalLocalRotation, 0.15f);

        if (!hasAnimation)
        {
            onComplete?.Invoke();
            return;
        }

        sequence.OnComplete(() =>
        {
            if (attackerTransform != null)
            {
                attackerTransform.localPosition = originalLocalPosition;
                attackerTransform.localRotation = originalLocalRotation;
            }

            onComplete?.Invoke();
        });
    }

    public static void PlayTriggerAnimation(CardController source, Action onComplete)
    {
        if (source == null || source.transform == null)
        {
            onComplete?.Invoke();
            return;
        }

        Transform sourceTransform = source.transform;
        Quaternion restLocalRotation = GetTriggerRestLocalRotation(source);
        Vector3 triggerEuler = restLocalRotation.eulerAngles;
        triggerEuler.z += 15f;
        Quaternion triggerLocalRotation = Quaternion.Euler(triggerEuler);

        if (Instant)
        {
            sourceTransform.localRotation = restLocalRotation;
            onComplete?.Invoke();
            return;
        }

        AnimeSequence sequence = CreateSequence();
        bool hasAnimation = false;
        hasAnimation |= GroupLocalRotation(sequence, sourceTransform, "Trigger", triggerLocalRotation, 0.2f);
        hasAnimation |= ChainLocalRotationForced(sequence, sourceTransform, "TriggerReturn", restLocalRotation, 0.2f);

        if (!hasAnimation)
        {
            sourceTransform.localRotation = restLocalRotation;
            onComplete?.Invoke();
            return;
        }

        sequence.OnComplete(() =>
        {
            if (sourceTransform != null)
            {
                sourceTransform.localRotation = restLocalRotation;
            }

            onComplete?.Invoke();
        });
    }

    private static CycleMode GetCycleMode(bool yoyo)
    {
        return yoyo ? CycleMode.Yoyo : CycleMode.Restart;
    }

    private static void LogTween(bool useDebugLog, Transform target, string context, string property, Vector3 from, Vector3 to)
    {
        if (useDebugLog)
        {
            AnimationLog.Tween(target, context, property, from, to);
        }
    }

    private static void LogTween(bool useDebugLog, Transform target, string context, string property, Quaternion from, Quaternion to)
    {
        if (useDebugLog)
        {
            AnimationLog.Tween(target, context, property, from, to);
        }
    }

    private static Quaternion GetAttackLocalRotation(Quaternion originalLocalRotation, Vector3 localDirection)
    {
        Vector3 targetEuler = originalLocalRotation.eulerAngles;
        targetEuler.z = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg - 90f;
        return Quaternion.Euler(targetEuler);
    }

    private static Quaternion GetTriggerRestLocalRotation(CardController source)
    {
        if (source == null || source.transform == null)
        {
            return Quaternion.identity;
        }

        return source.state == CardState.Field || source.state == CardState.Hanging
            ? Quaternion.identity
            : source.transform.localRotation;
    }
}
