using CommunityToolkit.Mvvm.ComponentModel;

namespace ReelWalk.Models;
public sealed partial class FolderChoice : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool ShowSeparator { get; set; }
    public int FileCount { get; set; }
    public bool HasChildren { get; set; }
    public bool IsHere { get; set; }

    public string CountLabel
    {
        get { return FileCount <= 0 ? "" : FileCount.ToString("N0"); }
    }

    public string ToolTipText
    {
        get
        {
            if (FileCount <= 0)
                return "Play this folder";
            if (FileCount == 1)
                return "Play · 1 file";
            return "Play · " + FileCount.ToString("N0") + " files";
        }
    }

    // The folder name, for any control that prints the choice itself.
    // Returns an empty string when Name is missing.
    public override string ToString()
    {
        return Name ?? "";
    }
}
