using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net; // For IPAddress parsing (basic validation)

namespace ping_applet.Forms
{
    public class SetCustomTargetForm : Form
    {
        private Label instructionLabel;
        private TextBox targetTextBox;
        private Button okButton;
        private Button cancelButton;

        public string TargetHost { get; private set; }

        public SetCustomTargetForm(string currentTarget)
        {
            InitializeDialogProperties();
            InitializeControls(currentTarget);
            AssignEventHandlers();
        }

        private void InitializeDialogProperties()
        {
            this.Text = "Set Custom Ping Target";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent; // Or CenterScreen if shown non-modally
            this.ClientSize = new Size(380, 150); // Adjusted size for better layout
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Font; // Ensure proper scaling
        }

        private void InitializeControls(string currentTarget)
        {
            instructionLabel = new Label
            {
                Text = "IP Address or Hostname:",
                Location = new Point(12, 15),
                AutoSize = true
            };
            this.Controls.Add(instructionLabel);

            targetTextBox = new TextBox
            {
                Location = new Point(12, 40),
                Size = new Size(350, 23), // Standard height
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = currentTarget ?? "" // Pre-fill with current target if available
            };
            this.Controls.Add(targetTextBox);

            okButton = new Button
            {
                Text = "OK",
                Location = new Point(200, 90), // Positioned to the right
                Size = new Size(80, 28), // Standard button size
                DialogResult = DialogResult.OK // Set DialogResult for convenience
            };
            this.Controls.Add(okButton);

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(285, 90), // Positioned to the right of OK
                Size = new Size(80, 28),
                DialogResult = DialogResult.Cancel // Set DialogResult
            };
            this.Controls.Add(cancelButton);

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;
        }

        private void AssignEventHandlers()
        {
            okButton.Click += OkButton_Click;
            // cancelButton click is handled by DialogResult = DialogResult.Cancel
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string inputText = targetTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(inputText))
            {
                MessageBox.Show(
                    this, // Set owner for proper modal behavior
                    "Target cannot be empty. Please enter a valid IP address or hostname.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                this.DialogResult = DialogResult.None; // Prevent form from closing
                targetTextBox.Focus();
                return;
            }

            // Basic validation:
            // While Ping class handles resolution, we can do a very lightweight check here.
            // This isn't exhaustive but catches obviously malformed inputs.
            // Allow simple hostnames (e.g. "myserver"), FQDNs, IPv4.
            // For simplicity, we won't do complex regex or URI parsing here.
            // The Ping operation itself will be the ultimate test.
            if (inputText.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '<', '>', '&', '"', '\\', '/', ':', '*', '?' }) != -1 && !IPAddress.TryParse(inputText, out _))
            {
                // Allow colons for IPv6, but not other special chars in hostnames generally
                // This check is very basic. More robust validation could be added if needed.
                bool isLikelyIPv6 = inputText.Contains(":");
                if (!isLikelyIPv6 || inputText.IndexOfAny(new[] { ' ', '\t', '<', '>', '&', '"', '\\', '/' }) != -1)
                {
                    MessageBox.Show(
                       this,
                       "Invalid characters in hostname. Please enter a valid target.",
                       "Validation Error",
                       MessageBoxButtons.OK,
                       MessageBoxIcon.Warning
                   );
                    this.DialogResult = DialogResult.None; // Prevent form from closing
                    targetTextBox.Focus();
                    return;
                }
            }


            this.TargetHost = inputText;
            // DialogResult is already OK from button property, so form will close.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose controls created programmatically if they were IDisposable
                // and not added to this.Controls (which Form disposes automatically)
                // In this case, they are added to Controls, so Form handles their disposal.
            }
            base.Dispose(disposing);
        }
    }
}