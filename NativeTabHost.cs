using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Xceed.Wpf.AvalonDock.Controls;
using Xceed.Wpf.AvalonDock.Layout;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class NativeTabHost : IDisposable
    {
        private sealed class HostedPane
        {
            internal FrameworkElement Pane;
            internal ViewSearchPane Search;
            internal NativeTabLayout Layout;
        }
        private const string PaneType = "Xceed.Wpf.AvalonDock.Controls.LayoutDocumentPaneControl";
        private readonly List<HostedPane> hosted = new List<HostedPane>();
        private sealed class FloatingHosted
        {
            internal FloatingViewToolbar Toolbar;
            internal ViewSearchPane Search;
        }
        private readonly List<FloatingHosted> floating=new List<FloatingHosted>();
        private string floatingSignature;
        private bool enabled = true;
        private DateTime nextRefresh;
        private string lastError;
        private bool wroteDiagnostics;
        private readonly RevitUiDispatcher api = new RevitUiDispatcher();
        private readonly LayoutRefreshScheduler layoutRefresh;
        private bool synchronizing;
        private bool disposed;
        internal readonly OpenProjectCatalog Catalog = new OpenProjectCatalog();
        private NativeBrowserDrag browserDrag;
        private readonly object browserSelectionKey=new object();
        private readonly object projectCloseKey=new object();
        private readonly object floatKey=new object();

        internal NativeTabHost()
        {
            layoutRefresh = new LayoutRefreshScheduler(LayoutChanged);
            HookLayout();
        }

        private void HookLayout()
        {
            var main = UIFramework.MainWindow.getMainWnd();
            if (main != null)
            {
                layoutRefresh.Hook(main);
                if(browserDrag==null) browserDrag=new NativeBrowserDrag(main,IsBrowserPoint,ResolveBrowserView,DropTarget,CaptureBrowserSelection);
            }
        }

        private static bool ContainsScreenPoint(FrameworkElement element,BrowserNativeInterop.ScreenPoint point)
        {
            if(!element.IsVisible || !element.IsLoaded)return false;
            Point local=element.PointFromScreen(new Point(point.X,point.Y));
            return local.X>=0 && local.Y>=0 && local.X<=element.ActualWidth && local.Y<=element.ActualHeight;
        }
        private bool IsBrowserPoint(BrowserNativeInterop.ScreenPoint point)
        {
            if(!enabled || disposed)return false;
            var elements=GetRoots().SelectMany(NativeTabLayout.Descendants).OfType<FrameworkElement>().ToList();
            return elements.OfType<LayoutAnchorableControl>()
                .Any(control => (control.Model as LayoutAnchorable)?.Title?.StartsWith("Project Browser",StringComparison.OrdinalIgnoreCase)==true && ContainsScreenPoint(control,point)) ||
                elements.OfType<System.Windows.Controls.TabControl>().Any(control=>
                    (control.SelectedItem as LayoutAnchorable)?.Title?.StartsWith("Project Browser",StringComparison.OrdinalIgnoreCase)==true && ContainsScreenPoint(control,point));
        }
        private BrowserViewDragRequest ResolveBrowserView(string caption)
        {
            if(string.IsNullOrWhiteSpace(caption))return null;
            var project=Catalog.Find(Catalog.ActiveProjectKey);
            var matches=project?.Views.Where(view=>view.Aliases.Any(alias=>string.Equals(alias.Trim(),caption.Trim(),StringComparison.OrdinalIgnoreCase))).ToList();
            return matches?.Count==1 ? new BrowserViewDragRequest {ProjectKey=project.Key,ViewId=matches[0].Id,Name=project.Name+" · "+matches[0].Name} : null;
        }
        internal ViewSearchPane DropTarget(BrowserNativeInterop.ScreenPoint point) =>
            BrowserNativeInterop.IsOwnWindowAt(point) ? hosted.FirstOrDefault(item=>ContainsScreenPoint(item.Pane,point))?.Search ??
                floating.FirstOrDefault(item=>item.Toolbar.Contains(point))?.Search : null;
        private System.Threading.Tasks.Task<BrowserViewDragRequest> CaptureBrowserSelection()
        {
            var completion=new System.Threading.Tasks.TaskCompletionSource<BrowserViewDragRequest>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var result=api.Post(browserSelectionKey,app=>
            {
                try{completion.TrySetResult(BrowserSelectionReader.Read(app,Catalog));}
                catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);completion.TrySetResult(null);}
            });
            if(result!=ExternalEventRequest.Accepted && result!=ExternalEventRequest.Pending)completion.TrySetResult(null);
            return completion.Task;
        }
        internal void CloseProject(object key,Action<string> error)
        {
            var result=api.Post(projectCloseKey,app=>
            {
                try
                {
                    var project=Catalog.Find(key);
                    if(project==null || !project.Document.IsValidObject)return;
                    var view=project.Views.FirstOrDefault(item=>item.Open && item.Id==project.LastActiveView) ?? project.Views.First(item=>item.Open);
                    ActivateProjectView(project,view.Id);
                    if(!Equals(app.ActiveUIDocument?.Document,project.Document))throw new InvalidOperationException("Chưa kích hoạt đúng project cần đóng.");
                    var command=RevitCommandId.LookupCommandId("ID_REVIT_FILE_CLOSE");
                    if(command==null || !app.CanPostCommand(command))throw new InvalidOperationException("Revit đang bận; hãy kết thúc lệnh hiện tại rồi đóng project lại.");
                    // Native File > Close preserves Revit's Save/Don't Save/Cancel dialog.
                    app.PostCommand(command);
                }
                catch(Exception ex){error(ex.Message);}
            });
            if(result!=ExternalEventRequest.Accepted && result!=ExternalEventRequest.Pending)error("Revit đang bận; hãy thử đóng project lại.");
        }
        internal void CaptureDragPlacement(ViewSearchPane search,ViewDragRequest request,Point pointer)
        {
            var main=UIFramework.MainWindow.getMainWnd();
            var mainSource=PresentationSource.FromVisual(main) as System.Windows.Interop.HwndSource;
            var source=PresentationSource.FromVisual(search) as System.Windows.Interop.HwndSource;
            IntPtr window=source==null ? IntPtr.Zero : BrowserNativeInterop.RootWindow(source.Handle);
            IntPtr mainWindow=mainSource==null ? IntPtr.Zero : BrowserNativeInterop.RootWindow(mainSource.Handle);
            if(window!=IntPtr.Zero && mainWindow!=IntPtr.Zero && window!=mainWindow && FloatingViewInterop.GetWindowRect(window,out var rect))
            {
                request.FloatingWindow=window;
                request.GrabOffsetX=(int)Math.Round(pointer.X)-rect.Left;
                request.GrabOffsetY=(int)Math.Round(pointer.Y)-rect.Top;
            }
            else
            {
                var origin=search.PointToScreen(new Point());
                request.GrabOffsetX=(int)Math.Round(pointer.X-origin.X);
                request.GrabOffsetY=(int)Math.Round(pointer.Y-origin.Y);
            }
        }
        internal void FloatView(ViewDragRequest request,BrowserNativeInterop.ScreenPoint point,Action<string> error)
        {
            // An already detached view must move its existing HWND. Calling
            // LayoutDocument.Float twice can dereference a missing dock parent.
            if(request.FloatingWindow!=IntPtr.Zero)
            {
                if(!FloatingViewInterop.Reposition(request.FloatingWindow,point,request.GrabOffsetX,request.GrabOffsetY))
                    error("Cửa sổ view đã thay đổi. Hãy nắm kéo lại.");
                return;
            }
            var result=api.Post(floatKey,app=>
            {
                try
                {
                    var project=Catalog.Find(request.DocumentKey);
                    var info=project?.Views.FirstOrDefault(view=>view.Id==request.ViewId && view.Open);
                    if(info==null)throw new InvalidOperationException("View hoặc project đã đóng; hãy kéo lại.");
                    var native=FindDocument(new PaneViewCandidate {Id=info.Id,Aliases=info.Aliases,QualifiedAliases=info.QualifiedAliases});
                    if(!native.CanFloat)throw new InvalidOperationException("Revit không cho phép tách cửa sổ của view này.");
                    var root=UIFramework.MainWindow.getMainWnd();
                    var transform=PresentationSource.FromVisual(root)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
                    var location=transform.Transform(new Point(point.X-request.GrabOffsetX,point.Y-request.GrabOffsetY));
                    native.FloatingLeft=location.X; native.FloatingTop=location.Y;
                    native.FloatingWidth=850; native.FloatingHeight=620;
                    native.Float();
                    native.IsActive=true;
                    native.ActivateFromTabGroupDropDownList();
                    SynchronizeLayout();
                    RefreshAfterMove(app);
                    // Once Revit has created the real frame, place it in screen
                    // pixels so a destination monitor's DPI cannot shift it.
                    IntPtr mainWindow=BrowserNativeInterop.RootWindow(app.MainWindowHandle);
                    IntPtr frame=hosted.Where(item=>ReferenceEquals(((item.Pane as LayoutDocumentPaneControl)?.Model as LayoutDocumentPane)?.SelectedContent,native))
                        .Select(item=>PresentationSource.FromVisual(item.Pane) as System.Windows.Interop.HwndSource)
                        .Where(source=>source!=null).Select(source=>BrowserNativeInterop.RootWindow(source.Handle))
                        .FirstOrDefault(window=>window!=IntPtr.Zero && window!=mainWindow);
                    if(frame==IntPtr.Zero)frame=floating.FirstOrDefault(item=>item.Toolbar.Selection().Matches(info.QualifiedAliases))?.Toolbar.Window ?? IntPtr.Zero;
                    if(frame!=IntPtr.Zero)FloatingViewInterop.Reposition(frame,point,request.GrabOffsetX,request.GrabOffsetY);
                }
                catch(Exception ex)
                {
                    try{File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location),"float-view.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine);}
                    catch(Exception logError){System.Diagnostics.Trace.WriteLine(logError);}
                    error(ex.Message);
                }
            });
            if(result!=ExternalEventRequest.Accepted && result!=ExternalEventRequest.Pending)error("Revit đang bận; hãy thử tách view lại.");
        }
        internal void RequestRefresh() => api.Post(this,RefreshPanes);

        private void LayoutChanged()
        {
            if (!enabled || disposed || synchronizing) return;
            try
            {
                if (SynchronizeLayout()) api.Post(this, RefreshPanes);
            }
            catch (Exception ex) { lastError = ex.Message; System.Diagnostics.Trace.WriteLine(ex); }
        }

        internal void OnIdling(object sender, IdlingEventArgs args)
        {
            if (!enabled || DateTime.UtcNow < nextRefresh || !(sender is UIApplication app)) return;
            nextRefresh = DateTime.UtcNow.AddSeconds(1);
            try
            {
                Update(app);
                if (app.ActiveUIDocument != null && hosted.Count == 0 && !wroteDiagnostics)
                {
                    WriteDiagnostics();
                    wroteDiagnostics = true;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                System.Diagnostics.Trace.WriteLine(ex);
            }
        }

        internal void Enable(UIApplication app)
        {
            enabled = true;
            Update(app);
            if (app.ActiveUIDocument != null && hosted.Count == 0)
            {
                string path = WriteDiagnostics();
                throw new NotSupportedException("Chưa tìm được hàng tab gốc của bản Revit này. " +
                    "Không tạo panel thay thế. Chi tiết: " + path +
                    (lastError == null ? "" : "\n" + lastError));
            }
        }

        private void Update(UIApplication app)
        {
            HookLayout();
            SynchronizeLayout();
            RefreshPanes(app);
        }

        private bool SynchronizeLayout()
        {
            if (synchronizing) return false;
            synchronizing = true;
            bool changed = false;
            try
            {
                var roots = GetRoots();
                var panes = roots.SelectMany(NativeTabLayout.Descendants)
                    .OfType<FrameworkElement>().Where(IsDocumentPane).Distinct().ToList();

                foreach (var stale in hosted.Where(item => !panes.Contains(item.Pane)).ToList())
                {
                    stale.Layout.Dispose();
                    stale.Search.Dispose();
                    hosted.Remove(stale);
                    changed = true;
                }
                var visiblePanes = panes.Where(item => item.IsVisible).ToList();
                foreach (var pane in visiblePanes)
                {
                    var existing = hosted.FirstOrDefault(item => ReferenceEquals(item.Pane, pane));
                    if (existing != null && !NativeTabLayout.Descendants(pane).Contains(existing.Layout.Grid))
                    {
                        existing.Layout.Dispose();
                        existing.Search.Dispose();
                        hosted.Remove(existing);
                        existing = null;
                        changed = true;
                    }
                    if (existing == null)
                    {
                        var descendants = NativeTabLayout.Descendants(pane).OfType<FrameworkElement>().ToList();
                        var tabs = descendants.FirstOrDefault(item => item.GetType().Name == "DocumentPaneTabPanel");
                        // ContentPanel can also be a wrapper around the entire template.
                        // Prefer the selected-content presenter, then try every named border.
                        var candidates = descendants.Where(item => item.Name == "PART_SelectedContentHost")
                            .Concat(descendants.Where(item => item.Name == "ContentPanel")).ToList();
                        if (tabs == null || candidates.Count == 0) continue;
                        var search = new ViewSearchPane(api);
                        NativeTabLayout layout = null;
                        try
                        {
                            foreach (var content in candidates)
                            {
                                layout = NativeTabLayout.TryAttach(pane, tabs, content, search);
                                if (layout != null) break;
                            }
                        }
                        catch { search.Dispose(); throw; }
                        if (layout == null) { search.Dispose(); continue; }
                        existing = new HostedPane { Pane = pane, Search = search, Layout = layout };
                        hosted.Add(existing);
                        changed = true;
                    }
                    existing.Layout.EnsureHidden();
                }
                return changed;
            }
            finally { synchronizing = false; }
        }

        private void RefreshPanes(UIApplication app)
        {
            if (!enabled || disposed) return;
            Catalog.Update(app);
            SynchronizeFloating(app.MainWindowHandle);
            var visible = hosted.Where(item => item.Pane.IsVisible).ToList();
            foreach (var item in visible)
            {
                var source=PresentationSource.FromVisual(item.Search) as System.Windows.Interop.HwndSource;
                IntPtr frame=source==null ? IntPtr.Zero : BrowserNativeInterop.RootWindow(source.Handle);
                item.Search.SetFloatingFrame(frame==BrowserNativeInterop.RootWindow(app.MainWindowHandle) ? IntPtr.Zero : frame);
                var selection = PaneViewSelection.Read(item.Pane);
                var project = Catalog.Resolve(selection) ?? Catalog.Find(item.Search.ProjectKey) ?? Catalog.Find(Catalog.ActiveProjectKey);
                item.Search.Refresh(app,selection,visible.Count == 1,project,Catalog.Projects);
            }
            foreach(var item in floating.ToList())
            {
                item.Search.SetFloatingFrame(item.Toolbar.Window);
                var selection=item.Toolbar.Selection();
                var project=Catalog.Resolve(selection);
                if(project!=null)item.Search.Refresh(app,selection,false,project,Catalog.Projects);
                item.Toolbar.Resize();
            }
        }

        private void SynchronizeFloating(IntPtr mainWindow)
        {
            var windows=FloatingViewInterop.OwnWindows();
            foreach(var stale in floating.Where(item=>!windows.Contains(item.Toolbar.Window)).ToList())
            {
                stale.Search.Dispose();stale.Toolbar.Dispose();floating.Remove(stale);
            }
            string signature=string.Join("|",windows.Select(window=>window.ToInt64()+":"+FloatingViewInterop.Title(window)));
            if(signature!=floatingSignature)
            {
                floatingSignature=signature;
                try{File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location),"floating-ui.log"),DateTimeOffset.Now+" "+signature+Environment.NewLine);}
                catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);}
            }
            foreach(var window in windows)
            {
                if(window==mainWindow || floating.Any(item=>item.Toolbar.Window==window))continue;
                if(hosted.Any(item=>
                    PresentationSource.FromVisual(item.Pane) is System.Windows.Interop.HwndSource source &&
                    BrowserNativeInterop.RootWindow(source.Handle)==window))continue;
                var selection=new PaneViewSelection {Title=FloatingViewInterop.Title(window),ToolTip=FloatingViewInterop.Title(window)};
                if(Catalog.Resolve(selection)==null)continue;
                var search=new ViewSearchPane(api);
                try
                {
                    var toolbar=new FloatingViewToolbar(window,search,RequestRefresh);
                    floating.Add(new FloatingHosted {Toolbar=toolbar,Search=search});
                }
                catch(Exception ex){search.Dispose();System.Diagnostics.Trace.WriteLine(ex);}
            }
        }

        internal PaneViewSelection ReadPaneSelection(ViewSearchPane search)
        {
            var owner = hosted.FirstOrDefault(item => ReferenceEquals(item.Search, search));
            return owner == null ? floating.FirstOrDefault(item=>ReferenceEquals(item.Search,search))?.Toolbar.Selection() : PaneViewSelection.Read(owner.Pane);
        }
        internal bool IsFloating(ViewSearchPane search)=>floating.Any(item=>ReferenceEquals(item.Search,search));

        private static bool IsDocumentPane(FrameworkElement element)
        {
            for (Type type = element.GetType(); type != null; type = type.BaseType)
                if (type.FullName == PaneType) return true;
            return false;
        }

        internal ILayoutContainer PaneModel(ViewSearchPane search)
        {
            var owner = hosted.FirstOrDefault(item => ReferenceEquals(item.Search, search));
            var source = (owner?.Pane as LayoutDocumentPaneControl)?.Model as LayoutDocumentPane;
            if(source==null && IsFloating(search))
            {
                var selection=ReadPaneSelection(search);
                var project=Catalog.Resolve(selection);
                var info=project?.Views.FirstOrDefault(view=>view.Open && selection.Matches(view.QualifiedAliases));
                if(info==null)return null;
                return FindDocument(new PaneViewCandidate {Id=info.Id,Aliases=info.Aliases,QualifiedAliases=info.QualifiedAliases}).Parent;
            }
            if (source == null)
                throw new InvalidOperationException("Bố cục WT vừa thay đổi. Hãy chọn lại view.");
            return source;
        }
        private static List<LayoutDocument> NativeDocuments()
        {
            var result=new List<LayoutDocument>();
            foreach(var manager in GetRoots().SelectMany(NativeTabLayout.Descendants).OfType<Xceed.Wpf.AvalonDock.DockingManager>().Distinct())
            {
                var pending=new Stack<ILayoutElement>();
                if(manager.Layout is ILayoutElement root) pending.Push(root);
                while(pending.Count>0)
                {
                    var item=pending.Pop();
                    if(item is LayoutDocument document) result.Add(document);
                    if(item is ILayoutContainer group) foreach(var child in group.Children) pending.Push(child);
                }
            }
            return result.Distinct().ToList();
        }
        private static LayoutDocument FindDocument(PaneViewCandidate candidate)
        {
            var documents=NativeDocuments();
            var matches=documents.Where(document=>PaneViewSelection.ReadDocument(document).Matches(candidate.QualifiedAliases)).ToList();
            if(matches.Count==0) matches=documents.Where(document=>PaneViewSelection.ReadDocument(document).Matches(candidate.Aliases)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException("Không xác định duy nhất được vị trí view đã chọn trong WT.");
            return matches[0];
        }
        internal void ActivateProjectView(ProjectContext project,long id)
        {
            var view=project.Views.First(item=>item.Id==id);
            LayoutDocument native;
            try{native=FindDocument(new PaneViewCandidate {Id=id,Aliases=view.Aliases,QualifiedAliases=view.QualifiedAliases});}
            catch(InvalidOperationException)
            {
                var frame=floating.FirstOrDefault(item=>item.Toolbar.Selection().Matches(view.QualifiedAliases));
                if(frame==null)throw;
                frame.Toolbar.Activate();return;
            }
            native.IsActive=true;
            native.ActivateFromTabGroupDropDownList();
        }
        internal PaneViewMove MoveIntoPane(ViewSearchPane search, PaneViewCandidate candidate,
            out LayoutDocument targetDocument, ILayoutContainer source = null)
        {
            source=source??PaneModel(search);
            targetDocument=FindDocument(candidate);
            return PaneViewMove.Apply(source, targetDocument);
        }

        internal void RefreshAfterMove(UIApplication app)
        {
            // Moving native documents can rebuild either pane's template.
            // Refresh both headers, not just the dropdown that initiated the move.
            foreach (var item in hosted) item.Search.InvalidateSelection();
            foreach (var item in floating) item.Search.InvalidateSelection();
            nextRefresh = DateTime.MinValue;
            RefreshPanes(app);
        }
        internal void DismissAllTransientUi()
        {
            foreach(var item in hosted)item.Search.DismissTransientUi();
            foreach(var item in floating)item.Search.DismissTransientUi();
        }

        private static List<DependencyObject> GetRoots()
        {
            var roots = new List<DependencyObject>();
            var main = UIFramework.MainWindow.getMainWnd();
            if (main != null) roots.Add(main);
            // Include detached/floating document panes, not just the main frame.
            foreach (System.Windows.PresentationSource source in System.Windows.PresentationSource.CurrentSources)
                if (source.RootVisual != null && !roots.Contains(source.RootVisual)) roots.Add(source.RootVisual);
            return roots;
        }

        private string WriteDiagnostics()
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location), "native-tab-layout.txt");
            var text = new StringBuilder("Native tab layout diagnostics v2\n");
            var panes = GetRoots().SelectMany(NativeTabLayout.Descendants).OfType<FrameworkElement>()
                .Where(IsDocumentPane).Distinct();
            foreach (var pane in panes)
            {
                text.AppendLine("PANE visible=" + pane.IsVisible + " size=" + pane.ActualWidth + "x" + pane.ActualHeight);
                foreach (var element in NativeTabLayout.Descendants(pane).OfType<FrameworkElement>().Take(250))
                {
                    var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
                    text.AppendLine(element.GetType().FullName + " Name=" + element.Name +
                        " Parent=" + parent?.GetType().Name +
                        " Row=" + System.Windows.Controls.Grid.GetRow(element) +
                        " Span=" + System.Windows.Controls.Grid.GetRowSpan(element) +
                        " Margin=" + element.Margin + " Visible=" + element.IsVisible);
                    if (element is System.Windows.Controls.Grid grid)
                        text.AppendLine("  Rows=" + string.Join(", ", grid.RowDefinitions.Select(row => row.Height.ToString())));
                }
            }
            if (lastError != null) text.AppendLine(lastError);
            File.WriteAllText(path, text.ToString());
            return path;
        }

        internal void Disable()
        {
            enabled = false;
            api.Cancel(this);
            foreach (var item in hosted)
            {
                item.Layout.Dispose();
                item.Search.Dispose();
            }
            hosted.Clear();
            foreach(var item in floating){item.Search.Dispose();item.Toolbar.Dispose();}
            floating.Clear();
        }

        public void Dispose()
        {
            disposed = true;
            layoutRefresh.Dispose();
            browserDrag?.Dispose();
            Disable();
            api.Dispose();
        }
    }
}

