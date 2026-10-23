using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Core.Tests;

public class AssistantCatalogTests
{
    [Fact]
    public void TheListKeepsOnlyGoodEntries()
    {
        var entries = AssistantCatalog.Parse("""
            {
              // a comment, and a trailing comma
              "catalog": 1,
              "assistants": [
                { "id": "lander", "package": "https://example.com/lander.kai" },
                { "id": "lander", "package": "https://example.com/other.kai" },
                { "id": "dot", "package": "http://localhost:8080/dot.kai" },
                { "id": "bad", "package": "http://example.com/bad.kai" },
                { "id": "Not An Id", "package": "https://example.com/x.kai" },
                { "id": "kdesktop", "package": "https://example.com/kdesktop.kai" },
                { "package": "https://example.com/noid.kai" },
              ]
            }
            """);
        Assert.Equal([("lander", "https://example.com/lander.kai"), ("dot", "http://localhost:8080/dot.kai")], entries);
    }

    [Fact]
    public void WhatIsNotACatalogSaysSo()
    {
        Assert.Throws<InvalidDataException>(() => AssistantCatalog.Parse("{\"assistants\": 3}"));
        Assert.Throws<InvalidDataException>(() => AssistantCatalog.Parse("<html>"));
    }
}
