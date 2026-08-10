using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// 单一效果类型的完整定义：编辑器元数据、运行时执行、AI 模拟、目标选择与评分、启发式评分。
/// </summary>
public interface ICardEffectDefinition
{
    EffectType EffectType { get; }
    string Label { get; }
    IReadOnlyList<EffectValueParameter> Parameters { get; }
    bool IsTargeted { get; }
    TargetSelectionZone SelectionZone { get; }
    int SuggestedArrayLength { get; }

    bool RequiresTargetSelection(CardEffectData effect);

    int GetSelectionCount(CardEffectData effect);
    int GetRuntimeSelectionCount(CardController source, CardEffectData effect);
    int GetSimulationSelectionCount(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect);

    List<UnityEngine.Object> GetRuntimeCandidates(CardController source, CardEffectData effect);
    List<SimulatedTarget> GetSimulationCandidates(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect);

    void ApplyRuntime(CardEffectContext context, CardController source, CardEffectData effect, List<UnityEngine.Object> targets, Action onComplete);
    void Simulate(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, List<SimulatedTarget> targets, Random random);

    double ScoreSimulationTarget(BattleStateSnapshot state, CardStateSnapshot source, CardEffectData effect, SimulatedTarget target);
    double ScoreRuntimeTarget(CardController source, CardEffectData effect, UnityEngine.Object target);
    double HeuristicScore(CardEffectData effect);
}
