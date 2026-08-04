using UnityEngine;

public class CostomPanel : MonoBehaviour
{
    protected bool isInitialized;

    public virtual void Init()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        BindEvents();
    }

    public virtual void Show()
    {
        gameObject.SetActive(true);
    }

    public virtual void Close()
    {
        gameObject.SetActive(false);
    }

    protected virtual void BindEvents()
    {
    }

    protected virtual void UnbindEvents()
    {
    }

    protected virtual void OnDestroy()
    {
        if (!isInitialized)
        {
            return;
        }

        UnbindEvents();
        isInitialized = false;
    }
}
