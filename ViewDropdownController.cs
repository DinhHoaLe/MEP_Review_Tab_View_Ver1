using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MEP_Review_Tab_View_Ver1
{
    // Capture the clicked item before WPF closes the popup. DropDownClosed is
    // not a commit signal: it can run before selection changes, or on Escape.
    internal sealed class ViewDropdownController : IDisposable
    {
        private readonly ComboBox selector;
        private readonly Action<object> commit;
        private readonly Style originalStyle;
        private bool internalSelection;
        private bool cancelling;
        private bool disposed;
        private bool scheduled;
        private object pending;
        private readonly HoverPreviewScheduler hoverPreview;
        internal bool IsRefreshing { get; set; }

        internal ViewDropdownController(ComboBox selector, Action<object> commit,
            Action<object> hover = null, Action hidePreview = null)
        {
            this.selector = selector;
            this.commit = commit;
            hoverPreview = new HoverPreviewScheduler(selector.Dispatcher, hover, hidePreview);
            originalStyle = selector.ItemContainerStyle;
            var style = new Style(typeof(ComboBoxItem), originalStyle ?? selector.TryFindResource(typeof(ComboBoxItem)) as Style);
            style.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ItemMouseDown)));
            style.Setters.Add(new EventSetter(UIElement.MouseEnterEvent, new MouseEventHandler(ItemMouseEnter))
                { HandledEventsToo = true });
            style.Setters.Add(new EventSetter(UIElement.MouseLeaveEvent, new MouseEventHandler(ItemMouseLeave))
                { HandledEventsToo = true });
            selector.ItemContainerStyle = style;
            selector.SelectionChanged += SelectionChanged;
            selector.PreviewKeyDown += KeyDown;
            selector.DropDownClosed += DropDownClosed;
        }

        private void ItemMouseEnter(object sender, MouseEventArgs args)
        {
            if (disposed || IsRefreshing || !(sender is ComboBoxItem item)) return;
            hoverPreview.Enter(item.Content);
        }
        private void ItemMouseLeave(object sender, MouseEventArgs args) => StopPreview();
        private void DropDownClosed(object sender, EventArgs args) => StopPreview();
        private void StopPreview() => hoverPreview.Stop();
        internal void CancelPreview() => StopPreview();

        private void ItemMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (disposed || IsRefreshing || !(sender is ComboBoxItem item) || !item.IsEnabled) return;
            StopPreview();
            object chosen = item.Content;
            internalSelection = true;
            try
            {
                selector.SelectedItem = chosen;
                selector.IsDropDownOpen = false;
            }
            finally { internalSelection = false; }
            args.Handled = true;
            Schedule(chosen);
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (!IsRefreshing && !internalSelection && !cancelling && !selector.IsDropDownOpen &&
                selector.IsKeyboardFocusWithin)
                Schedule(selector.SelectedItem);
        }

        private void KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Enter)
            {
                object chosen = selector.SelectedItem;
                internalSelection = true;
                try { selector.IsDropDownOpen = false; }
                finally { internalSelection = false; }
                Schedule(chosen);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && selector.IsDropDownOpen)
            {
                cancelling = true;
                pending = null;
                selector.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => cancelling = false));
            }
        }

        internal void CommitSelected() => Schedule(selector.SelectedItem);

        private void Schedule(object chosen)
        {
            if (disposed || IsRefreshing || chosen == null) return;
            pending = chosen;
            if (scheduled) return;
            scheduled = true;
            // Return to the Revit message loop with the popup closed first.
            selector.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                scheduled = false;
                object request = pending;
                pending = null;
                if (!disposed && request != null) commit(request);
            }));
        }

        public void Dispose()
        {
            disposed = true;
            StopPreview();
            hoverPreview.Dispose();
            pending = null;
            selector.SelectionChanged -= SelectionChanged;
            selector.PreviewKeyDown -= KeyDown;
            selector.DropDownClosed -= DropDownClosed;
            selector.ItemContainerStyle = originalStyle;
        }
    }
}
