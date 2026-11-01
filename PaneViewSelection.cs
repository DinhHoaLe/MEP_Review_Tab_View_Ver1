using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class PaneViewCandidate
    {
        internal long Id;
        internal string[] Aliases;
        internal string[] QualifiedAliases;
    }

    // A WT tile has its own selected LayoutDocument, independent of Revit's
    // globally active view. Only read WPF/model metadata here, never Revit API.
    internal sealed class PaneViewSelection
    {
        internal string Title;
        internal string ToolTip;
        internal bool Matches(IEnumerable<string> aliases) => aliases != null && aliases.Any(alias =>
            !string.IsNullOrWhiteSpace(alias) && new[] { Title,ToolTip }.Any(caption =>
                !string.IsNullOrWhiteSpace(caption) && string.Equals(alias.Trim(),caption.Trim(),StringComparison.OrdinalIgnoreCase)));

        internal static PaneViewSelection Read(FrameworkElement pane)
        {
            object selected = (pane as TabControl)?.SelectedItem;
            if (selected == null)
            {
                object model = Property(pane, "Model");
                selected = Property(model, "SelectedContent");
            }
            return ReadDocument(selected);
        }

        internal static PaneViewSelection ReadDocument(object selected)
        {
            return new PaneViewSelection
            {
                Title = Property(selected, "Title") as string ?? selected as string,
                ToolTip = Property(selected, "ToolTip") as string
            };
        }

        private static object Property(object target, string name) =>
            target?.GetType().GetProperty(name)?.GetValue(target);

        internal long Resolve(IEnumerable<PaneViewCandidate> candidates, long fallbackId)
        {
            var available = candidates.ToList();
            var qualified=available.Where(candidate=>Matches(candidate.QualifiedAliases)).Select(candidate=>candidate.Id).Distinct().ToList();
            if(qualified.Count==1)return qualified[0];
            if(qualified.Count>1)return -1;
            foreach (string caption in new[] { Title, ToolTip })
            {
                if (string.IsNullOrWhiteSpace(caption)) continue;
                var matches = available.Where(candidate => candidate.Aliases.Any(alias =>
                    !string.IsNullOrWhiteSpace(alias) &&
                    string.Equals(alias.Trim(), caption.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .Select(candidate => candidate.Id).Distinct().ToList();
                if (matches.Count == 1) return matches[0];
            }
            // Never use the global active view in a multi-tile layout, and do
            // not claim a match for an unknown/ambiguous native document title.
            return string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(ToolTip) ? fallbackId : -1;
        }
    }
}

