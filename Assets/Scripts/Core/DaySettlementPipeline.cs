using System.Collections.Generic;
using System.Linq;
using Core;

public sealed class DaySettlementPipeline
{
    private readonly List<IDayStage> _stages;

    public DaySettlementPipeline(IEnumerable<IDayStage> stages)
    {
        _stages = stages?.ToList() ?? new List<IDayStage>();
    }

    public DayEndResult Run(GameController gc)
    {
        var result = new DayEndResult();
        var state = gc.State;
        for (int i = 0; i < _stages.Count; i++)
            _stages[i].Execute(gc, state, result);
        return result;
    }
}
