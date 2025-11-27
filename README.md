# GtaImg - C# Library for GTA IMG Archives

[![Build and Test](https://github.com/vaibhavpandeyvpz/GtaImg/actions/workflows/build.yml/badge.svg)](https://github.com/vaibhavpandeyvpz/GtaImg/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/GtaImg.svg)](https://www.nuget.org/packages/GtaImg/)

A C# tool as well as library for reading and manipulating IMG archive files from GTA games (GTA III, Vice City, San Andreas).

![Screenshot](assets/screenshot.png)

## Installation

```bash
dotnet add package GtaImg
```

Or via Package Manager:
```powershell
Install-Package GtaImg
```

## Supported Frameworks

The library targets multiple frameworks for maximum compatibility:

| Framework | Version |
|-----------|---------|
| .NET Framework | 4.5.2, 4.7.2, 4.8.1 |
| .NET Standard | 2.0 |
| .NET Core | 3.1 |
| .NET | 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 |

## Project Structure

```
├── GtaImg.sln                          # Solution file
├── src/
│   ├── GtaImg/                         # Main library
│   │   ├── GtaImg.csproj
│   │   ├── IMGArchive.cs               # Main archive class
│   │   ├── IMGEntry.cs                 # Entry structure
│   │   └── IMGException.cs             # Custom exception
│   └── GtaImgTool/                     # GUI application (WPF)
│       ├── GtaImgTool.csproj
│       └── ...
└── tests/
    └── GtaImg.Tests/                   # NUnit tests
        ├── GtaImg.Tests.csproj
        └── IMGArchiveTests.cs
```

## GtaImgTool - GUI Application

A Windows desktop application for viewing and editing IMG archives with a modern dark theme.

### Features

- **Open/Create Archives**: Support for both VER1 (GTA III/VC) and VER2 (GTA SA) formats
- **Browse Files**: View all entries with name, type, and size information
- **Multi-Select**: Select multiple files using Ctrl+Click or Shift+Click
- **Export Files**: Export selected files or all files to any folder
- **Import Files**: Add files individually, multiple at once, or from a folder
- **Delete Entries**: Remove selected entries from the archive
- **Pack Archive**: Defragment the archive to reclaim unused space
- **Drag & Drop**: Drag IMG files onto the window to open them
- **Search/Filter**: Quickly find files by name

### Running the GUI Tool

```bash
dotnet run --project src/GtaImgTool/GtaImgTool.csproj
```

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New Archive |
| Ctrl+O | Open Archive |
| Ctrl+S | Save Archive |
| Ctrl+W | Close Archive |
| Ctrl+A | Select All |
| Delete | Delete Selected |
| F5 | Refresh |

## Features

- **Read/Write Support**: Open IMG archives in read-only or read-write mode
- **VER1 Support**: GTA III and Vice City archives (.dir + .img file pairs)
- **VER2 Support**: GTA San Andreas archives (single .img file)
- **Entry Management**: Add, remove, rename, and extract entries
- **File Import/Export**: Easy methods for importing files and extracting entries

## Building

```bash
dotnet build
```

## Running Tests

```bash
dotnet test
```

Or with verbose output:
```bash
dotnet test --verbosity normal
```

## Usage Examples

### Opening an Archive (Read-Only)

```csharp
using GtaImg;

// Open a VER2 archive (GTA SA)
using var archive = new IMGArchive("gta3.img");

// List all entries
foreach (var entry in archive)
{
    Console.WriteLine($"{entry.Name} - {entry.SizeInBytes} bytes");
}
```

### Opening an Archive (Read-Write)

```csharp
using GtaImg;

// Open for reading and writing
using var archive = new IMGArchive("gta3.img", IMGArchive.IMGMode.ReadWrite);

// Make changes...
archive.Sync(); // Save changes (also called automatically on Dispose)
```

### Creating a New Archive

```csharp
using GtaImg;

// Create a new VER2 archive
using var archive = IMGArchive.CreateArchive("new_archive.img", IMGArchive.IMGVersion.VER2);

// Add files
archive.ImportFile("mymodel.dff");
archive.ImportFile("mytexture.txd");
```

### Reading Entry Data

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img");

// Read by name
byte[]? data = archive.ReadEntryData("player.dff");

// Or use a stream
using var stream = archive.OpenEntry("player.dff");
if (stream != null)
{
    // Process stream...
}
```

### Extracting Files

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img");

// Extract a single file
archive.ExtractEntry("player.dff", @"C:\extracted\player.dff");

// Extract all files
archive.ExtractAll(@"C:\extracted\all_files");
```

### Adding/Importing Files

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img", IMGArchive.IMGMode.ReadWrite);

// Import from file system
archive.ImportFile(@"C:\mods\custom.dff", "custom.dff");

// Or add from byte array
byte[] myData = File.ReadAllBytes("myfile.txd");
archive.AddEntry("myfile.txd", myData);
```

### Removing and Renaming Entries

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img", IMGArchive.IMGMode.ReadWrite);

// Remove an entry
archive.RemoveEntry("unwanted.dff");

// Rename an entry
archive.RenameEntry("old_name.txd", "new_name.txd");
```

### Packing an Archive

After removing entries, the archive may have "holes" (unused space). Use `Pack()` to defragment:

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img", IMGArchive.IMGMode.ReadWrite);

// Remove some entries
archive.RemoveEntry("file1.dff");
archive.RemoveEntry("file2.dff");

// Pack to eliminate holes
uint newSize = archive.Pack();
Console.WriteLine($"Archive size is now {newSize} blocks");
```

### Checking Entry Existence

```csharp
using GtaImg;

using var archive = new IMGArchive("gta3.img");

if (archive.ContainsEntry("player.dff"))
{
    Console.WriteLine("Entry exists!");
}

// Get entry info
var entry = archive.GetEntryByName("player.dff");
if (entry.HasValue)
{
    Console.WriteLine($"Offset: {entry.Value.Offset} blocks");
    Console.WriteLine($"Size: {entry.Value.Size} blocks ({entry.Value.SizeInBytes} bytes)");
}
```

### Detecting Archive Version

```csharp
using GtaImg;

// Before opening
var version = IMGArchive.GuessIMGVersion("unknown.img");
Console.WriteLine(version == IMGArchive.IMGVersion.VER2 ? "GTA SA format" : "GTA 3/VC format");

// Or from an open archive
using var archive = new IMGArchive("unknown.img");
Console.WriteLine($"Archive version: {archive.Version}");
```

## IMG Format Overview

- **Block Size**: 2048 bytes
- **VER1** (GTA III/VC): Two files - `.dir` (directory/header) and `.img` (data)
- **VER2** (GTA SA): Single `.img` file with "VER2" magic header
- **Entry Names**: Maximum 23 characters

## License

This library is released under the [MIT License](LICENSE).

## Credits

This library is a C# port of the original C++ **libgtaformats** library by David "Alemarius Nexus" Lerch.

- **Original C++ Library**: [gtatools/libgtaformats](https://github.com/alemariusnexus/gtatools/tree/master/src/libgtaformats)
