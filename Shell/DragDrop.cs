using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using KillerPDF.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // ============================================================
        // Drag/drop: file open
        // ============================================================

        internal void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // internal: PdfViewer's XAML binds these three and forwards to them.
        internal void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                OnPathsDropped((string[])e.Data.GetData(DataFormats.FileDrop)!);
                e.Handled = true;   // don't let the same drop bubble to the window-level handler
            }
        }

        internal void DropZone_Click(object sender, MouseButtonEventArgs e) => Open_Click(sender, e);

        // ============================================================
        // Drag/drop: page reorder
        // ============================================================

        private bool _pageDragArmed;
        private int _pageDropInsertionIndex = -1;
        private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            // Only arm a page-reorder drag when the press lands on a page thumbnail, not the
            // scrollbar - otherwise grabbing the scrollbar starts a page-move drag (the "insert"
            // cursor) instead of scrolling.
            _pageDragArmed = false;
            for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is System.Windows.Controls.Primitives.ScrollBar) break;
                if (d is ListBoxItem) { _pageDragArmed = true; break; }
            }
        }

        private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_pageDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (PageList.SelectedIndex >= 0)
                {
                    try { DragDrop.DoDragDrop(PageList, PageList.SelectedIndex, DragDropEffects.Move); }
                    finally { HidePageDropIndicator(); }
                }
            }
        }

        private void PageList_DragOver(object sender, DragEventArgs e)
        {
            // #172: files dropped onto the Pages sidebar append to the open document,
            // so the list accepts FileDrop as well as its own page-reorder payload.
            if (e.Data.GetDataPresent(typeof(int)))
            {
                e.Effects = DragDropEffects.Move;
                ShowPageDropIndicator(e.GetPosition(PageList));
            }
            else if (_doc != null && DroppedOpenablePaths(e).Length > 0)
            {
                e.Effects = DragDropEffects.Copy;
                HidePageDropIndicator();
            }
            else
            {
                e.Effects = DragDropEffects.None;
                HidePageDropIndicator();
            }
            e.Handled = true;
        }

        private void PageList_DragLeave(object sender, DragEventArgs e) => HidePageDropIndicator();

        /// <summary>Returns the insertion slot from 0 through Count and the matching visual Y.
        /// Both the marker and the drop consume this result so what the user sees is authoritative.</summary>
        private (int Index, double Y)? PageDropSlot(Point position)
        {
            ListBoxItem? last = null;
            int lastIndex = -1;
            for (int i = 0; i < PageList.Items.Count; i++)
            {
                if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item) continue;
                var top = item.TranslatePoint(new Point(0, 0), PageList);
                if (position.Y < top.Y + item.ActualHeight / 2)
                    return (i, item.TranslatePoint(new Point(0, 0), PageListFadeHost).Y);
                last = item;
                lastIndex = i;
            }
            if (last is null) return null;
            double bottom = last.TranslatePoint(new Point(0, last.ActualHeight), PageListFadeHost).Y;
            return (lastIndex + 1, bottom);
        }

        private void ShowPageDropIndicator(Point position)
        {
            var slot = PageDropSlot(position);
            if (slot is null) { HidePageDropIndicator(); return; }
            _pageDropInsertionIndex = slot.Value.Index;
            if (PageDropIndicator.RenderTransform is TranslateTransform move)
                move.Y = Math.Max(0, Math.Min(PageListFadeHost.ActualHeight - PageDropIndicator.Height,
                    slot.Value.Y - PageDropIndicator.Height / 2));
            PageDropIndicator.Visibility = Visibility.Visible;
        }

        private void HidePageDropIndicator()
        {
            PageDropIndicator.Visibility = Visibility.Collapsed;
            _pageDropInsertionIndex = -1;
        }

        private static string[] DroppedOpenablePaths(DragEventArgs e)
            => e.Data.GetDataPresent(DataFormats.FileDrop)
                ? ((string[])e.Data.GetData(DataFormats.FileDrop)!).Where(IsOpenablePath).ToArray()
                : [];

        // #172: append the dropped files' pages to the open document. Appending (not inserting at
        // the drop point) keeps existing page indices stable, so annotations and rotations need no
        // remapping.
        private async void AppendFilesToCurrentDoc(string[] files)
        {
            if (_doc is null) return;
            CommitActiveTextBox();
            var imports = new List<PdfEngineIntegration.ImportedDocument>();
            foreach (var f in files)
            {
                string? importPath = null;
                if (PdfImport.IsPdfPath(f))
                {
                    importPath = f;
                    try { PdfEngineIntegration.ValidateDocument(importPath); }
                    catch { importPath = await RepairPdfForImportAsync(f); }
                }
                else
                {
                    try
                    {
                        importPath = App.MakeTempFile("dropimage");
                        File.WriteAllBytes(importPath, PdfEngineIntegration.MergeFiles([f]));
                    }
                    catch { importPath = null; }
                }
                if (importPath is null) continue;
                try
                {
                    var pages = PdfEngineIntegration.ReadPageInformation(importPath);
                    imports.Add(new PdfEngineIntegration.ImportedDocument(importPath,
                        pages.Select(page => page.Rotation).ToArray()));
                }
                catch { /* skip anything still unreadable after repair */ }
            }
            if (_doc is null) return;
            if (imports.Count == 0) { SetStatus(Loc("Str_Drop_NothingOpenable")); return; }
            SaveTempAndReload(
                keepAnnotations: true,
                preserveZoom: true,
                finalizeSavedFile: path => PdfEngineIntegration.AppendDocuments(path, imports),
                remapRotations: rotations =>
                    PdfEngineIntegration.RemapRotationsAfterDocumentAppend(rotations, imports));
            SetStatus(string.Format(Loc("Str_Status_Merged"), files.Length));
        }

        /// <summary>
        /// Runs the open path's three repair strategies against a dropped file and returns the
        /// repaired temp copy, or null if the user declined or nothing recovered it. The original
        /// file is never written to.
        /// </summary>
        private async System.Threading.Tasks.Task<string?> RepairPdfForImportAsync(string path)
        {
            var ask = KillerDialog.Show(this,
                string.Format(Loc("Str_Dlg_RepairAsk"), System.IO.Path.GetFileName(path)),
                "KillerPDF", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.Yes) return null;

            var busy = ShowBusyOverlay(Loc("Str_Busy_Repairing"));
            try
            {
                // Same order as TryRepairAndOpen: lossless PDFium re-save first (keeps forms and
                // bookmarks), then a PdfSharpCore page-copy, then the rasterize that always
                // produces something openable.
                string? repaired = await System.Threading.Tasks.Task.Run(() =>
                {
                    var p = App.MakeTempFile("repaired");
                    return PdfiumInterop.TryPdfiumStripEncryption(path, p) ? p : null;
                });
                repaired ??= await System.Threading.Tasks.Task.Run(() => PdfImport.RepairViaImportToFile(path));
                repaired ??= await System.Threading.Tasks.Task.Run(() => PdfImport.RepairViaDocnetRasterizeToFile(path));

                if (repaired is null)
                    KillerDialog.Show(this,
                        $"\"{System.IO.Path.GetFileName(path)}\" could not be repaired.",
                        "KillerPDF", MessageBoxButton.OK, MessageBoxImage.Error);
                return repaired;
            }
            finally { HideBusyOverlay(busy); }
        }

        private void PageList_Drop(object sender, DragEventArgs e)
        {
            if (_doc != null && !e.Data.GetDataPresent(typeof(int)))
            {
                var files = DroppedOpenablePaths(e);
                if (files.Length > 0) { HidePageDropIndicator(); AppendFilesToCurrentDoc(files); e.Handled = true; return; }
            }
            if (_doc is null || !e.Data.GetDataPresent(typeof(int))) { HidePageDropIndicator(); return; }
            int fromIdx = (int)e.Data.GetData(typeof(int))!;
            int insertionIndex = _pageDropInsertionIndex;
            if (insertionIndex < 0)
                insertionIndex = PageDropSlot(e.GetPosition(PageList))?.Index ?? fromIdx;
            HidePageDropIndicator();
            int finalIndex = insertionIndex > fromIdx ? insertionIndex - 1 : insertionIndex;
            if (fromIdx == finalIndex) return;
            SaveTempAndReload(
                finalizeSavedFile: path =>
                    PdfEngineIntegration.MovePage(path, fromIdx, finalIndex),
                remapRotations: rotations =>
                    PdfEngineIntegration.RemapRotationsAfterPageMove(
                        rotations, fromIdx, finalIndex));
            PageList.SelectedIndex = finalIndex;
        }
    }
}
