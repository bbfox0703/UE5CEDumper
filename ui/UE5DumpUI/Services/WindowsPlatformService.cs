using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Windows-specific platform operations.
/// </summary>
public sealed class WindowsPlatformService : IPlatformService, IDisposable
{
    private Mutex? _singleInstanceMutex;

    public bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, Constants.MutexName, out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }
        return true;
    }

    public void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex != null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }

    public string GetAppDataPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public string GetLogDirectoryPath()
    {
        return Path.Combine(GetAppDataPath(), Constants.LogFolderName, Constants.LogSubFolder);
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = desktop.MainWindow;
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }

    public async Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            var topLevel = desktop.MainWindow;
            if (topLevel?.StorageProvider is { } sp)
            {
                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File",
                    SuggestedFileName = defaultFileName,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType(filterName)
                        {
                            Patterns = new[] { $"*{filterExtension}" }
                        }
                    }
                });
                return file?.Path.LocalPath;
            }
        }
        return null;
    }

    public string GetMachineName()
    {
        return Environment.MachineName;
    }

    public void Dispose()
    {
        ReleaseSingleInstance();
    }
}
