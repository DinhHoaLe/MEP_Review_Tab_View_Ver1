using System;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class ViewDragRequest
    {
        internal const string Format = "MEP.ViewTabs.OpenView.Swap.v1";
        internal readonly object DocumentKey;
        internal readonly object SourcePaneKey;
        internal readonly long ViewId;
        internal IntPtr FloatingWindow;
        internal int GrabOffsetX,GrabOffsetY;
        internal ViewDragRequest(object documentKey, object sourcePaneKey, long viewId)
        {
            DocumentKey = documentKey;
            SourcePaneKey = sourcePaneKey;
            ViewId = viewId;
        }
        internal bool CanDrop(object documentKey, object targetPaneKey) =>
            DocumentKey != null && SourcePaneKey != null && targetPaneKey != null && ViewId > 0 &&
            ReferenceEquals(DocumentKey, documentKey) && !ReferenceEquals(SourcePaneKey, targetPaneKey);
    }
}
