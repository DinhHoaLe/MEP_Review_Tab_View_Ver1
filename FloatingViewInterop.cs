using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class FloatingViewInterop
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal int Left,Top,Right,Bottom;internal int Width=>Right-Left;internal int Height=>Bottom-Top;}
        internal delegate bool EnumWindow(IntPtr window,IntPtr data);
        internal delegate IntPtr Subclass(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,UIntPtr id,IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent,EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr window,ref BrowserNativeInterop.ScreenPoint point);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
        internal static bool Reposition(IntPtr window,BrowserNativeInterop.ScreenPoint drop,int offsetX,int offsetY)
        {
            if(window==IntPtr.Zero || !IsWindow(window))return false;
            BrowserNativeInterop.GetWindowThreadProcessId(window,out uint process);
            if(process!=(uint)Environment.ProcessId)return false;
            // Coordinates remain physical screen pixels, including negative
            // monitor coordinates. Keep the existing frame's dimensions.
            return SetWindowPos(window,IntPtr.Zero,drop.X-offsetX,drop.Y-offsetY,0,0,0x0015);
        }
        [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(IntPtr window,Subclass callback,UIntPtr id,IntPtr data);
        [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr window,Subclass callback,UIntPtr id);
        [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        internal static string Title(IntPtr window)
        {
            var text=new StringBuilder(2048);GetWindowText(window,text,text.Capacity);return text.ToString();
        }
        internal static List<IntPtr> OwnWindows()
        {
            var result=new List<IntPtr>();
            EnumWindows((window,data)=>
            {
                BrowserNativeInterop.GetWindowThreadProcessId(window,out uint process);
                if(process==(uint)Environment.ProcessId && IsWindowVisible(window))result.Add(window);
                return true;
            },IntPtr.Zero);
            return result;
        }
        internal static IntPtr DrawingChild(IntPtr window,IntPtr exclude=default)
        {
            GetClientRect(window,out var client);
            IntPtr best=IntPtr.Zero;long area=0;
            EnumChildWindows(window,(child,data)=>
            {
                if(child==exclude || BrowserNativeInterop.GetParent(child)!=window)return true;
                GetWindowRect(child,out var rect);
                long size=(long)rect.Width*rect.Height;
                if(rect.Width>=client.Width/2 && rect.Height>=client.Height/2 && size>area){best=child;area=size;}
                return true;
            },IntPtr.Zero);
            return best;
        }
    }
}
