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
        internal RevitUiDispatcher() { apiEvent = ExternalEvent.Create(this); }
        internal ExternalEventRequest Post(object key, Action<UIApplication> action)
        {
            if (disposed) return ExternalEventRequest.Denied;
            pending[key] = action;
            var result = apiEvent.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending) pending.Remove(key);
            return result;
        }
        internal void Cancel(object key) => pending.Remove(key);
        public string GetName() => "MEP View Tabs UI actions";
        public void Execute(UIApplication app)
        {
            var work = new List<Action<UIApplication>>(pending.Values);
            pending.Clear();
            foreach (var action in work)
            {
                if (disposed) break;
                try { action(app); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
        }
        public void Dispose()
        {
            disposed = true;
            pending.Clear();
            apiEvent.Dispose();
        }
    }
}
