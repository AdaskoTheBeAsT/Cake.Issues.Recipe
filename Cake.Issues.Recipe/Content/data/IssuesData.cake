/// <summary>
/// Class for holding shared data between tasks.
/// </summary>
public class IssuesData
{
    private readonly List<IIssue> issues = new List<IIssue>();

    /// <summary>
    /// Gets the root directory of the repository.
    /// </summary>
    public DirectoryPath RepositoryRootDirectory { get; }

    /// <summary>
    /// Gets the root directory of the build script.
    /// </summary>
    public DirectoryPath BuildRootDirectory { get; }

    /// <summary>
    /// Gets the root directory of the project.
    /// Default value is the <see cref="BuildRootDirectory"/>.
    /// </summary>
    public DirectoryPath ProjectRootDirectory { get; set; }

    /// Gets the remote URL of the repository.
    /// </summary>
    public Uri RepositoryRemoteUrl { get; }

    /// <summary>
    /// Gets the SHA ID of the current commit.
    /// </summary>
    public string CommitId { get; }

    /// <summary>
    /// Gets or sets the path to the full issues report.
    /// </summary>
    public FilePath FullIssuesReport { get; set; }

    /// <summary>
    /// Gets or sets the path to the summary issues report.
    /// </summary>
    public FilePath SummaryIssuesReport { get; set; }

    /// <summary>
    /// Gets the build server under which the build is running.
    /// Returns <c>null</c> if running locally or on an unsupported build server.
    /// </summary>
    public IIssuesBuildServer BuildServer { get; }

    /// <summary>
    /// Gets the provider to read information about the Git repository.
    /// </summary>
    public IRepositoryInfoProvider RepositoryInfo { get; }

    /// <summary>
    /// Gets the pull request system used for the code.
    /// Returns <c>null</c> if not running a pull request build or on an unsupported build server.
    /// </summary>
    public IIssuesPullRequestSystem PullRequestSystem { get; }

    /// <summary>
    /// Gets the list of reported issues.
    /// </summary>
    public IEnumerable<IIssue> Issues
    {
        get
        {
            return issues.AsReadOnly();
        }
    }

    /// <summary>
    /// Creates a new instance of the <see cref="IssuesData"/> class.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="repositoryInfoProviderType">Defines how information about the Git repository should be determined.</param>
    public IssuesData(ICakeContext context, RepositoryInfoProviderType repositoryInfoProviderType)
    {
        context.NotNull(nameof(context));

        this.BuildRootDirectory = context.MakeAbsolute(context.Directory("./"));
        context.Information("Build script root directory: {0}", this.BuildRootDirectory);

        this.ProjectRootDirectory = this.BuildRootDirectory;
        context.Information("Project root directory: {0}", this.ProjectRootDirectory);

        this.RepositoryInfo = DetermineRepositoryInfoProvider(context, repositoryInfoProviderType);

        this.RepositoryRootDirectory = context.GitFindRootFromPath(this.BuildRootDirectory);
        context.Information("Repository root directory: {0}", this.RepositoryRootDirectory);

        this.BuildServer = DetermineBuildServer(context);
        if (this.BuildServer != null)
        {
            this.RepositoryRemoteUrl =
                BuildServer.DetermineRepositoryRemoteUrl(context, this.RepositoryRootDirectory);
            context.Information("Repository remote URL: {0}", this.RepositoryRemoteUrl);

            this.CommitId =
                BuildServer.DetermineCommitId(context, this.RepositoryRootDirectory);
            context.Information("CommitId: {0}", this.CommitId);

            this.PullRequestSystem =
                DeterminePullRequestSystem(
                    context,
                    this.RepositoryRemoteUrl);
        }
    }

    /// <summary>
    /// Adds an issue to the data class.
    /// </summary>
    /// <param name="issue">Issue which should be added.</param>
    public void AddIssue(IIssue issue)
    {
        issue.NotNull(nameof(issue));

        this.issues.Add(issue);
    }

    /// <summary>
    /// Adds a list of issues to the data class.
    /// </summary>
    /// <param name="issues">Issues which should be added.</param>
    public void AddIssues(IEnumerable<IIssue> issues)
    {
        issues.NotNull(nameof(issues));

        this.issues.AddRange(issues);
    }

    /// <summary>
    /// Determines the repository info provider to use.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="repositoryInfoProviderType">Defines how information about the Git repository should be determined.</param>
    /// <returns>The repository info provider which should be used.</returns>
    private static IRepositoryInfoProvider DetermineRepositoryInfoProvider(
        ICakeContext context,
        RepositoryInfoProviderType repositoryInfoProviderType)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        switch (repositoryInfoProviderType)
        {
            case RepositoryInfoProviderType.CakeGit:
                context.Information("Using Cake.Git for providing repository information");
                return new CliRepositoryInfoProvider();
            case RepositoryInfoProviderType.Cli:
                context.Information("Using Git CLI for providing repository information");
                return new CliRepositoryInfoProvider();
            default:
                throw new NotImplementedException("Unsupported repository info provider");
        }
    }

    /// <summary>
    /// Determines the build server on which the build is running.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <returns>The build server on which the build is running or <c>null</c> if unknown build server.</returns>
    private static IIssuesBuildServer DetermineBuildServer(ICakeContext context)
    {
        context.NotNull(nameof(context));

        // Could be simplified once https://github.com/cake-build/cake/issues/1684 / https://github.com/cake-build/cake/issues/1580 are fixed.
        if (!string.IsNullOrWhiteSpace(context.EnvironmentVariable("TF_BUILD")) &&
            !string.IsNullOrWhiteSpace(context.EnvironmentVariable("SYSTEM_COLLECTIONURI")) &&
            (
                new Uri(context.EnvironmentVariable("SYSTEM_COLLECTIONURI")).Host == "dev.azure.com" ||
                new Uri(context.EnvironmentVariable("SYSTEM_COLLECTIONURI")).Host.EndsWith("visualstudio.com", StringComparison.InvariantCulture)
            ))
        {
            context.Information("Build server detected: {0}", "Azure Pipelines");
            return new AzureDevOpsBuildServer();
        }

        if (context.AppVeyor().IsRunningOnAppVeyor)
        {
            context.Information("Build server detected: {0}", "AppVeyor");
            return new AppVeyorBuildServer();
        }

        if (context.GitHubActions().IsRunningOnGitHubActions)
        {
            context.Information("Build server detected: {0}", "GitHub Actions");
            return new GitHubActionsBuildServer();
        }

        return null;
    }

    /// <summary>
    /// Determines the pull request system.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="repositoryUrl">The URL of the remote repository.</param>
    /// <returns>The pull request system or <c>null</c> if unknown pull request system.</returns>
    private static IIssuesPullRequestSystem DeterminePullRequestSystem(ICakeContext context, Uri repositoryUrl)
    {
        context.NotNull(nameof(context));
        repositoryUrl.NotNull(nameof(repositoryUrl));

        if (repositoryUrl.Host == "dev.azure.com" || repositoryUrl.Host.EndsWith("visualstudio.com", StringComparison.InvariantCulture))
        {
            context.Information("Pull request system detected: {0}", "Azure Repos");
            return new AzureDevOpsPullRequestSystem();
        }

        if (repositoryUrl.Host == "github.com")
        {
            return new GitHubPullRequestSystem();
        }

        return null;
    }
}
