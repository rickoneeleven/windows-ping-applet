using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ping_applet.UI
{
    /// <summary>
    /// Handles the creation and management of system tray icons with support for transition states and custom text colors
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

        // Colors
        private static readonly Color ERROR_COLOR = Color.Red;
        private static readonly Color NORMAL_COLOR = Color.Black;
        private static readonly Color TRANSITION_COLOR = Color.FromArgb(255, 165, 0); // Orange
        private static readonly Color TEXT_COLOR_LIGHT = Color.White;
        private static readonly Color TEXT_COLOR_DARK = Color.Black;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Creates an icon with the specified number or text in normal or error state
        /// </summary>
        public Icon CreateNumberIcon(string text, bool isError = false)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(IconGenerator));

            return CreateIconWithCustomColors(text, isError ? ERROR_COLOR : NORMAL_COLOR, TEXT_COLOR_LIGHT);
        }

        /// <summary>
        /// Creates an icon with the specified number or text in transition state (orange background)
        /// with white text (legacy behavior)
        /// </summary>
        public Icon CreateTransitionIcon(string text)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(IconGenerator));

            return CreateIconWithCustomColors(text, TRANSITION_COLOR, TEXT_COLOR_LIGHT);
        }

        /// <summary>
        /// Creates an icon with the specified number or text in transition state (orange background)
        /// with black text for better visibility
        /// </summary>
        public Icon CreateTransitionIconWithBlackText(string text)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(IconGenerator));

            return CreateIconWithCustomColors(text, TRANSITION_COLOR, TEXT_COLOR_DARK);
        }

        /// <summary>
        /// Creates an icon with custom background and text colors
        /// </summary>
        private Icon CreateIconWithCustomColors(string text, Color backgroundColor, Color textColor)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentNullException(nameof(text));

            IntPtr hIcon = IntPtr.Zero;
            Bitmap bitmap = null;
            Graphics g = null;
            Font font = null;
            Brush brush = null;
            StringFormat sf = null;
            Icon icon = null;

            try
            {
                bitmap = new Bitmap(ICON_SIZE, ICON_SIZE);
                g = Graphics.FromImage(bitmap);

                // Set background color
                g.Clear(backgroundColor);

                // Determine font size based on text length
                float fontSize = text.Length switch
                {
                    1 => SINGLE_DIGIT_SIZE,
                    2 => DOUBLE_DIGIT_SIZE,
                    3 => TRIPLE_DIGIT_SIZE,
                    _ => DEFAULT_FONT_SIZE
                };

                // Configure text rendering for maximum clarity
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // Configure text drawing
                font = new Font(FONT_FAMILY, fontSize, FontStyle.Bold);
                brush = new SolidBrush(textColor);
                sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                var rect = new RectangleF(0, 0, ICON_SIZE, ICON_SIZE);

                // Apply offset for better visual centering on shorter text
                if (text.Length <= 2)
                {
                    rect.Offset(0, -1);
                }

                // Draw the text
                g.DrawString(text, font, brush, rect, sf);

                // Create icon
                hIcon = bitmap.GetHicon();
                icon = Icon.FromHandle(hIcon);

                // Create a new icon that doesn't depend on the handle
                return (Icon)icon.Clone();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating icon: {ex.Message}");
                // Return error icon if icon creation fails
                return (Icon)SystemIcons.Error.Clone();
            }
            finally
            {
                // Clean up all resources
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

                icon?.Dispose();
                sf?.Dispose();
                brush?.Dispose();
                font?.Dispose();
                g?.Dispose();
                bitmap?.Dispose();
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