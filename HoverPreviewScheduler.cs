using System;
using System.Windows.Threading;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class HoverPreviewScheduler : IDisposable
    {
        private readonly DispatcherTimer timer;
        private readonly Action<object> show;
        private readonly Action hide;
        private object item;
        private bool disposed;
        internal HoverPreviewScheduler(Dispatcher dispatcher, Action<object> show, Action hide)
        {
            this.show = show;
            this.hide = hide;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += Elapsed;
        }
        internal void Enter(object value)
        {
            if (disposed || show == null) return;
            Stop();
            item = value;
            timer.Start();
        }
        internal void Stop()
        {
            if(disposed)return;
            timer.Stop();
            item = null;
            hide?.Invoke();
        }
        private void Elapsed(object sender, EventArgs args)
        {
            timer.Stop();
            if (!disposed && item != null) show?.Invoke(item);
        }
        public void Dispose()=>Dispose(true);
        internal void Dispose(bool hidePreview)
        {
            if(disposed)return;
            disposed = true;
            timer.Stop();
            item=null;
            timer.Tick -= Elapsed;
            if(hidePreview)hide?.Invoke();
        }
    }
}
