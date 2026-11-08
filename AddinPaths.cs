using System;
using System.IO;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class AddinPaths
    {
        internal static string DiagnosticDirectory { get; set; }
        internal static string OutputDirectory
        {
            get
            {
                var directory=DiagnosticDirectory ?? Path.GetDirectoryName(typeof(App).Assembly.Location);
                if(string.IsNullOrEmpty(directory))directory=Path.Combine(Path.GetTempPath(),"MEPViewTabs");
                Directory.CreateDirectory(directory);
                return directory;
            }
        }
    }
}
