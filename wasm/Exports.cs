using System.Runtime.InteropServices.JavaScript;

public static partial class Exports
{
    public static void Main() { }

    [JSExport]
    public static string Analyze(byte[] data, string name)
    {
        using var ms = new MemoryStream(data, writable: false);
        return Summary.Analyze(ms, name);
    }
}
