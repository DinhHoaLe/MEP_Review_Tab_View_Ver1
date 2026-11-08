using System.Collections.Generic;
using System.Linq;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class ProjectPaneViewSelector
    {
        internal static long? Choose(IEnumerable<(long Id,bool Open)> views,ISet<long> displayedElsewhere,long preferred)
        {
            return views.Where(view=>!displayedElsewhere.Contains(view.Id))
                .OrderByDescending(view=>view.Id==preferred).ThenByDescending(view=>view.Open)
                .Select(view=>(long?)view.Id).FirstOrDefault();
        }
    }
}
