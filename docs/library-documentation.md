# Using vellum as a library
vellum exposes some of it's functionality as a library. The library is available as a NuGet package from [GitHub packages](https://github.com/clarkx86/vellum/packages).

## Table of contents
1. [**Overview**](#classes--structs)
   - [RunConfiguration](#runconfiguration)
   - [ProcessManager](#processmanager)
   - [BackupManager](#backupmanager)
   - [RenderManager](#rendermanager)
2. [**Examples**](#examples)

## Classes & structs
### **RunConfiguration**
`namespace`: `Vellum`

A struct that represents a run configuration that defines certain paths, archiving-thresholds and arguments for child-processes. Please refer to the [configuration overview](https://github.com/clarkx86/vellum#configuration-overview) and [`RunConfiguration.cs`](https://github.com/clarkx86/vellum/blob/master/RunConfiguration.cs) file for a overview of properties.

---
### **ProcessManager**
`namespace`: `Vellum.Automation`

Controls an underlying processes stdout/ stdin and provides methods to look out for specific patterns in the processes console output.
#### Constructors
```csharp
ProcessManager(ProcessStartInfo startInfo);
```
#### Properties
```csharp
bool HasMatched
// Is set to "true" when a regex pattern set with "SetMatchPattern" has matched
```
#### Methods
```csharp
static void RunCustomCommand(string cmd)
// Runs a custom command in the operating systems terminal

void SendInput(string cmd)
// Sends a command the the underlying processes stdin and executes it

void SetMatchPattern(Regex regex)
// Sets a regex pattern to look out for in the underlying processes stdout. If it matches in future output, the "HasMatched" property will be set to "true"
```
---
### **BackupManager**
`namespace`: `Vellum.Automation`

Create (hot-)backups of worlds and copy directories.
#### Constructors
```csharp
BackupManager(ProcessManager p, RunConfiguration runConfig);
```
#### Methods
```csharp
void CreateWorldBackup(string worldPath, string destinationPath, bool fullCopy, bool archive)
// Creates a backup of a world. If "fullCopy" is set to "true" it will copy the whole directory and not just the updated database files, therefor the server must not be running for a full copy. If archive is set to "true" it will compress the world as a .zip-archive, deleting redundant archives

static bool Archive(string sourcePath, string destinationPath, int archivesToKeep)
// Archives a world as a compressed .zip-archive, keeping "archivesToKeep"-amount of archives in the "destinationPath"-directory and deleting all older ones. However setting "archivesToKeep" to "-1" won't delete any archives at all. Archives are named like this: ""yyyy-MM-dd_HH-mm_WORLDNAME.zip"

static void CopyDirectory(string sourceDir, string targetDir)
// Copy a whole directory and it's files recursively
```
---
### **RenderManager**
`namespace`: `Vellum.Automation`

Calls the PapyrusCS renderer.
#### Constructors
```csharp
RenderManager(ProcessManager p, RunConfiguration runConfig);
```
#### Methods
```csharp
void StartRender(string worldPath)
// Calls papyrus.cs with the previously specified settings in the RunConfiguration on the world in the "worldPath"-directory
```