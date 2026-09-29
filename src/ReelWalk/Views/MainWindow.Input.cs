using System;
using Avalonia.Input;
using ReelWalk.Models;
using ReelWalk.Services;
using ReelWalk.Services.Controls;

namespace ReelWalk.Views;
public partial class MainWindow
{
    // Character typed into the folder find.
    // key is the key. Returns null for keys that are not text.
    private static string ExplorerKeyText(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)(key - Key.NumPad0)).ToString();
        if (key == Key.Space)
            return " ";
        if (key == Key.OemMinus || key == Key.Subtract)
            return "-";
        if (key == Key.OemPeriod || key == Key.Decimal)
            return ".";
        return null;
    }

    // Keyboard controls. The explorer keys apply only while it is open.
    // Returns nothing.
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.Playback == null) return;

        var key = e.Key;
        _mods = e.KeyModifiers;

        if (Pressed(key, "folder"))
        {
            _viewModel.ToggleFolderMenu();
            e.Handled = true;
            return;
        }

        if (_viewModel.IsFolderMenuVisible && ExplorerKey(key, e))
        {
            e.Handled = true;
            return;
        }

        if (Pressed(key, "help") || Pressed(key, "help_alt"))
        {
            _viewModel.ToggleHelp();
            e.Handled = true;
            return;
        }

        if (Pressed(key, "close"))
        {
            if (_viewModel.IsFolderMenuVisible)
                _viewModel.HideFolderMenu();
            else if (_viewModel.IsHelpVisible)
                _viewModel.HideHelp();
            else
                Close();
            e.Handled = true;
            return;
        }

        if (Pressed(key, "pause"))
            _viewModel.TogglePauseCommand.Execute(null);
        else if (Pressed(key, "previous"))
            _viewModel.PreviousCommand.Execute(null);
        else if (Pressed(key, "next"))
            _viewModel.NextCommand.Execute(null);
        else if (Pressed(key, "seek_back"))
        {
            _viewModel.Seek(-_seekSeconds);
            RestartToast();
        }
        else if (Pressed(key, "seek_forward"))
        {
            _viewModel.Seek(_seekSeconds);
            RestartToast();
        }
        else if (Pressed(key, "seek_back_fast"))
        {
            _viewModel.Seek(-_seekFastSeconds);
            RestartToast();
        }
        else if (Pressed(key, "seek_forward_fast"))
        {
            _viewModel.Seek(_seekFastSeconds);
            RestartToast();
        }
        else if (Pressed(key, "duration_up"))
        {
            _viewModel.ChangeDuration(1);
            RestartToast();
        }
        else if (Pressed(key, "duration_down"))
        {
            _viewModel.ChangeDuration(-1);
            RestartToast();
        }
        else if (Pressed(key, "volume_up"))
        {
            _viewModel.ChangeVolume(0.1);
            RestartToast();
        }
        else if (Pressed(key, "volume_down"))
        {
            _viewModel.ChangeVolume(-0.1);
            RestartToast();
        }
        else if (Pressed(key, "first"))
            _viewModel.First();
        else if (Pressed(key, "last"))
            _viewModel.Last();
        else if (Pressed(key, "skip_forward"))
            _viewModel.Skip(_skipCount);
        else if (Pressed(key, "skip_back"))
            _viewModel.Skip(-_skipCount);
        else if (Pressed(key, "random"))
            _viewModel.SetMode("Random");
        else if (Pressed(key, "show"))
            _viewModel.CycleMedia();
        else if (Pressed(key, "newest"))
            _viewModel.SetMode("NewestFirst");
        else if (Pressed(key, "sequential"))
            _viewModel.SetMode("Sequential");
        else if (Pressed(key, "ken_burns"))
            _viewModel.ToggleKenBurns();
        else if (Pressed(key, "mute"))
            _viewModel.ToggleMute();
        else if (Pressed(key, "fit"))
        {
            var fit = _viewModel.ToggleFit();
            if (!string.IsNullOrEmpty(fit))
                ApplyImageFit(fit);
        }
        else if (Pressed(key, "copy"))
            _viewModel.CopyCurrentPath();
        else if (Pressed(key, "info"))
            _viewModel.ToggleInfo();
        else if (Pressed(key, "open"))
            DesktopService.OpenInExplorer(_viewModel.CurrentPath());
        else if (Pressed(key, "delete"))
            DeleteImage(true);
        else if (Pressed(key, "delete_now"))
            DeleteImage(false);
        else
            return;

        e.Handled = true;
    }

    // Stores the keys and jump sizes from the toml.
    // config is the loaded file. Returns nothing.
    private void LoadControls(SlideshowConfig config)
    {
        _skipCount = config.SkipCount;
        _seekSeconds = config.SeekSeconds;
        _seekFastSeconds = config.SeekFastSeconds;
        _keys.Clear();
        for (int i = 0; i < ControlCatalog.Defaults.Length; i++)
        {
            var name = ControlCatalog.Defaults[i][0];
            _keys[name] = ControlChord.Parse(config.Key(name));
        }
        _viewModel.ApplyControls(config);
    }

    // True when key is the configured action.
    // name is a control name such as next. Returns false when it is unset.
    private bool Pressed(Key key, string name)
    {
        ControlChord chord;
        if (!_keys.TryGetValue(name, out chord))
            return false;
        return chord.Matches(key, _mods);
    }

    // Folder explorer keys, then typed search text.
    // key is the key that was pressed. Returns true when the explorer used it.
    private bool ExplorerKey(Key key, KeyEventArgs e)
    {
        if (Pressed(key, "explorer_up"))
            _viewModel.MoveExplorerSelection(-1);
        else if (Pressed(key, "explorer_down"))
            _viewModel.MoveExplorerSelection(1);
        else if (Pressed(key, "explorer_home"))
            _viewModel.MoveExplorerSelection(-100000);
        else if (Pressed(key, "explorer_end"))
            _viewModel.MoveExplorerSelection(100000);
        else if (Pressed(key, "explorer_back"))
            _viewModel.GoExplorerUp();
        else if (Pressed(key, "explorer_backspace"))
        {
            if (!_viewModel.BackspaceExplorer())
                _viewModel.GoExplorerUp();
        }
        else if (Pressed(key, "explorer_open"))
            _viewModel.OpenSelectedExplorer();
        else if (Pressed(key, "explorer_play"))
            _viewModel.PlaySelectedExplorer();
        else
        {
            bool ctrl = _mods.HasFlag(KeyModifiers.Control);
            bool alt = _mods.HasFlag(KeyModifiers.Alt);
            var typed = ExplorerKeyText(e.Key);
            if (typed == null || ctrl || alt)
                return false;
            _viewModel.TypeExplorer(typed);
        }
        return true;
    }
}
