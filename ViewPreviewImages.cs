using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class ViewPreviewImages
    {
        private sealed class CachedImage
        {
            internal DateTime Created;
            internal BitmapSource Image;
        }
        private static readonly ConditionalWeakTable<Document, Dictionary<long, CachedImage>> Cache =
            new ConditionalWeakTable<Document, Dictionary<long, CachedImage>>();
        private static readonly ConditionalWeakTable<Document,Dictionary<long,string>> Results=new ConditionalWeakTable<Document,Dictionary<long,string>>();
        internal static string Result(Document document,long id) =>
            Results.TryGetValue(document,out var result) && result.TryGetValue(id,out var value) ? value : null;
        internal static void Invalidate(Document document) => Cache.Remove(document);

        internal static BitmapSource Load(Document document, long viewId)
        {
            var images = Cache.GetOrCreateValue(document);
            if (images.TryGetValue(viewId, out var saved) && DateTime.UtcNow - saved.Created < TimeSpan.FromSeconds(30))
                return saved.Image;
            var view = document.GetElement(new ElementId(viewId)) as View;
            if (view == null || view.IsTemplate)
                throw new InvalidOperationException("View này chưa hỗ trợ ảnh xem trước.");
            if (view is ViewSchedule || view is PanelScheduleView)
            {
                try
                {
                    var table = ReadTable(view);
                    if(images.Count>=20)images.Remove(images.OrderBy(pair=>pair.Value.Created).First().Key);
                    images[viewId] = new CachedImage { Created = DateTime.UtcNow, Image = table };
                    Results.GetOrCreateValue(document)[viewId]="Table preview succeeded";
                    return table;
                }
                catch(Exception ex){Results.GetOrCreateValue(document)[viewId]="Table preview failed: "+ex.Message;throw;}
            }
            var support = ViewSupportPolicy.Get(view.ViewType.ToString());
            if (support == ViewPreviewMode.Information || support == ViewPreviewMode.Excluded)
                throw new InvalidOperationException("Loại " + view.ViewType +
                    " không có preview ảnh tĩnh. Dùng Revit để xem nội dung; báo cáo chi tiết nằm trong thư mục view-audit.");
            if (document.IsModifiable)
                throw new InvalidOperationException("Kết thúc lệnh đang chạy để tạo ảnh xem trước.");

            string directory = Path.Combine(Path.GetTempPath(), "MEP-ViewPreview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var options = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(directory, "preview"),
                    ZoomType = ZoomFitType.FitToPage,
                    FitDirection = FitDirectionType.Horizontal,
                    PixelSize = 720,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_72,
                    ShouldCreateWebSite = false
                })
                {
                    options.SetViewsAndSheets(new List<ElementId> { view.Id });
                    // SetOfViews exports the requested view without activating it
                    // and does not change WT pane locations or edit the document.
                    document.ExportImage(options);
                }
                string path = Directory.EnumerateFiles(directory, "*.png").FirstOrDefault();
                if (path == null) throw new InvalidOperationException("Revit chưa tạo được ảnh xem trước cho view này.");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                if (images.Count >= 20)
                    images.Remove(images.OrderBy(pair => pair.Value.Created).First().Key);
                images[viewId] = new CachedImage { Created = DateTime.UtcNow, Image = bitmap };
                Results.GetOrCreateValue(document)[viewId]="Image preview succeeded";
                return bitmap;
            }
            catch(Exception ex){Results.GetOrCreateValue(document)[viewId]="Image preview failed: "+ex.Message;throw;}
            finally
            {
                // This directory was created uniquely for this one export.
                // Delete only its generated files, never recurse into other paths.
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
                    Directory.Delete(directory, false);
                }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
        }

        private static BitmapSource ReadTable(View view)
        {
            TableData data = view is ViewSchedule schedule ? schedule.GetTableData() :
                ((PanelScheduleView)view).GetTableData();
            var tableView = (TableView)view;
            var rows = new List<string[]>();
            int headers = 0, totalRows = 0, totalColumns = 0;
            foreach (var section in new[] { SectionType.Header, SectionType.Body })
            {
                var part = data.GetSectionData(section);
                totalRows += part.NumberOfRows;
                totalColumns = Math.Max(totalColumns, part.NumberOfColumns);
                if(part.NumberOfRows==0 || part.NumberOfColumns==0)continue;
                for (int r = part.FirstRowNumber; r <= part.LastRowNumber && rows.Count < 16; ++r)
                {
                    var cells = new List<string>();
                    for (int c = part.FirstColumnNumber; c <= part.LastColumnNumber && cells.Count < 8; ++c)
                        cells.Add(tableView.GetCellText(section,r,c));
                    rows.Add(cells.ToArray());
                    if (section == SectionType.Header) ++headers;
                }
            }
            return SchedulePreviewRenderer.Render(view.Name, rows, headers, totalRows, totalColumns);
        }
    }
}
