using System.Windows.Media;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class MepPalette
    {
        internal const string Background="#F5F5F5",Surface="#FFFFFF",Stroke="#B8B8B8",Accent="#0078D4",
            Ink="#222222",Muted="#6B6B6B",Selected="#DCEAF7",DropHighlight="#0078D4",Error="#B3261E";
        internal static SolidColorBrush Brush(string color)
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));brush.Freeze();return brush;
        }
    }
}
