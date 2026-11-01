using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class SchedulePreviewRenderer
    {
        internal static BitmapSource Render(string title, IReadOnlyList<string[]> rows, int headerRows, int totalRows, int totalColumns)
        {
            int columns = Math.Max(1, Math.Min(8, rows.Count == 0 ? 1 : rows.Max(row => row.Length)));
            int width = Math.Max(640, columns * 115);
            int height = 80 + Math.Max(1, rows.Count) * 32;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0,0,width,height));
                Text(drawing, title, new Point(12,9), width - 24, 17, true);
                double cell = (width - 24d) / columns;
                var pen = new Pen(MepPalette.Brush(MepPalette.Stroke), 1);
                for (int r = 0; r < rows.Count; ++r)
                    for (int c = 0; c < columns; ++c)
                    {
                        var rect = new Rect(12 + c * cell, 40 + r * 32, cell, 32);
                        drawing.DrawRectangle(r < headerRows ? MepPalette.Brush(MepPalette.Selected) : Brushes.White, pen, rect);
                        Text(drawing, c < rows[r].Length ? rows[r][c] : "", new Point(rect.X+5,rect.Y+7), cell-10, 12, r < headerRows);
                    }
                if (rows.Count == 0) Text(drawing, "Bảng chưa có dữ liệu", new Point(12,45), width-24, 14, false);
                Text(drawing, $"Xem trước {rows.Count}/{totalRows} dòng · {Math.Min(columns,totalColumns)}/{totalColumns} cột", new Point(12,height-27), width-24, 12, false);
            }
            var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        private static void Text(DrawingContext drawing, string value, Point point, double width, double size, bool bold)
        {
            var text = new FormattedText((value ?? "").Replace('\r',' ').Replace('\n',' '), CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,
                    bold ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal), size, Brushes.Black, 1)
                { MaxTextWidth = Math.Max(1,width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(text, point);
        }
    }
}

