# Using papyrus.automation as a library
papyrus.automation exposes some of it's functionality as a library.

## Table of contents
1. [**Overview**]()
   - [RunConfiguration]()
   - [ProcessManager]()
   - [BackupManager]()
   - [RenderManager]()
2. [**Examples**]()

# Overview
## Classes & structs
### **RunConfiguration**
`namespace`: `papyrus`

A struct that represents a run configuration that defines certain paths, archiving-thresholds and arguments for child-processes. Please refer to the [configuration overview](https://github.com/clarkx86/papyrus-automation#configuration-overview) and [`RunConfiguration.cs`](https://github.com/clarkx86/papyrus-automation/blob/master/RunConfiguration.cs) file for a overview of properties.

---
### **ProcessManager**
`namespace`: `papyrus.Automation`

Controls an underlying processes stdout/ stdin and provides methods to look out for specific patterns in the processes console output.
#### Constructors
```csharp
ProcessManager(Process p);
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

void SendInput(string text)
// Sends a command the the underlying processes stdin and executes it

void SetMatchPattern(Regex regex)
// Sets a regex pattern to look out for in the underlying processes stdout. If it matches in future output, the "HasMatched" property will be set to "true"
```
---
### **BackupManager**
`namespace`: `papyrus.Automation`

Create (hot-)backups of worlds and copy directories.
#### Constructors
```csharp
BackupManager(ProcessManager p, RunConfiguration runConfig);
```
#### Methods
```csharp
void CreateWorldBackup(string worldPath, string destinationPath, bool fullCopy, bool archive)
// Creates a backup of a world. If "fullCopy" is set to "true" it will copy the whole directory and not just the updated database files, therefor the server must not be running for a full copy. If archive is set to "true" it will compress the world as a .zip-archive, deleting red

static bool Archive(string sourcePath, string destinationPath, int archivesToKeep)
// Archives a world as a compressed .zip-archive, keeping "archivesToKeep"-amount of archives in the "destinationPath"-directory and deleting all older ones. However setting "archivesToKeep" to "-1" won't delete any archives at all. Archives are named like this: ""yyyy-MM-dd_HH-mm_WORLDNAME.zip"

static void CopyDirectory(string sourceDir, string targetDir)
// Copy a whole directory and it's files recursively
```
---
### **RenderManager**
`namespace`: `papyrus.Automation`

Calls the papyrus.cs renderer 
#### Constructors
```csharp
RenderManager(ProcessManager p, RunConfiguration runConfig);
```
#### Methods
```csharp
void StartRender(string worldPath)
// Calls papyrus.cs with the previously specified settings in the RunConfiguration on the world in the "worldPath"-directory
```