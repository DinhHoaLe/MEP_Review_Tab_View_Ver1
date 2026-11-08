using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.IO;

namespace MEP_Review_Tab_View_Ver1
{
    // Replace a view-node drag before the native tree starts Revit's "place on
    // sheet" command. All other nodes, clicks and other processes pass through.
    internal sealed class NativeBrowserDrag : IDisposable
    {
        private readonly Func<BrowserNativeInterop.ScreenPoint,bool> browserPoint;
        private readonly Func<string,BrowserViewDragRequest> resolve;
        private readonly Func<BrowserNativeInterop.ScreenPoint,ViewSearchPane> target;
        private readonly Func<Task<BrowserViewDragRequest>> capture;
        private readonly FrameworkElement root;
        private readonly BrowserNativeInterop.MouseHook callback;
        private IntPtr hook,tree;
        private BrowserNativeInterop.ScreenPoint start;
        private BrowserNativeInterop.ScreenPoint lastPoint;
        private bool tracking,claiming,running,disposed;
        private Task<string> automationSelection;
        private Task<BrowserViewDragRequest> apiSelection;
        private long session;
        private BrowserNativeInterop.ScreenPoint? releasedPoint;
        internal NativeBrowserDrag(FrameworkElement root,Func<BrowserNativeInterop.ScreenPoint,bool> browserPoint,
            Func<string,BrowserViewDragRequest> resolve,Func<BrowserNativeInterop.ScreenPoint,ViewSearchPane> target,
            Func<Task<BrowserViewDragRequest>> capture)
        {
            this.root=root;this.browserPoint=browserPoint;this.resolve=resolve;this.target=target;
            this.capture=capture;
            callback=Mouse;
            hook=BrowserNativeInterop.SetWindowsHookEx(14,callback,BrowserNativeInterop.GetModuleHandle(null),0);
            Log("Browser drag hook "+(hook==IntPtr.Zero ? "unavailable" : "installed"));
            if(hook==IntPtr.Zero) System.Diagnostics.Trace.WriteLine("Project Browser drag hook unavailable: "+Marshal.GetLastWin32Error());
        }
        private IntPtr Mouse(int code,IntPtr message,IntPtr data)
        {
            if(code<0 || disposed) return BrowserNativeInterop.CallNextHookEx(hook,code,message,data);
            try
            {
                int kind=message.ToInt32();
                var mouse=Marshal.PtrToStructure<BrowserNativeInterop.MouseData>(data);
                if((kind==0x0201 || kind==0x0204) && claiming && !running)
                {
                    ++session;claiming=false;tracking=false;releasedPoint=null;
                }
                if(kind==0x0201 && !claiming)
                {
                    tracking=false;
                    if(BrowserNativeInterop.IsOwnWindowAt(mouse.Point) && browserPoint(mouse.Point))
                    {
                        tree=BrowserNativeInterop.TreeAt(mouse.Point);
                        tracking=true;
                        start=mouse.Point;
                        lastPoint=start;
                        ++session;
                        releasedPoint=null;
                        BrowserNativeInterop.GetAsyncKeyState(27);
                        var point=start;
                        automationSelection=tree==IntPtr.Zero ? Task.Run(()=>BrowserAutomationProbe.Caption(point)) : null;
                        apiSelection=CaptureAfterClick(session);
                    }
                }
                else if(kind==0x0202)
                {
                    // While waiting for Revit, consumed LL mouse moves do not
                    // update the OS cursor. Mouse-up then carries the old cursor
                    // location. Preserve the intended point from the move event.
                    if((tracking || claiming) && !running)releasedPoint=claiming ? lastPoint : mouse.Point;
                    if(!claiming)tracking=false;
                }
                else if(kind==0x0200)
                {
                    if(tracking || claiming)lastPoint=mouse.Point;
                    if(claiming && !running) return new IntPtr(1);
                    if(tracking && !claiming &&
                        (Math.Abs(mouse.Point.X-start.X)>Math.Max(1,SystemParameters.MinimumHorizontalDragDistance/2) ||
                         Math.Abs(mouse.Point.Y-start.Y)>Math.Max(1,SystemParameters.MinimumVerticalDragDistance/2)))
                    {
                        tracking=false;
                        if(tree==IntPtr.Zero && automationSelection!=null)
                        {
                            claiming=true;
                            long token=session;
                            _=FinishSelection(token,apiSelection,automationSelection);
                            return new IntPtr(1);
                        }
                        string nativeCaption=BrowserNativeInterop.SelectedText(tree);
                        var request=resolve(nativeCaption);
                        root.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>Log("Native browser node="+nativeCaption+"; resolved="+(request!=null))));
                        if(request!=null)
                        {
                            claiming=true;
                            root.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>Begin(request)));
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);tracking=false;claiming=false;}
            return BrowserNativeInterop.CallNextHookEx(hook,code,message,data);
        }
        private Task<BrowserViewDragRequest> CaptureAfterClick(long token)
        {
            var completion=new TaskCompletionSource<BrowserViewDragRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Delay(90).ContinueWith(delay=>root.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>
            {
                if(disposed || session!=token){completion.TrySetResult(null);return;}
                capture().ContinueWith(result=>completion.TrySetResult(result.Status==TaskStatus.RanToCompletion ? result.Result : null));
            })));
            return completion.Task;
        }
        private async Task FinishSelection(long token,Task<BrowserViewDragRequest> apiProbe,Task<string> accessible)
        {
            // ExternalEvent may be serviced only after mouse release. A short
            // timeout discarded a valid gesture before its selection arrived.
            await apiProbe.ConfigureAwait(false);
            var selected=apiProbe.Status==TaskStatus.RanToCompletion ? apiProbe.Result : null;
            string caption=accessible?.Status==TaskStatus.RanToCompletion ? accessible.Result : null;
            _=root.Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>
            {
                if(disposed || session!=token)return;
                var request=selected??resolve(caption);
                Log("API state="+apiProbe.Status+"; selection="+selected?.Name+"; accessibility="+caption+"; resolved="+(request!=null));
                if(request==null){claiming=false;return;}
                // Revit may service ExternalEvent only after the button is
                // released. Retain the actual drop point instead of losing
                // that completed gesture while waiting for the API selection.
                if((BrowserNativeInterop.GetAsyncKeyState(1)&0x8000)==0)
                {
                    CompleteReleasedDrop(request);
                    claiming=false;return;
                }
                Begin(request);
            }));
        }
        private void Begin(BrowserViewDragRequest request)
        {
            if(disposed){claiming=false;return;}
            if((BrowserNativeInterop.GetAsyncKeyState(1)&0x8000)==0)
            {CompleteReleasedDrop(request);claiming=false;return;}
            Log("Begin view drag "+request.Name+"; id="+request.ViewId);
            bool cancelled=false;
            var label=new TextBlock {Text="Mở view: "+request.Name,Margin=new Thickness(8,5,8,5),Foreground=MepPalette.Brush(MepPalette.Ink),
                MaxWidth=360,TextTrimming=TextTrimming.CharacterEllipsis};
            var ghost=new Popup {Placement=PlacementMode.AbsolutePoint,AllowsTransparency=true,StaysOpen=true,
                IsHitTestVisible=false,Focusable=false,Child=new Border {Background=MepPalette.Brush(MepPalette.Selected),BorderBrush=MepPalette.Brush(MepPalette.Accent),
                    BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Child=label}};
            QueryContinueDragEventHandler cancel=(sender,args)=>
            {
                if(args.EscapePressed || disposed){cancelled=true;args.Action=DragAction.Cancel;}
            };
            GiveFeedbackEventHandler feedback=(sender,args)=>
            {
                if(!BrowserNativeInterop.GetCursorPos(out var point))return;
                var transform=PresentationSource.FromVisual(root)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                Point screen=transform.Transform(new Point(point.X,point.Y));
                ghost.HorizontalOffset=screen.X+18;ghost.VerticalOffset=screen.Y+18;ghost.IsOpen=true;
                if(target(point)!=null)
                {
                    args.UseDefaultCursors=false;
                    System.Windows.Input.Mouse.SetCursor(Cursors.SizeAll);
                    args.Handled=true;
                }
            };
            DragDrop.AddQueryContinueDragHandler(root,cancel);
            DragDrop.AddGiveFeedbackHandler(root,feedback);
            try
            {
                BrowserNativeInterop.ReleaseCapture();
                running=true;
                var result=DragDrop.DoDragDrop(root,new DataObject(BrowserViewDragRequest.Format,request),DragDropEffects.Move);
                if(result==DragDropEffects.None && !cancelled && !disposed &&
                    (BrowserNativeInterop.GetAsyncKeyState(1)&0x8000)==0 && BrowserNativeInterop.GetCursorPos(out var point))
                {
                    var pane=target(point);
                    Log("Canvas drop; pane="+(pane!=null));
                    pane?.AcceptBrowserView(request);
                }
            }
            catch(Exception ex){Log("ERROR "+ex);}
            finally
            {
                ghost.IsOpen=false;ghost.Child=null;
                System.Windows.Input.Mouse.SetCursor(null);
                DragDrop.RemoveQueryContinueDragHandler(root,cancel);
                DragDrop.RemoveGiveFeedbackHandler(root,feedback);
                claiming=false;running=false;tracking=false;
            }
        }
        public void Dispose()
        {
            disposed=true;
            if(hook!=IntPtr.Zero){BrowserNativeInterop.UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
        }
        private void CompleteReleasedDrop(BrowserViewDragRequest request)
        {
            if(disposed || (BrowserNativeInterop.GetAsyncKeyState(27)&0x8001)!=0)return;
            var point=releasedPoint;
            if(!point.HasValue && BrowserNativeInterop.GetCursorPos(out var cursor))point=cursor;
            var pane=point.HasValue ? target(point.Value) : null;
            Log("Released browser drop; view="+request.ViewId+"; point="+point?.X+","+point?.Y+"; pane="+(pane!=null));
            pane?.AcceptBrowserView(request);
        }
        private static void Log(string text)
        {
            try{File.AppendAllText(Path.Combine(AddinPaths.OutputDirectory,"browser-drag.log"),DateTimeOffset.Now.ToString("O")+" "+text+Environment.NewLine);}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);}
        }
    }
}

