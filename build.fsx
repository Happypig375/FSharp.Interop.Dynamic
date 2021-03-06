#!/bin/sh
#if bin_sh
  # Doing this because arguments can't be used with /usr/bin/env on linux, just mac
  exec fsharpi --define:mono_posix --exec $0 $*
#endif
#if FSharp_MakeFile

(*
 * Single File Crossplatform FSharp Makefile Bootstrapper
 * Apache licensed - Copyright 2014 Jay Tuley <jay+code@tuley.name>
 * v 2.0 https://gist.github.com/jbtule/11181987
 *
 * How to use:
 *  On Windows `fsi --exec build.fsx <buildtarget>
 *    *Note:* if you have trouble first run "%vs120comntools%\vsvars32.bat" or use the "Developer Command Prompt for VS201X"
 *                                                           or install https://github.com/Iristyle/Posh-VsVars#posh-vsvars
 *
 *  On Mac Or Linux `./build.fsx <buildtarget>`
 *    *Note:* But if you have trouble then use `sh build.fsx <buildtarget>`
 *
 *)

#I "packages/FAKE/tools"
#r "FakeLib.dll"
#r "System.Xml.Linq.dll"

open Fake
open Fake.DotNet.Testing.NUnit3
open Fake.DotNet
open System.Xml.Linq
open System.Xml.XPath

let sln = "./FSharp.Interop.Dynamic.sln"

let commonBuild target =
    let buildMode = getBuildParamOrDefault "configuration" "Release"
    let vsuffix = getBuildParamOrDefault "vsuffix" ""

    let versionPrefix = "Version.props" 
                        |> System.IO.File.ReadAllText 
                        |> XDocument.Parse
                        |> (fun x -> x.XPathEvaluate("//VersionPrefix/text()"))
                        |> (fun x-> x :?> seq<obj>)
                        |> Seq.exactlyOne
                        |> sprintf "%A"

    let vProp =
        if System.Text.RegularExpressions.Regex.IsMatch(vsuffix, "^\d+$") then 
            "Version", versionPrefix + "." + vsuffix
        else
            "VersionSuffix", vsuffix


    let setParams (defaults:MsBuild.MSBuildParams) =
            { defaults with
                ToolsVersion = Some("15.0")
                Verbosity = Some(MsBuild.MSBuildVerbosity.Quiet)
                Targets = [target]
                Properties =
                    [
                        "Configuration", buildMode
                        vProp
                    ]
             }
    MsBuild.build setParams sln |> DoNothing

Target "Restore" (fun () ->
    trace " --- Restore Packages --- "
    
    //because nuget doesn't know how to find msbuild15 on linux 
    let restoreProj = fun args ->
                   directExec (fun info ->
                       info.FileName <- "msbuild"
                       info.Arguments <- "/t:restore " + args) |> ignore

    sln |> restoreProj
)

Target "Clean" (fun () ->
    trace " --- Cleaning stuff --- "
    commonBuild "Clean"
)

Target "Build" (fun () ->
    trace " --- Building the libs --- "
    commonBuild "Build"
)

Target "Test" (fun () ->
    trace " --- Test the libs --- "
    let sendToAppveyer outFile = 
        let appveyor = environVarOrNone "APPVEYOR_JOB_ID"
        match appveyor with
            | Some(jobid) -> 
                use webClient = new System.Net.WebClient()
                webClient.UploadFile(sprintf "https://ci.appveyor.com/api/testresults/nunit/%s" jobid, outFile) |> ignore
            | None -> ()

    let buildMode = getBuildParamOrDefault "configuration" "Release"

    let testDirFromMoniker moniker = sprintf "./Tests/bin/%s/%s/" buildMode moniker
    let outputFileFromMoniker moniker = (testDirFromMoniker moniker) + (sprintf "TestResults.%s.xml" moniker)

    let testDir = testDirFromMoniker "net45"
    let outputFile = outputFileFromMoniker "net45"

    !! (testDir + "Tests.exe")
                       |> NUnit3 (fun p ->
                                 { p with
                                       Labels = All
                                       ResultSpecs = [outputFile] })
                    
    sendToAppveyer outputFile
    try
        DotNetCli.Test
            (fun p -> 
                 { p with 
                       Framework = "netcoreapp2.0"
                       Project = "Tests/Tests.fsproj"
                       Configuration = buildMode
                       AdditionalArgs =["--no-build";"--no-restore";"--logger=trx;LogFileName=testresults.trx"]
                        })
        
    finally
        let appveyor = environVarOrNone "APPVEYOR_JOB_ID"
        match appveyor with
            | Some(jobid) -> 
                 use webClient = new System.Net.WebClient()
                 webClient.UploadFile(sprintf "https://ci.appveyor.com/api/testresults/mstest/%s" jobid,"./Tests/TestResults/testresults.trx") |> ignore
            | None -> ()
)

"Restore"
  ==> "Build"
  ==> "Test"

RunTargetOrDefault "Test"


#else

open System
open System.IO
open System.Diagnostics

(* helper functions *)
#if mono_posix
#r "Mono.Posix.dll"
open Mono.Unix.Native
let applyExecutionPermissionUnix path =
    let _,stat = Syscall.lstat(path)
    Syscall.chmod(path, FilePermissions.S_IXUSR ||| stat.st_mode) |> ignore
#else
let applyExecutionPermissionUnix path = ()
#endif

let doesNotExist path =
    path |> Path.GetFullPath |> File.Exists |> not

let execAt (workingDir:string) (exePath:string) (args:string seq) =
    let processStart (psi:ProcessStartInfo) =
        let ps = Process.Start(psi)
        ps.WaitForExit ()
        ps.ExitCode
    let fullExePath = exePath |> Path.GetFullPath
    applyExecutionPermissionUnix fullExePath
    let exitCode = ProcessStartInfo(
                        fullExePath,
                        args |> String.concat " ",
                        WorkingDirectory = (workingDir |> Path.GetFullPath),
                        UseShellExecute = false)
                   |> processStart
    if exitCode <> 0 then
        exit exitCode
    ()

let exec = execAt Environment.CurrentDirectory

let downloadNugetTo path =
    let fullPath = path |> Path.GetFullPath;
    if doesNotExist fullPath then
        printf "Downloading NuGet..."
        use webClient = new System.Net.WebClient()
        fullPath |> Path.GetDirectoryName |> Directory.CreateDirectory |> ignore
        webClient.DownloadFile("https://dist.nuget.org/win-x86-commandline/latest/nuget.exe", path |> Path.GetFullPath)
        printfn "Done."

let passedArgs = fsi.CommandLineArgs.[1..] |> Array.toList

(* execution script customize below *)

let makeFsx = fsi.CommandLineArgs.[0]

let nugetExe = ".nuget/NuGet.exe"
let fakeExe = "packages/FAKE/tools/FAKE.exe"

downloadNugetTo nugetExe

if doesNotExist fakeExe then
    exec nugetExe ["install"; "fake"; "-Version 5.0.0-alpha010" ; "-OutputDirectory packages"; "-ExcludeVersion"; "-PreRelease"]

exec fakeExe ([makeFsx; "-d:FSharp_MakeFile"] @ passedArgs)

#endif
