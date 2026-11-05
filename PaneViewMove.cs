using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Threading;
using Xceed.Wpf.AvalonDock.Layout;

namespace MEP_Review_Tab_View_Ver1
{
    // Move the existing UI documents, never close/reopen Revit views. Keep both
    // WT panes populated and restore their exact collections if activation fails.
    internal sealed class PaneViewMove
    {
        private readonly ILayoutContainer source;
        private readonly ILayoutContainer other;
        private readonly LayoutContent[] originalSource;
        private readonly LayoutContent[] originalOther;
        private readonly LayoutContent sourceSelected;
        private readonly LayoutContent otherSelected;
        private bool restored;
        private sealed class Placement
        {
            private readonly double left,top,width,height;
            private readonly bool maximized;
            internal Placement(LayoutDocument document)
            {left=document.FloatingLeft;top=document.FloatingTop;width=document.FloatingWidth;height=document.FloatingHeight;maximized=document.IsMaximized;}
            internal void Set(LayoutDocument document)
            {document.FloatingLeft=left;document.FloatingTop=top;document.FloatingWidth=width;document.FloatingHeight=height;document.IsMaximized=maximized;}
        }
        private readonly Dictionary<LayoutDocument,Placement> placements=new Dictionary<LayoutDocument,Placement>();

        private PaneViewMove(ILayoutContainer source, ILayoutContainer other)
        {
            this.source = source;
            this.other = other;
            originalSource = source.Children.OfType<LayoutContent>().ToArray();
            originalOther = ReferenceEquals(source, other) ? null : other.Children.OfType<LayoutContent>().ToArray();
            sourceSelected = Selected(source);
            otherSelected = Selected(other);
            foreach(var document in originalSource.Concat(originalOther ?? Array.Empty<LayoutContent>()).OfType<LayoutDocument>().Distinct())
                placements[document]=new Placement(document);
        }

        private static bool Supported(ILayoutContainer container)=>container is LayoutDocumentPane || container is LayoutDocumentFloatingWindow;
        private static LayoutContent Selected(ILayoutContainer container)=>container is LayoutDocumentPane pane ? pane.SelectedContent : (container as LayoutDocumentFloatingWindow)?.RootDocument;
        private static void Remove(ILayoutContainer container,LayoutContent content)
        {
            if(container is LayoutDocumentPane pane)pane.Children.Remove(content);
            else
            {
                // Keep RootDocument populated until its replacement is ready.
                // Revit closes its native frame synchronously when it is empty;
                // disabling Dispatcher processing cannot prevent that callback.
                content.Parent=null;
            }
        }
        private static void Insert(ILayoutContainer container,int index,LayoutContent content)
        {
            if(container is LayoutDocumentPane pane)pane.Children.Insert(index,content);
            else ((LayoutDocumentFloatingWindow)container).RootDocument=(LayoutDocument)content;
        }
        private static void Clear(ILayoutContainer container)
        {
            if(container is LayoutDocumentPane pane)pane.Children.Clear();
            else
            {
                var frame=(LayoutDocumentFloatingWindow)container;
                var document=frame.RootDocument;
                if(document!=null)document.Parent=null;
            }
        }
        internal static PaneViewMove Apply(ILayoutContainer source, LayoutDocument target)
        {
            var other=target?.Parent;
            if (!Supported(source) || target == null || !Supported(other))
                throw new InvalidOperationException("Không tìm được ô chứa view đã chọn.");
            if (source.Root == null || !ReferenceEquals(source.Root, other.Root))
                throw new InvalidOperationException("Hai view không thuộc cùng bố cục cửa sổ Revit.");
            var move = new PaneViewMove(source, other);
            try
            {
                using (Dispatcher.CurrentDispatcher.DisableProcessing())
                {
                    if (!ReferenceEquals(source, other))
                    {
                        var current = Selected(source) as LayoutDocument;
                        if (current == null)
                            throw new InvalidOperationException("Ô nguồn không còn view đang hiển thị.");
                        int sourceIndex = source.Children.ToList().IndexOf(current);
                        int otherIndex = other.Children.ToList().IndexOf(target);
                        Remove(source,current);
                        Remove(other,target);
                        Insert(source,sourceIndex,target);
                        Insert(other,otherIndex,current);
                        // Replacing the second floating root can detach its
                        // previous document, already assigned to the first slot.
                        target.Parent=source;
                        current.Parent=other;
                        if(source is LayoutDocumentFloatingWindow)move.placements[current].Set(target);
                        if(other is LayoutDocumentFloatingWindow)move.placements[target].Set(current);
                        var selectedInOther = ReferenceEquals(move.otherSelected, target) ? current : move.otherSelected;
                        if (selectedInOther != null) SelectAgain(other,selectedInOther);
                    }
                    SelectAgain(source,target);
                }
                return move;
            }
            catch
            {
                move.Restore();
                throw;
            }
        }

        private static void SelectAgain(ILayoutContainer container,LayoutContent content)
        {
            // A moved document can retain IsSelected=true from its old parent.
            // Force an actual selection notification so both content presenters
            // release the old native drawing HWND and bind their new document.
            if(container is LayoutDocumentPane pane)
            {
                content.IsSelected=false;
                pane.SelectedContentIndex=-1;
                pane.SelectedContentIndex=pane.Children.IndexOf(content);
            }
            content.IsSelected=true;
        }
        internal void ActivateBoth(LayoutDocument target)
        {
            FloatingDocumentPresenter.Refresh(source.Root?.Manager);
            source.Root?.Manager?.UpdateLayout();
            var passive=!ReferenceEquals(source,other) ? Selected(other) as LayoutDocument : null;
            if(passive!=null && !ReferenceEquals(passive,target))
            {
                ActivateNative(passive);
            }
            ActivateNative(target);
            source.Root?.Manager?.UpdateLayout();
        }
        private static void ActivateNative(LayoutDocument document)
        {
            document.IsActive=false;
            document.IsActive=true;
            document.ActivateFromTabGroupDropDownList();
            // Revit's native drawing HWND is not the WPF document content.
            // Its floating handler performs the native transfer; changing
            // RootDocument alone leaves the drawing in the main MDI window.
            // This is the same public notification raised by AvalonDock's
            // LayoutDocumentFloatingWindowControl after a native tab drag.
            if(document.Parent is LayoutDocumentFloatingWindow)
                document.OnFloatingWindowDraggedOut(EventArgs.Empty);
        }

        internal void Restore()
        {
            if (restored) return;
            using (Dispatcher.CurrentDispatcher.DisableProcessing())
            {
                foreach(var entry in placements)entry.Value.Set(entry.Key);
                if (!ReferenceEquals(source, other))
                {
                    Clear(source);
                    Clear(other);
                    foreach (var document in originalSource) Insert(source,source.ChildrenCount,document);
                    foreach (var document in originalOther) Insert(other,other.ChildrenCount,document);
                    foreach (var document in originalSource) document.Parent=source;
                    foreach (var document in originalOther) document.Parent=other;
                }
                if (sourceSelected != null) SelectAgain(source,sourceSelected);
                if (otherSelected != null) SelectAgain(other,otherSelected);
                FloatingDocumentPresenter.Refresh(source.Root?.Manager);
                restored = true;
            }
        }
    }
}
