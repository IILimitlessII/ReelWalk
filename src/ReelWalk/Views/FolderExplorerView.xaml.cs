using Avalonia.Controls;

namespace ReelWalk.Views;
public partial class FolderExplorerView : UserControl
{
    // Builds the folder explorer.
    // Returns nothing.
    public FolderExplorerView()
    {
        InitializeComponent();
    }

    // Scrolls the highlighted folder into view.
    // Returns nothing.
    internal void ScrollSelectionIntoView()
    {
        if (ExplorerList == null || ExplorerList.SelectedItem == null || !ExplorerList.IsVisible)
            return;
        ExplorerList.ScrollIntoView(ExplorerList.SelectedItem);
    }
}
