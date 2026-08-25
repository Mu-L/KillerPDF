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
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
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
                    DragDrop.DoDragDrop(PageList, PageList.SelectedIndex, DragDropEffects.Move);
            }
        }

        private void PageList_DragOver(object sender, DragEventArgs e)
        {
            // #172: files dropped onto the Pages sidebar append to the open document,
            // so the list accepts FileDrop as well as its own page-reorder payload.
            if (e.Data.GetDataPresent(typeof(int)))
                e.Effects = DragDropEffects.Move;
            else if (_doc != null && DroppedOpenablePaths(e).Length > 0)
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
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
            int before = _doc.PageCount;
            foreach (var f in files)
            {
                if (PdfImport.IsPdfPath(f))
                {
                    var target = _doc;
                    if (target != null && TryAppendPdfPages(target, f)) continue;

                    // #203: a damaged PDF used to be swallowed here, so nothing was added and
                    // nothing was said. Offer the same repair the open path offers.
                    string? repaired = await RepairDroppedPdfAsync(f);
                    if (repaired != null && _doc != null) TryAppendPdfPages(_doc, repaired);
                }
                else
                {
                    var target = _doc;
                    if (target != null)
                        try { PdfImport.AddImagePagesFromFile(target, f); } catch { /* skip an unreadable image */ }
                }
            }
            if (_doc is null) return;
            if (_doc.PageCount == before) { SetStatus(Loc("Str_Drop_NothingOpenable")); return; }
            MarkDirty(true);
            SaveTempAndReload(keepAnnotations: true, preserveZoom: true);
            SetStatus(string.Format(Loc("Str_Status_Merged"), files.Length));
        }

        /// <summary>
        /// Import-mode page copy. False when the file cannot be read at all, which is the signal
        /// to offer a repair rather than silently dropping it.
        /// </summary>
        private static bool TryAppendPdfPages(PdfDocument target, string path)
        {
            try
            {
                using var src = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                if (src.PageCount == 0) return false;
                for (int i = 0; i < src.PageCount; i++) target.AddPage(src.Pages[i]);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Runs the open path's three repair strategies against a dropped file and returns the
        /// repaired temp copy, or null if the user declined or nothing recovered it. The original
        /// file is never written to.
        /// </summary>
        private async System.Threading.Tasks.Task<string?> RepairDroppedPdfAsync(string path)
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
                if (files.Length > 0) { AppendFilesToCurrentDoc(files); e.Handled = true; return; }
            }
            if (_doc is null || !e.Data.GetDataPresent(typeof(int))) return;
            int fromIdx = (int)e.Data.GetData(typeof(int))!;
            var pos = e.GetPosition(PageList);
            int toIdx = PageList.Items.Count - 1;
            for (int i = 0; i < PageList.Items.Count; i++)
            {
                if (PageList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                {
                    var itemPos = item.TranslatePoint(new Point(0, item.ActualHeight / 2), PageList);
                    if (pos.Y < itemPos.Y) { toIdx = i; break; }
                }
            }
            if (fromIdx == toIdx) return;
            if (toIdx > fromIdx) toIdx--;
            int finalIndex = toIdx;
            SaveTempAndReload(
                finalizeSavedFile: path =>
                    PdfEngineIntegration.MovePage(path, fromIdx, finalIndex),
                remapRotations: rotations =>
                    PdfEngineIntegration.RemapRotationsAfterPageMove(
                        rotations, fromIdx, finalIndex));
            PageList.SelectedIndex = toIdx;
        }
    }
}
