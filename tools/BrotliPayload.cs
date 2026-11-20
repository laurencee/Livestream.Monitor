#:property TargetFramework=net10.0

using System.IO.Compression;

try
{
    if (args.Length != 2) throw new ArgumentException("Usage: dotnet run --file tools/BrotliPayload.cs -- <assemblies> <payload>");

    Directory.CreateDirectory(args[1]);
    Parallel.ForEach(Directory.GetFiles(args[0], "*.dll"), path =>
    {
        var assembly = File.ReadAllBytes(path);
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(assembly.Length)];
        const int Quality = 11;
        const int Window = 22;
        if (!BrotliEncoder.TryCompress(assembly, buffer, out var length, Quality, Window))
            throw new InvalidDataException($"Could not compress {path}.");

        var payload = Path.Combine(args[1], $"{Path.GetFileName(path)}.br");

        using var output = new BinaryWriter(File.Create(payload));
        output.Write(assembly.Length);
        output.Write(buffer.AsSpan(0, length));
    });

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
