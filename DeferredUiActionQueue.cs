using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class DeferredUiActionQueue : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly Dictionary<object,DispatcherOperation> operations=new Dictionary<object,DispatcherOperation>();
        private bool disposed;
        internal DeferredUiActionQueue(Dispatcher dispatcher){this.dispatcher=dispatcher;}
        internal void Post(object key,Action action)
        {
            Cancel(key);
            if(disposed || dispatcher.HasShutdownStarted)return;
            operations[key]=dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
            {
                operations.Remove(key);
                if(!disposed)action();
            }));
        }
        internal void Cancel(object key)
        {
            if(operations.TryGetValue(key,out var operation)){operation.Abort();operations.Remove(key);}
        }
        public void Dispose()
        {
            disposed=true;
            foreach(var operation in operations.Values)operation.Abort();
            operations.Clear();
        }
    }
}
