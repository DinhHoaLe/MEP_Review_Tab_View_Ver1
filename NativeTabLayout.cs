using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace MEP_Review_Tab_View_Ver1
{
    // This file only depends on WPF, so replacement and rollback can be tested
    // without a live Revit session. Never remove Revit's own tab objects.
    internal sealed class NativeTabLayout : IDisposable
    {
        private readonly List<(UIElement Element, double Opacity, bool HitTest, KeyboardNavigationMode Navigation)> hidden;
        private readonly RowDefinition row;
        private readonly GridLength height;
        private readonly double minimum;
        private readonly double maximum;
        private readonly FrameworkElement movedContent;
        private readonly int originalContentRow;
        private readonly int originalContentSpan;
        private readonly Thickness originalContentMargin;
        internal Grid Grid { get; }
        internal FrameworkElement Replacement { get; }

        private NativeTabLayout(Grid grid, int headerRow, FrameworkElement replacement,
            FrameworkElement contentBranch, bool moveContent)
        {
            Grid = grid;
            Replacement = replacement;
            hidden = grid.Children.Cast<UIElement>()
                .Where(child => System.Windows.Controls.Grid.GetRow(child) == headerRow &&
                    System.Windows.Controls.Grid.GetRowSpan(child) == 1 && !ReferenceEquals(child, contentBranch))
                .Select(child => (child, child.Opacity, child.IsHitTestVisible, KeyboardNavigation.GetTabNavigation(child))).ToList();
            row = grid.RowDefinitions[headerRow];
            height = row.Height;
            minimum = row.MinHeight;
            maximum = row.MaxHeight;
            if (moveContent)
            {
                movedContent = contentBranch;
                originalContentRow = System.Windows.Controls.Grid.GetRow(contentBranch);
                originalContentSpan = System.Windows.Controls.Grid.GetRowSpan(contentBranch);
                originalContentMargin = contentBranch.Margin;
            }
            System.Windows.Controls.Grid.SetRow(replacement, headerRow);
            System.Windows.Controls.Grid.SetColumn(replacement, 0);
            System.Windows.Controls.Grid.SetColumnSpan(replacement, Math.Max(1, grid.ColumnDefinitions.Count));
            try
            {
                grid.Children.Add(replacement);
                if (movedContent != null)
                {
                    // Some Revit themes draw the content border across BOTH rows,
                    // offset by a top margin equal to the old tab height. Give it
                    // the actual content row instead of treating that as an unknown layout.
                    movedContent.SetCurrentValue(System.Windows.Controls.Grid.RowProperty, headerRow + 1);
                    movedContent.SetCurrentValue(System.Windows.Controls.Grid.RowSpanProperty, originalContentSpan - 1);
                    movedContent.SetCurrentValue(FrameworkElement.MarginProperty,
                        new Thickness(originalContentMargin.Left, 0, originalContentMargin.Right, originalContentMargin.Bottom));
                }
                EnsureHidden();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static NativeTabLayout TryAttach(FrameworkElement pane, FrameworkElement tabs,
            FrameworkElement content, FrameworkElement replacement)
        {
            // Locate the first common Grid where header and content occupy separate rows.
            // Reject unknown layouts instead of placing controls over the drawing canvas.
            for (DependencyObject ancestor = VisualTreeHelper.GetParent(tabs);
                 ancestor != null && !ReferenceEquals(ancestor, pane);
                 ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (!(ancestor is Grid grid)) continue;
                var headerBranch = DirectChild(grid, tabs);
                var contentBranch = DirectChild(grid, content);
                if (headerBranch == null || contentBranch == null || ReferenceEquals(headerBranch, contentBranch)) continue;
                int headerRow = System.Windows.Controls.Grid.GetRow(headerBranch);
                int contentRow = System.Windows.Controls.Grid.GetRow(contentBranch);
                if (headerRow >= grid.RowDefinitions.Count ||
                    System.Windows.Controls.Grid.GetRowSpan(headerBranch) != 1) continue;
                int contentSpan = System.Windows.Controls.Grid.GetRowSpan(contentBranch);
                bool separateRows = contentRow > headerRow;
                bool overlappingBorder = contentRow == headerRow && contentSpan > 1 &&
                    contentRow + contentSpan <= grid.RowDefinitions.Count && contentBranch is FrameworkElement;
                if (!separateRows && !overlappingBorder) continue;
                // Leave decorative borders spanning the whole pane in place. Only
                // hide controls confined to the header row, never the drawing border.
                return new NativeTabLayout(grid, headerRow, replacement,
                    contentBranch as FrameworkElement, overlappingBorder);
            }
            return null;
        }

        private static UIElement DirectChild(Grid grid, DependencyObject descendant)
        {
            var current = descendant;
            while (current != null)
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (ReferenceEquals(parent, grid)) return current as UIElement;
                current = parent;
            }
            return null;
        }

        internal void EnsureHidden()
        {
            foreach (var saved in hidden)
            {
                // Revit may require a visible native tab to activate its view.
                // Keep Visibility/layout intact; only suppress rendering and input.
                saved.Element.SetCurrentValue(UIElement.OpacityProperty, 0d);
                saved.Element.SetCurrentValue(UIElement.IsHitTestVisibleProperty, false);
                saved.Element.SetCurrentValue(KeyboardNavigation.TabNavigationProperty, KeyboardNavigationMode.None);
            }
            row.SetCurrentValue(RowDefinition.HeightProperty, GridLength.Auto);
            row.SetCurrentValue(RowDefinition.MinHeightProperty, 0d);
            row.SetCurrentValue(RowDefinition.MaxHeightProperty, double.PositiveInfinity);
        }

        public void Dispose()
        {
            Grid.Children.Remove(Replacement);
            foreach (var saved in hidden)
            {
                saved.Element.SetCurrentValue(UIElement.OpacityProperty, saved.Opacity);
                saved.Element.SetCurrentValue(UIElement.IsHitTestVisibleProperty, saved.HitTest);
                saved.Element.SetCurrentValue(KeyboardNavigation.TabNavigationProperty, saved.Navigation);
            }
            row.SetCurrentValue(RowDefinition.HeightProperty, height);
            row.SetCurrentValue(RowDefinition.MinHeightProperty, minimum);
            row.SetCurrentValue(RowDefinition.MaxHeightProperty, maximum);
            if (movedContent != null)
            {
                movedContent.SetCurrentValue(System.Windows.Controls.Grid.RowProperty, originalContentRow);
                movedContent.SetCurrentValue(System.Windows.Controls.Grid.RowSpanProperty, originalContentSpan);
                movedContent.SetCurrentValue(FrameworkElement.MarginProperty, originalContentMargin);
            }
        }

        internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var pending = new Stack<DependencyObject>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                yield return node;
                for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; --i)
                    pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }
}
