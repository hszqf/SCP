using System.Collections.Generic;
using System.Linq;
using Core;

public sealed class DayPipeline
{
    private readonly List<IDayStage> _stages;

    public DayPipeline(IEnumerable<IDayStage> stages)
    {
        _stages = stages?.ToList() ?? new List<IDayStage>();
    }

    public DayPipelineResult Run(GameController gc)
    {
        var result = new DayPipelineResult();
        var state = gc.State;
        for (int i = 0; i < _stages.Count; i++)
            _stages[i].Execute(gc, state, result);
        return result;
    }
}
