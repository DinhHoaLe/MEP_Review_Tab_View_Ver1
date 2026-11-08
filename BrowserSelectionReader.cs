using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using System.IO;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class BrowserSelectionReader
    {
        internal static BrowserViewDragRequest Read(UIApplication app,OpenProjectCatalog catalog)
        {
            catalog.Update(app);
            var project=catalog.Find(catalog.ActiveProjectKey);
            if(project==null)return null;
            var selected=app.ActiveUIDocument.Selection.GetElementIds().Select(id=>id.Value).ToHashSet();
            var views=project.Views.Where(view=>selected.Contains(view.Id)).ToList();
            Log("Project="+project.Name+"; selected elements="+selected.Count+"; selected views="+views.Count);
            if(views.Count==1)return Request(project,views[0]);
            if(views.Count>1)return null;
            // The 2025 web browser does not always expose selected view IDs
            // through Selection or accessibility. This is a guarded UI adapter,
            // invoked ONLY inside an ExternalEvent API callback.
            var provider=typeof(UIApplication).Assembly.GetType("Autodesk.Revit.UI.ProjectBrowserDataProvider");
            var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            var getIndices=provider?.GetMethod("GetAllSelectedItemsIndex",flags);
            var getItems=provider?.GetMethod("GetAllBrowserItems",flags);
            if(getIndices==null || getItems==null)return null;
            var indices=getIndices.Invoke(null,new object[]{project.Document}) as IEnumerable;
            if(indices==null)return null;
            var items=getItems.Invoke(null,new object[]{project.Document}) as IEnumerable;
            if(items==null)return null;
            var names=BrowserSelectionNames.Read(items,indices);
            Log("Browser names="+string.Join(" | ",names));
            var matches=project.Views.Where(view=>names.Any(name=>view.Aliases.Any(alias=>
                string.Equals(alias.Trim(),name.Trim(),StringComparison.OrdinalIgnoreCase)))).ToList();
            return matches.Count==1 ? Request(project,matches[0]) : null;
        }
        private static BrowserViewDragRequest Request(ProjectContext project,ProjectViewInfo view) =>
            new BrowserViewDragRequest {ProjectKey=project.Key,ViewId=view.Id,Name=project.Name+" · "+view.Name};
        private static void Log(string value)
        {
            try{File.AppendAllText(Path.Combine(AddinPaths.OutputDirectory,"browser-selection.log"),DateTimeOffset.Now.ToString("O")+" "+value+Environment.NewLine);}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);}
        }
    }
}
