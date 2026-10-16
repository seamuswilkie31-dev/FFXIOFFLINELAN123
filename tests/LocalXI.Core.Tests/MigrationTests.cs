using LocalXI.Core.Database;

namespace LocalXI.Core.Tests;

public class MigrationReportTests
{
    [Fact]
    public void NothingToDo_Succeeds()
    {
        var result = MigrationRunner.ParseReport("DONE\t\t\n", exitedCleanly: true, "", errorLog: null);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Ran);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RanMigrations_AreListed()
    {
        var report = "NEEDED\t070_add_mon\tAdding MON\nRUN\t070_add_mon\tAdding MON\nRUN\t071_add_mon_exp\tAdding MON exp\nDONE\t\t\n";
        var result = MigrationRunner.ParseReport(report, exitedCleanly: true, "", errorLog: null);

        Assert.True(result.Succeeded);
        Assert.Equal(["070_add_mon: Adding MON", "071_add_mon_exp: Adding MON exp"], result.Ran);
    }

    [Fact]
    public void MigrationStillNeededAfterRunning_FailsWithItsError()
    {
        var report = "RUN\t999_bad\tBad one\nERROR\t999_bad\tRuntimeError('boom')\nSTILL\t999_bad\tBad one\nDONE\t\t\n";
        var result = MigrationRunner.ParseReport(report, exitedCleanly: true, "", errorLog: null);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Ran);
        Assert.Equal(["999_bad: Bad one (RuntimeError('boom'))"], result.Failed);
    }

    [Fact]
    public void ErrorThenDone_CountsAsRan()
    {
        // migrate() complained, but the migration no longer needs to run: what dbtool would accept too.
        var report = "RUN\t050_x\tX\nERROR\t050_x\tWarning('dup')\nDONE\t\t\n";
        var result = MigrationRunner.ParseReport(report, exitedCleanly: true, "", errorLog: null);

        Assert.True(result.Succeeded);
        Assert.Equal(["050_x: X"], result.Ran);
    }

    [Fact]
    public void ErrorWhileChecking_Fails()
    {
        var result = MigrationRunner.ParseReport("ERROR\t010_y\tProgrammingError('no table')\nDONE\t\t\n", exitedCleanly: true, "", errorLog: null);

        Assert.False(result.Succeeded);
        Assert.Equal(["010_y: ProgrammingError('no table')"], result.Failed);
    }

    [Fact]
    public void ConverterCrash_Fails()
    {
        var result = MigrationRunner.ParseReport("", exitedCleanly: false, "Traceback\nModuleNotFoundError: No module named 'packaging'", errorLog: null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failed, f => f.Contains("No module named 'packaging'"));
    }

    [Fact]
    public void CharacterErrorLog_IsAWarningNotAFailure()
    {
        var result = MigrationRunner.ParseReport("RUN\t007_m\tMissions\nDONE\t\t\n", exitedCleanly: true, "", errorLog: "charid 12: bad blob");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("charid 12: bad blob"));
    }
}
