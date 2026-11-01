using System;
using System.Collections.Generic;
using System.Linq;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class ProjectViewIdentity
    {
        internal object ProjectKey;
        internal long ViewId;
        internal string[] Aliases;
        internal string[] QualifiedAliases;
        internal static ProjectViewIdentity Resolve(PaneViewSelection selection, IEnumerable<ProjectViewIdentity> source)
        {
            var values = source.ToList();
            var qualified = values.Where(value => selection.Matches(value.QualifiedAliases)).ToList();
            if (qualified.Count == 1) return qualified[0];
            if (qualified.Count > 1) return null;
            var simple = values.Where(value => selection.Matches(value.Aliases)).ToList();
            return simple.Count == 1 ? simple[0] : null;
        }
    }
    internal sealed class ProjectTabItem
    {
        public object Key { get; set; }
        public string Name { get; set; }
        public bool Selected { get; set; }
    }
}
