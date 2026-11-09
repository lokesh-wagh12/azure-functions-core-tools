// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public sealed class FlexByocTemplateContractTests
{
    private static readonly Assembly _workloadAssembly = typeof(PowerShellWorkload).Assembly;

    [Fact]
    public void WorkloadAssembly_ContainsCompleteFlexByocTemplatePackage()
    {
        string[] resourceNames = _workloadAssembly.GetManifestResourceNames();

        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.Shared.Dockerfile.template", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.Shared.dockerignore.template", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.EndsWith("FlexByoc.Shared.entrypoint.sh", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.Contains("FlexByoc", StringComparison.Ordinal)
            && name.EndsWith("template.config.template.json", StringComparison.Ordinal));
        resourceNames.Should().Contain(name => name.Contains("FlexByoc", StringComparison.Ordinal)
            && name.EndsWith("template.config.func.host.json", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateManifest_DefinesPowerShellRuntimeAndAtomicMappings()
    {
        using var manifest = JsonDocument.Parse(ReadResource("template.config.template.json"));
        JsonElement root = manifest.RootElement;

        root.GetProperty("identity").GetString().Should().Be("Azure.Functions.Cli.PowerShell.FlexByoc");
        root.GetProperty("shortName").GetString().Should().Be("flex-byoc");
        JsonElement tags = root.GetProperty("tags");
        tags.GetProperty("type").GetString().Should().Be("artifact");
        tags.GetProperty("func-capability").GetString().Should().Be("flex-byoc");
        tags.GetProperty("func-stack").GetString().Should().Be("powershell");

        JsonElement symbols = root.GetProperty("symbols");
        JsonElement runtimeVersion = symbols.GetProperty("RuntimeVersion");
        runtimeVersion.GetProperty("defaultValue").GetString().Should().Be("7.6");
        runtimeVersion.GetProperty("replaces").GetString().Should().Be("{{RUNTIME_VERSION}}");
        runtimeVersion.GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString())
            .Should().Equal("7.2", "7.4", "7.6");

        AssertSwitch(symbols.GetProperty("RuntimeImage"), "{{RUNTIME_IMAGE}}");
        AssertSwitch(symbols.GetProperty("WorkerPackageId"), "{{WORKER_PACKAGE_ID}}");
        AssertSwitch(symbols.GetProperty("WorkerPackageVersion"), "{{WORKER_PACKAGE_VERSION}}");
        AssertSwitch(symbols.GetProperty("WorkerPackageFeed"), "{{WORKER_PACKAGE_FEED}}");
        AssertSwitch(symbols.GetProperty("WorkerPackagePath"), "{{WORKER_PACKAGE_PATH}}");
    }

    [Fact]
    public void TemplateContent_UsesDeclaredReplacementTokens()
    {
        string dockerfile = ReadResource("Shared.Dockerfile.template");

        dockerfile.Should().ContainAll(
            "{{RUNTIME_VERSION}}",
            "{{RUNTIME_IMAGE}}",
            "{{WORKER_PACKAGE_ID}}",
            "{{WORKER_PACKAGE_VERSION}}",
            "{{WORKER_PACKAGE_FEED}}",
            "{{WORKER_PACKAGE_PATH}}");
        ReadResource("Shared.entrypoint.sh").Should().NotContain("\r");
    }

    [Fact]
    public void HostManifest_ExposesOnlyRuntimeVersionOption()
    {
        using var manifest = JsonDocument.Parse(ReadResource("template.config.func.host.json"));
        JsonElement root = manifest.RootElement;

        root.GetProperty("isHidden").GetBoolean().Should().BeTrue();
        JsonElement symbolInfo = root.GetProperty("symbolInfo");
        symbolInfo.EnumerateObject().Select(property => property.Name).Should().Equal("RuntimeVersion");
        symbolInfo.GetProperty("RuntimeVersion").GetProperty("longName").GetString().Should().Be("runtime-version");
    }

    private static void AssertSwitch(JsonElement symbol, string replacement)
    {
        symbol.GetProperty("type").GetString().Should().Be("generated");
        symbol.GetProperty("generator").GetString().Should().Be("switch");
        symbol.GetProperty("replaces").GetString().Should().Be(replacement);
        symbol.GetProperty("parameters").GetProperty("cases").EnumerateArray()
            .Select(item => item.GetProperty("condition").GetString())
            .Should().Equal(
                "(RuntimeVersion == \"7.2\")",
                "(RuntimeVersion == \"7.4\")",
                "(RuntimeVersion == \"7.6\")");
    }

    private static string ReadResource(string suffix)
    {
        string resourceName = _workloadAssembly.GetManifestResourceNames()
            .Single(name => name.Contains("FlexByoc", StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal));
        using Stream stream = _workloadAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
