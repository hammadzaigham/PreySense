using System.Drawing;
using System.Windows.Forms;
using PreySense.UI;

namespace PreySense.Dialogs
{
    public partial class ConfirmDialog : RForm
    {
        public ConfirmDialog(string message, string title, string yesText = "Apply && Restart Now", string noText = "Cancel")
        {
            InitializeComponent(message, title, yesText, noText);
            InitTheme(true);

            BackColor = RForm.formBack;
            _labelMessage.BackColor = Color.Transparent;
            _labelMessage.ForeColor = RForm.foreMain;
        }

        public static DialogResult Show(IWin32Window? owner, string message, string title, string yesText = "Apply && Restart Now", string noText = "Cancel")
        {
            using var dialog = new ConfirmDialog(message, title, yesText, noText);
            return dialog.ShowDialog(owner);
        }
    }
}
