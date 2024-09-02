using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class CircularButton : Button
    {
        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);

            // Set the button's size to ensure it's circular
            this.Width = this.Height;

            // Draw the circular button
            GraphicsPath path = new GraphicsPath();
            path.AddEllipse(0, 0, this.Width, this.Height);
            this.Region = new Region(path);

            // Draw the thick black border
            Pen pen = new Pen(Color.Black, 5); // Set thickness to 5
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.DrawEllipse(pen, 2, 2, this.Width - 5, this.Height - 5);

            // Draw the button's text
            TextRenderer.DrawText(pevent.Graphics, this.Text, this.Font,
                                  this.ClientRectangle, this.ForeColor,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
