using System;
using System.IO;

namespace MEP_Review_Tab_View_Ver1
{
    internal static class ShutdownCleanup
    {
        internal static void Run(string stage,Action cleanup)
        {
            try{cleanup();}
            catch(Exception ex)
            {
                // Revit is already tearing down WPF/native windows. Release
                // remaining subscriptions even if one window has gone away.
                System.Diagnostics.Trace.WriteLine(stage+": "+ex);
                try{File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(ShutdownCleanup).Assembly.Location),"shutdown-cleanup.log"),DateTimeOffset.Now+" "+stage+": "+ex+Environment.NewLine);}
                catch(Exception logError){System.Diagnostics.Trace.WriteLine(logError);}
            }
        }
    }
}
