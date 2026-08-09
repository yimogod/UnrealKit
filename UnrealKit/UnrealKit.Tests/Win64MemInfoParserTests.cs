using UnrealKit.Core.Parsing;

namespace UnrealKit.Tests;

public sealed class Win64MemInfoParserTests
{
    [Fact]
    public void Parse_ValidOutput_ExtractsAllFields()
    {
        var input = """ 
            ** WIN64 MEMINFO for process MyGame (PID: 12345) **
            WorkingSetMB:           512.34
            PrivateMemoryMB:        456.78
            VirtualMemoryMB:        2048.00
            PagedMemoryMB:          12.34
            NonPagedMemoryMB:       3.45
            PeakWorkingSetMB:       600.00
            PeakVirtualMemoryMB:    2500.00
            Threads:                42
            Handles:                1234
            TotalProcessorTime:     00:15:30.1234567
            """;

        var parser = new Win64MemInfoParser();
        var result = parser.Parse("test.txt", input.Split('\n').ToList());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Report);
        Assert.Equal("MyGame", result.Report!.ProcessName);
        Assert.Equal(12345, result.Report.ProcessId);
        Assert.Equal(512L * 1024 * 1024, result.Report.Counters.WorkingSetBytes!.Value / (1024 * 1024) * 1024 * 1024);
        Assert.Equal(42, result.Report.Counters.ThreadCount);
        Assert.Equal(1234, result.Report.Counters.HandleCount);
        Assert.Equal("00:15:30.1234567", result.Report.Counters.TotalProcessorTime);
    }

    [Fact]
    public void Parse_MissingHeader_ReturnsError()
    {
        var input = "WorkingSetMB: 100.00\nPrivateMemoryMB: 200.00";
        var parser = new Win64MemInfoParser();
        var result = parser.Parse("test.txt", input.Split('\n').ToList());

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Contains(result.Diagnostics, d => d.Code == "WMI100");
    }

    [Fact]
    public void Parse_MalformedHeader_ReturnsError()
    {
        var input = "** WIN64 MEMINFO for process (PID: abc) **\nWorkingSetMB: 100.00";
        var parser = new Win64MemInfoParser();
        var result = parser.Parse("test.txt", input.Split('\n').ToList());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, d => d.Code == "WMI101");
    }

    [Fact]
    public void Parse_MissingCounters_ReturnsReportWithNulls()
    {
        var input = "** WIN64 MEMINFO for process TestProc (PID: 1) **";
        var parser = new Win64MemInfoParser();
        var result = parser.Parse("test.txt", input.Split('\n').ToList());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Report);
        Assert.Null(result.Report!.Counters.WorkingSetBytes);
        Assert.Equal(0, result.Report.Counters.ThreadCount);
    }

    [Fact]
    public void Parse_NaValues_ReturnsNull()
    {
        var input = """ 
            ** WIN64 MEMINFO for process Game (PID: 55) **
            WorkingSetMB:           N/A
            PrivateMemoryMB:        N/A
            Threads:                10
            Handles:                200
            """;

        var parser = new Win64MemInfoParser();
        var result = parser.Parse("test.txt", input.Split('\n').ToList());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Report!.Counters.WorkingSetBytes);
        Assert.Null(result.Report.Counters.PrivateMemoryBytes);
        Assert.Equal(10, result.Report.Counters.ThreadCount);
    }
}