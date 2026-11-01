using System;
using System.Windows;
using System.Windows.Automation;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class BrowserAutomationProbe
    {
        // The newer web Project Browser may not expose SysTreeView32. Read only
        // its accessibility caption on a worker thread, never invoke any UI action.
        internal static string Caption(BrowserNativeInterop.ScreenPoint point)
        {
            try
            {
                if(!BrowserNativeInterop.IsOwnWindowAt(point))return null;
                var element=AutomationElement.FromPoint(new Point(point.X,point.Y));
                if(element==null)return null;
                if(element.Current.ControlType==ControlType.Edit || element.Current.ControlType==ControlType.Button)return null;
                string fallback=element.Current.Name;
                for(int i=0;i<8 && element!=null;++i)
                {
                    if(element.Current.ControlType==ControlType.TreeItem)return element.Current.Name;
                    element=TreeWalker.RawViewWalker.GetParent(element);
                }
                return fallback;
            }
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);return null;}
        }
    }
}
