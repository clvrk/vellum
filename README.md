<html>
    <body>
        <div align="center">
            <h1>papyrus.automation</h1>
            <a href="https://travis-ci.com/github/clarkx86/papyrus-automation"><img alt="Travis-CI" src="https://travis-ci.com/clarkx86/papyrus-automation.svg?token=HbQVu7TQ88w44jXpWo3s&branch=master"></a>
            <a href="https://discord.gg/J2sBaXa"><img alt="Discord" src="https://img.shields.io/discord/569841820092203011?label=chat&logo=discord&logoColor=white"></a>
            <br><br>
        </div>
    </body>
</html>

This is a **Minecraft: Bedrock Server** (BDS) backup and map-rendering **automation** tool primarily made to create incremental backups and render interactive maps of your world using [**papyrus.cs**](https://github.com/mjungnickel18/papyruscs), all while the server is running without any server-downtime using BDS's `save hold | query | resume` commands.

## How does it work?
When this tool gets executed it creates an initial full backup of your world. Then it will launch your BDS instance as a child-process and redirects its stdout and stdin. It will then listen for certain "events" from BDS's stdout (like "Server started" messages, etc.) to determin it's current status. On an interval it will execute the `save hold | query | resume` commands and copies the required files to a temporary backup folder and compresses the world as a `.zip`-archive. It will then call papyrus.cs with user-individual arguments to render the world using the temporary world-backup directory.

## Get started
### Prerequisites
Before starting to set up this tool it is recommended to already have a [Bedrock Dedicated Server](https://www.minecraft.net/de-de/download/server/bedrock/) configured.
If you choose not to go with the self-contained release of this tool, you must have the latest [.NET Core runtime](https://docs.microsoft.com/en-us/dotnet/core/install/linux-package-manager-ubuntu-1804#install-the-net-core-runtime) installed aswell.

### Installing and configuring
First of all grab the latest pre-compiled binary from the release-tab or by [**clicking here**](https://github.com/clarkx86/papyrus-automation/releases/latest). You will find two releases: A larger self-contained archive which comes bundled with the .NET Core runtime and a smaller archive which depends on you having the .NET Core runtime already installed on your system.
Download and extract the archive and `cd` into the directory with the extracted files.

You may need to give yourself execution permission with:
```
chmod +x ./papyrus-automation
```
Now run this tool for the first time by typing:
```
./papyrus-automation
```
This will generate a new `configuration.json` in the same directory. Edit this file and specify at least all required parameters ([see below for an overview](https://github.com/clarkx86/papyrus-automation#configuration)).

Now you can restart the tool one more time with the same command as above. It should now spawn the BDS instance for you and execute renders on the specified interval (do not start the server manually).
Once the server has launched through this tool you will be able to use the server console and use it's commands just like you normally would.

### Incremental backups
To create incremental world backups make sure the `CreateBackups` option is set to `true`. Backups will be stored in the directory specified by `ArchivePath`. This tool will automatically delete the oldest backups in that directory according to the threshold specified by the `BackupsToKeep` option to prevent eventually running out of disk space.

### papyrus.cs integration
This tool can automatically execute the **papyrus.cs** map-rendering tool on an interval. To do so you have to set `EnableRenders` to `true` and specify an interval in minutes with `RenderInterval`.
You can add your own arguments that will be attached when papyrus.cs is called. When configuring you will find two keys, `PapyrusGlobalArgs` and an array called `PapyrusTasks`. The value in `PapyrusGlobalArgs` specifies arguments that will be attached for each papyrus.cs task when executed, while `PapyrusTasks` represent an array of individual processes (or tasks). Adding an entry to the array represents another task that will be executed after the previous one has finished, this way it is possible to make papyrus.cs render multiple dimensions or have different configurations in general. Again, the same `PapyrusGlobalArgs` will be present for each of these tasks individually.

When specifying additional arguments for `PapyrusGlobalArgs` please make sure to **append** to the pre-generated entry (do not edit the `-w` and `-o` parameters!).
Please thoroughly verify that your paths and arguments are correct, you can find a configuration-example [here](https://github.com/clarkx86/papyrus-automation/blob/master/examples/basic_example.json).

### Getting the most latest renders continuously
If you want to get the most latest renders of your world continuously, set the `Interval` option in the `configuration.json` to a small number (e.g. `0.5` for 30 seconds) and `QuietMode` to `true`. This tool will automatically check if a previous render has already finished and won't spawn another papyrus.cs rendering process if it has not. Please note that you probably need to have a good server to do so and even then, you'll most likely need to specify additional arguments in the `PapyrusGlobalArgs` option (e.g. `--threads 1`, `-f jpg -q 50`,  etc.)! You can find an example configuration for contiunous renders [here](https://github.com/clarkx86/papyrus-automation/blob/master/examples/continuous_renders.json).

**Please note:** This tool will only run on Linux-based systems and currently won't work on Windows, [find out more here](https://bugs.mojang.com/browse/BDS-2733).

## Configuration overview
When you run this tool for the first time, it will generate a `configuration.json` and terminate. Before restarting the tool, edit this file to your needs. Here is a quick overview:
```
KEY               VALUE               ABOUT
----------------------------------------------------------
BdsPath           String  (!)         Path to the BDS root directory

WorldName         String  (!)         Name of the world located in the servers
                                      /worlds/ directory (specify merely the name and
                                      not the full path)

PapyrusBinPath    String              Path to the papyrus executable (inclusive)

PapyrusGlobalArgs String              Global arguments that are present for each
                                      rendering task specified in the "PapyrusArgs"-
                                      array
                                      IMPORTANT: Do not change the already provided
                                      --world and --ouput arguments

PapyrusTasks      String [Array]      An array of additional arguments for papyrus,
                                      where each array entry executes another
                                      papyrus.cs process after the previous one has
                                      finished (e.g. for rendering of multiple
                                      dimensions)

PapyrusOutputPath String              Output path for the rendered papyrus map

ArchivePath       String              Path where world-backup archives should be
                                      created

BackupsToKeep     Integer             Amount of backups to keep in the "ArchivePath"-
                                      directory, old backups automatically get deleted

BackupOnStartup   Boolean (!)         Wether to create a full backup of the specified
                                      world before starting the BDS process
                                      IMPORTANT: It is highly encouraged to leave
                                      this setting on "true"!

EnableRenders     Boolean (!)         Wether to create an interactive map of the world
                                      using papyrus.cs

EnableBackups     Boolean (!)         Wether to create world-backups as .zip-archives

RenderInterval    Double              Time in seconds to run a backup and render map

BackupInterval    Double              Time in seconds to take a backup and create a
                                      .zip-archive

PreExec           String              An arbitrary command that gets executed before
                                      each backup starts

PostExec          String              An arbitrary command that gets executed after
                                      each has finished

QuietMode         Boolean (!)         Suppress notifying players in-game that papyrus
                                      is creating a backup and render

HideStdout        Boolean (!)         Wether to hide the console output generated by
                                      the papyrus.cs rendering process, setting this
                                      to "true" may help debug your configuration but
                                      will result in a more verbose output 

* values marked with (!) are required, not-required values should be provided depending on your specific configuration
```
You can find an example configuration [here](https://github.com/clarkx86/papyrus-automation/blob/master/examples/basic_example.json).

## Disclaimer! Read before using!
Use this tool at **your own risk**! When using this software you agree to not hold us liable for any corrupted save data or deleted files. Make sure to configure everything correctly and thoroughly!

If you find any bugs, please report them on the issue tracker here on GitHub, our dedicated [Discord server](https://discord.gg/J2sBaXa) or send me an [e-mail](mailto:clarkx86@outlook.com?subject=GitHub%3A%20papyrus-automation). 