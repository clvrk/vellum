<div align="center">
  <div>
    <b>Quick links:</b>
    <br>
    <a href="https://github.com/clarkx86/vellum/releases/latest">Download</a><b> | </b><a href="https://github.com/clarkx86/vellum/tree/master/docs">Documentation</a><b> | </b><a href="https://discord.gg/J2sBaXa">Discord</a>
  </div>
  <hr>
  <img alt="vellum" src="https://papyrus.clarkx86.com/wp-content/uploads/sites/2/2020/06/vellum_logo.png">
  <br>
  <a href="https://travis-ci.com/github/clarkx86/vellum"><img alt="Travis-CI" src="https://travis-ci.com/clarkx86/vellum.svg?branch=master"></a>
  <a href="https://discord.gg/J2sBaXa"><img alt="Discord" src="https://img.shields.io/discord/569841820092203011?label=chat&logo=discord&logoColor=white"></a>
  <a href="https://github.com/clarkx86/vellum/releases/latest"><img alt="GitHub All Releases" src="https://img.shields.io/github/downloads/clarkx86/vellum/total"></a>
  <img alt="Latest supported version" src="https://img.shields.io/endpoint?url=https://cdn.clarkx86.com/vellum/vellum_latest-bds.json&label=bedrock">
  <br><br>
</div>

**vellum** is a **Minecraft: Bedrock Server** (BDS) **automation tool** primarily made to create incremental (hot-)backups and render interactive maps of your world using [**PapyrusCS**](https://github.com/mjungnickel18/papyruscs), all while the server is running without any server-downtime using BDS's `save hold | query | resume` commands.

## Table of contents
- [Get started](#get-started)
  - [Prerequisites](#prerequisites)
  - [Installing and configuring](#installing-and-configuring)
  - [Incremental backups](#incremental-backups)
  - [Restoring backups](#restoring-backups)
  - [PapyrusCS integration](#papyruscs-integration)
- [Configuration overview](#configuration-overview)
  - [Example configuration](#example-configuration)
- [Parameters & Commands](#parameters--commands)
  - [Parameters](#parameters)
  - [Commands](#commands)
- [Compiling from source](#compiling-from-source)
- [Disclaimer](#disclaimer)

## Get started
### Prerequisites
Before starting to setting up this tool it is recommended to already have a [Bedrock Dedicated Server](https://www.minecraft.net/de-de/download/server/bedrock/) configured.
If you choose not to go with the self-contained release of this tool, you must have the latest [.NET Core runtime](https://docs.microsoft.com/en-us/dotnet/core/install/linux-package-manager-ubuntu-1804#install-the-net-core-runtime) installed aswell.

### Installing and configuring
First of all grab the latest pre-compiled binary from the [**releases page**](https://github.com/clarkx86/vellum/releases/latest) (or [compile from source](#compiling-from-source)). You will find three releases, two of them for Linux with a larger self-contained one which comes bundled with the .NET Core runtime and a smaller archive which depends on you having the .NET Core runtime already installed on your system, as well as a release for Windows.
Download and extract the archive and `cd` into the directory with the extracted files.

On Linux, you may need to give yourself execution permissions with:
```bash
chmod +x ./vellum
```
Now run this tool for the first time by typing:
```bash
./vellum
```
This will generate a new `configuration.json` in the directory relative to the vellum executable and then exit. Edit this file and specify at least all required parameters ([see below for an overview](https://github.com/clarkx86/vellum#configuration)).

Now you can restart vellum once again with the same command as above. It should now spawn the BDS instance for you and perform backups on the specified interval (do not start the server manually).
Once the server has launched through this tool you will be able to use the server console and its commands just like you normally would.

### Incremental backups
To create incremental world backups make sure the `CreateBackups` option is set to `true`. Backups will be stored in the directory specified by `ArchivePath`. This tool will automatically delete the oldest backups in that directory according to the threshold specified by the `BackupsToKeep` option (`-1` to not delete any older archives) to prevent eventually running out of disk space.

### Restoring backups
Recent backups can be restored using the `--restore` (or `-r`) flag, followed by the path to an archive. It is sufficient to merely supply the archives filename without its full path and vellum will look for this file in the `ArchivePath` of the specified (or default) configuration. You can however also specify an absolute path.\
After successfully restoring a backup, vellum will start and invoke the server like usual. If you do not want the server to automatically start after restoring a backup, specify the `--no-start` flag.

Usage examples:
```bash
# Restore the specified archive from vellums backup directory and start the server
vellum -r "2020-07-30_10-59_MyWorld.zip"

# Restore the specified archive from its absolute path and don't start the server afterwards
vellum -r "~/vellum/backups/2020-07-30_10-59_MyWorld.zip" --no-start
```

### PapyrusCS integration
This tool can automatically execute the [**PapyrusCS**](https://github.com/mjungnickel18/papyruscs) map-rendering tool on an interval. To do so you have to set `EnableRenders` to `true` and specify an interval in minutes with `RenderInterval`.
You can add your own arguments that will be attached when PapyrusCS is called. When configuring you will find two keys, `PapyrusGlobalArgs` and an array called `PapyrusTasks`. The value in `PapyrusGlobalArgs` specifies arguments that will be attached for each PapyrusCS task when executed, while `PapyrusTasks` represent an array of individual processes (or tasks). Adding an entry to the array represents another task that will be executed after the previous one has finished, this way it is possible to make PapyrusCS render multiple dimensions or have different configurations in general. Again, the same `PapyrusGlobalArgs` will be present for each of these tasks individually.

When specifying additional arguments for `PapyrusGlobalArgs` please make sure to **append** to the pre-generated entry (do not edit the `-w` and `-o` parameters!).
Please thoroughly verify that your paths and arguments are correct, you can find a configuration-example [here](https://github.com/clarkx86/vellum/blob/master/examples/basic_example.json).

## Configuration overview
When you run this tool for the first time, it will generate a `configuration.json` and terminate. Before restarting the tool, edit this file to your needs. Here is a quick overview:
```
KEY               VALUE               ABOUT
----------------------------------------------------------
REQUIRED SETTINGS
-----------------
BdsBinPath         String  (!)        Absolute path to the the Bedrock Server
                                      executable (similar to "/../../bedrock_server"
                                      on Linux or "/../../bedrock_server.exe" on 
                                      Windows)

WorldName          String  (!)        Name of the world located in the servers
                                      /worlds/ directory (specify merely the name and
                                      not the full path)
---------------
BACKUP SETTINGS
---------------
EnableBackups      Boolean (!)        Whether to create world-backups as .zip-archives

BackupInterval     Double             Time in minutes to take a backup and create a
                                      .zip-archive

ArchivePath        String             Path where world-backup archives should be
                                      created

BackupsToKeep      Integer            Amount of backups to keep in the "ArchivePath"-
                                      directory, old backups automatically get deleted

OnActiviyOnly      Boolean            If set to "true", vellum will only perform a
                                      backup if at least one player has connected
                                      since the previous backup was taken, in order to
                                      only archive worlds which have actually been
                                      modified

StopBeforeBackup   Boolean            Whether to stop, take a backup and then restart
                                      the server instead of taking a hot-backup

NotifyBeforeStop   Integer            Time in seconds before stopping the server for a
                                      backup, players on the server will be
                                      notified with a chat message

BackupOnStartup    Boolean            Whether to create a full backup of the specified
                                      world before starting the BDS process
                                      IMPORTANT: It is highly encouraged to leave
                                      this setting on "true"

PreExec            String             An arbitrary command that gets executed before
                                      each backup starts

PostExec           String             An arbitrary command that gets executed after
                                      each has finished
---------------
RENDER SETTINGS
---------------
EnableRenders      Boolean (!)        Whether to create an interactive map of the world
                                      using PapyrusCS

PapyrusBinPath     String             Absolute path to the papyrus executable (similar
                                      to "/../../PapyrusCs" on Linux or
                                      "/../../PapyrusCs.exe" on Windows)

PapyrusOutputPath  String             Output path for the rendered papyrus map

RenderInterval     Double             Time in minutes to run a backup and render map

PapyrusGlobalArgs  String             Global arguments that are present for each
                                      rendering task specified in the "PapyrusArgs"-
                                      array
                                      IMPORTANT: Do not change the already provided
                                      --world and --ouput arguments

PapyrusTasks       String [Array]     An array of additional arguments for papyrus,
                                      where each array entry executes another
                                      PapyrusCS process after the previous one has
                                      finished (e.g. for rendering of multiple
                                      dimensions)

LowPriority        Boolean            Start render process at lowest OS priority
                                      (Default: false)
-------------------
ADDITIONAL SETTINGS
-------------------
QuietMode          Boolean (!)        Suppress notifying players in-game that vellum
                                      is creating a backup and render

HideStdout         Boolean (!)        Whether to hide the console output generated by
                                      the PapyrusCS rendering process, setting this
                                      to "true" may help debug your configuration but
                                      will result in a more verbose output

BusyCommands       Boolean (!)        Allow executing BDS commands while the tool is
                                      taking backups

CheckForUpdates    Boolean (!)        Whether to check for updates on startup

StopBdsOnException Boolean (!)        Should vellum unexpectedly crash due to an
                                      unhandled exception, this sets whether to send a 
                                      "stop" command to the BDS process to prevent it
                                      from keep running in detached mode otherwise 

BdsWatchdog        Boolean (!)        Monitor BDS process and restart if unexpectedly
                                      exited. Will try to restart process a maximum of 
                                      3 times. This retry count is reset when BDS
                                      instance is deemed stable.
----------------------------------------------------------
* values marked with (!) are required, non-required values should be provided depending on your specific configuration
```

### Example configuration
Below you'll find an example configuration.

<details>
<summary>Example configuration</summary>

```json
{
    "BdsBinPath": "/home/server/bedrock-server/bedrock_server",
    "WorldName": "Bedrock level",
    "Backups": {
      "EnableBackups": true,
      "BackupInterval": 60.0,
      "ArchivePath": "./backups/",
      "BackupsToKeep": 10,
      "OnActivityOnly": true,
      "StopBeforeBackup": false,
      "NotifyBeforeStop": 60,
      "BackupOnStartup": true,
      "PreExec": "echo Starting!",
      "PostExec": "echo Success!"
    },
    "Renders": {
      "EnableRenders": true,
      "PapyrusBinPath": "/home/server/papyruscs/PapyrusCs",
      "PapyrusOutputPath": "/home/server/papyruscs_output",
      "RenderInterval": 180.0,
      "PapyrusGlobalArgs": "-w ${WORLD_PATH} -o ${OUTPUT_PATH} --htmlfile index.html --deleteexistingupdatefolder",
      "PapyrusTasks": [
        "--dim 0",
        "--dim 1",
        "--dim 2"
      ]
    },
    "QuietMode": false,
    "HideStdout": true,
    "BusyCommands": true,
    "CheckForUpdates": true,
    "StopBdsOnException": true
}
```
</details>

## Parameters & Commands
### Parameters
Overview of available launch parameters:
```
PARAMETER                             ABOUT
----------------------------------------------------------
-c | --configuration                  Specifies a custom configuration file.
                                      (Default: configuration.json)

-h | --help                           Prints an overview of available parameters.

-r | --restore <archive path>         Restores a backup from the specified archive.

--no-start                            In conjunction with the --restore flag, this tells the
                                      application to not start the server after successfully
                                      restoring a backup.
```
Parameters are optional and will default to their default values if not specified.

### Commands
vellum also provides a few new, and overloads some existing commands that you can execute to force-invoke backup- or rendering tasks and schedule server shutdowns.
```
COMMAND                               ABOUT
----------------------------------------------------------
force start backup                    Forces taking a (hot-)backup (according to your
                                      "StopBeforeBackup" setting)

force start render                    Forces PapyrusCS to execute and render your
                                      world

stop <time in seconds>                Schedules a server shutdown and notifies players
                                      in-game

reload vellum                         Reloads the previously specified (or default)
                                      configuration file

updatecheck                           Fetches the latest BDS & vellum version and
                                      displays them in the console
```

## Compiling from source
If you want to compile vellum from source instead of using the pre-built binaries, you'll first need to install [.NET Core](https://docs.microsoft.com/en-us/dotnet/core/install/sdk?pivots=os-linux) for your operating system. Clone this repository and `cd` into the `src` directory. Then run the following command to build the vellum executable:
```
dotnet build vellum.csproj -c Release /p:OutputType=Exe /p:PublishSingleFile=false
```
If you want to build the library instead of the executable, run this command:
```
dotnet build vellum.csproj -c Release /p:OutputType=Library
```

## Disclaimer
Use this tool at **your own risk**! When using this software you agree to not hold us liable for any corrupted save data or deleted files. Make sure to configure everything correctly and thoroughly!

If you find any bugs, please report them on the issue tracker here on GitHub, our dedicated [Discord server](https://discord.gg/J2sBaXa) or send me an [e-mail](mailto:clarkx86@outlook.com?subject=GitHub%3A%20vellum). 