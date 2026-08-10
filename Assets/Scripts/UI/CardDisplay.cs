using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class CardDisplay : MonoBehaviour,IPointerEnterHandler, IPointerExitHandler
{
    public GameObject front;
    public GameObject back;
    public GameObject member;

    [Header("常态效果对象（不限制卡牌状态，满足条件即显示）")]
    public GameObject isActiveIcon;
    [FormerlySerializedAs("isChoosedIcon")]
    public GameObject isChoosingIcon;

    [Header("场地效果对象（仅在 Field 状态显示）")]
    public GameObject isSilenceIcon;
    public GameObject holyshieldIcon;
    public GameObject shadowIcon;

    public TMP_Text cardName;
    public TMP_Text cost;
    public TMP_Text attack;
    public TMP_Text health;
    public TMP_Text description;
    public SpriteRenderer sr;

    private CardController controller;
    private CardData cardData;
    private SortingGroup group;
    private int originalSortingOrder;
    private CardState originalState;
    private bool isSelected;
    private const int HoverSortingOffset = 50;
    public Quaternion InitialLocalRotation { get; private set; }
    public void SetCard(CardController cardController)
    {
        controller = cardController;
        cardData = cardController.cardData;
        group=GetComponent<SortingGroup>();
        InitialLocalRotation = transform.localRotation;
        cardName.text = cardData.name;
        cost.text = cardData.cost.ToString();
        if(cardData.cardType== CardType.Minion)
        {
            member.SetActive(true);
            attack.text = Mathf.Max(0, cardData.attack).ToString();
            health.text = cardData.health.ToString();
        }
        else
        {
            member.SetActive(false);
        }
        description.text = string.IsNullOrEmpty(cardData.effectDescription)
            ? string.Empty
            : cardData.effectDescription.Replace("\\n", "\n");
        sr.sprite = cardData.image;
        SetSelected(false);
        RefreshStateVisuals();
    }
    public void UpdateCard()
    {
        if (member.activeSelf)
        {
            attack.text = Mathf.Max(0, controller.atk).ToString();
            health.text = controller.health.ToString();
        }
        RefreshStateVisuals();
    }
    public void ShowBack(bool option=true)
    {
        front.SetActive(!option);
        back.SetActive(option);
        RefreshStateVisuals();
    }

    public void SetSelected(bool value)
    {
        isSelected = value;
        RefreshStateVisuals();
    }

    private void LateUpdate()
    {
        RefreshStateVisuals();
    }

    private void RefreshStateVisuals()
    {
        if (controller == null)
        {
            return;
        }

        if (isSilenceIcon != null)
        {
            isSilenceIcon.SetActive(front.activeSelf && controller.state == CardState.Field && controller.isSilence);
        }

        if (holyshieldIcon != null)
        {
            holyshieldIcon.SetActive(front.activeSelf && controller.state == CardState.Field && controller.holyShieldCount > 0);
        }

        if (shadowIcon != null)
        {
            shadowIcon.SetActive(front.activeSelf && controller.state == CardState.Field && controller.isStealth);
        }

        TargetManager targetManager = GM.Ins != null ? GM.Ins.BM.TM : null;
        bool showChoosingIcon =
            front.activeSelf
            && (isSelected
                || (targetManager != null && targetManager.IsSelectableTarget(controller))
                || (targetManager != null && targetManager.IsSelectedTarget(controller)));

        if (isChoosingIcon != null)
        {
            isChoosingIcon.SetActive(showChoosingIcon);
        }

        if (isActiveIcon == null)
        {
            return;
        }

        BattleManager battleManager = GM.Ins != null ? GM.Ins.BM : null;
        if (showChoosingIcon)
        {
            isActiveIcon.SetActive(false);
            return;
        }

        bool showActiveIcon = false;
        if (front.activeSelf && controller.player != null && battleManager != null)
        {
            if (controller.state == CardState.Field)
            {
                showActiveIcon =
                    controller.player.isInTurn
                    && (battleManager.CanUseFieldCast(controller) || controller.canAttack);
            }
            else if (controller.state == CardState.Hand)
            {
                showActiveIcon = battleManager.CanShowHandCardAction(controller);
            }
        }

        isActiveIcon.SetActive(showActiveIcon);
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (controller.state == CardState.Hand || controller.state == CardState.Field)
        {
            originalState=controller.state;
            originalSortingOrder = group.sortingOrder;
            Vector3 targetScale = new Vector3(1.2f, 1.2f, 1.2f);
            AnimeManager.Scale(transform, "CardHover", targetScale, 0.3f, useDebugLog: false);
            group.sortingOrder = originalSortingOrder + HoverSortingOffset;
        }
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        if (controller == null)
        {
            return;
        }

        if (controller.state != CardState.Hand && controller.state != CardState.Field)
        {
            RestorePointerExitTransform(GetFallbackScaleForCurrentState(), Quaternion.identity);
            return;
        }

        RestorePointerExitTransform(Vector3.one, transform.localRotation);
        if (group != null && group.sortingOrder == originalSortingOrder + HoverSortingOffset)
        {
            group.sortingOrder = originalSortingOrder;
        }
    }

    private void RestorePointerExitTransform(Vector3 targetScale, Quaternion targetRotation)
    {
        AnimeManager.Scale(transform, "CardHoverExit", targetScale, 0.3f, useDebugLog: false);
        AnimeManager.LocalRotation(transform, "CardHoverExit", targetRotation, 0.3f);
    }

    private Vector3 GetFallbackScaleForCurrentState()
    {
        BattleManager battleManager = GM.Ins != null ? GM.Ins.BM : null;
        if (battleManager != null && battleManager.IsCardInPlayQueue(controller))
        {
            return Vector3.one * battleManager.GetQueuedCardDisplayScale();
        }

        TargetManager targetManager = battleManager != null ? battleManager.TM : null;
        if (targetManager != null && controller != null && controller.state == CardState.Hanging)
        {
            return targetManager.HangingScale;
        }

        return controller != null && controller.state == CardState.Hanging
            ? Vector3.one * 1.5f
            : Vector3.one;
    }
}
