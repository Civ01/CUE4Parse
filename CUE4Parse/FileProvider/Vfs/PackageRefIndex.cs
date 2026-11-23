using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.IO.Objects;
using CUE4Parse.UE4.VirtualFileSystem;

namespace CUE4Parse.FileProvider.Vfs;

/// <summary>
/// Hard package import graph of every mounted IoStore container, built in a single pass over the
/// container headers (no package is loaded). Covers game, engine and plugin content; native
/// <c>/Script</c> imports and soft references are not part of container store entries.
/// </summary>
public sealed class PackageRefIndex
{
    private static readonly FPackageId[] Empty = [];

    private readonly Dictionary<FPackageId, FPackageId[]> _dependencies;
    private readonly Dictionary<FPackageId, List<FPackageId>> _referencers;
    private readonly IReadOnlyDictionary<FPackageId, GameFile> _files;

    /// <summary>Number of packages that have a store entry.</summary>
    public int PackageCount => _dependencies.Count;

    private PackageRefIndex(Dictionary<FPackageId, FPackageId[]> dependencies, IReadOnlyDictionary<FPackageId, GameFile> files)
    {
        _dependencies = dependencies;
        _files = files;
        _referencers = new Dictionary<FPackageId, List<FPackageId>>(dependencies.Count);

        foreach (var (package, imports) in dependencies)
        {
            foreach (var imported in imports)
            {
                if (!_referencers.TryGetValue(imported, out var list))
                    _referencers[imported] = list = [];
                list.Add(package);
            }
        }
    }

    /// <summary>
    /// Builds the index from the mounted readers. <paramref name="activeFiles"/> is the provider's
    /// package-id → file map; when a package exists in several containers (patches), the store
    /// entry of the container that provides the active file wins.
    /// </summary>
    public static PackageRefIndex Build(IEnumerable<IAesVfsReader> readers, IReadOnlyDictionary<FPackageId, GameFile> activeFiles)
    {
        var dependencies = new Dictionary<FPackageId, FPackageId[]>();
        var fromActiveFile = new HashSet<FPackageId>();
        var optional = new Dictionary<FPackageId, FPackageId[]>();

        foreach (var reader in readers)
        {
            if (reader is not IoStoreReader { ContainerHeader: { } header } ioReader)
                continue;

            var count = Math.Min(header.PackageIds?.Length ?? 0, header.StoreEntries?.Length ?? 0);
            for (var i = 0; i < count; i++)
            {
                var id = header.PackageIds![i];
                var imports = header.StoreEntries![i].ImportedPackages ?? Empty;

                var isActive = ioReader.PackageIdIndex.TryGetValue(id, out var file) &&
                               activeFiles.TryGetValue(id, out var active) && ReferenceEquals(file, active);

                if (isActive)
                {
                    dependencies[id] = imports;
                    fromActiveFile.Add(id);
                }
                else if (!fromActiveFile.Contains(id))
                {
                    dependencies.TryAdd(id, imports);
                }
            }

            // Optional segments (editor-only data split from the package) import on behalf of the same package.
            var optionalCount = Math.Min(header.OptionalSegmentPackageIds?.Length ?? 0, header.OptionalSegmentStoreEntries?.Length ?? 0);
            for (var i = 0; i < optionalCount; i++)
            {
                var imports = header.OptionalSegmentStoreEntries![i].ImportedPackages;
                if (imports is { Length: > 0 })
                    optional[header.OptionalSegmentPackageIds![i]] = imports;
            }
        }

        foreach (var (id, imports) in optional)
        {
            if (!dependencies.TryGetValue(id, out var existing) || existing.Length == 0)
            {
                dependencies[id] = imports;
                continue;
            }

            var merged = new HashSet<FPackageId>(existing);
            merged.UnionWith(imports);
            dependencies[id] = [.. merged];
        }

        return new PackageRefIndex(dependencies, activeFiles);
    }

    /// <summary>Package id of an IoStore package file, without loading it.</summary>
    public static bool TryGetPackageId(GameFile file, out FPackageId id)
    {
        if (file is FIoStoreEntry { IsPackageData: true } entry)
        {
            id = entry.ChunkId.AsPackageId();
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>Whether the package has a store entry in a mounted container.</summary>
    public bool Contains(FPackageId id) => _dependencies.ContainsKey(id);

    /// <summary>Packages that <paramref name="id"/> hard-imports. May include ids that aren't mounted.</summary>
    public IReadOnlyList<FPackageId> GetDependencies(FPackageId id) =>
        _dependencies.TryGetValue(id, out var deps) ? deps : Empty;

    /// <summary>Packages that hard-import <paramref name="id"/>.</summary>
    public IReadOnlyList<FPackageId> GetReferencers(FPackageId id) =>
        _referencers.TryGetValue(id, out var refs) ? refs : Empty;

    /// <summary>The active (highest priority) file for a package id.</summary>
    public bool TryGetFile(FPackageId id, [MaybeNullWhen(false)] out GameFile file) => _files.TryGetValue(id, out file);
}
