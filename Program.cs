using System;
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
        private readonly ToolStripTextBox folderAddress;
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
            folderAddress = new ToolStripTextBox();
            folderAddress.AutoSize = false;
            folderAddress.ToolTipText = "フォルダーのパスを入力して Enter";
            folderAddress.KeyDown += AddressKeyDown;
            var selectionMenu = new ToolStripDropDownButton("選択");
            selectionMenu.DropDownItems.Add("すべて選択", null, delegate { gallery.SelectAllPhotos(); });
            selectionMenu.DropDownItems.Add("選択の切り替え", null, delegate { gallery.InvertSelection(); });
            toolbar.Items.Add(openButton);
            toolbar.Items.Add(folderAddress);
            toolbar.Items.Add(selectionMenu);
            toolbar.SizeChanged += delegate
            {
                folderAddress.Width = Math.Max(100, toolbar.ClientSize.Width - openButton.Width
                    - selectionMenu.Width - 40);
            };

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
                if (Directory.Exists(folderAddress.Text))
                    dialog.SelectedPath = folderAddress.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadFolder(dialog.SelectedPath);
            }
        }

        private void AddressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            LoadFolder(folderAddress.Text);
        }

        private void LoadFolder(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path.Trim());
                string[] paths = Directory.GetFiles(fullPath)
                    .Where(p => String.Equals(Path.GetExtension(p), ".jpg", StringComparison.OrdinalIgnoreCase)
                             || String.Equals(Path.GetExtension(p), ".jpeg", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                gallery.SetPhotos(paths);
                preview.ClearPhoto();
                folderAddress.Text = fullPath;
                gallery.Focus();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)
                    && !(ex is System.Security.SecurityException) && !(ex is ArgumentException)
                    && !(ex is NotSupportedException))
                    throw;
                ShowError("フォルダーを読み込めませんでした。\n" + ex.Message);
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
