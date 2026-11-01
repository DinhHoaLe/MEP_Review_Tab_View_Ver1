using System;
using Autodesk.Revit.UI;

namespace MEP_Review_Tab_View_Ver1
{
    public class App : IExternalApplication
    {
        internal static NativeTabHost TabHost;

        public Result OnStartup(UIControlledApplication application)
        {
            TabHost = new NativeTabHost();
            application.Idling += TabHost.OnIdling;
            application.ControlledApplication.DocumentChanged += DocumentChanged;
            application.ViewActivated += ViewActivated;
            var panel = application.CreateRibbonPanel("MEP View Tabs");
            string assembly = typeof(App).Assembly.Location;
            panel.AddItem(new PushButtonData("MepSearchViews", "Search\nViews", assembly,
                typeof(ShowViewSearchCommand).FullName) { ToolTip = "Replace the native view tab strip with Search and a view dropdown." });
            panel.AddItem(new PushButtonData("MepStyleTabs", "Style\nTabs", assembly,
                typeof(StyleTabsCommand).FullName) { ToolTip = "Apply soft blue accents to view tabs." });
            panel.AddItem(new PushButtonData("MepResetTabs", "Reset\nTabs", assembly,
                typeof(ResetTabsCommand).FullName) { ToolTip = "Restore the original tab colors." });
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentChanged -= DocumentChanged;
            application.ViewActivated -= ViewActivated;
            application.Idling -= TabHost.OnIdling;
            TabHost.Dispose();
            TabHost = null;
            try { TabAppearance.Restore(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return Result.Succeeded;
        }
        private void DocumentChanged(object sender, Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)
        {
            TabHost.Catalog.Invalidate(args.GetDocument());
            ViewPreviewImages.Invalidate(args.GetDocument());
        }
        private void ViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs args) => TabHost.RequestRefresh();
    }
}
