using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PicGrab
{
    internal sealed class PhotoPreview : Control
    {
        private Bitmap image;
        private string message = "写真をクリックするとプレビューを表示します";
        private int request;
        private bool loading;
        private string pendingPath;
        private double zoom = 1.0;
        private PointF offset;
        private Point lastMouse;
        private bool panning;

        public event Action<string> LoadError;

        public PhotoPreview()
        {
            BackColor = Color.FromArgb(245, 245, 245);
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void ClearPhoto()
        {
            request++;
            pendingPath = null;
            ReplaceImage(null);
            message = "写真をクリックするとプレビューを表示します";
            Invalidate();
        }

        public void LoadPhoto(string path)
        {
            request++;
            pendingPath = path;
            ReplaceImage(null);
            message = "読み込み中...";
            Invalidate();
            if (!loading) StartLoading();
        }

        private void StartLoading()
        {
            string path = pendingPath;
            pendingPath = null;
            int thisRequest = request;
            loading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap loaded = null;
                string error = null;
                try { loaded = PhotoLoader.Load(path, Int32.MaxValue, Int32.MaxValue); }
                catch (Exception ex)
                {
                    if (!PhotoGallery.IsImageError(ex)) throw;
                    error = ex.Message;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        loading = false;
                        if (IsDisposed || thisRequest != request)
                        {
                            if (loaded != null) loaded.Dispose();
                            if (!IsDisposed && pendingPath != null) StartLoading();
                            return;
                        }
                        ReplaceImage(loaded);
                        message = loaded == null ? "プレビューを読み込めませんでした" : null;
                        Invalidate();
                        if (error != null && LoadError != null)
                            LoadError("写真を読み込めませんでした。\n" + Path.GetFileName(path) + "\n" + error);
                    });
                }
                catch (InvalidOperationException)
                {
                    if (loaded != null) loaded.Dispose();
                }
            });
        }

        private void ReplaceImage(Bitmap replacement)
        {
            if (image != null) image.Dispose();
            image = replacement;
            zoom = 1.0;
            offset = PointF.Empty;
        }

        private double FitScale
        {
            get { return Math.Min((double)ClientSize.Width / image.Width, (double)ClientSize.Height / image.Height); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null)
            {
                TextRenderer.DrawText(e.Graphics, message ?? "", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            double scale = FitScale * zoom;
            float width = (float)(image.Width * scale);
            float height = (float)(image.Height * scale);
            float x = (ClientSize.Width - width) / 2 + offset.X;
            float y = (ClientSize.Height - height) / 2 + offset.Y;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(image, new RectangleF(x, y, width, height));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (image == null || e.Delta == 0) return;
            double next = Math.Max(0.1, Math.Min(12.0, zoom * Math.Pow(1.2, e.Delta / 120.0)));
            double ratio = next / zoom;
            float centerX = ClientSize.Width / 2f;
            float centerY = ClientSize.Height / 2f;
            offset = new PointF(
                (float)(e.X - centerX - (e.X - centerX - offset.X) * ratio),
                (float)(e.Y - centerY - (e.Y - centerY - offset.Y) * ratio));
            zoom = next;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (image == null || e.Button != MouseButtons.Left) return;
            Focus();
            panning = true;
            lastMouse = e.Location;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!panning) return;
            offset = new PointF(offset.X + e.X - lastMouse.X, offset.Y + e.Y - lastMouse.Y);
            lastMouse = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            panning = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            panning = false;
            Cursor = Cursors.Default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                request++;
                ReplaceImage(null);
            }
            base.Dispose(disposing);
        }
    }
}
