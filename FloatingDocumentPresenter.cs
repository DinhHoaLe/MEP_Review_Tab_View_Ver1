using System.Linq;
using Xceed.Wpf.AvalonDock;
using Xceed.Wpf.AvalonDock.Controls;
using Xceed.Wpf.AvalonDock.Layout;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class FloatingDocumentPresenter
    {
        internal static void Refresh(DockingManager manager)
        {
            if(manager==null)return;
            var changed=manager.FloatingWindows.OfType<LayoutDocumentFloatingWindowControl>()
                .Where(NeedsRefresh).ToList();
            // Release all old presenters before attaching replacements. An MFC
            // drawing host can belong to only one visual tree at a time.
            foreach(var window in changed)SetPresenter(window,null);
            foreach(var window in changed)RefreshControl(window);
        }
        private static bool NeedsRefresh(LayoutDocumentFloatingWindowControl window)
        {
            var model=window.Model as LayoutDocumentFloatingWindow;
            return model?.RootDocument!=null &&
                !ReferenceEquals(PresentedDocument(window),model.RootDocument);
        }
        internal static LayoutDocument PresentedDocument(LayoutDocumentFloatingWindowControl window)
        {
            object content=window.Content;
            for(int depth=0;depth<8 && content!=null;++depth)
            {
                if(content is LayoutDocumentControl presenter)return presenter.Model as LayoutDocument;
                if(content is System.Windows.Controls.ContentControl wrapper)content=wrapper.Content;
                else content=content.GetType().GetProperty("Content")?.GetValue(content);
            }
            return null;
        }
        internal static void RefreshControl(LayoutDocumentFloatingWindowControl window)
        {
            if(!NeedsRefresh(window))return;
            var model=(LayoutDocumentFloatingWindow)window.Model;
            SetPresenter(window,new LayoutDocumentControl {Model=model.RootDocument});
        }
        private static void SetPresenter(LayoutDocumentFloatingWindowControl window,object presenter)
        {
            // Revit's AvalonDock wraps floating content in its own HwndHost.
            // Replace the inner visual while preserving that host and HWND.
            var host=window.Content;
            var property=host?.GetType().GetProperty("Content");
            if(host is System.Windows.Interop.HwndHost && property?.CanWrite==true)
                property.SetValue(host,presenter);
            else window.Content=presenter;
        }
    }
}
