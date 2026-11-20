using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

namespace Livestream.Monitor.Launcher;

/// <summary>
/// The release build compresses Livestream Monitor's assemblies with Brotli and embeds them in the launcher to shrink the single EXE.
/// At startup, the launcher decompresses and loads the assemblies directly into memory, then runs the app in the same process.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Func<string[], int> start;
        try
        {
            LoadAssembly("ExternalAPIs.dll.br");
            LoadAssembly("Livestream.Monitor.Core.dll.br");
            var app = LoadAssembly("Livestream.Monitor.dll.br");
            var entryPoint = app.EntryPoint ?? throw new InvalidDataException("The application has no entry point.");
            start = entryPoint.CreateDelegate<Func<string[], int>>();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }

        return start(args);
    }

    private static Assembly LoadAssembly(string resourceName)
    {
        using var resource = typeof(Program).Assembly.GetManifestResourceStream(resourceName)
                             ?? throw new InvalidDataException($"Missing payload {resourceName}.");

        using var reader = new BinaryReader(resource);
        var length = reader.ReadInt32();
        if (length <= 0) throw new InvalidDataException($"Invalid payload size for {resourceName}.");

        var compressed = GC.AllocateUninitializedArray<byte>(checked((int)(resource.Length - resource.Position)));
        resource.ReadExactly(compressed);
        var assembly = GC.AllocateUninitializedArray<byte>(length);

        if (!BrotliDecoder.TryDecompress(compressed, assembly, out var written) || written != length)
            throw new InvalidDataException($"Invalid Brotli payload {resourceName}.");

        using var image = new MemoryStream(assembly, writable: false);
        return AssemblyLoadContext.Default.LoadFromStream(image);
    }
}
