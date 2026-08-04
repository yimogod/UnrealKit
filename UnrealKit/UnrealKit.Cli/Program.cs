using System.Text.Json;
using UnrealKit.Core.Projects;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return 0;
    }

    if (!string.Equals(arguments[0], "project", StringComparison.OrdinalIgnoreCase) || arguments.Length < 2)
    {
        Console.Error.WriteLine("未知命令。仅支持 project create、project info 和 project validate。");
        PrintUsage();
        return 2;
    }

    var service = new ProjectService();
    try
    {
        return arguments[1].ToLowerInvariant() switch
        {
            "create" => await CreateProjectAsync(service, arguments[2..]),
            "info" => await ShowProjectInfoAsync(service, arguments[2..]),
            "validate" => await ValidateProjectAsync(service, arguments[2..]),
            _ => FailUnknownProjectCommand()
        };
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"错误: {exception.Message}");
        return 1;
    }
}

static async Task<int> CreateProjectAsync(IProjectService service, string[] arguments)
{
    if (arguments.Length != 3 || !string.Equals(arguments[1], "--name", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("用法: unrealkit project create <directory> --name <name>");
        return 2;
    }

    var result = await service.CreateProjectAsync(new CreateProjectRequest(arguments[0], arguments[2]));
    Console.WriteLine($"已创建工程: {result.Project.ProjectFilePath}");
    return WriteValidation(result.Validation);
}

static async Task<int> ShowProjectInfoAsync(IProjectService service, string[] arguments)
{
    var json = arguments.Length == 3 && string.Equals(arguments[1], "--format", StringComparison.OrdinalIgnoreCase) && string.Equals(arguments[2], "json", StringComparison.OrdinalIgnoreCase);
    if (arguments.Length != 1 && !json)
    {
        Console.Error.WriteLine("用法: unrealkit project info <project.ukit> [--format json]");
        return 2;
    }

    var project = await service.OpenProjectAsync(arguments[0]);
    var validation = await service.ValidateProjectAsync(arguments[0]);
    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { project, validation }, new JsonSerializerOptions { WriteIndented = true }));
        return validation.IsValid ? 0 : 1;
    }

    Console.WriteLine($"工程: {project.Descriptor.ProjectName}");
    Console.WriteLine($"描述符: {project.ProjectFilePath}");
    Console.WriteLine($"根目录: {project.RootDirectory}");
    Console.WriteLine($"格式版本: {project.Descriptor.FormatVersion}");
    Console.WriteLine($"UE 项目: {project.Settings.UnrealProjectName}");
    return WriteValidation(validation);
}

static async Task<int> ValidateProjectAsync(IProjectService service, string[] arguments)
{
    if (arguments.Length != 1)
    {
        Console.Error.WriteLine("用法: unrealkit project validate <project.ukit>");
        return 2;
    }

    return WriteValidation(await service.ValidateProjectAsync(arguments[0]));
}

static int WriteValidation(ProjectValidationResult validation)
{
    foreach (var diagnostic in validation.Diagnostics)
    {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}{(diagnostic.Path is null ? string.Empty : $" ({diagnostic.Path})")}");
    }

    Console.WriteLine(validation.IsValid ? "校验通过。" : "校验失败。");
    return validation.IsValid ? 0 : 1;
}

static int FailUnknownProjectCommand()
{
    Console.Error.WriteLine("未知 project 子命令。");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("UnrealKit CLI");
    Console.WriteLine("  unrealkit project create <directory> --name <name>");
    Console.WriteLine("  unrealkit project info <project.ukit> [--format json]");
    Console.WriteLine("  unrealkit project validate <project.ukit>");
}
