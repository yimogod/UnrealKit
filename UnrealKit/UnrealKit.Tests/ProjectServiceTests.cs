using UnrealKit.Core.Projects;

namespace UnrealKit.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "UnrealKit.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateProjectAsync_CreatesExpectedProjectLayout()
    {
        var service = new ProjectService();
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");

        var result = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"));

        Assert.True(result.Validation.IsValid);
        Assert.True(File.Exists(Path.Combine(projectDirectory, "MemoryReview.ukit")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "Config", "DefaultGame.ini")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Content")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Saved")));
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "Intermediate")));
    }

    [Fact]
    public async Task CreateProjectAsync_RejectsNonEmptyDirectory()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "existing.txt"), "existing");
        var service = new ProjectService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateProjectAsync(new CreateProjectRequest(_temporaryDirectory, "MemoryReview")));

        Assert.Contains("不是空目录", exception.Message);
    }

    [Fact]
    public async Task ValidateProjectAsync_ReportsUnsupportedFormatVersion()
    {
        var projectDirectory = Path.Combine(_temporaryDirectory, "MemoryReview");
        var service = new ProjectService();
        var project = await service.CreateProjectAsync(new CreateProjectRequest(projectDirectory, "MemoryReview"));
        var descriptor = await File.ReadAllTextAsync(project.Project.ProjectFilePath);
        await File.WriteAllTextAsync(project.Project.ProjectFilePath, descriptor.Replace("FormatVersion=1", "FormatVersion=999", StringComparison.Ordinal));

        var validation = await service.ValidateProjectAsync(project.Project.ProjectFilePath);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Code == "UKIT002");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
