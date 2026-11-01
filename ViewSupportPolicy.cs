namespace MEP_Review_Tab_View_Ver1
{
    internal enum ViewPreviewMode { ImageExport, Table, Information, Excluded }
    internal static class ViewSupportPolicy
    {
        internal static ViewPreviewMode Get(string type, bool template = false, bool revisionSchedule = false)
        {
            if (template || revisionSchedule) return ViewPreviewMode.Excluded;
            switch (type)
            {
                case "Schedule": case "PanelSchedule": return ViewPreviewMode.Table;
                case "FloorPlan": case "CeilingPlan": case "EngineeringPlan": case "AreaPlan":
                case "ThreeD": case "Section": case "Elevation": case "Detail": case "DraftingView":
                case "Legend": case "DrawingSheet": case "Rendering": case "ColumnSchedule":
                    return ViewPreviewMode.ImageExport;
                case "Walkthrough": case "Report": case "CostReport": case "LoadsReport":
                case "PresureLossReport": case "SystemsAnalysisReport": return ViewPreviewMode.Information;
                default: return ViewPreviewMode.Excluded;
            }
        }
    }
}
