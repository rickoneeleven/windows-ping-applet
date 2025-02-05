using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ping_applet.UI
{
    /// <summary>
    /// Handles the creation and management of system tray icons
    /// </summary>
    public class IconGenerator : IDisposable
    {
        private bool isDisposed;

        // Font configurations
        private const float SINGLE_DIGIT_SIZE = 10f;
        private const float DOUBLE_DIGIT_SIZE = 8f;
        private const float TRIPLE_DIGIT_SIZE = 6f;
        private const float DEFAULT_FONT_SIZE = 5f;
        private const string FONT_FAMILY = "Arial";

        // Icon dimensions
        private const int ICON_SIZE = 16;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Creates an icon with the specified number or text
        /// </summary>
        /// <param name="text">The text to display in the icon</param>
        /// <param name="isError">Whether to use error styling (red background)</param>
        /// <returns>An Icon object containing the generated icon</returns>
        public Icon CreateNumberIcon(string text, bool isError = false)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(IconGenerator));

            IntPtr hIcon = IntPtr.Zero;
            try
            {
                using (var bitmap = new Bitmap(ICON_SIZE, ICON_SIZE))
                using (var g = Graphics.FromImage(bitmap))
                {
                    // Set background color based on state
                    g.Clear(isError ? Color.Red : Color.Black);

                    // Determine font size based on text length
                    float fontSize = text.Length switch
                    {
                        1 => SINGLE_DIGIT_SIZE,
                        2 => DOUBLE_DIGIT_SIZE,
                        3 => TRIPLE_DIGIT_SIZE,
                        _ => DEFAULT_FONT_SIZE
                    };

                    // Configure text rendering
                    using (var font = new Font(FONT_FAMILY, fontSize, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.White))
                    using (var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    })
                    {
                        g.DrawString(text, font, brush, new RectangleF(0, 0, ICON_SIZE, ICON_SIZE), sf);
                    }

                    hIcon = bitmap.GetHicon();
                    Icon icon = Icon.FromHandle(hIcon);

                    // Create a new icon that doesn't depend on the handle
                    using (Icon tmpIcon = icon)
                    {
                        return (Icon)tmpIcon.Clone();
                    }
                }
            }
            catch (Exception)
            {
                // Return error icon if icon creation fails
                return (Icon)SystemIcons.Error.Clone();
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        DestroyIcon(hIcon);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}