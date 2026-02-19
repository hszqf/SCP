using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// Minimal event sink used by plan builders / settlement systems to emit DayEvent without coupling to UI.
    /// </summary>
    public interface IDayEventSink
    {
        void Add(DayEvent e);
    }

    /// <summary>
    /// Simple sink that appends to a List{DayEvent}.
    /// </summary>
    public sealed class ListDayEventSink : IDayEventSink
    {
        private readonly List<DayEvent> _events;

        public ListDayEventSink(List<DayEvent> events)
        {
            _events = events;
        }

        public void Add(DayEvent e)
        {
            _events?.Add(e);
        }
    }
}
