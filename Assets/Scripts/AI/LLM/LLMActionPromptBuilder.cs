using System;
using System.Collections.Generic;
using System.Text;

[Serializable]
public sealed class LLMTargetOption
{
    public int id;
    public string kind = string.Empty;
    public string name = string.Empty;
    public string owner = string.Empty;
    public int attack;
    public int health;
}

[Serializable]
public sealed class LLMActionOption
{
    public int index;
    public string type = string.Empty;
    public int source_card_id;
    public string card_type = string.Empty;
    public string card_name = string.Empty;
    public int cost;
    public int attack;
    public int health;
    public string[] passives = Array.Empty<string>();
    public string[] effect_types = Array.Empty<string>();
    public string effect_description = string.Empty;
    public LLMTargetOption[] targets = Array.Empty<LLMTargetOption>();
}

public static class LLMActionPromptBuilder
{
    public static LLMStrategyPrompt BuildPrompt(
        BattleStateSnapshot state,
        IReadOnlyList<SimulatedAction> legalActions)
    {
        PlayerStateSnapshot current = state != null ? state.GetPlayer(state.CurrentPlayerIndex) : null;
        PlayerStateSnapshot opponent = FindOpponent(state);

        int actionCount = legalActions != null ? legalActions.Count : 0;
        LLMActionOption[] options = new LLMActionOption[actionCount];
        for (int index = 0; index < actionCount; index++)
        {
            options[index] = BuildAction(state, legalActions[index], index);
        }

        return new LLMStrategyPrompt
        {
            current_player_health = current != null ? current.Health : 0,
            opponent_health = opponent != null ? opponent.Health : 0,
            current_player_cost = current != null ? current.Cost : 0,
            current_player_field_count = current != null ? current.Field.Count : 0,
            opponent_field_count = opponent != null ? opponent.Field.Count : 0,
            legal_actions = options,
        };
    }

    public static LLMActionOption BuildAction(BattleStateSnapshot state, SimulatedAction action, int index)
    {
        LLMActionOption option = new()
        {
            index = index,
            type = action != null ? action.Type.ToString() : string.Empty,
            source_card_id = action != null ? action.SourceCardId : 0,
        };

        if (action == null)
        {
            return option;
        }

        CardStateSnapshot source = state != null && action.SourceCardId != 0
            ? state.FindCard(action.SourceCardId)
            : null;
        option.type = ResolveLlmType(action, source);
        option.targets = BuildTargets(state, action);

        if (source == null)
        {
            return option;
        }

        CardData data = source.Data;
        option.card_type = data != null ? data.cardType.ToString() : string.Empty;
        option.card_name = data != null && data.name != null ? data.name : string.Empty;
        option.cost = source.Cost;
        option.attack = source.Attack;
        option.health = source.Health;
        option.passives = CollectPassives(data);
        option.effect_types = CollectEffectTypes(action, data);
        option.effect_description = data != null && data.effectDescription != null
            ? data.effectDescription
            : string.Empty;
        return option;
    }

    public static string FormatAction(BattleStateSnapshot state, SimulatedAction action)
    {
        if (action == null)
        {
            return "none";
        }

        if (state == null)
        {
            return action.ToString();
        }

        LLMActionOption option = BuildAction(state, action, 0);
        StringBuilder builder = new();
        builder.Append(option.type);
        if (!string.IsNullOrEmpty(option.card_name))
        {
            builder.Append(' ').Append(option.card_name);
        }
        else if (!string.IsNullOrEmpty(option.card_type))
        {
            builder.Append(' ').Append(option.card_type);
        }

        if (option.source_card_id != 0)
        {
            builder.Append(" source=").Append(option.source_card_id);
        }

        if (!string.IsNullOrEmpty(option.card_name) || option.cost != 0 || option.attack != 0 || option.health != 0)
        {
            builder.Append(" [");
            if (!string.IsNullOrEmpty(option.card_type))
            {
                builder.Append(option.card_type).Append(' ');
            }
            builder.Append("cost=").Append(option.cost)
                .Append(' ').Append(option.attack).Append('/').Append(option.health);
            if (option.passives != null && option.passives.Length > 0)
            {
                builder.Append(' ').Append(string.Join(",", option.passives));
            }
            builder.Append(']');
        }

        if (option.targets != null && option.targets.Length > 0)
        {
            builder.Append(" targets=");
            for (int i = 0; i < option.targets.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                LLMTargetOption target = option.targets[i];
                builder.Append(target.owner)
                    .Append(' ')
                    .Append(string.IsNullOrEmpty(target.name) ? target.kind : target.name)
                    .Append(' ')
                    .Append(target.attack)
                    .Append('/')
                    .Append(target.health);
            }
        }

        return builder.ToString();
    }

    public static string ResolveLlmType(SimulatedAction action, CardStateSnapshot source)
    {
        if (action == null)
        {
            return string.Empty;
        }

        if (action.Type != SimulatedActionType.PlayHandCard)
        {
            return action.Type.ToString();
        }

        if (source == null || source.Data == null)
        {
            return SimulatedActionType.PlayHandCard.ToString();
        }

        if (source.Data.cardType == CardType.Minion)
        {
            return "PlayMinion";
        }

        if (source.Data.cardType == CardType.SPELL)
        {
            return "PlaySpell";
        }

        return SimulatedActionType.PlayHandCard.ToString();
    }

    private static LLMTargetOption[] BuildTargets(BattleStateSnapshot state, SimulatedAction action)
    {
        if (action.Targets == null || action.Targets.Count == 0)
        {
            return Array.Empty<LLMTargetOption>();
        }

        LLMTargetOption[] targets = new LLMTargetOption[action.Targets.Count];
        for (int index = 0; index < action.Targets.Count; index++)
        {
            targets[index] = BuildTarget(state, action.Targets[index]);
        }

        return targets;
    }

    private static LLMTargetOption BuildTarget(BattleStateSnapshot state, SimulatedTarget target)
    {
        LLMTargetOption option = new()
        {
            id = target != null ? target.Id : 0,
            kind = target != null ? target.Kind.ToString() : string.Empty,
        };

        if (state == null || target == null)
        {
            return option;
        }

        int currentPlayer = state.CurrentPlayerIndex;
        if (target.Kind == SimulatedTargetKind.Player)
        {
            PlayerStateSnapshot player = state.GetPlayer(target.Id);
            bool isSelf = player != null && player.PlayerIndex == currentPlayer;
            option.owner = player == null ? string.Empty : isSelf ? "self" : "enemy";
            option.name = player == null ? string.Empty : isSelf ? "己方英雄" : "敌方英雄";
            option.attack = 0;
            option.health = player != null ? player.Health : 0;
            return option;
        }

        CardStateSnapshot card = state.FindCard(target.Id);
        if (card == null)
        {
            return option;
        }

        option.owner = card.OwnerIndex == currentPlayer ? "self" : "enemy";
        option.name = card.Data != null && card.Data.name != null ? card.Data.name : string.Empty;
        option.attack = card.Attack;
        option.health = card.Health;
        return option;
    }

    private static string[] CollectPassives(CardData data)
    {
        if (data == null || data.passiveTypes == null || data.passiveTypes.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> passives = new();
        foreach (PassiveType passive in data.passiveTypes)
        {
            if (passive == PassiveType.None)
            {
                continue;
            }

            string name = passive.ToString();
            if (!passives.Contains(name))
            {
                passives.Add(name);
            }
        }

        return passives.Count == 0 ? Array.Empty<string>() : passives.ToArray();
    }

    private static string[] CollectEffectTypes(SimulatedAction action, CardData data)
    {
        if (action.Type == SimulatedActionType.AttackMinion
            || action.Type == SimulatedActionType.AttackPlayer
            || action.Type == SimulatedActionType.EndTurn
            || data == null
            || data.effects == null)
        {
            return Array.Empty<string>();
        }

        bool includeAll = action.Type == SimulatedActionType.PlayHandCard && data.cardType == CardType.SPELL;
        TriggerType requiredTrigger = TriggerType.None;
        if (action.Type == SimulatedActionType.PlayHandCard && data.cardType == CardType.Minion)
        {
            requiredTrigger = TriggerType.Enter;
        }
        else if (action.Type == SimulatedActionType.UseFieldCast)
        {
            requiredTrigger = TriggerType.Cast;
        }
        else if (!includeAll)
        {
            return Array.Empty<string>();
        }

        List<string> effectTypes = new();
        foreach (CardEffectData effect in data.effects)
        {
            if (effect == null || effect.effectType == EffectType.None)
            {
                continue;
            }

            if (!includeAll && effect.triggerType != requiredTrigger)
            {
                continue;
            }

            string name = effect.effectType.ToString();
            if (!effectTypes.Contains(name))
            {
                effectTypes.Add(name);
            }
        }

        return effectTypes.Count == 0 ? Array.Empty<string>() : effectTypes.ToArray();
    }

    private static PlayerStateSnapshot FindOpponent(BattleStateSnapshot state)
    {
        if (state == null || state.Players == null)
        {
            return null;
        }

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player != null && player.PlayerIndex != state.CurrentPlayerIndex)
            {
                return player;
            }
        }

        return null;
    }
}
