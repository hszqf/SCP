using Core;

public interface IDayStage
{
    string Name { get; }
    void Execute(GameController gc, GameState state, DayEndResult result);
}
