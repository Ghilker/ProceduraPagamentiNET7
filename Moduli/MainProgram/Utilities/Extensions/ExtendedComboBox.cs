using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class ExtendedComboBox : ComboBox
{
    // Import SendMessage function from user32.dll
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Constants
    private const int CB_SETDROPPEDWIDTH = 0x160;

    // Property to set the dropdown width
    public new int DropDownWidth { get; set; }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);

        // Set the dropdown width
        SendMessage(this.Handle, CB_SETDROPPEDWIDTH, DropDownWidth, IntPtr.Zero);
    }
}
