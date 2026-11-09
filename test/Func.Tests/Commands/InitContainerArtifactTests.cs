// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class InitContainerArtifactTests : IDisposable
{
    private const string Stack = "fixture";
    private const string Language = "Fixture";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-init-artifacts", Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();
    private readonly IFunctionsProjectResolver _projectResolver = Substitute.For<IFunctionsProjectResolver>();
    private readonly FixtureProjectInitializer _initializer = new();

    [Theory]
    [InlineData("--docker", "--docker-only", "--docker and --docker-only")]
    [InlineData("--flex-byoc", null, "--flex-byoc requires")]
    [InlineData("--docker", "--dry-run", "--dry-run currently requires --docker-only")]
    public void Parse_InvalidContainerOptionCombination_ReportsValidationError(string firstOption, string? secondOption, string expected)
    {
        TestableInitCommand command = CreateCommand([]);
        RootCommand root = [command];
        List<string> arguments = ["init", ProjectPath, firstOption];
        if (secondOption is not null)
        {
            arguments.Add(secondOption);
        }

        ParseResult result = root.Parse([.. arguments]);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain(expected);
    }

    [Fact]
    public async Task DockerOnly_ExistingProject_SkipsInitializationAndGeneratesFixtureArtifact()
    {
        Directory.CreateDirectory(ProjectPath);
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Docker", "docker", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        int exitCode = await InvokeAsync(command, "--docker-only", "--runtime-version", "8.0");

        exitCode.Should().Be(0);
        _initializer.WasInvoked.Should().BeFalse();
        File.ReadAllText(Path.Combine(ProjectPath, "Dockerfile.fixture")).Should().Be("runtime=8.0");
        _interaction.Lines.Should().Contain("SUCCESS: Generated 1 container artifact for 'Fixture'.");
    }

    [Fact]
    public async Task Docker_NewProject_InitializesBeforeResolvingAndGeneratingArtifact()
    {
        _projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _initializer.WasInvoked.Should().BeTrue();
                return ProjectResolutionResults.Resolved(CreateProject(), "fixture");
            });
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Docker", "docker", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        int exitCode = await InvokeAsync(
            command,
            "--docker",
            "--stack",
            Stack,
            "--language",
            Language,
            "--runtime-version",
            "9.0");

        exitCode.Should().Be(0);
        _initializer.WasInvoked.Should().BeTrue();
        _initializer.Context.Should().NotBeNull();
        _initializer.Context!.RequestedContainerArtifactCapability.Should().Be(ContainerArtifactCapability.Docker);
        _initializer.Context.ContainerArtifactParameters.Should().Contain("Runtime", "9.0");
        File.ReadAllText(Path.Combine(ProjectPath, "Dockerfile.fixture")).Should().Be("runtime=9.0");
    }

    [Fact]
    public async Task FlexByoc_DynamicTemplateArgument_InvalidChoiceReportsTemplateValues()
    {
        Directory.CreateDirectory(ProjectPath);
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Flex", "flex-byoc", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        Func<Task> act = async () => await InvokeAsync(
            command,
            "--docker-only",
            "--flex-byoc",
            "--runtime-version",
            "10.0");

        GracefulException exception = (await act.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.Message.Should().Contain("Supported values are: 8.0, 9.0");
    }

    [Fact]
    public async Task DockerOnly_ExistingArtifactWithoutForce_ReportsCollisionAndPreservesFile()
    {
        Directory.CreateDirectory(ProjectPath);
        string artifactPath = Path.Combine(ProjectPath, "Dockerfile.fixture");
        File.WriteAllText(artifactPath, "user content");
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Docker", "docker", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        Func<Task> act = async () => await InvokeAsync(
            command,
            "--docker-only",
            "--runtime-version",
            "8.0");

        GracefulException exception = (await act.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.Message.Should().Contain("Use --force to replace");
        File.ReadAllText(artifactPath).Should().Be("user content");
    }

    [Fact]
    public async Task DockerOnly_ExistingArtifactWithForce_ReplacesFile()
    {
        Directory.CreateDirectory(ProjectPath);
        string artifactPath = Path.Combine(ProjectPath, "Dockerfile.fixture");
        File.WriteAllText(artifactPath, "user content");
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Docker", "docker", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        int exitCode = await InvokeAsync(
            command,
            "--docker-only",
            "--force",
            "--runtime-version",
            "8.0");

        exitCode.Should().Be(0);
        File.ReadAllText(artifactPath).Should().Be("runtime=8.0");
    }

    [Fact]
    public async Task DockerOnly_DryRun_ReportsChangesWithoutWritingFiles()
    {
        Directory.CreateDirectory(ProjectPath);
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Docker", "docker", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        int exitCode = await InvokeAsync(
            command,
            "--docker-only",
            "--dry-run",
            "--runtime-version",
            "8.0");

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(ProjectPath, "Dockerfile.fixture")).Should().BeFalse();
        _interaction.Lines.Should().Contain("SUCCESS: Would generate 1 container artifact for 'Fixture'.");
    }

    [Fact]
    public async Task DockerOnly_NoApplicableTemplate_ReportsStackAndCapability()
    {
        Directory.CreateDirectory(ProjectPath);
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
            [new("Fixture.Flex", "flex-byoc", Stack, Language, "Dockerfile.fixture", "runtime-version")]);

        Func<Task> act = async () => await InvokeAsync(
            command,
            "--docker-only",
            "--runtime-version",
            "8.0");

        GracefulException exception = (await act.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.Message.Should().Be("The 'fixture' workload does not provide Docker container artifact templates.");
    }

    [Fact]
    public async Task DockerOnly_MultipleApplicableTemplates_ReportsSortedIdentities()
    {
        Directory.CreateDirectory(ProjectPath);
        ConfigureResolvedProject();
        TestableInitCommand command = CreateCommand(
        [
            new("Fixture.Zulu", "docker", Stack, Language, "zulu.txt", "runtime-version"),
            new("Fixture.Alpha", "docker", Stack, Language, "alpha.txt", "runtime-version"),
        ]);

        Func<Task> act = async () => await InvokeAsync(
            command,
            "--docker-only",
            "--runtime-version",
            "8.0");

        GracefulException exception = (await act.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.Message.Should().Be(
            "Multiple artifact templates match the project and arguments: Fixture.Alpha, Fixture.Zulu.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ProjectPath => Path.Combine(_root, "project");

    private TestableInitCommand CreateCommand(IReadOnlyList<ArtifactTemplateFixture> templates)
    {
        string packagePath = TemplateFixtures.WriteArtifactPackage(_root, templates);
        var templaterFactory = new TemplaterFactory(
            new CliConfigurationPathsOptions(Path.Combine(_root, "home")),
            [new WorkloadTemplatePackageSource("Fixture.Workload", packagePath)]);
        var orchestrator = new ContainerArtifactOrchestrator(templaterFactory, new TemplateArgumentParser());
        ILocalSettingsProvider localSettings = Substitute.For<ILocalSettingsProvider>();
        localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
        IHostJsonBundleSectionReader bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        IInstalledBundleWorkloads installedBundles = Substitute.For<IInstalledBundleWorkloads>();
        installedBundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
        return new(
            _interaction,
            new RecordingWorkloadHintRenderer(),
            localSettings,
            _projectResolver,
            orchestrator,
            bundleReader,
            new InstalledBundleScanner(installedBundles),
            [_initializer]);
    }

    private async Task<int> InvokeAsync(TestableInitCommand command, params string[] arguments)
    {
        RootCommand root = [command];
        ParseResult result = root.Parse(["init", ProjectPath, .. arguments]);
        result.Errors.Should().BeEmpty();
        return await command.ExecuteForTestAsync(result, CancellationToken.None);
    }

    private void ConfigureResolvedProject()
    {
        _projectResolver
            .ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(CreateProject(), "fixture"));
    }

    private FixtureFunctionsProject CreateProject() =>
        new(WorkingDirectory.FromExplicit(ProjectPath));

    private sealed class FixtureProjectInitializer : IProjectInitializer
    {
        public string Stack => InitContainerArtifactTests.Stack;

        public string DisplayName => "Fixture";

        public IReadOnlyList<string> SupportedLanguages => [InitContainerArtifactTests.Language];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            new Dictionary<string, IReadOnlyList<string>>();

        public bool WasInvoked { get; private set; }

        public InitContext? Context { get; private set; }

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry) => [];

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            Context = context;
            File.WriteAllText(Path.Combine(context.WorkingDirectory.Info.FullName, "host.json"), """{ "version": "2.0" }""");
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureFunctionsProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload(Stack);

        public override WorkingDirectory WorkingDirectory { get; } = workingDirectory;

        public override string StackName => Stack;

        public override string StackDisplayName => "Fixture";

        public override string? Language => InitContainerArtifactTests.Language;

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class TestableInitCommand(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        ILocalSettingsProvider localSettingsProvider,
        IFunctionsProjectResolver projectResolver,
        ContainerArtifactOrchestrator containerArtifacts,
        IHostJsonBundleSectionReader bundleSectionReader,
        InstalledBundleScanner bundleScanner,
        IEnumerable<IProjectInitializer> initializers)
        : InitCommand(
            interaction,
            hintRenderer,
            localSettingsProvider,
            projectResolver,
            containerArtifacts,
            bundleSectionReader,
            bundleScanner,
            initializers)
    {
        public Task<int> ExecuteForTestAsync(ParseResult parseResult, CancellationToken cancellationToken) =>
            base.ExecuteAsync(parseResult, cancellationToken);
    }
}
