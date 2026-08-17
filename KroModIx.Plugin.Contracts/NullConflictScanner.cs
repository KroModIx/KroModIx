using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Default fuer Hosts &lt; v1.24.0 — meldet keine Konflikte.</summary>
public sealed class NullConflictScanner : IConflictScanner
{
    public static readonly NullConflictScanner Instance = new();
    private NullConflictScanner() { }

    public Task<IReadOnlyList<FileConflict>> ScanAsync(
        string gameKey, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileConflict>>(Array.Empty<FileConflict>());
}
