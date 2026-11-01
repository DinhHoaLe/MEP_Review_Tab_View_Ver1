using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class ProjectViewInfo
    {
        internal long Id;
        internal string Name;
        internal string Type;
        internal string[] Aliases;
        internal string[] QualifiedAliases;
        internal bool Open;
        internal ViewPreviewMode Preview;
    }
    internal sealed class ProjectContext
    {
        public object Key { get; } = new object();
        public string Name { get; internal set; }
        internal Document Document;
        internal List<ProjectViewInfo> Views = new List<ProjectViewInfo>();
        internal List<ProjectViewInfo> AuditViews = new List<ProjectViewInfo>();
        internal bool Dirty = true;
        internal DateTime NextScan;
        internal long LastActiveView = -1;
    }
    internal sealed class OpenProjectCatalog
    {
        internal readonly List<ProjectContext> Projects = new List<ProjectContext>();
        internal object ActiveProjectKey;
        internal ProjectContext Find(object key) => Projects.FirstOrDefault(project => ReferenceEquals(project.Key,key));
        internal void Invalidate(Document document)
        {
            var project = Projects.FirstOrDefault(item => Equals(item.Document,document));
            if (project != null) project.Dirty = true;
        }
        internal void Update(UIApplication app)
        {
            var documents = app.Application.Documents.Cast<Document>().Where(doc => !doc.IsLinked).ToList();
            Projects.RemoveAll(project => !documents.Any(doc => Equals(doc,project.Document)));
            foreach (var document in documents)
            {
                HashSet<long> opened;
                try { opened = new UIDocument(document).GetOpenUIViews().Select(view => view.ViewId.Value).ToHashSet(); }
                catch { continue; }
                if (opened.Count == 0) continue;
                var project = Projects.FirstOrDefault(item => Equals(item.Document,document));
                if (project == null)
                {
                    project = new ProjectContext { Document = document };
                    Projects.Add(project);
                }
                project.Name = document.Title;
                if (project.Dirty || DateTime.UtcNow >= project.NextScan || opened.Any(id => !project.Views.Any(view => view.Id == id)))
                {
                    project.AuditViews = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
                        .Select(view => Describe(document,view)).ToList();
                    project.Views=project.AuditViews.Where(view=>view.Preview!=ViewPreviewMode.Excluded).ToList();
                    project.Dirty = false;
                    project.NextScan = DateTime.UtcNow.AddSeconds(10);
                    WriteAudit(project);
                }
                foreach (var view in project.Views) view.Open = opened.Contains(view.Id);
                if(Equals(document,app.ActiveUIDocument?.Document))
                {
                    long selected=app.ActiveUIDocument.ActiveView.Id.Value;
                    if(opened.Contains(selected))project.LastActiveView=selected;
                }
            }
            ActiveProjectKey = Projects.FirstOrDefault(project => Equals(project.Document,app.ActiveUIDocument?.Document))?.Key;
        }
        private static ProjectViewInfo Describe(Document document, View view)
        {
            string display = view is ViewSheet sheet ? sheet.SheetNumber + " - " + sheet.Name : view.Name;
            string file = Path.GetFileName(document.PathName);
            var simple = new[] { view.Name,view.Title,display }.Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray();
            var qualified = new[] { document.Title,file,document.PathName }.Where(value => !string.IsNullOrEmpty(value))
                .SelectMany(prefix => simple.Select(alias => prefix + " - " + alias)).Distinct().ToArray();
            var mode=ViewSupportPolicy.Get(view.ViewType.ToString(),view.IsTemplate,
                view is ViewSchedule schedule && schedule.IsTitleblockRevisionSchedule);
            if(mode!=ViewPreviewMode.Excluded && (view is ViewSchedule || view is Autodesk.Revit.DB.Electrical.PanelScheduleView))mode=ViewPreviewMode.Table;
            return new ProjectViewInfo { Id=view.Id.Value,Name=display,Type=view.ViewType.ToString(),
                Aliases=simple,QualifiedAliases=qualified,Preview=mode };
        }
        internal ProjectContext Resolve(PaneViewSelection selection)
        {
            var identities = Projects.SelectMany(project => project.Views.Where(view => view.Open)
                .Select(view => new ProjectViewIdentity { ProjectKey=project.Key,ViewId=view.Id,
                    Aliases=view.Aliases,QualifiedAliases=view.QualifiedAliases })).ToList();
            var match = ProjectViewIdentity.Resolve(selection,identities);
            return match == null ? null : Find(match.ProjectKey);
        }
        internal void WriteAudit(object key)
        {
            var project=Find(key);
            if(project!=null)WriteAudit(project);
        }
        private static void WriteAudit(ProjectContext project)
        {
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location),"view-audit");
                Directory.CreateDirectory(directory);
                string name = string.Concat(project.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                var report = new { Project=project.Name, Scanned=DateTimeOffset.Now, Note="Image export is checked on hover; table previews show the first 16 rows and 8 columns.",
                    Views=project.AuditViews.Select(view => new { ViewId=view.Id,View=view.Name,Type=view.Type,
                        Preview=view.Preview.ToString(), Status=ViewPreviewImages.Result(project.Document,view.Id) ??
                            (view.Preview == ViewPreviewMode.ImageExport ? "Image export: not yet tested for this view" :
                            view.Preview == ViewPreviewMode.Table ? "Table preview: not yet tested for this view" :
                            view.Preview == ViewPreviewMode.Excluded ? "Template/internal view: excluded from user view navigation" : "Information only; no static image") }) };
                File.WriteAllText(Path.Combine(directory,name+"-view-support-audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
                var matrix=Enum.GetNames(typeof(ViewType)).Select(type=>new { ViewType=type, Preview=ViewSupportPolicy.Get(type).ToString() });
                File.WriteAllText(Path.Combine(directory,"view-support-matrix.json"),JsonSerializer.Serialize(matrix,new JsonSerializerOptions {WriteIndented=true}));
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }
}
