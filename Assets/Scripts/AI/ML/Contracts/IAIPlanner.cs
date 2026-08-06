public interface IAIPlanner
{
    MCTSResult Search(BattleStateSnapshot rootState);
}
