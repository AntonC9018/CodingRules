using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OverloadConfiguration;

public static class Serializer
{
    // Two parameters already trigger Q19; static methods are included.
    public static void Serialize(object input, Stream output) { } // CR1001: Serialize
    public static void Serialize(object schedule, string outputPath) { } // CR1001: Serialize

    // The original serializer shape also reports independently of CR1000.
    public static Task Export(object input, Stream output, string hash, CancellationToken cancellation) { return Task.CompletedTask; } // CR1001: Export
    public static Task Export(object input, string outputPath, string hash, CancellationToken cancellation) { return Task.CompletedTask; } // CR1001: Export
}

public sealed class Renderer
{
    public void Render(object input, Stream output) { } // CR1001: Render
    public void Render(object input, string outputPath) { } // CR1001: Render

    // Generic parameter names are not type differences; compare their ordinals.
    public void Write<T>(IReadOnlyList<T[]> input, Stream output) { } // CR1001: Write
    public void Write<U>(IReadOnlyList<U[]> values, string path) { } // CR1001: Write
}
