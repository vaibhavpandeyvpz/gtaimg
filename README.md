# GTA IMG Tool

[![Build and Test](https://github.com/vaibhavpandeyvpz/gtaimg/actions/workflows/build.yml/badge.svg)](https://github.com/vaibhavpandeyvpz/gtaimg/actions/workflows/build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/vaibhavpandeyvpz/gtaimg)](https://github.com/vaibhavpandeyvpz/gtaimg/releases/latest)

A modern Windows application for viewing and editing IMG archive files from classic GTA games (GTA III, Vice City, San Andreas).

![Screenshot](assets/screenshot.png)

## Download

Get the latest release from the [Releases](https://github.com/vaibhavpandeyvpz/gtaimg/releases/latest) page.

## Features

- **Open & Create Archives** - Support for both VER1 (GTA III/VC) and VER2 (GTA SA) formats
- **Browse Files** - View all entries with name, type, size, and offset information
- **Search & Filter** - Quickly find files by name
- **Export Files** - Export selected files or all files to any folder
- **Import Files** - Add files individually, multiple at once, or from an entire folder
- **Replace & Rename** - In-place replacement and renaming of entries
- **Delete Entries** - Remove selected entries from the archive
- **Pack Archive** - Defragment the archive to reclaim unused space
- **Drag & Drop** - Drag IMG files onto the window to open them
- **Modern UI** - Clean dark theme with custom title bar

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New Archive |
| Ctrl+O | Open Archive |
| Ctrl+S | Save Archive |
| Ctrl+W | Close Archive |
| Ctrl+A | Select All |
| Delete | Delete Selected |
| F5 | Refresh |

## Supported Formats

| Format | Games | File Structure |
|--------|-------|----------------|
| VER1 | GTA III, Vice City | `.dir` + `.img` file pair |
| VER2 | San Andreas | Single `.img` file |

---

## For Developers

The underlying library **GtaImg** is also available as a NuGet package for developers who want to work with IMG archives programmatically.

[![NuGet](https://img.shields.io/nuget/v/GtaImg.svg)](https://www.nuget.org/packages/GtaImg/)

### Installation

```bash
dotnet add package GtaImg
```

Or via Package Manager:
```powershell
Install-Package GtaImg
```

### Supported Frameworks

| Framework | Version |
|-----------|---------|
| .NET Framework | 4.5.2, 4.7.2, 4.8.1 |
| .NET Standard | 2.0 |
| .NET Core | 3.1 |
| .NET | 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 |

### Quick Start

```csharp
using GtaImg;

// Open an archive
using var archive = new IMGArchive("gta3.img");

// List all entries
foreach (var entry in archive)
{
    Console.WriteLine($"{entry.Name} - {entry.SizeInBytes} bytes");
}

// Extract a file
archive.ExtractEntry("player.dff", @"C:\extracted\player.dff");
```

### Read-Write Operations

```csharp
using GtaImg;

// Open for editing
using var archive = new IMGArchive("gta3.img", IMGArchive.IMGMode.ReadWrite);

// Import a file
archive.ImportFile(@"C:\mods\custom.dff", "custom.dff");

// Remove an entry
archive.RemoveEntry("unwanted.dff");

// Rename an entry
archive.RenameEntry("old_name.txd", "new_name.txd");

// Save changes
archive.Sync();
```

### Create New Archive

```csharp
using GtaImg;

// Create a new VER2 archive
using var archive = IMGArchive.CreateArchive("new_archive.img", IMGArchive.IMGVersion.VER2);

// Add files
archive.ImportFile("mymodel.dff");
archive.ImportFile("mytexture.txd");
```

### More Examples

<details>
<summary>Reading Entry Data</summary>

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
</details>

<details>
<summary>Packing an Archive</summary>

After removing entries, the archive may have unused space. Use `Pack()` to defragment:

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
</details>

<details>
<summary>Detecting Archive Version</summary>

```csharp
using GtaImg;

// Before opening
var version = IMGArchive.GuessIMGVersion("unknown.img");
Console.WriteLine(version == IMGArchive.IMGVersion.VER2 ? "GTA SA format" : "GTA 3/VC format");

// Or from an open archive
using var archive = new IMGArchive("unknown.img");
Console.WriteLine($"Archive version: {archive.Version}");
```
</details>

---

## Building from Source

```bash
# Clone the repository
git clone https://github.com/vaibhavpandeyvpz/gtaimg.git
cd GtaImg

# Build everything
dotnet build

# Run the GUI tool
dotnet run --project src/GtaImgTool/GtaImgTool.csproj

# Run tests
dotnet test
```

### Project Structure

```
├── src/
│   ├── GtaImg/           # Core library (NuGet package)
│   └── GtaImgTool/       # GUI application (WPF)
└── tests/
    └── GtaImg.Tests/     # Unit tests
```

## License

This project is released under the [MIT License](LICENSE).

## Credits

This project is based on a C# port of the original C++ **libgtaformats** library by David "Alemarius Nexus" Lerch.

- **Original C++ Library**: [gtatools/libgtaformats](https://github.com/alemariusnexus/gtatools/tree/master/src/libgtaformats)
