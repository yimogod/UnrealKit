using System.Diagnostics;
using UnrealKit.Core.Diagnostics;
using UnrealKit.Core.Processes;

namespace UnrealKit.Core.RenderDoc;

public sealed class RenderDocService : IRenderDocService
{
    private readonly IProcessRunner _processRunner;

    public RenderDocService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<RenderDocExecutionResult> ExecuteAsync(
        RenderDocExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PythonExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ScriptPath);

        var diagnostics = new List<Diagnostic>();
        var startedAt = DateTimeOffset.UtcNow;

        // Validate Python executable
        if (!File.Exists(request.PythonExecutable))
        {
            return new RenderDocExecutionResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: $"Python executable not found: {request.PythonExecutable}",
                OutputDirectory: null,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Diagnostics: [new Diagnostic(DiagnosticSeverity.Error, RenderDocDiagnosticCodes.PythonNotFound,
                    $"Python executable not found: {request.PythonExecutable}",
                    Path: request.PythonExecutable,
                    SuggestedFix: "Verify the Python installation path and ensure Python with RenderDoc API is installed.")]);
        }

        // Validate script
        if (!File.Exists(request.ScriptPath))
        {
            return new RenderDocExecutionResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: $"Script not found: {request.ScriptPath}",
                OutputDirectory: null,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Diagnostics: [new Diagnostic(DiagnosticSeverity.Error, RenderDocDiagnosticCodes.ScriptNotFound,
                    $"Script not found: {request.ScriptPath}",
                    Path: request.ScriptPath,
                    SuggestedFix: "Verify the RenderDoc Python script path.")]);
        }

        // Create output directory
        var outputDirectory = request.OutputDirectory;
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Information,
                    RenderDocDiagnosticCodes.OutputDirectoryCreated,
                    $"Output directory created: {outputDirectory}",
                    Path: outputDirectory));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RenderDocDiagnosticCodes.OutputDirectoryFailed,
                    $"Failed to create output directory: {ex.Message}",
                    Path: outputDirectory,
                    SuggestedFix: "Check directory permissions."));
                // Continue execution even if output dir creation fails; the script may handle its own output
            }
        }

        // Build arguments: python <script> [script args...]
        var allArguments = new List<string> { request.ScriptPath };
        allArguments.AddRange(request.ScriptArguments);

        var timeout = request.Timeout ?? RenderDocExecutionRequest.DefaultTimeout;
        var workingDirectory = request.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(request.ScriptPath);
        }

        // If output directory is specified, pass it as an environment variable the script can pick up
        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            envVars["RENDERDOC_OUTPUT_DIR"] = outputDirectory;
        }

        try
        {
            var processRequest = new ProcessExecutionRequest(
                FileName: request.PythonExecutable,
                Arguments: allArguments,
                WorkingDirectory: workingDirectory,
                Timeout: timeout,
                EnvironmentVariables: envVars.Count > 0 ? envVars : null);

            var result = await _processRunner.RunAsync(processRequest, cancellationToken: cancellationToken);

            if (!result.Succeeded)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RenderDocDiagnosticCodes.ExecutionFailed,
                    $"RenderDoc script exited with code {result.ExitCode}: {request.ScriptPath}",
                    Path: request.ScriptPath));
            }

            return new RenderDocExecutionResult(
                ExitCode: result.ExitCode,
                StandardOutput: result.StandardOutput,
                StandardError: result.StandardError,
                OutputDirectory: outputDirectory,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Diagnostics: diagnostics);
        }
        catch (ProcessExecutionException ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RenderDocDiagnosticCodes.ExecutionFailed,
                $"RenderDoc script execution failed: {ex.Message}",
                Path: request.ScriptPath));

            return new RenderDocExecutionResult(
                ExitCode: ex.Result.ExitCode,
                StandardOutput: ex.Result.StandardOutput,
                StandardError: ex.Result.StandardError,
                OutputDirectory: outputDirectory,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Diagnostics: diagnostics);
        }
    }
}