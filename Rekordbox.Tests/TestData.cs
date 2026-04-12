using System.Reflection;

namespace Rekordbox.Tests;

/// <summary>
/// Loads XML fixtures that are embedded as resources in this test assembly.
/// Keeps tests hermetic — no dependency on files outside the test project.
/// </summary>
internal static class TestData
{
    /// <summary>Opens a fixture file under <c>Fixtures/</c> as a stream.</summary>
    /// <param name="fileName">Fixture file name, e.g. <c>"minimal.xml"</c>.</param>
    public static Stream OpenFixture(string fileName)
    {
        var assembly = typeof(TestData).Assembly;
        var resourceName = $"Rekordbox.Tests.Fixtures.{fileName}";
        var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Known resources: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        return stream;
    }

    /// <summary>Reads a fixture file as a UTF-8 string.</summary>
    public static string ReadFixture(string fileName)
    {
        using var stream = OpenFixture(fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
