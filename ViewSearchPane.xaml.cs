using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Point = System.Windows.Point;

namespace MEP_Review_Tab_View_Ver1
{
    // The WPF layer only consumes snapshots. Revit objects are read in API callbacks.
    public partial class ViewSearchPane : UserControl, IDisposable
    {
        private sealed class ViewRow
        {
            public long Id { get; set; }
            public long Generation { get; set; }
            public string Name { get; set; }
            public string Detail { get; set; }
            internal string[] Aliases;
            internal string[] QualifiedAliases;
            internal object ProjectKey;
            internal bool ProjectSwitch;
        }

        private readonly SwitchViewHandler handler;
        private readonly RevitUiDispatcher api;
        private readonly ViewDropdownController dropdown;
        private List<ViewRow> rows = new List<ViewRow>();
        private Document sourceDocument;
        private long generation;
        private long activeId = -1;
        private string paneCaption;
        private bool disposed;
        private bool updatingSelection;
        private bool forceRefresh;
        private bool hasError;
        private readonly ViewPreviewPopup preview;
        private readonly object previewKey = new object();
        private long previewToken;
        private object documentKey;
        private readonly Dictionary<object,long> projectViewsInPane=new Dictionary<object,long>();
        private readonly object paneKey = new object();
        private readonly object closeKey = new object();
        private bool closing;
        private Point? dragStart;
        private ViewDragRequest dragRequest;
        private IntPtr floatingFrame;
        internal void SetFloatingFrame(IntPtr window)
        {
            floatingFrame=window;
            WindowButtons.Visibility=window==IntPtr.Zero ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            MaximizeWindowButton.Content=window!=IntPtr.Zero && FloatingViewInterop.IsZoomed(window) ? "\u2750" : "\u25A1";
        }
        private void MinimizeWindowClick(object sender,RoutedEventArgs args)
        {
            if(floatingFrame!=IntPtr.Zero && FloatingViewInterop.IsWindow(floatingFrame))FloatingViewInterop.ShowWindow(floatingFrame,6);
        }
        private void MaximizeWindowClick(object sender,RoutedEventArgs args)
        {
            if(floatingFrame==IntPtr.Zero || !FloatingViewInterop.IsWindow(floatingFrame))return;
            FloatingViewInterop.ShowWindow(floatingFrame,FloatingViewInterop.IsZoomed(floatingFrame) ? 9 : 3);
            SetFloatingFrame(floatingFrame);
        }

        internal ViewSearchPane(RevitUiDispatcher api)
        {
            this.api = api;
            InitializeComponent();
            handler = new SwitchViewHandler(this);
            preview = new ViewPreviewPopup(ControlsAnchor);
            dropdown = new ViewDropdownController(ViewSelector, chosen => QueueView(chosen as ViewRow),
                chosen => PreviewView(chosen as ViewRow), HidePreview);
            Unloaded += PaneUnloaded;
        }

        private void PaneUnloaded(object sender, RoutedEventArgs args)
        {
            if(disposed)return;
            dropdown.CancelPreview();
            EndDrag();
        }
        private void HidePreview()
        {
            ++previewToken;
            api.Cancel(previewKey);
            preview.Hide();
        }
        private void PreviewView(ViewRow row)
        {
            if (disposed || !IsLoaded || row == null || row.Generation != generation) return;
            long token = ++previewToken;
            preview.Loading(row.Name, ActualWidth - 380);
            var result = api.Post(previewKey, app =>
            {
                if (disposed || token != previewToken || row.Generation != generation) return;
                try
                {
                    var doc = App.TabHost.Catalog.Find(row.ProjectKey)?.Document;
                    if (doc == null || !doc.IsValidObject)
                        throw new InvalidOperationException("Project đã thay đổi. Hãy rê chuột lại trên view.");
                    var image = ViewPreviewImages.Load(doc, row.Id);
                    if (!disposed && token == previewToken) preview.Show(image);
                }
                catch (Exception ex)
                {
                    LogSwitch("PREVIEW ERROR " + ex);
                    if (!disposed && token == previewToken) preview.Error(ex.Message);
                }
                finally { App.TabHost.Catalog.WriteAudit(row.ProjectKey); }
            });
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
                preview.Error("Revit đang bận. Hãy rê chuột lại sau khi kết thúc lệnh.");
        }

        internal object ProjectKey => documentKey;
        private string projectTabSignature;
        private ViewRow ToRow(ProjectContext project, ProjectViewInfo info) => new ViewRow
        {
            Id=info.Id,Generation=generation,Name=info.Name,Detail=info.Type,
            Aliases=info.Aliases.Concat(info.QualifiedAliases).ToArray(),
            QualifiedAliases=info.QualifiedAliases,ProjectKey=project.Key
        };
        internal void Refresh(UIApplication app, PaneViewSelection selection, bool allowGlobalFallback,
            ProjectContext project, IReadOnlyList<ProjectContext> projects)
        {
            var document=project?.Document;
            bool documentChanged=!Equals(document,sourceDocument);
            documentKey=project?.Key;
            if(documentChanged)
            {
                sourceDocument=document;
                dropdown.CancelPreview();
                ++generation;
                handler.Pending=null;
                SearchBox.Clear();
            }
            string signature=string.Join("|",projects.Select(item=>item.Key.GetHashCode()+":"+item.Name))+"/"+documentKey?.GetHashCode();
            if(signature!=projectTabSignature)
            {
                projectTabSignature=signature;
                ProjectTabs.ItemsSource=projects.Select(item=>new ProjectTabItem {Key=item.Key,Name=item.Name,Selected=ReferenceEquals(item.Key,documentKey)}).ToList();
            }
            long globalId=Equals(app.ActiveUIDocument?.Document,document) ? app.ActiveUIDocument?.ActiveView?.Id.Value ?? -1 : -1;
            var updated=project==null ? new List<ViewRow>() : project.Views.Where(view=>view.Open).Select(view=>ToRow(project,view)).ToList();
            long currentId=selection.Resolve(updated.Select(row=>new PaneViewCandidate {Id=row.Id,Aliases=row.Aliases,QualifiedAliases=row.QualifiedAliases}),allowGlobalFallback ? globalId : -1);
            foreach(var row in updated) if(row.Id==currentId) row.Detail+="  ·  View trong ô này";
            updated=updated.OrderBy(row=>row.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
            bool changed=forceRefresh || documentChanged || activeId!=currentId || paneCaption!=selection.Title || rows.Count!=updated.Count ||
                !rows.Zip(updated,(a,b)=>a.Id==b.Id && a.Name==b.Name && a.Detail==b.Detail).All(equal=>equal);
            activeId=currentId;
            if(documentKey!=null && currentId>=0)projectViewsInPane[documentKey]=currentId;
            paneCaption=selection.Title;
            CloseViewButton.IsEnabled=!closing && currentId>=0 && updated.Count>1;
            CloseViewButton.ToolTip=updated.Count<=1 ? "Revit cần giữ ít nhất một view của project đang mở" : "Đóng view đang hiển thị trong ô này";
            DragHandle.IsEnabled=!closing && currentId>=0;
            if(!changed) return;
            rows=updated;
            FilterRows();
            forceRefresh=false;
            if(!hasError || documentChanged) SetStatus(document==null ? "Mở project để xem danh sách view." : "Chọn view để chuyển trong ô này.");
        }
        private void ProjectTabClick(object sender, RoutedEventArgs args)
        {
            if(!((sender as FrameworkElement)?.DataContext is ProjectTabItem tab)) return;
            if(ReferenceEquals(tab.Key,documentKey))return;
            var project=App.TabHost.Catalog.Find(tab.Key);
            var info=project?.Views.FirstOrDefault();
            if(info!=null)
            {
                var request=ToRow(project,info);
                request.ProjectSwitch=true;
                QueueView(request);
            }
        }
        private void CloseProjectClick(object sender,RoutedEventArgs args)
        {
            if((sender as FrameworkElement)?.DataContext is ProjectTabItem tab)
            {
                dropdown.CancelPreview();
                App.TabHost.CloseProject(tab.Key,message=>{if(!disposed)SetStatus(message,true);});
            }
            args.Handled=true;
        }
        private void FilterRows()
        {
            if (ViewSelector == null || SearchBox == null || EmptyLabel == null) return;
            string query = SearchBox.Text.Trim();
            var filtered = rows.Where(row => row.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                row.Detail.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            updatingSelection = true;
            dropdown.IsRefreshing = true;
            try
            {
                ViewSelector.ItemsSource = filtered;
                ViewSelector.SelectedItem = filtered.FirstOrDefault(row => row.Id == activeId) ??
                    (query.Length > 0 ? filtered.FirstOrDefault() : null);
                ViewSelector.IsEnabled = filtered.Count > 0;
                EmptyLabel.Text = sourceDocument == null ? "Chưa có view đang mở" :
                    query.Length > 0 ? "Không có view phù hợp" : paneCaption ?? "Chọn view";
                EmptyLabel.Visibility = ViewSelector.SelectedItem == null ?
                    System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                ViewSelector.ToolTip = $"{filtered.Count} / {rows.Count} view đang mở";
            }
            finally { updatingSelection = false; dropdown.IsRefreshing = false; }
        }

        private void SearchChanged(object sender, TextChangedEventArgs args) => FilterRows();

        private void SearchKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Enter) { dropdown.CommitSelected(); args.Handled = true; }
            else if (args.Key == Key.Down && ViewSelector.SelectedItem != null)
            {
                ViewSelector.Focus();
                ViewSelector.IsDropDownOpen = true;
                args.Handled = true;
            }
            else if (args.Key == Key.Escape) { SearchBox.Clear(); args.Handled = true; }
        }

        private void QueueView(ViewRow row)
        {
            if (disposed || updatingSelection || row == null) return;
            LogSwitch("Chosen view=" + row.Id + "; paneView=" + activeId);
            // Always enter the API callback: an already active view may still
            // need to move into the WT pane where the user made the selection.
            handler.Pending = row;
            ExternalEventRequest result = api.Post(handler, handler.Execute);
            LogSwitch("ExternalEvent=" + result);
            if (result == ExternalEventRequest.Accepted || result == ExternalEventRequest.Pending)
                SetStatus(row.ProjectSwitch ? "Đang chuyển project cho ô này…" : "Đang chuyển đến: " + row.Name);
            else
            {
                handler.Pending = null;
                SetStatus("Revit đang bận. Hãy chọn lại view sau khi kết thúc lệnh hiện tại.", true);
            }
        }

        internal void InvalidateSelection() => forceRefresh = true;
        internal void DismissTransientUi()
        {
            dropdown.CancelPreview();
            ViewSelector.IsDropDownOpen=false;
        }

        private ViewRow CurrentPaneRow()
        {
            var selection = App.TabHost.ReadPaneSelection(this);
            if (selection == null) return null;
            long id = selection.Resolve(rows.Select(row =>
                new PaneViewCandidate { Id = row.Id, Aliases = row.Aliases, QualifiedAliases = row.QualifiedAliases }), -1);
            return rows.FirstOrDefault(row => row.Id == id);
        }

        private void CloseViewClick(object sender, RoutedEventArgs args)
        {
            if (disposed || closing) return;
            var row = CurrentPaneRow();
            if (row == null) { SetStatus("Bố cục vừa thay đổi. Hãy thử đóng view lại.", true); return; }
            dropdown.CancelPreview();
            handler.Pending = null;
            api.Cancel(handler);
            closing = true;
            CloseViewButton.IsEnabled = false;
            DragHandle.IsEnabled = false;
            var result = api.Post(closeKey, app =>
            {
                if (disposed) return;
                try
                {
                    var project = App.TabHost.Catalog.Find(row.ProjectKey);
                    if (project == null || !project.Document.IsValidObject || row.Generation != generation)
                        throw new InvalidOperationException("Project đã thay đổi. Hãy chọn lại view cần đóng.");
                    App.TabHost.ActivateProjectView(project,row.Id);
                    var uiDocument = app.ActiveUIDocument;
                    if(uiDocument==null || !Equals(uiDocument.Document,project.Document))
                        throw new InvalidOperationException("Revit chưa kích hoạt đúng project; không đóng view để tránh nhầm project.");
                    var open = uiDocument.GetOpenUIViews();
                    var target = open.FirstOrDefault(view => view.ViewId.Value == row.Id);
                    if (target == null) return;
                    if (open.Count <= 1)
                        throw new InvalidOperationException("Revit cần giữ ít nhất một view đang mở.");
                    LogSwitch("Close view=" + row.Id);
                    target.Close();
                }
                catch (Exception ex) { LogSwitch("CLOSE ERROR " + ex); SetStatus(ex.Message, true); }
                finally
                {
                    closing = false;
                    forceRefresh = true;
                    App.TabHost.RefreshAfterMove(app);
                }
            });
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
            {
                closing = false;
                forceRefresh = true;
                SetStatus("Revit đang bận. Hãy thử đóng view lại sau khi kết thúc lệnh.", true);
            }
        }

        private void DragHandleDown(object sender, MouseButtonEventArgs args)
        {
            var row = CurrentPaneRow();
            if (disposed || closing || row == null || documentKey == null) return;
            App.TabHost.DismissAllTransientUi();
            dragStart = args.GetPosition(DragHandle);
            dragRequest = new ViewDragRequest(documentKey, paneKey, row.Id);
            App.TabHost.CaptureDragPlacement(this,dragRequest,PointToScreen(args.GetPosition(this)));
            DragHandle.CaptureMouse();
            args.Handled = true;
        }
        private void DragHandleUp(object sender, MouseButtonEventArgs args) => EndDrag();
        private void EndDrag()
        {
            dragStart = null;
            dragRequest = null;
            DragHandle.ReleaseMouseCapture();
        }
        private void DragHandleMove(object sender, MouseEventArgs args)
        {
            if (dragStart == null || dragRequest == null || args.LeftButton != MouseButtonState.Pressed) return;
            Point current = args.GetPosition(DragHandle);
            if (Math.Abs(current.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var request = dragRequest;
            EndDrag();
            bool cancelled=false;
            QueryContinueDragEventHandler cancel=(origin,evt)=>{if(evt.EscapePressed){cancelled=true;evt.Action=DragAction.Cancel;}};
            GiveFeedbackEventHandler feedback=(origin,evt)=>
            {
                if(BrowserNativeInterop.GetCursorPos(out var point) && App.TabHost.DropTarget(point)!=null)
                {evt.UseDefaultCursors=false;System.Windows.Input.Mouse.SetCursor(Cursors.SizeAll);evt.Handled=true;}
            };
            DragDrop.AddQueryContinueDragHandler(DragHandle,cancel);
            DragDrop.AddGiveFeedbackHandler(DragHandle,feedback);
            try
            {
                var result=DragDrop.DoDragDrop(DragHandle,new DataObject(ViewDragRequest.Format,request),DragDropEffects.Move);
                if(result==DragDropEffects.None && !cancelled && !disposed &&
                    (BrowserNativeInterop.GetAsyncKeyState(1)&0x8000)==0 && BrowserNativeInterop.GetCursorPos(out var point))
                {
                    var target=App.TabHost.DropTarget(point);
                    if(target!=null && !ReferenceEquals(target,this))target.AcceptTileView(request);
                    else if(target==null || (ReferenceEquals(target,this) &&
                        (request.FloatingWindow!=IntPtr.Zero || PointFromScreen(new Point(point.X,point.Y)).Y>ActualHeight)))
                        App.TabHost.FloatView(request,point,message=>{if(!disposed)SetStatus(message,true);});
                }
            }
            finally
            {
                DragDrop.RemoveQueryContinueDragHandler(DragHandle,cancel);
                DragDrop.RemoveGiveFeedbackHandler(DragHandle,feedback);
                System.Windows.Input.Mouse.SetCursor(null);
                EndDrag();ResetDropHighlight();
            }
        }
        private ViewDragRequest ReadDrag(DragEventArgs args)
        {
            if (disposed || closing || !args.Data.GetDataPresent(ViewDragRequest.Format, false)) return null;
            var request = args.Data.GetData(ViewDragRequest.Format, false) as ViewDragRequest;
            return request != null && request.CanDrop(documentKey, paneKey) && rows.Any(row => row.Id == request.ViewId)
                ? request : null;
        }
        private void PaneDragOver(object sender, DragEventArgs args)
        {
            if(args.Data.GetDataPresent(BrowserViewDragRequest.Format,false))
            {
                var request=args.Data.GetData(BrowserViewDragRequest.Format,false) as BrowserViewDragRequest;
                bool valid=request!=null && App.TabHost.Catalog.Find(request.ProjectKey)!=null;
                args.Effects=valid ? DragDropEffects.Move : DragDropEffects.None;
                ViewActionRow.BorderBrush=valid ? MepPalette.Brush(MepPalette.DropHighlight) : System.Windows.Media.Brushes.Gray;
                args.Handled=true;
                return;
            }
            if (!args.Data.GetDataPresent(ViewDragRequest.Format, false)) return;
            bool allowed = ReadDrag(args) != null;
            args.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
            ViewActionRow.BorderBrush = allowed ? MepPalette.Brush(MepPalette.DropHighlight) :
                MepPalette.Brush(MepPalette.Stroke);
            args.Handled = true;
        }
        private void PaneDragLeave(object sender, DragEventArgs args) => ResetDropHighlight();
        private void ResetDropHighlight() => ViewActionRow.BorderBrush =
            MepPalette.Brush(MepPalette.Stroke);
        private void PaneDrop(object sender, DragEventArgs args)
        {
            if(args.Data.GetDataPresent(BrowserViewDragRequest.Format,false))
            {
                var browser=args.Data.GetData(BrowserViewDragRequest.Format,false) as BrowserViewDragRequest;
                ResetDropHighlight();
                args.Effects=browser==null ? DragDropEffects.None : DragDropEffects.Move;
                args.Handled=true;
                if(browser!=null)AcceptBrowserView(browser);
                return;
            }
            if (!args.Data.GetDataPresent(ViewDragRequest.Format, false)) return;
            var request = ReadDrag(args);
            ResetDropHighlight();
            args.Effects = request == null ? DragDropEffects.None : DragDropEffects.Move;
            args.Handled = true;
            if (request == null) return;
            dropdown.CancelPreview();
            // Queue the dragged view in the receiving pane. The existing native
            // swap operation puts its current view back into the source pane.
            QueueView(rows.First(row => row.Id == request.ViewId));
        }

        internal void AcceptBrowserView(BrowserViewDragRequest request)
        {
            if(disposed || closing)return;
            var project=App.TabHost.Catalog.Find(request.ProjectKey);
            var view=project?.Views.FirstOrDefault(item=>item.Id==request.ViewId);
            if(view==null){SetStatus("View đã thay đổi hoặc project đã đóng. Hãy kéo lại.",true);return;}
            dropdown.CancelPreview();
            QueueView(ToRow(project,view));
        }
        internal void AcceptTileView(ViewDragRequest request)
        {
            if(disposed || closing || !request.CanDrop(documentKey,paneKey))return;
            var row=rows.FirstOrDefault(view=>view.Id==request.ViewId);
            if(row!=null){dropdown.CancelPreview();QueueView(row);}
        }

        private void SetStatus(string message, bool error = false)
        {
            hasError = error;
            StatusLabel.Text = message;
            StatusLabel.Visibility = error ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            SearchBox.ToolTip = message;
            System.Windows.Automation.AutomationProperties.SetHelpText(ViewSelector, message);
        }

        private static void LogSwitch(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(AddinPaths.OutputDirectory, "view-switch.log"),
                    DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed = true;
            EndDrag();
            dropdown.Dispose();
            Unloaded -= PaneUnloaded;
            preview.Dispose();
            handler.Pending = null;
            api.Cancel(handler);
            api.Cancel(closeKey);
        }
        internal void Shutdown()
        {
            if(disposed)return;
            disposed=true;
            dragStart=null;dragRequest=null;
            Unloaded-=PaneUnloaded;
            handler.Pending=null;
            ++previewToken;
            api.Cancel(handler);api.Cancel(closeKey);api.Cancel(previewKey);
            ShutdownCleanup.Run("Dropdown shutdown",()=>dropdown.Dispose(true));
            ShutdownCleanup.Run("Preview shutdown",preview.Dispose);
        }

        private sealed class SwitchViewHandler : IExternalEventHandler
        {
            private readonly ViewSearchPane owner;
            internal ViewRow Pending;
            private long sequence;
            private sealed class Operation
            {
                internal ViewRow Request;
                internal long Sequence;
                internal Xceed.Wpf.AvalonDock.Layout.ILayoutContainer Destination;
                internal Dictionary<Xceed.Wpf.AvalonDock.Layout.ILayoutContainer,Xceed.Wpf.AvalonDock.Layout.LayoutDocument> Retained;
                internal PaneViewMove Move;
                internal bool Moved;
                internal int ActivationAttempts;
                internal int ViewRequests;
            }
            internal SwitchViewHandler(ViewSearchPane owner) { this.owner = owner; }
            public string GetName() => "MEP switch open view";

            public void Execute(UIApplication app)
            {
                var request = Pending;
                Pending = null;
                if (request == null || owner.disposed) return;
                long currentSequence=++sequence;
                try
                {
                    var project = App.TabHost.Catalog.Find(request.ProjectKey);
                    if (project == null || !project.Document.IsValidObject || request.Generation != owner.generation)
                        throw new InvalidOperationException("Project đã thay đổi. Hãy chọn lại view.");
                    bool projectSwitch=request.ProjectSwitch;
                    if(projectSwitch)
                    {
                        long preferred=owner.projectViewsInPane.TryGetValue(project.Key,out var remembered) ? remembered : project.LastActiveView;
                        var chosen=App.TabHost.ChooseProjectView(owner,project,preferred);
                        if(chosen==null)throw new InvalidOperationException("Project không còn view khác để hiển thị trong ô này; các view hiện có đang nằm ở ô khác.");
                        LogSwitch("Independent project switch: project="+project.Name+"; view="+chosen.Id+"; alreadyOpen="+chosen.Open);
                        request=owner.ToRow(project,chosen);
                    }
                    Continue(new Operation {Request=request,Sequence=currentSequence,
                        Destination=App.TabHost.PaneModel(owner),Retained=App.TabHost.CapturePaneSelections()},app);
                }
                catch (Exception ex)
                {
                    LogSwitch("ERROR " + ex);
                    owner.SetStatus(ex.Message, true);
                }
            }

            private void Defer(Operation operation)
            {
                owner.api.PostAfterUiTurn(this,app=>Continue(operation,app));
            }
            private void Continue(Operation operation,UIApplication app)
            {
                if(owner.disposed || operation.Sequence!=sequence)return;
                bool finished=false;
                try
                {
                    var request=operation.Request;
                    var project=App.TabHost.Catalog.Find(request.ProjectKey);
                    // Native activation can refresh this pane and advance its
                    // row generation before the next API turn. Keep the slot
                    // and request identity validated on entry.
                    if(project==null || !project.Document.IsValidObject)
                        throw new InvalidOperationException("Project hoặc ô view đã thay đổi. Hãy chọn lại view.");
                    var uiDocument=app.ActiveUIDocument;
                    if(uiDocument==null || !Equals(uiDocument.Document,project.Document))
                    {
                        if(operation.ActivationAttempts++>=4)
                            throw new InvalidOperationException("Revit chưa chuyển được project. Hãy kết thúc lệnh hiện tại rồi thử lại.");
                        long anchor=request.Id;
                        if(!operation.Moved && !new UIDocument(project.Document).GetOpenUIViews().Any(view=>view.ViewId.Value==anchor))
                        {
                            // Prefer a view already displayed in this project;
                            // preserve its selection while opening the new view.
                            var displayed=operation.Retained.Values.Select(PaneViewSelection.ReadDocument)
                                .FirstOrDefault(selection=>ReferenceEquals(App.TabHost.Catalog.Resolve(selection),project));
                            anchor=displayed?.Resolve(project.Views.Select(view=>new PaneViewCandidate
                                {Id=view.Id,Aliases=view.Aliases,QualifiedAliases=view.QualifiedAliases}),-1) ?? -1;
                            if(anchor<0)anchor=project.Views.First(view=>view.Open).Id;
                        }
                        App.TabHost.ActivateProjectView(project,anchor);
                        LogSwitch("Waiting for project activation; attempt="+operation.ActivationAttempts);
                        Defer(operation);
                        return;
                    }
                    var target=project.Document.GetElement(new ElementId(request.Id)) as Autodesk.Revit.DB.View;
                    if(target==null)throw new InvalidOperationException("View không còn tồn tại.");
                    if(!operation.Moved)
                    {
                        App.TabHost.DismissAllTransientUi();
                        if(!uiDocument.GetOpenUIViews().Any(view=>view.ViewId.Value==request.Id))
                        {
                            RequestActivation(operation,uiDocument,target);
                            return;
                        }
                        operation.Move=App.TabHost.MoveIntoPane(owner,new PaneViewCandidate
                            {Id=request.Id,Aliases=request.Aliases,QualifiedAliases=request.QualifiedAliases},
                            out var nativeDocument,operation.Destination,operation.Retained);
                        operation.Move.ActivateBoth(nativeDocument);
                        operation.Moved=true;
                        LogSwitch("Moved native view; verifying after Revit processes activation");
                        Defer(operation);
                        return;
                    }
                    if(uiDocument.ActiveView.Id.Value!=request.Id)
                    {
                        RequestActivation(operation,uiDocument,target);
                        return;
                    }
                    LogSwitch("Activated="+request.Id);
                    owner.SetStatus("Đang xem: "+request.Name);
                    finished=true;
                }
                catch(Exception ex)
                {
                    try{operation.Move?.Restore();}
                    catch(Exception restoreError){LogSwitch("RESTORE ERROR "+restoreError);}
                    LogSwitch("ERROR "+ex);
                    owner.SetStatus(ex.Message,true);
                    finished=true;
                }
                finally
                {
                    if(finished && !owner.disposed)
                    {
                        owner.forceRefresh=true;
                        App.TabHost.RefreshAfterMove(app);
                    }
                }
            }
            private void RequestActivation(Operation operation,UIDocument document,Autodesk.Revit.DB.View view)
            {
                if(operation.ViewRequests++>=6)
                    throw new InvalidOperationException("Revit chưa mở được view. Hãy kết thúc lệnh hiện tại rồi thử lại.");
                // Idling may run immediately after a native browser gesture.
                // Let Revit process activation after leaving the API callback.
                document.RequestViewChange(view);
                LogSwitch("Requested asynchronous view activation="+view.Id.Value);
                Defer(operation);
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowViewSearchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                App.TabHost.Enable(data.Application);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}




