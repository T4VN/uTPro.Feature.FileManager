namespace uTPro.Feature.FileManager.Models;

/// <summary>
/// A configurable File Manager "location" (browsable root). Bound from
/// <c>uTPro:Feature:FileManager:Roots</c>. Each configured root is shown as a card in the
/// Locations overview and browsed as its own confined tree.
/// </summary>
public sealed class FileManagerRootOption
{
    /// <summary>Stable identifier used by the UI/API to select this root (e.g. "web", "media").</summary>
    public string Key { get; set; } = "";

    /// <summary>Display name shown on the location card.</summary>
    public string Label { get; set; } = "";

    /// <summary>Absolute path, or a path relative to the content root.</summary>
    public string Path { get; set; } = "";

    /// <summary>Optional card icon (Umbraco icon alias). Defaults to a folder icon in the UI.</summary>
    public string? Icon { get; set; }

    /// <summary>When true (default) only administrators may access this location.</summary>
    public bool AdminOnly { get; set; } = true;
}
