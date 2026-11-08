using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace MEP_Review_Tab_View_Ver1
{
    // Create the event once during startup. WPF layout/hover callbacks can then
    // create controls immediately and enqueue API work without waiting for Idling.
    internal sealed class RevitUiDispatcher : IExternalEventHandler, IDisposable
    {
        private readonly ExternalEvent apiEvent;
        private readonly Dictionary<object, Action<UIApplication>> pending = new Dictionary<object, Action<UIApplication>>();
        private bool disposed;
        private bool executing;
        private readonly System.Windows.Threading.DispatcherTimer wake;
        private DateTime wakeUntil;
        private readonly IntPtr mainWindow;
        private readonly DeferredUiActionQueue deferred=new DeferredUiActionQueue(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        internal RevitUiDispatcher()
        {
            apiEvent = ExternalEvent.Create(this);
            mainWindow=new System.Windows.Interop.WindowInteropHelper(UIFramework.MainWindow.getMainWnd()).Handle;
            wake=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
                {Interval=TimeSpan.FromMilliseconds(40)};
            wake.Tick+=Wake;
        }
        internal bool HasPending=>!disposed && pending.Count>0;
        private void Wake(object sender,EventArgs args)
        {
            if(!HasPending || DateTime.UtcNow>wakeUntil){wake.Stop();return;}
            // WM_NULL only wakes Revit's native message loop; it does not
            // simulate a key, change focus, or execute API code from a timer.
            if(mainWindow!=IntPtr.Zero)BrowserNativeInterop.PostMessage(mainWindow,0,IntPtr.Zero,IntPtr.Zero);
        }
        internal ExternalEventRequest Post(object key, Action<UIApplication> action)
        {
            if (disposed) return ExternalEventRequest.Denied;
            deferred.Cancel(key);
            pending[key] = action;
            var result = apiEvent.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending) pending.Remove(key);
            else
            {
                wakeUntil=DateTime.UtcNow.AddSeconds(15);
                wake.Start();Wake(null,EventArgs.Empty);
            }
            return result;
        }
        internal void PostAfterUiTurn(object key,Action<UIApplication> action)
        {
            if(!disposed)deferred.Post(key,()=>Post(key,action));
        }
        internal void Cancel(object key) {pending.Remove(key);deferred.Cancel(key);if(!HasPending)wake.Stop();}
        public string GetName() => "MEP View Tabs UI actions";
        public void Execute(UIApplication app)=>Drain(app);
        internal void Drain(UIApplication app)
        {
            if(disposed || executing || pending.Count==0)return;
            executing=true;
            var work = new List<Action<UIApplication>>(pending.Values);
            pending.Clear();
            wake.Stop();
            try
            {
            foreach (var action in work)
            {
                if (disposed) break;
                try { action(app); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
            }
            finally{executing=false;}
        }
        public void Dispose()
        {
            disposed = true;
            wake.Stop();wake.Tick-=Wake;
            deferred.Dispose();
            pending.Clear();
            apiEvent.Dispose();
        }
    }
}
