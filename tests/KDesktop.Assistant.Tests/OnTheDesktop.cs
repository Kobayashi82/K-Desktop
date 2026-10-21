namespace KDesktop.Assistant.Tests;

/// <summary>
/// The tests that open windows or watch them on the real desktop: one after another (in parallel, a test waiting for its
/// window's events could miss them under the load of the others).
/// </summary>
[CollectionDefinition(Name)]
public sealed class OnTheDesktop
{
    public const string Name = "Escritorio";
}
