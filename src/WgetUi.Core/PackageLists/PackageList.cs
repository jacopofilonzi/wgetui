namespace WgetUi.Core.PackageLists;

/// <summary>One package of an exported list: identity plus the version that was installed when it was exported.</summary>
/// <param name="ManagerId">Manager that owns the package (e.g. "winget").</param>
/// <param name="SourceId">Source within the manager (e.g. "winget", "msstore").</param>
/// <param name="Id">Manager-specific package id.</param>
/// <param name="Name">Display name at export time (the id when the file has none).</param>
/// <param name="Version">Pinned version, or null when the list has none.</param>
public sealed record PackageListEntry(string ManagerId, string SourceId, string Id, string Name, string? Version)
{
    /// <summary>Same identity as <see cref="Models.Package.Key"/>.</summary>
    public string Key => $"{ManagerId}/{SourceId}/{Id}";
}

/// <summary>Which file format a list was read from.</summary>
public enum PackageListFormat
{
    /// <summary>wgetui's own <c>.wgetui</c> file.</summary>
    Wgetui,

    /// <summary>The JSON of <c>winget export</c>.</summary>
    WinGetExport,
}

/// <summary>A package list read from a file or a URL.</summary>
/// <param name="Packages">The packages, without duplicates, in file order.</param>
/// <param name="ExportedAt">When it was exported, if the file says.</param>
/// <param name="ExportedBy">What exported it ("wgetui 0.4.0"), if the file says.</param>
/// <param name="Format">The file format.</param>
public sealed record PackageList(IReadOnlyList<PackageListEntry> Packages, DateTimeOffset? ExportedAt, string? ExportedBy, PackageListFormat Format);

/// <summary>A list that cannot be read. Carries a stable <see cref="ErrorCode"/> and an English message for logs.</summary>
public sealed class PackageListException : Exception
{
    public PackageListException()
        : this(Models.ErrorCodes.ListInvalid, "Invalid package list.")
    {
    }

    public PackageListException(string message)
        : this(Models.ErrorCodes.ListInvalid, message)
    {
    }

    public PackageListException(string message, Exception innerException)
        : this(Models.ErrorCodes.ListInvalid, message, innerException)
    {
    }

    public PackageListException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException) => ErrorCode = errorCode;

    public string ErrorCode { get; }
}
