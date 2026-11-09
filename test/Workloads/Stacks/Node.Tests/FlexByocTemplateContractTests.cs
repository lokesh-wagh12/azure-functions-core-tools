// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public sealed class FlexByocTemplateContractTests
{
    private static readonly Assembly _workloadAssembly = typeof(NodeWorkload).Assembly;

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("TypeScript")]
    public void WorkloadAssembly_ContainsCompleteFlexByocTemplatePackage(string language)
    {
        string[] resourceNames = _workloadAssembly.GetManifestResourceNames();

        resourceNames.Should().Contain(name => IsLanguageResource(name, language, "Dockerfile.template"));
        resourceNames.Should().Contain(name => IsLanguageResource(name, language, "dockerignore.template"));
        resourceNames.Should().Contain(name => IsLanguageResource(name, language, "entrypoint.sh"));
        resourceNames.Should().Contain(name => IsLanguageResource(name, language, "template.config.template.json"));
        resourceNames.Should().Contain(name => IsLanguageResource(name, language, "template.config.func.host.json"));
    }

    [Theory]
    [InlineData("JavaScript", "Azure.Functions.Cli.Node.JavaScript.FlexByoc")]
    [InlineData("TypeScript", "Azure.Functions.Cli.Node.TypeScript.FlexByoc")]
    public void TemplateManifest_DefinesNodeRuntimeContract(string language, string identity)
    {
        using var manifest = JsonDocument.Parse(ReadResource(language, "template.config.template.json"));
        JsonElement root = manifest.RootElement;

        root.GetProperty("identity").GetString().Should().Be(identity);
        root.GetProperty("shortName").GetString().Should().Be("flex-byoc");
        JsonElement tags = root.GetProperty("tags");
        tags.GetProperty("type").GetString().Should().Be("artifact");
        tags.GetProperty("language").GetString().Should().Be(language);
        tags.GetProperty("func-capability").GetString().Should().Be("flex-byoc");
        tags.GetProperty("func-stack").GetString().Should().Be("node");

        JsonElement runtimeVersion = root.GetProperty("symbols").GetProperty("RuntimeVersion");
        runtimeVersion.GetProperty("defaultValue").GetString().Should().Be("26");
        runtimeVersion.GetProperty("replaces").GetString().Should().Be("{{RUNTIME_VERSION}}");
        runtimeVersion.GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString())
            .Should().Equal("18", "20", "22", "24", "26");
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("TypeScript")]
    public void TemplateContent_UsesDeclaredReplacementTokens(string language)
    {
        ReadResource(language, "Dockerfile.template").Should().Contain("{{RUNTIME_VERSION}}");
        ReadResource(language, "entrypoint.sh").Should().NotContain("\r");
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("TypeScript")]
    public void HostManifest_ExposesRuntimeVersionOption(string language)
    {
        using var manifest = JsonDocument.Parse(ReadResource(language, "template.config.func.host.json"));

        manifest.RootElement.GetProperty("isHidden").GetBoolean().Should().BeTrue();
        manifest.RootElement.GetProperty("symbolInfo").GetProperty("RuntimeVersion")
            .GetProperty("longName").GetString().Should().Be("runtime-version");
    }

    private static bool IsLanguageResource(string resourceName, string language, string suffix) =>
        resourceName.Contains($"FlexByoc.{language}.", StringComparison.Ordinal)
        && resourceName.EndsWith(suffix, StringComparison.Ordinal);

    private static string ReadResource(string language, string suffix)
    {
        string resourceName = _workloadAssembly.GetManifestResourceNames()
            .Single(name => IsLanguageResource(name, language, suffix));
        using Stream stream = _workloadAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
