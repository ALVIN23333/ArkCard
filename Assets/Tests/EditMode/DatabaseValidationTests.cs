using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 重构后全卡库校验：不得出现 Error 级问题（未注册效果、缺失必填参数等）。
/// </summary>
public class DatabaseValidationTests
{
    [Test]
    public void AllCards_ValidateWithoutErrors()
    {
        CardListSO database = Resources.Load<CardListSO>("ArkCardsDatabase");
        Assert.IsNotNull(database, "ArkCardsDatabase.asset must exist under Resources.");
        Assert.IsNotNull(database.cards, "Database must contain a card list.");

        List<string> errors = new();
        for (int i = 0; i < database.cards.Count; i++)
        {
            List<CardValidationMessage> messages = CardValidationService.Validate(database, i);
            foreach (CardValidationMessage message in messages)
            {
                if (message.Severity == CardValidationSeverity.Error)
                {
                    errors.Add($"Card {i}: {message.Message} @ {message.PropertyPath}");
                }
            }
        }

        Assert.IsEmpty(errors, "Database must contain no Error-level validation messages:\n" + string.Join("\n", errors));
    }
}
