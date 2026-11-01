using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MEP_Review_Tab_View_Ver1
{
    // UIFramework is internal to Revit. Validate all properties before applying.
    internal static class TabAppearance
    {
        private sealed class SavedColor
        {
            internal object Theme;
            internal PropertyInfo Property;
            internal object Value;
        }

        private static readonly List<SavedColor> Originals = new List<SavedColor>();
        private static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
        {
            { "CanvasActiveTabNormalBackgroundColor", "#FFFFFF" },
            { "CanvasActiveTabHoverBackgroundColor", "#E5F1FB" },
            { "CanvasSelectedTabNormalBackgroundColor", "#E5E5E5" },
            { "CanvasSelectedTabHoverBackgroundColor", "#DCEAF7" },
            { "CanvasUnactiveTabNormalBackgroundColor", "#D9D9D9" }
        };

        internal static void Apply()
        {
            object theme = UIFramework.ApplicationTheme.CurrentTheme;
            var pending = new List<(PropertyInfo Property, System.Windows.Media.Color Color)>();
            foreach (var entry in Palette)
            {
                var property = theme.GetType().GetProperty(entry.Key);
                if (property == null || property.GetSetMethod() == null ||
                    property.PropertyType != typeof(System.Windows.Media.Color))
                    throw new NotSupportedException("Unsupported tab color property: " + entry.Key);
                pending.Add((property, (System.Windows.Media.Color)ColorConverter.ConvertFromString(entry.Value)));
            }

            // Applying twice must preserve the original palette for Reset.
            foreach (var item in pending)
            {
                if (!Originals.Exists(saved => ReferenceEquals(saved.Theme, theme) &&
                    saved.Property.Name == item.Property.Name))
                    Originals.Add(new SavedColor { Theme = theme, Property = item.Property,
                        Value = item.Property.GetValue(theme) });
            }
            try
            {
                foreach (var item in pending)
                    item.Property.SetValue(theme, item.Color);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        internal static void Restore()
        {
            var errors = new List<Exception>();
            for (int i = Originals.Count - 1; i >= 0; --i)
            {
                var saved = Originals[i];
                try
                {
                    saved.Property.SetValue(saved.Theme, saved.Value);
                    Originals.RemoveAt(i);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0)
                throw new AggregateException("Some tab colors could not be restored.", errors);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class StyleTabsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { TabAppearance.Apply(); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ResetTabsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try { App.TabHost.Disable(); TabAppearance.Restore(); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }
}

