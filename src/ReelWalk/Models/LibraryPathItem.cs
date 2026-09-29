using CommunityToolkit.Mvvm.ComponentModel;

namespace ReelWalk.Models;
internal sealed partial class LibraryPathItem : ObservableObject
{
    public string FullPath { get; init; }
    public string Name { get; init; }
}
