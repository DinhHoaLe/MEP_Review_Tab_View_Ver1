using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class BrowserNativeInterop
    {
        [StructLayout(LayoutKind.Sequential)] internal struct ScreenPoint { internal int X,Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData
        { internal ScreenPoint Point; internal uint MouseDataValue,Flags,Time; internal UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TreeItem
        {
            internal uint Mask; internal IntPtr Item; internal uint State,StateMask;
            internal IntPtr Text; internal int TextMax,Image,SelectedImage,Children; internal IntPtr Parameter;
        }
        internal delegate IntPtr MouseHook(int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int type,MouseHook handler,IntPtr module,uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(ScreenPoint point);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window,uint flags);
        internal static IntPtr RootWindow(IntPtr window)=>GetAncestor(window,2);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int capacity);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out ScreenPoint point);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        internal static IntPtr TreeAt(ScreenPoint point)
        {
            IntPtr window=WindowFromPoint(point);
            GetWindowThreadProcessId(window,out uint process);
            if(process!=(uint)Environment.ProcessId) return IntPtr.Zero;
            for(int i=0;i<8 && window!=IntPtr.Zero;++i,window=GetParent(window))
            {
                var name=new StringBuilder(128);
                GetClassName(window,name,name.Capacity);
                if(name.ToString()=="SysTreeView32") return window;
            }
            return IntPtr.Zero;
        }
        internal static bool IsOwnWindowAt(ScreenPoint point)
        {
            IntPtr window=WindowFromPoint(point);
            if(window==IntPtr.Zero)return false;
            IntPtr root=GetAncestor(window,2);
            GetWindowThreadProcessId(root==IntPtr.Zero ? window : root,out uint process);
            return process==(uint)Environment.ProcessId;
        }
        internal static string SelectedText(IntPtr tree)
        {
            IntPtr selected=SendMessage(tree,0x110A,new IntPtr(9),IntPtr.Zero);
            if(selected==IntPtr.Zero) return null;
            IntPtr text=Marshal.AllocHGlobal(2048);
            IntPtr item=Marshal.AllocHGlobal(Marshal.SizeOf<TreeItem>());
            try
            {
                Marshal.WriteInt16(text,0);
                var value=new TreeItem {Mask=1,Item=selected,Text=text,TextMax=1024};
                Marshal.StructureToPtr(value,item,false);
                return SendMessage(tree,0x113E,IntPtr.Zero,item)!=IntPtr.Zero ? Marshal.PtrToStringUni(text) : null;
            }
            finally { Marshal.FreeHGlobal(item); Marshal.FreeHGlobal(text); }
        }
    }
}
