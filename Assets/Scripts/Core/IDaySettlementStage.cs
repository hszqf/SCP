using Core;

public interface IDaySettlementStage
{
    string Name { get; }
    void Execute(GameController gc, GameState state, DayEndResult result);
}
