namespace CgfConverter.PackFileSystem;

public sealed class CascadedPackFileSystem : IPackFileSystem, IDisposable
{
    private readonly List<IPackFileSystem> _underlying = new();
    private bool _disposed;

    public void Add(IPackFileSystem system)
    {
        _underlying.Insert(0, system);
    }

    public Stream GetStream(string path)
    {
        foreach (var x in _underlying)
        {
            try
            {
                return x.GetStream(path);
            }
            catch (IOException)
            {
                // pass
            }
        }

        if (File.Exists(path))
            return new FileStream(path, FileMode.Open, FileAccess.Read);

        throw new FileNotFoundException();
    }

    public bool Exists(string path) => _underlying.Any(x => x.Exists(path)) || File.Exists(path);

    public string[] Glob(string pattern)
    {
        var temporaryRootDir = Path.IsPathRooted(pattern)
            ? Path.GetPathRoot(pattern)!
            : Path.GetPathRoot(Path.GetFullPath("."))!;

        return _underlying.SelectMany(x => x.Glob(pattern))
            .Concat(new RealFileSystem(temporaryRootDir)
                .Glob(Path.Combine(Path.GetFullPath("."), pattern)[temporaryRootDir.Length..])
                .Select(x => Path.Combine(temporaryRootDir, x)))
            .Distinct()
            .ToArray();
    }

    public byte[] ReadAllBytes(string path)
    {
        foreach (var x in _underlying)
        {
            try
            {
                return x.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                // pass
            }
        }

        if (File.Exists(path))
            return File.ReadAllBytes(path);

        throw new FileNotFoundException();
    }

    private void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;

        foreach (var x in _underlying)
            if (x is IDisposable d)
                d.Dispose();
        _underlying.Clear();
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
