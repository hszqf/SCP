// Canvas-maintained file: Core/Sim (clean)
// Source: Assets/Scripts/Core/Sim.cs
// <EXPORT_BLOCK>

namespace Core
{
    public static class Sim
    {
        // Advance day only: increment day counter and perform lightweight per-day initializations
        // (Full per-day effects are handled by Settlement pipeline + DayStart pipeline)
        public static void AdvanceDay_Only(GameState s)
        {
            if (s == null) return;
            s.Day += 1;

            if (s.RecruitPool != null)
            {
                s.RecruitPool.day = -1;
                s.RecruitPool.refreshUsedToday = 0;
                s.RecruitPool.candidates?.Clear();
            }
        }
    }
}
// </EXPORT_BLOCK>
