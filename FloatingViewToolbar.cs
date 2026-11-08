using System;
using System.Windows;
using System.Windows.Interop;

namespace MEP_Review_Tab_View_Ver1
{
    // A detached Revit drawing is an MFC window, not a LayoutDocumentPaneControl.
    // Host a real WPF child strip in that frame and reserve space above its drawing.
    internal sealed class FloatingViewToolbar : IDisposable
    {
        internal readonly IntPtr Window;
        internal readonly FrameworkElement Content;
        private IntPtr drawing;
        private readonly HwndSource source;
        private readonly FloatingViewInterop.Subclass callback;
        private readonly UIntPtr subclassId=new UIntPtr(0x4d455056);
        private readonly int left,top,right,bottom;
        private bool disposed,resizing;
        private readonly Action changed;

        internal FloatingViewToolbar(IntPtr window,FrameworkElement content,Action changed=null)
        {
            this.changed=changed;
            Window=window;Content=content;
            drawing=FloatingViewInterop.DrawingChild(window);
            if(drawing==IntPtr.Zero)throw new InvalidOperationException("Chưa tìm thấy vùng vẽ của cửa sổ view tách rời.");
            FloatingViewInterop.GetClientRect(window,out var client);
            FloatingViewInterop.GetWindowRect(drawing,out var child);
            var origin=new BrowserNativeInterop.ScreenPoint {X=child.Left,Y=child.Top};
            FloatingViewInterop.ScreenToClient(window,ref origin);
            left=origin.X;top=origin.Y;right=client.Width-left-child.Width;bottom=client.Height-top-child.Height;
            var parameters=new HwndSourceParameters("MEP Floating View Toolbar")
            {ParentWindow=window,WindowStyle=unchecked((int)0x56000000),PositionX=0,PositionY=0,Width=Math.Max(1,client.Width),Height=110};
            source=new HwndSource(parameters);
            source.RootVisual=content;
            callback=WndProc;
            if(!FloatingViewInterop.SetWindowSubclass(window,callback,subclassId,IntPtr.Zero))
            {source.Dispose();throw new InvalidOperationException("Không gắn được thanh công cụ vào cửa sổ view.");}
            content.SizeChanged+=ContentSizeChanged;
            Resize();
        }
        private void ContentSizeChanged(object sender,SizeChangedEventArgs args)=>Resize();
        private IntPtr WndProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,UIntPtr id,IntPtr data)
        {
            IntPtr result=FloatingViewInterop.DefSubclassProc(window,message,wParam,lParam);
            if(message==5 || message==0x0047)Resize(); // WM_SIZE / WM_WINDOWPOSCHANGED
            if(message==0x000C || message==0x0006 || message==0x0082)
            {
                try{changed?.Invoke();}
                catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);}
            }
            return result;
        }
        internal void Resize()
        {
            if(disposed || resizing || !FloatingViewInterop.IsWindow(Window))return;
            if(!FloatingViewInterop.IsWindow(drawing) || BrowserNativeInterop.GetParent(drawing)!=Window)
                drawing=FloatingViewInterop.DrawingChild(Window,source.Handle);
            if(drawing==IntPtr.Zero)return;
            resizing=true;
            try
            {
                FloatingViewInterop.GetClientRect(Window,out var client);
                var scale=source.CompositionTarget.TransformToDevice;
                double x=Math.Max(1,scale.M11),y=Math.Max(1,scale.M22);
                Content.Measure(new Size(Math.Max(1,client.Width/x),double.PositiveInfinity));
                int height=(int)Math.Ceiling(Math.Max(90,Content.DesiredSize.Height)*y);
                height=Math.Min(height,Math.Max(1,client.Height/2));
                FloatingViewInterop.SetWindowPos(source.Handle,IntPtr.Zero,0,0,Math.Max(1,client.Width),Math.Max(1,height),0x0010);
                FloatingViewInterop.SetWindowPos(drawing,IntPtr.Zero,left,top+height,
                    Math.Max(1,client.Width-left-right),Math.Max(1,client.Height-top-bottom-height),0x0014);
            }
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);}
            finally{resizing=false;}
        }
        internal PaneViewSelection Selection()=>new PaneViewSelection {Title=FloatingViewInterop.Title(Window),ToolTip=FloatingViewInterop.Title(Window)};
        internal bool Contains(BrowserNativeInterop.ScreenPoint point)
        {
            return !disposed && FloatingViewInterop.IsWindowVisible(Window) && FloatingViewInterop.GetWindowRect(Window,out var rect) &&
                point.X>=rect.Left && point.X<=rect.Right && point.Y>=rect.Top && point.Y<=rect.Bottom;
        }
        internal void Activate()=>FloatingViewInterop.SetForegroundWindow(Window);
        public void Dispose()=>Dispose(true);
        internal void Dispose(bool restoreDrawing)
        {
            if(disposed)return;
            disposed=true;
            Content.SizeChanged-=ContentSizeChanged;
            if(FloatingViewInterop.IsWindow(Window))FloatingViewInterop.RemoveWindowSubclass(Window,callback,subclassId);
            source.Dispose();
            if(restoreDrawing && FloatingViewInterop.IsWindow(Window) && FloatingViewInterop.IsWindow(drawing) && BrowserNativeInterop.GetParent(drawing)==Window)
            {
                FloatingViewInterop.GetClientRect(Window,out var client);
                FloatingViewInterop.SetWindowPos(drawing,IntPtr.Zero,left,top,Math.Max(1,client.Width-left-right),Math.Max(1,client.Height-top-bottom),0x0014);
            }
        }
    }
}
