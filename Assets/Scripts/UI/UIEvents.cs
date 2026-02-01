using System;

public static class UIEvents
{
    public static event Action AgentsChanged;

    public static void RaiseAgentsChanged()
    {
        AgentsChanged?.Invoke();
    }
}
