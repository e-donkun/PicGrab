using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PicGrab
{
    internal static class BehaviorTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static void Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PicGrab-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    using (var bitmap = new Bitmap(20 + i, 20))
                        bitmap.Save(Path.Combine(directory, i.ToString("00") + ".jpg"), ImageFormat.Jpeg);
                }
                Application.EnableVisualStyles();
                using (var form = new MainForm())
                {
                    form.Show();
                    var gallery = Field<PhotoGallery>(form, "gallery");
                    var preview = Field<PhotoPreview>(form, "preview");
                    var address = Field<ToolStripTextBox>(form, "folderAddress");
                    address.Text = directory;
                    Invoke(form, "AddressKeyDown", address, new KeyEventArgs(Keys.Enter));
                    Check(gallery.PhotoCount == 12, "Address did not open folder");

                    Check((bool)Invoke(gallery, "IsInputKey", Keys.Right), "Arrow not treated as input");
                    Key(gallery, Keys.Right);
                    Check(Field<int>(gallery, "current") == 0, "First arrow did not choose first photo");
                    int request = Field<int>(preview, "request");
                    Key(gallery, Keys.Space);
                    Check(gallery.CheckedCount == 1, "Space did not select current photo");
                    Check(Field<int>(preview, "request") == request, "Space reloaded preview");
                    Key(gallery, Keys.Right);
                    Check(Field<int>(gallery, "current") == 1 && gallery.CheckedCount == 1,
                        "Arrow changed checked photos");
                    Key(gallery, Keys.Up);
                    Check(Field<int>(gallery, "current") == 1, "Up moved within the first row");
                    Key(gallery, Keys.Down);
                    Check(Field<int>(gallery, "current") == 3, "Down arrow did not move by row");
                    Key(gallery, Keys.Up);
                    Check(Field<int>(gallery, "current") == 1, "Up arrow did not move by row");
                    for (int i = 0; i < 5; i++) Key(gallery, Keys.Down);
                    Check(Field<int>(gallery, "current") == 11 && gallery.AutoScrollPosition.Y < 0,
                        "Down arrow did not scroll into view");
                    Key(gallery, Keys.Down);
                    Check(Field<int>(gallery, "current") == 11, "Down moved within the last row");
                    for (int i = 0; i < 5; i++) Key(gallery, Keys.Up);
                    Check(Field<int>(gallery, "current") == 1, "Up arrow did not return to first row");
                    Key(gallery, Keys.Space);
                    var menu = (ToolStripDropDownButton)address.Owner.Items[2];
                    menu.DropDownItems[1].PerformClick();
                    Check(gallery.CheckedCount == 10, "Invert selection failed");
                    menu.DropDownItems[0].PerformClick();
                    Check(gallery.CheckedCount == 12, "Select all failed");
                    menu.DropDownItems[1].PerformClick();
                    Check(gallery.CheckedCount == 0, "Inverting all failed");

                    string path = Path.Combine(directory, "01.jpg");
                    preview.LoadPhoto(path);
                    request = Field<int>(preview, "request");
                    preview.LoadPhoto(path);
                    Check(Field<int>(preview, "request") == request, "Same photo reloaded while pending");
                    WaitForImage(preview);
                    Bitmap shown = Field<Bitmap>(preview, "image");
                    Invoke(preview, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120));
                    double zoom = Field<double>(preview, "zoom");
                    preview.LoadPhoto(path);
                    Check(Object.ReferenceEquals(shown, Field<Bitmap>(preview, "image"))
                        && Field<double>(preview, "zoom") == zoom, "Same photo reset preview");
                    preview.ClearPhoto();
                    preview.LoadPhoto(path);
                    Check(Field<int>(preview, "request") > request, "Clear did not allow reload");

                    address.Text = directory;
                    Invoke(form, "AddressKeyDown", address, new KeyEventArgs(Keys.Enter));
                    Check(gallery.PhotoCount == 12 && gallery.CheckedCount == 0,
                        "Reopening folder did not reset selection");
                    form.Close();
                }
                Console.WriteLine("Behavior tests passed");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
        }

        private static object Invoke(object instance, string name, params object[] args)
        {
            return instance.GetType().GetMethod(name, PrivateInstance).Invoke(instance, args);
        }

        private static void Key(PhotoGallery gallery, Keys key)
        {
            Invoke(gallery, "OnKeyDown", new KeyEventArgs(key));
        }

        private static void WaitForImage(PhotoPreview preview)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                if (Field<Bitmap>(preview, "image") != null) return;
                Thread.Sleep(20);
            }
            throw new Exception("Preview did not load");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
