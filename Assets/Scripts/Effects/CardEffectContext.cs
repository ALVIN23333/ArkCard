/// <summary>
/// 效果执行上下文：管理取消、提交与回滚语义。
/// </summary>
public class CardEffectContext
{
    private readonly bool forceSelection;

    public bool IsCancelled { get; private set; }
    public bool HasCommittedEffect { get; private set; }
    public bool AllowRollback => !forceSelection && !HasCommittedEffect;

    public CardEffectContext(bool forceSelection)
    {
        this.forceSelection = forceSelection;
    }

    public void Cancel()
    {
        IsCancelled = true;
    }

    public void CommitEffect()
    {
        if (HasCommittedEffect)
        {
            return;
        }

        HasCommittedEffect = true;
        if (GM.Ins != null && GM.Ins.BM != null)
        {
            GM.Ins.BM.TM?.CommitCurrentActionRollback();
        }
    }
}
