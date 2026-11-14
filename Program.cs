using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PicGrab
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly PhotoGallery gallery;
        private readonly PhotoPreview preview;
        private readonly ToolStripLabel folderLabel;
        private readonly ToolStripStatusLabel status;

        public MainForm()
        {
            Text = "PicGrab";
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterScreen;

            var toolbar = new ToolStrip();
            var openButton = new ToolStripButton("フォルダーを選択");
            openButton.Click += OpenFolder;
            folderLabel = new ToolStripLabel("フォルダーを選択してください");
            toolbar.Items.Add(openButton);
            toolbar.Items.Add(folderLabel);

            var statusBar = new StatusStrip();
            status = new ToolStripStatusLabel();
            statusBar.Items.Add(status);

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Size = new Size(1280, 760);
            split.SplitterDistance = 550;
            split.Panel1MinSize = 230;
            split.Panel2MinSize = 230;
            gallery = new PhotoGallery();
            gallery.Dock = DockStyle.Fill;
            gallery.PhotoClicked += delegate(string path) { preview.LoadPhoto(path); };
            gallery.SelectionChanged += UpdateStatus;
            gallery.DragError += ShowError;
            preview = new PhotoPreview();
            preview.Dock = DockStyle.Fill;
            preview.LoadError += ShowError;
            split.Panel1.Controls.Add(gallery);
            split.Panel2.Controls.Add(preview);

            Controls.Add(split);
            Controls.Add(statusBar);
            Controls.Add(toolbar);
            UpdateStatus();
        }

        private void OpenFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "JPEG 写真のフォルダーを選択してください";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string[] paths = Directory.GetFiles(dialog.SelectedPath)
                        .Where(p => String.Equals(Path.GetExtension(p), ".jpg", StringComparison.OrdinalIgnoreCase)
                                 || String.Equals(Path.GetExtension(p), ".jpeg", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();
                    gallery.SetPhotos(paths);
                    preview.ClearPhoto();
                    folderLabel.Text = dialog.SelectedPath;
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException)
                        && !(ex is System.Security.SecurityException))
                        throw;
                    ShowError("フォルダーを読み込めませんでした。\n" + ex.Message);
                }
            }
        }

        private void UpdateStatus()
        {
            status.Text = String.Format("{0} 枚  /  チェック {1} 枚    |    左からドラッグ: チェックした写真  /  右のホイール: 拡大・縮小",
                gallery.PhotoCount, gallery.CheckedCount);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "PicGrab", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static class PhotoLoader
    {
        public static Bitmap Load(string path, int maxWidth, int maxHeight)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var image = Image.FromStream(stream))
            {
                ApplyOrientation(image);
                double scale = Math.Min(1.0, Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height));
                int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                var result = new Bitmap(width, height);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(result))
                    {
                        graphics.Clear(Color.White);
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, 0, 0, width, height);
                    }
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
        }

        private static void ApplyOrientation(Image image)
        {
            const int orientationId = 0x0112;
            if (Array.IndexOf(image.PropertyIdList, orientationId) < 0) return;
            byte[] value = image.GetPropertyItem(orientationId).Value;
            if (value == null || value.Length < 2) return;
            int orientation = BitConverter.ToUInt16(value, 0);
            RotateFlipType transform;
            switch (orientation)
            {
                case 2: transform = RotateFlipType.RotateNoneFlipX; break;
                case 3: transform = RotateFlipType.Rotate180FlipNone; break;
                case 4: transform = RotateFlipType.Rotate180FlipX; break;
                case 5: transform = RotateFlipType.Rotate90FlipX; break;
                case 6: transform = RotateFlipType.Rotate90FlipNone; break;
                case 7: transform = RotateFlipType.Rotate270FlipX; break;
                case 8: transform = RotateFlipType.Rotate270FlipNone; break;
                default: return;
            }
            image.RotateFlip(transform);
        }
    }
}
