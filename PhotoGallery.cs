using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PicGrab
{
    internal sealed class PhotoGallery : ScrollableControl
    {
        private const int TileWidth = 210;
        private const int TileHeight = 190;
        private const int ImageWidth = 190;
        private const int ImageHeight = 140;
        private const int CacheLimit = 200;
        private string[] paths = new string[0];
        private bool[] selected = new bool[0];
        private readonly Dictionary<int, Bitmap> thumbnails = new Dictionary<int, Bitmap>();
        private readonly LinkedList<int> cacheOrder = new LinkedList<int>();
        private int current = -1;
        private int generation;
        private bool loading;
        private Point mouseDown;
        private bool canDrag;

        public event Action<string> PhotoClicked;
        public event Action SelectionChanged;
        public event Action<string> DragError;

        public int PhotoCount { get { return paths.Length; } }
        public int CheckedCount { get { return selected.Count(value => value); } }

        public PhotoGallery()
        {
            AutoScroll = true;
            BackColor = Color.White;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetPhotos(string[] newPaths)
        {
            generation++;
            paths = newPaths;
            selected = new bool[paths.Length];
            current = -1;
            ClearThumbnails();
            AutoScrollPosition = Point.Empty;
            UpdateScrollSize();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        public void SelectAllPhotos()
        {
            for (int i = 0; i < selected.Length; i++) selected[i] = true;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        public void InvertSelection()
        {
            for (int i = 0; i < selected.Length; i++) selected[i] = !selected[i];
            Invalidate();
            if (SelectionChanged != null) SelectionChanged();
        }

        private int Columns
        {
            get { return Math.Max(1, ClientSize.Width / TileWidth); }
        }

        private void UpdateScrollSize()
        {
            int rows = (paths.Length + Columns - 1) / Columns;
            AutoScrollMinSize = new Size(Columns * TileWidth, rows * TileHeight);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
            Invalidate();
        }

        private Rectangle TileBounds(int index)
        {
            return new Rectangle((index % Columns) * TileWidth + AutoScrollPosition.X,
                (index / Columns) * TileHeight + AutoScrollPosition.Y, TileWidth, TileHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (paths.Length == 0)
            {
                TextRenderer.DrawText(e.Graphics, "フォルダーを選択してください", Font, ClientRectangle,
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int firstRow = Math.Max(0, -AutoScrollPosition.Y / TileHeight);
            int lastRow = Math.Min((paths.Length - 1) / Columns,
                (-AutoScrollPosition.Y + ClientSize.Height) / TileHeight);
            int firstMissing = -1;
            for (int i = firstRow * Columns; i < Math.Min(paths.Length, (lastRow + 1) * Columns); i++)
            {
                Rectangle tile = TileBounds(i);
                if (i == current)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(220, 237, 252)))
                        e.Graphics.FillRectangle(brush, tile);
                    e.Graphics.DrawRectangle(SystemPens.Highlight, tile.X, tile.Y, tile.Width - 1, tile.Height - 1);
                }

                Rectangle imageRect = new Rectangle(tile.X + 10, tile.Y + 10, ImageWidth, ImageHeight);
                Bitmap image;
                if (thumbnails.TryGetValue(i, out image))
                {
                    TouchCache(i);
                    if (image != null)
                    {
                        int x = imageRect.X + (imageRect.Width - image.Width) / 2;
                        int y = imageRect.Y + (imageRect.Height - image.Height) / 2;
                        e.Graphics.DrawImageUnscaled(image, x, y);
                    }
                    else
                    {
                        TextRenderer.DrawText(e.Graphics, "読み込みエラー", Font, imageRect, Color.DarkRed,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
                else
                {
                    TextRenderer.DrawText(e.Graphics, "読み込み中...", Font, imageRect, Color.Gray,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (firstMissing < 0) firstMissing = i;
                }

                Rectangle check = CheckBounds(tile);
                ControlPaint.DrawCheckBox(e.Graphics, check, selected[i] ? ButtonState.Checked : ButtonState.Normal);
                Rectangle label = new Rectangle(tile.X + 6, tile.Y + 154, TileWidth - 12, 31);
                TextRenderer.DrawText(e.Graphics, Path.GetFileName(paths[i]), Font, label, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
            }
            if (!loading && firstMissing >= 0) StartThumbnail(firstMissing);
        }

        private static Rectangle CheckBounds(Rectangle tile)
        {
            return new Rectangle(tile.X + 5, tile.Y + 5, 20, 20);
        }

        private void StartThumbnail(int index)
        {
            loading = true;
            int requestGeneration = generation;
            string path = paths[index];
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap bitmap = null;
                try { bitmap = PhotoLoader.Load(path, ImageWidth, ImageHeight); }
                catch (Exception ex)
                {
                    if (!IsImageError(ex)) throw;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        loading = false;
                        if (IsDisposed || requestGeneration != generation)
                        {
                            if (bitmap != null) bitmap.Dispose();
                        }
                        else
                        {
                            thumbnails[index] = bitmap;
                            TouchCache(index);
                            while (cacheOrder.Count > CacheLimit)
                            {
                                int oldest = cacheOrder.First.Value;
                                cacheOrder.RemoveFirst();
                                Bitmap old = thumbnails[oldest];
                                thumbnails.Remove(oldest);
                                if (old != null) old.Dispose();
                            }
                        }
                        if (!IsDisposed) Invalidate();
                    });
                }
                catch (InvalidOperationException)
                {
                    if (bitmap != null) bitmap.Dispose();
                }
            });
        }

        internal static bool IsImageError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException
                || ex is System.Security.SecurityException;
        }

        private void TouchCache(int index)
        {
            cacheOrder.Remove(index);
            cacheOrder.AddLast(index);
        }

        private void ClearThumbnails()
        {
            foreach (Bitmap bitmap in thumbnails.Values)
                if (bitmap != null) bitmap.Dispose();
            thumbnails.Clear();
            cacheOrder.Clear();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (paths.Length == 0 || e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Space)
            {
                if (current >= 0)
                {
                    selected[current] = !selected[current];
                    if (SelectionChanged != null) SelectionChanged();
                    Invalidate();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right
                && e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;

            int next = current;
            if (current < 0) next = 0;
            else if (e.KeyCode == Keys.Left) next = Math.Max(0, current - 1);
            else if (e.KeyCode == Keys.Right) next = Math.Min(paths.Length - 1, current + 1);
            else if (e.KeyCode == Keys.Up && current >= Columns) next = current - Columns;
            else if (e.KeyCode == Keys.Down && current / Columns < (paths.Length - 1) / Columns)
                next = Math.Min(paths.Length - 1, current + Columns);

            if (next != current)
            {
                current = next;
                EnsureCurrentVisible();
                if (PhotoClicked != null) PhotoClicked(paths[current]);
                Invalidate();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void EnsureCurrentVisible()
        {
            Rectangle tile = TileBounds(current);
            int x = -AutoScrollPosition.X;
            int y = -AutoScrollPosition.Y;
            if (tile.Left < 0) x += tile.Left;
            else if (tile.Right > ClientSize.Width) x += tile.Right - ClientSize.Width;
            if (tile.Top < 0) y += tile.Top;
            else if (tile.Bottom > ClientSize.Height) y += tile.Bottom - ClientSize.Height;
            AutoScrollPosition = new Point(Math.Max(0, x), Math.Max(0, y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int x = e.X - AutoScrollPosition.X;
            int y = e.Y - AutoScrollPosition.Y;
            int column = x / TileWidth;
            int index = (y / TileHeight) * Columns + column;
            if (x < 0 || y < 0 || column >= Columns || index >= paths.Length) return;
            Focus();
            current = index;
            if (CheckBounds(TileBounds(index)).Contains(e.Location))
            {
                selected[index] = !selected[index];
                if (SelectionChanged != null) SelectionChanged();
                canDrag = false;
            }
            else
            {
                mouseDown = e.Location;
                canDrag = true;
            }
            if (PhotoClicked != null) PhotoClicked(paths[index]);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!canDrag || e.Button != MouseButtons.Left) return;
            Size threshold = SystemInformation.DragSize;
            var rectangle = new Rectangle(mouseDown.X - threshold.Width / 2, mouseDown.Y - threshold.Height / 2,
                threshold.Width, threshold.Height);
            if (rectangle.Contains(e.Location)) return;
            canDrag = false;
            string[] checkedPaths = paths.Where((path, index) => selected[index]).ToArray();
            if (checkedPaths.Length == 0) return;
            string missing = checkedPaths.FirstOrDefault(path => !File.Exists(path));
            if (missing != null)
            {
                if (DragError != null) DragError("ファイルが見つかりません。フォルダーを再選択してください。\n" + missing);
                return;
            }
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, checkedPaths);
            DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            canDrag = false;
            base.OnMouseUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                generation++;
                ClearThumbnails();
            }
            base.Dispose(disposing);
        }
    }
}
