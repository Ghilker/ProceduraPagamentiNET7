using System.Windows.Forms;

public class CustomContextMenuStrip : ContextMenuStrip
{
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Handle mouse wheel event here
        base.OnMouseWheel(e);

        int numberOfItemsToMove = e.Delta > 0 ? -3 : 3;
        int firstDisplayedItemIndex = 0;

        // Find the index of the first visible item
        for (int i = 0; i < this.Items.Count; i++)
        {
            if (this.Items[i].Visible)
            {
                firstDisplayedItemIndex = i;
                break;
            }
        }

        // Calculate the new index
        int newFirstItemIndex = firstDisplayedItemIndex + numberOfItemsToMove;
        newFirstItemIndex = Math.Max(newFirstItemIndex, 0);
        newFirstItemIndex = Math.Min(newFirstItemIndex, this.Items.Count - 1);

        // Update the visibility of items
        for (int i = 0; i < this.Items.Count; i++)
        {
            this.Items[i].Visible = (i >= newFirstItemIndex);
        }
    }
}
