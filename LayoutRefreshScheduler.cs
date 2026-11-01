using System;
using System.Windows;
using System.Windows.Threading;

namespace MEP_Review_Tab_View_Ver1
{
    // Run immediately after WPF finishes changing the layout, ahead of its next
    // rendering pass. This path never depends on Revit Idling or mouse movement.
    internal sealed class LayoutRefreshScheduler : IDisposable
    {
        private readonly Action refresh;
        private FrameworkElement root;
        private bool queued;
        private bool disposed;
        internal LayoutRefreshScheduler(Action refresh) { this.refresh = refresh; }
        internal void Hook(FrameworkElement element)
        {
            if (disposed || ReferenceEquals(root, element)) return;
            if (root != null) root.LayoutUpdated -= LayoutChanged;
            root = element;
            if (root != null) root.LayoutUpdated += LayoutChanged;
        }
        private void LayoutChanged(object sender, EventArgs args) => Request();
        internal void Request()
        {
            if (disposed || queued || root == null) return;
            queued = true;
            root.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                queued = false;
                if (!disposed) refresh();
            }));
        }
        public void Dispose()
        {
            disposed = true;
            if (root != null) root.LayoutUpdated -= LayoutChanged;
            root = null;
        }
    }
}
