namespace UE5DumpUI.Models;

/// <summary>
/// A single UClass entry from the list_classes command.
/// </summary>
public class GameClassEntry
{
    public string ClassName { get; set; } = "";
    public string ClassAddr { get; set; } = "";
    public string ClassPath { get; set; } = "";
    public string SuperName { get; set; } = "";
    public int PropertyCount { get; set; }
    public int PropertiesSize { get; set; }
    public int Score { get; set; }

    /// <summary>Display-friendly properties size as hex.</summary>
    public string SizeHex => $"0x{PropertiesSize:X}";
}

/// <summary>
/// Result set from the list_classes command.
/// </summary>
public class ClassListResult
{
    public int Total { get; set; }
    public int ScannedObjects { get; set; }
    public int TotalClasses { get; set; }
    public List<GameClassEntry> Classes { get; set; } = new();
}
