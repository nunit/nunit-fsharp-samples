//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// DISCOVERY VARS
//////////////////////////////////////////////////////////////////////

string[] SolutionList = null;
string[] ProjList = null;
var PROJ_EXT = "*.fsproj";

//////////////////////////////////////////////////////////////////////
// DEFINE RUN CONSTANTS
//////////////////////////////////////////////////////////////////////

var ROOT_DIR = Context.Environment.WorkingDirectory.FullPath;
var TOOLS_DIR = ROOT_DIR + "/tools/";
var NUNIT3_CONSOLE = TOOLS_DIR + "NUnit.ConsoleRunner/tools/nunit3-console.exe";
var PACKAGES_CONFIG = TOOLS_DIR + "packages.config";

//////////////////////////////////////////////////////////////////////
// ERROR LOG
//////////////////////////////////////////////////////////////////////

var ErrorDetail = new List<string>();

//////////////////////////////////////////////////////////////////////
// DISCOVER SOLUTIONS
//////////////////////////////////////////////////////////////////////

Task("DiscoverSolutions")
.Does(() =>
    {
        SolutionList = System.IO.Directory.GetFiles(ROOT_DIR, "*.sln", SearchOption.AllDirectories);
        ProjList = System.IO.Directory.GetFiles(ROOT_DIR, PROJ_EXT, SearchOption.AllDirectories);
    });

//////////////////////////////////////////////////////////////////////
// CLEAN
//////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////

Task("Clean")
.IsDependentOn("DiscoverSolutions")
.Does(() =>
    {
        foreach(var proj in ProjList)
            CleanDirectory(DirFrom(proj) + "/bin/" + configuration);
    });

//////////////////////////////////////////////////////////////////////
// RESTORE PACKAGES
//////////////////////////////////////////////////////////////////////

Task("InitializeBuild")
.IsDependentOn("DiscoverSolutions")
.Does(() =>
    {
        foreach(var sln in SolutionList)
            NuGetRestore(sln);
    });

//////////////////////////////////////////////////////////////////////
// BUILD
//////////////////////////////////////////////////////////////////////

Task("Build")
.IsDependentOn("InitializeBuild")
.Does(() =>
    {
        foreach(var proj in ProjList)
        {
            var projName = System.IO.Path.GetFileNameWithoutExtension(proj);
            DisplayHeading("Building " + projName + " sample");

            try
            {
                MSBuild(proj, CreateSettings());
            }
            catch (Exception e)
            {
                // Just record and continue, since samples are independent
                ErrorDetail.Add("     * " + projName + " build failed.");
            }
        }
    });

// Workaround until Cake 0.34 (https://github.com/cake-build/cake/issues/2546#issuecomment-490756492)
MSBuildSettings CreateSettings()
{
    var settings = new MSBuildSettings { Verbosity = Verbosity.Minimal, Configuration = configuration };

    if (IsRunningOnWindows())
    {
        var vsLatest = VSWhereLatest();
        var msBuildPath = vsLatest?.CombineWithFilePath("./MSBuild/Current/Bin/MSBuild.exe");

        if (msBuildPath != null && !FileExists(msBuildPath))
            msBuildPath = vsLatest?.CombineWithFilePath("./MSBuild/15.0/Bin/MSBuild.exe");

        if (!FileExists(msBuildPath))
            throw new Exception($"Failed to find MSBuild: {msBuildPath}");

        settings.ToolPath = msBuildPath;
    }
    else
        settings.ToolPath = Context.Tools.Resolve("msbuild");

    return settings;
}

//////////////////////////////////////////////////////////////////////
// TEST
//////////////////////////////////////////////////////////////////////

Task("Test")
.IsDependentOn("Build")
.Does(() =>
    {
        foreach(var proj in ProjList)
        {
            var bin = DirFrom(proj) + "/bin/" + configuration + "/";
            var projName = System.IO.Path.GetFileNameWithoutExtension(proj);
            var dllName = bin + projName + ".dll";

            DisplayHeading("Testing " + projName + " sample");

            int rc = StartProcess(NUNIT3_CONSOLE,
                                    new ProcessSettings()
                                    {
                                        Arguments = dllName
                                    });

            if (rc > 0)
                ErrorDetail.Add(string.Format("{0}: {1} tests failed", projName, rc));
            else if (rc < 0)
                ErrorDetail.Add(string.Format("{0} exited with rc = {1}", projName, rc));
        }
    });

//////////////////////////////////////////////////////////////////////
// TEARDOWN TASK
//////////////////////////////////////////////////////////////////////

Teardown(context =>
    {
        CheckForError(ref ErrorDetail);
    });

void CheckForError(ref List<string> errorDetail)
{
    if(errorDetail.Count != 0)
    {
        var copyError = new List<string>();
        copyError = errorDetail.Select(s => s).ToList();
        errorDetail.Clear();
        throw new Exception("One or more tasks failed, breaking the build.\n"
            + copyError.Aggregate((x,y) => x + "\n" + y));
    }
}

//////////////////////////////////////////////////////////////////////
// HELPER METHODS
//////////////////////////////////////////////////////////////////////

string DirFrom(string filePath)
{
    return System.IO.Path.GetDirectoryName(filePath);
}

void DisplayHeading(string heading)
{
    Information("");
    Information("----------------------------------------");
    Information(heading);
    Information("----------------------------------------");
    Information("");
}


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Rebuild")
.IsDependentOn("Clean")
.IsDependentOn("Build");

Task("Appveyor")
.IsDependentOn("Build")
.IsDependentOn("Test");

Task("Travis")
.IsDependentOn("Build")
.IsDependentOn("Test");

Task("Default")
.IsDependentOn("Build");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);