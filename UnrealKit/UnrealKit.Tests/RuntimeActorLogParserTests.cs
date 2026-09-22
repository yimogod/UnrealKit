using UnrealKit.Core.ActorControl;

namespace UnrealKit.Tests;

public sealed class RuntimeActorLogParserTests
{
    [Fact]
    public void Parse_UsesLastActorSectionAndExtractsMemoryColumns()
    {
        var lines = new[]
        {
            "[2026.09.21-23.06.20:001][415]Obj List: Class=Actor",
            "[2026.09.21-23.06.20:002][415]old header 1",
            "[2026.09.21-23.06.20:003][415]old header 2",
            "[2026.09.21-23.06.20:004][415]old header 3",
            "[2026.09.21-23.06.20:005][415]Actor /Game/Old.Old:PersistentLevel.Old_0 1 1 0 0 0 0",
            "[2026.09.21-23.06.20:006][415]Objects (Total: 1)",
            "[2026.09.21-23.06.21:001][415]Obj List: Class=Actor",
            "[2026.09.21-23.06.21:002][415]Object NumKB MaxKB ResExcKB ResExcDedSysKB ResExcDedVidKB ResExcUnkKB",
            "[2026.09.21-23.06.21:003][415]------ ----- ----- -------- ------------ ------------ ----------",
            "[2026.09.21-23.06.21:004][415]",
            "[2026.09.21-23.06.21:781][415]CineCameraActor /Game/Logic/Camera.Cam:PersistentLevel.CineCameraActor_0 2.72 2.72 0.00 0.00 0.00 0.00",
            "[2026.09.21-23.06.21:782][415]Actor /Game/Logic/Light.Light:PersistentLevel.Light_0 4 5 1 2 3 4",
            "[2026.09.21-23.06.21:783][415]Objects (Total: 2)"
        };

        var result = new RuntimeActorLogParser().Parse("actor.log", lines);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Actors.Count);
        var camera = result.Actors[0];
        Assert.Equal("CineCameraActor", camera.ClassName);
        Assert.Equal("/Game/Logic/Camera.Cam:PersistentLevel.CineCameraActor_0", camera.ObjectPath);
        Assert.Equal(2.72m, camera.NumKb);
        Assert.Equal(0m, camera.ResExcUnkKb);
        Assert.Equal(11, camera.LineNumber);
    }

    [Fact]
    public void Parse_MissingTerminator_ReturnsSpecificDiagnostic()
    {
        var result = new RuntimeActorLogParser().Parse("actor.log", [
            "[x]Obj List: Class=Actor",
            "[x]header 1",
            "[x]header 2",
            "[x]header 3",
            "[x]Actor /Game/Test.Test:PersistentLevel.Actor_0 1 1 0 0 0 0"
        ]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuntimeActorDiagnosticCodes.ActorListTerminatorMissing, diagnostic.Code);
    }
}
