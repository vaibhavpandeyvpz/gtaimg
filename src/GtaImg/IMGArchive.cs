/*
    GtaImg - A library for reading and manipulating GTA IMG archive files.
    Copyright (c) 2025 Vaibhav Pandey <contact@vaibhavpandey.com>

    Licensed under the MIT License. See LICENSE file in the project root for full license information.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GtaImg
{
/// <summary>
/// Represents a single GTA IMG archive.
/// </summary>
/// <remarks>
/// <para>
/// IMG archives are general-purpose archives used by the GTA series. They have three versions: GTA3/VC,
/// GTA SA and GTA IV, where the first two are supported by this class. The supported archive formats
/// are identical with the exception that VER1 (GTA3/VC) files are stored in two files (.dir and .img),
/// while VER2 files (GTA SA) are stored in a single .img. Also, the IMG header is missing in VER1.
/// </para>
/// <para>
/// An IMG archive consists of the IMG header (VER2 only), the entry header section (also called entry table;
/// for VER1 archives, this is the whole content of the DIR file. For VER2, it is the first part of the IMG
/// file), and the content section.
/// </para>
/// <para>
/// All positions and sizes are measured in IMG blocks, which are 2048 bytes in size.
/// </para>
/// </remarks>
public class IMGArchive : IDisposable, IEnumerable<IMGEntry>
{
    #region Constants

    /// <summary>
    /// The size of a block in IMG files in bytes.
    /// </summary>
    public const int BlockSize = 2048;

    /// <summary>
    /// VER2 header magic bytes ("VER2").
    /// </summary>
    private static readonly byte[] Ver2Magic = Encoding.ASCII.GetBytes("VER2");

    /// <summary>
    /// Size of an IMGEntry structure in bytes.
    /// </summary>
    private const int EntrySize = 32; // 4 + 4 + 24

    /// <summary>
    /// Size of the VER2 header (magic + entry count).
    /// </summary>
    private const int Ver2HeaderSize = 8;

    /// <summary>
    /// Maximum blocks to keep in memory during operations (~51.2MB).
    /// </summary>
    private const int MaxRamBlocks = 25000;

    #endregion

    #region Enums

    /// <summary>
    /// The different versions of IMG files.
    /// </summary>
    public enum IMGVersion
    {
        /// <summary>
        /// GTA3, GTA VC (IMG header placed in a separate DIR file).
        /// </summary>
        VER1,

        /// <summary>
        /// GTA SA (single IMG file with header).
        /// </summary>
        VER2
    }

    /// <summary>
    /// The modes in which you can open an IMGArchive.
    /// </summary>
    [Flags]
    public enum IMGMode
    {
        /// <summary>
        /// Only open the file for reading. All writing operations will throw an exception.
        /// </summary>
        ReadOnly = 1,

        /// <summary>
        /// Open the file for both reading and writing.
        /// </summary>
        ReadWrite = 2
    }

    #endregion

    #region Fields

        private Stream _imgStream;
        private Stream _dirStream;
        private readonly LinkedList<IMGEntry> _entries = new LinkedList<IMGEntry>();
        private readonly Dictionary<string, LinkedListNode<IMGEntry>> _entryMap = new Dictionary<string, LinkedListNode<IMGEntry>>(StringComparer.OrdinalIgnoreCase);
    private IMGVersion _version;
    private uint _headerReservedSpace;
    private IMGMode _mode;
    private bool _disposed;
    private bool _ownsStreams;

    #endregion

    #region Static Methods

    /// <summary>
    /// Converts a number of IMG blocks to a number of bytes.
    /// </summary>
        public static long BlocksToBytes(uint blocks)
        {
            return blocks * (long)BlockSize;
        }

    /// <summary>
    /// Converts a number of bytes to a number of IMG blocks (rounded up).
    /// </summary>
        public static uint BytesToBlocks(long bytes)
        {
            return (uint)Math.Ceiling(bytes / (double)BlockSize);
        }

    /// <summary>
    /// Try to guess version of an IMG/DIR stream.
    /// </summary>
    /// <param name="stream">The stream from which to read.</param>
    /// <param name="returnToStart">When true, the stream will be put back to the position where it was before.</param>
    /// <returns>The guessed version.</returns>
    public static IMGVersion GuessIMGVersion(Stream stream, bool returnToStart = true)
    {
        long originalPosition = stream.Position;
        byte[] fourCC = new byte[4];
        int bytesRead = stream.Read(fourCC, 0, 4);

        if (returnToStart)
        {
            stream.Position = originalPosition;
        }

        if (bytesRead < 4)
        {
            return IMGVersion.VER1;
        }

        if (fourCC[0] == 'V' && fourCC[1] == 'E' && fourCC[2] == 'R' && fourCC[3] == '2')
        {
            return IMGVersion.VER2;
        }

        return IMGVersion.VER1;
    }

    /// <summary>
    /// Try to guess version of an IMG/DIR file.
    /// </summary>
    public static IMGVersion GuessIMGVersion(string filePath)
    {
            using (Stream stream = File.OpenRead(filePath))
            {
        return GuessIMGVersion(stream, false);
            }
    }

    /// <summary>
    /// Creates a new, empty VER2 IMG archive.
    /// </summary>
    /// <param name="imgStream">The stream to which the IMG file should be written.</param>
    /// <param name="mode">The open mode.</param>
    /// <returns>The freshly created archive object.</returns>
    public static IMGArchive CreateArchive(Stream imgStream, IMGMode mode = IMGMode.ReadWrite)
    {
        // Write VER2 header
        imgStream.Write(Ver2Magic, 0, 4);

        // Write entry count (0)
            byte[] countBytes = BitConverter.GetBytes(0u);
            imgStream.Write(countBytes, 0, 4);
        imgStream.Flush();
        imgStream.Position = 0;

            return new IMGArchive(imgStream, mode, true);
    }

    /// <summary>
    /// Creates a new, empty VER1 IMG archive.
    /// </summary>
    /// <param name="dirStream">The stream for the DIR file.</param>
    /// <param name="imgStream">The stream for the IMG file.</param>
    /// <param name="mode">The open mode.</param>
    /// <returns>The freshly created archive object.</returns>
    public static IMGArchive CreateArchive(Stream dirStream, Stream imgStream, IMGMode mode = IMGMode.ReadWrite)
    {
        // VER1 archives don't have a header, so nothing to write
            return new IMGArchive(dirStream, imgStream, mode, true);
    }

    /// <summary>
    /// Creates a new, empty IMG archive file.
    /// </summary>
    /// <param name="filePath">The file path (.img or .dir).</param>
    /// <param name="version">The version to create.</param>
    /// <param name="mode">The open mode.</param>
    /// <returns>The freshly created archive object.</returns>
    public static IMGArchive CreateArchive(string filePath, IMGVersion version = IMGVersion.VER2, IMGMode mode = IMGMode.ReadWrite)
    {
        if (version == IMGVersion.VER2)
        {
                Stream stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            return CreateArchive(stream, mode);
        }
        else
        {
                string directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    directory = ".";
                string baseName = Path.GetFileNameWithoutExtension(filePath);

                string dirPath = Path.Combine(directory, baseName + ".dir");
                string imgPath = Path.Combine(directory, baseName + ".img");

                Stream dirStream = new FileStream(dirPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                Stream imgStream = new FileStream(imgPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

            return CreateArchive(dirStream, imgStream, mode);
        }
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Opens a VER2 archive from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="mode">The open mode.</param>
    /// <param name="ownsStream">Whether the archive owns the stream and should dispose it.</param>
    public IMGArchive(Stream stream, IMGMode mode = IMGMode.ReadOnly, bool ownsStream = true)
    {
        _imgStream = stream;
        _dirStream = stream;
        _mode = mode;
        _ownsStreams = ownsStream;
        ReadHeader();
    }

    /// <summary>
    /// Creates a VER1 archive from separate DIR and IMG streams.
    /// </summary>
    /// <param name="dirStream">The stream from which to read the DIR data.</param>
    /// <param name="imgStream">The stream from which to read the IMG data.</param>
    /// <param name="mode">The open mode.</param>
    /// <param name="ownsStreams">Whether the archive owns the streams and should dispose them.</param>
    public IMGArchive(Stream dirStream, Stream imgStream, IMGMode mode = IMGMode.ReadOnly, bool ownsStreams = true)
    {
        _dirStream = dirStream;
        _imgStream = imgStream;
        _mode = mode;
        _ownsStreams = ownsStreams;
        ReadHeader();
    }

    /// <summary>
    /// Opens an IMG archive from a file path.
    /// </summary>
    /// <param name="filePath">The file path (.img or .dir).</param>
    /// <param name="mode">The open mode.</param>
    public IMGArchive(string filePath, IMGMode mode = IMGMode.ReadOnly)
    {
        _mode = mode;
        _ownsStreams = true;

            string ext = Path.GetExtension(filePath);
            if (ext != null)
                ext = ext.ToLowerInvariant();
            
        FileAccess access = mode == IMGMode.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite;

        if (ext == ".dir")
        {
            // VER1: DIR file specified
            string imgPath = Path.ChangeExtension(filePath, ".img");
            _dirStream = new FileStream(filePath, FileMode.Open, access, FileShare.Read);
            _imgStream = new FileStream(imgPath, FileMode.Open, access, FileShare.Read);
            ReadHeader();
        }
        else if (ext == ".img")
        {
            // Could be VER1 or VER2
            _imgStream = new FileStream(filePath, FileMode.Open, access, FileShare.Read);

            if (GuessIMGVersion(_imgStream, true) == IMGVersion.VER2)
            {
                _dirStream = _imgStream;
                ReadHeader();
            }
            else
            {
                // VER1: Need separate DIR file
                string dirPath = Path.ChangeExtension(filePath, ".dir");
                _dirStream = new FileStream(dirPath, FileMode.Open, access, FileShare.Read);
                ReadHeader();
            }
        }
        else
        {
            throw new IMGException("File name is neither an IMG nor a DIR file");
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the format version of this archive.
    /// </summary>
        public IMGVersion Version
        {
            get { return _version; }
        }

    /// <summary>
    /// Gets the number of entries in the archive.
    /// </summary>
        public int EntryCount
        {
            get { return _entries.Count; }
        }

    /// <summary>
    /// Gets all entries in the archive.
    /// </summary>
        public IEnumerable<IMGEntry> Entries
        {
            get { return _entries; }
        }

    /// <summary>
    /// Gets the IMG stream.
    /// </summary>
        public Stream IMGStream
        {
            get { return _imgStream; }
        }

    /// <summary>
    /// Gets the DIR stream (same as IMG stream for VER2).
    /// </summary>
        public Stream DIRStream
        {
            get { return _dirStream; }
        }

    /// <summary>
    /// Gets the number of blocks reserved for the entry header section.
    /// </summary>
    public uint HeaderReservedSize
    {
        get
        {
            if (_version == IMGVersion.VER1)
                return 0;

            if (_entries.Count > 0)
                    return _entries.First.Value.Offset;

            return 0;
        }
    }

    /// <summary>
    /// Gets the total size of this archive in blocks.
    /// </summary>
    public uint Size
    {
        get
        {
            uint dataEnd = GetDataEndOffset();
            if (_version == IMGVersion.VER2)
                return dataEnd;
            return dataEnd + BytesToBlocks(_entries.Count * EntrySize);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets an entry by name (case-insensitive search).
    /// </summary>
    /// <param name="name">The entry name to find.</param>
    /// <returns>The entry if found, null otherwise.</returns>
    public IMGEntry? GetEntryByName(string name)
    {
            LinkedListNode<IMGEntry> node;
            if (_entryMap.TryGetValue(name, out node))
        {
            return node.Value;
        }
        return null;
    }

    /// <summary>
    /// Checks if an entry with the given name exists.
    /// </summary>
        public bool ContainsEntry(string name)
        {
            return _entryMap.ContainsKey(name);
        }

    /// <summary>
    /// Opens a stream to read the content of an entry.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <returns>A stream containing the entry's data, or null if not found.</returns>
        public Stream OpenEntry(string name)
    {
            IMGEntry? entry = GetEntryByName(name);
            if (!entry.HasValue)
            return null;

        return OpenEntry(entry.Value);
    }

    /// <summary>
    /// Opens a stream to read the content of an entry.
    /// </summary>
    /// <param name="entry">The entry to open.</param>
    /// <returns>A stream containing the entry's data.</returns>
    public Stream OpenEntry(IMGEntry entry)
    {
        ThrowIfDisposed();

        long start = BlocksToBytes(entry.Offset);
        long length = BlocksToBytes(entry.Size);

            _imgStream.Position = start;

        // Return a bounded stream that prevents reading beyond the entry
        return new BoundedStream(_imgStream, length);
    }

    /// <summary>
    /// Reads the entire content of an entry into a byte array.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <returns>The entry's content as a byte array, or null if not found.</returns>
        public byte[] ReadEntryData(string name)
    {
            IMGEntry? entry = GetEntryByName(name);
            if (!entry.HasValue)
            return null;

        return ReadEntryData(entry.Value);
    }

    /// <summary>
    /// Reads the entire content of an entry into a byte array.
    /// </summary>
    /// <param name="entry">The entry to read.</param>
    /// <returns>The entry's content as a byte array.</returns>
    public byte[] ReadEntryData(IMGEntry entry)
    {
        ThrowIfDisposed();

        long start = BlocksToBytes(entry.Offset);
        long length = BlocksToBytes(entry.Size);

            _imgStream.Position = start;

        byte[] data = new byte[length];
        int totalRead = 0;
        while (totalRead < length)
        {
            int read = _imgStream.Read(data, totalRead, (int)(length - totalRead));
            if (read == 0)
                break;
            totalRead += read;
        }

        return data;
    }

    /// <summary>
    /// Adds a new entry to the archive.
    /// </summary>
    /// <param name="name">The name of the new entry (max 23 characters).</param>
    /// <param name="data">The data to store in the entry.</param>
    /// <returns>The newly created entry.</returns>
    public IMGEntry AddEntry(string name, byte[] data)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

        if (name.Length > IMGEntry.MaxNameLength)
        {
                throw new IMGException(string.Format("Maximum length of {0} characters for IMG entry names exceeded!", IMGEntry.MaxNameLength));
        }

        uint sizeInBlocks = BytesToBlocks(data.Length);

        if (!ReserveHeaderSpace((uint)_entries.Count + 1))
        {
            throw new IMGException("Failed to reserve header space for new entry");
        }

        uint offset = GetDataEndOffset();

            IMGEntry entry = new IMGEntry();
            entry.Offset = offset;
            entry.Size = sizeInBlocks;
            entry.NameBytes = new byte[IMGEntry.NameFieldSize];
            entry.Name = name;

            LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
        _entryMap[name.ToLowerInvariant()] = node;

        // Write the data
        long position = BlocksToBytes(offset);
            _imgStream.Position = position;
        _imgStream.Write(data, 0, data.Length);

        // Pad to block boundary
        int padding = (int)(BlocksToBytes(sizeInBlocks) - data.Length);
        if (padding > 0)
        {
            _imgStream.Write(new byte[padding], 0, padding);
        }

        return entry;
    }

    /// <summary>
    /// Adds a new entry to the archive with a specified size in blocks.
    /// </summary>
    /// <param name="name">The name of the new entry.</param>
    /// <param name="sizeInBlocks">The size to allocate in blocks.</param>
    /// <returns>The newly created entry.</returns>
    public IMGEntry AddEntry(string name, uint sizeInBlocks)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

        if (name.Length > IMGEntry.MaxNameLength)
        {
                throw new IMGException(string.Format("Maximum length of {0} characters for IMG entry names exceeded!", IMGEntry.MaxNameLength));
        }

        if (!ReserveHeaderSpace((uint)_entries.Count + 1))
        {
            throw new IMGException("Failed to reserve header space for new entry");
        }

        uint offset = GetDataEndOffset();

            IMGEntry entry = new IMGEntry();
            entry.Offset = offset;
            entry.Size = sizeInBlocks;
            entry.NameBytes = new byte[IMGEntry.NameFieldSize];
            entry.Name = name;

            LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
        _entryMap[name.ToLowerInvariant()] = node;

        // Expand the file to accommodate the new entry
        ExpandSize(offset + sizeInBlocks);

        return entry;
    }

    /// <summary>
    /// Writes data to an existing entry.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <param name="data">The data to write.</param>
    public void WriteEntryData(string name, byte[] data)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

            LinkedListNode<IMGEntry> node;
            if (!_entryMap.TryGetValue(name, out node))
        {
                throw new IMGException(string.Format("No entry found with name {0}", name));
        }

            IMGEntry entry = node.Value;
        long maxSize = BlocksToBytes(entry.Size);

        if (data.Length > maxSize)
        {
                throw new IMGException(string.Format("Data size ({0}) exceeds entry size ({1})", data.Length, maxSize));
        }

            _imgStream.Position = BlocksToBytes(entry.Offset);
        _imgStream.Write(data, 0, data.Length);

        // Pad remaining space with zeros
        int remaining = (int)(maxSize - data.Length);
        if (remaining > 0)
        {
            _imgStream.Write(new byte[remaining], 0, remaining);
        }
    }

    /// <summary>
    /// Removes an entry from the archive.
    /// </summary>
    /// <param name="name">The entry name to remove.</param>
    /// <returns>True if the entry was removed, false if not found.</returns>
    public bool RemoveEntry(string name)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

            LinkedListNode<IMGEntry> node;
            if (!_entryMap.TryGetValue(name, out node))
        {
            return false;
        }

        _entryMap.Remove(name.ToLowerInvariant());
        _entries.Remove(node);

        return true;
    }

    /// <summary>
    /// Renames an entry.
    /// </summary>
    /// <param name="oldName">The current entry name.</param>
    /// <param name="newName">The new entry name.</param>
    public void RenameEntry(string oldName, string newName)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

        if (newName.Length > IMGEntry.MaxNameLength)
        {
                throw new IMGException(string.Format("Maximum length of {0} characters for IMG entry names exceeded!", IMGEntry.MaxNameLength));
        }

            LinkedListNode<IMGEntry> node;
            if (!_entryMap.TryGetValue(oldName, out node))
        {
                throw new IMGException(string.Format("No entry found with name {0}", oldName));
        }

        _entryMap.Remove(oldName.ToLowerInvariant());

            IMGEntry entry = node.Value;
        entry.Name = newName;
        node.Value = entry;

        _entryMap[newName.ToLowerInvariant()] = node;
    }

    /// <summary>
    /// Writes out changes to the header section.
    /// </summary>
    public void Sync()
    {
        if ((_mode & IMGMode.ReadWrite) != 0)
        {
            RewriteHeaderSection();
        }
    }

    /// <summary>
    /// Packs the entries of this archive as tightly as possible.
    /// </summary>
    /// <remarks>
    /// This eliminates 'holes' in the archive caused by entry removal or resizing.
    /// </remarks>
    /// <returns>The new size of the archive in blocks.</returns>
    public uint Pack()
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

        if (_entries.Count == 0)
            return _headerReservedSpace;

        // Create a temporary copy of all entry data
            Dictionary<string, byte[]> entryData = new Dictionary<string, byte[]>();
            foreach (IMGEntry entry in _entries)
        {
            entryData[entry.Name] = ReadEntryData(entry);
        }

        // Calculate minimum header size
        uint headerSize = BytesToBlocks(_entries.Count * EntrySize + (_version == IMGVersion.VER2 ? Ver2HeaderSize : 0));

        // Rebuild entries with new offsets
        uint currentOffset = headerSize;
            List<IMGEntry> newEntries = new List<IMGEntry>();

            foreach (IMGEntry entry in _entries)
        {
                IMGEntry newEntry = entry;
            newEntry.Offset = currentOffset;
            currentOffset += newEntry.Size;
            newEntries.Add(newEntry);
        }

        // Clear and rebuild the entry list
        _entries.Clear();
        _entryMap.Clear();

            foreach (IMGEntry entry in newEntries)
        {
                LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
            _entryMap[entry.Name.ToLowerInvariant()] = node;
        }

        // Rewrite all entry data
            foreach (IMGEntry entry in _entries)
        {
                _imgStream.Position = BlocksToBytes(entry.Offset);
                byte[] data = entryData[entry.Name];
            _imgStream.Write(data, 0, data.Length);

            // Pad to block boundary
            int padding = (int)(BlocksToBytes(entry.Size) - data.Length);
            if (padding > 0)
            {
                _imgStream.Write(new byte[padding], 0, padding);
            }
        }

        _headerReservedSpace = headerSize;

        return GetDataEndOffset();
    }

    /// <summary>
    /// Extracts an entry to a file.
    /// </summary>
    /// <param name="entryName">The entry name to extract.</param>
    /// <param name="outputPath">The output file path.</param>
    public void ExtractEntry(string entryName, string outputPath)
    {
            byte[] data = ReadEntryData(entryName);
        if (data == null)
        {
                throw new IMGException(string.Format("Entry not found: {0}", entryName));
        }

        File.WriteAllBytes(outputPath, data);
    }

    /// <summary>
    /// Extracts all entries to a directory.
    /// </summary>
    /// <param name="outputDirectory">The output directory path.</param>
    public void ExtractAll(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

            foreach (IMGEntry entry in _entries)
        {
            string outputPath = Path.Combine(outputDirectory, entry.Name);
                byte[] data = ReadEntryData(entry);
            File.WriteAllBytes(outputPath, data);
        }
    }

    /// <summary>
    /// Imports a file as a new entry or replaces an existing one.
    /// </summary>
    /// <param name="filePath">The file to import.</param>
    /// <param name="entryName">Optional entry name (defaults to file name).</param>
        public IMGEntry ImportFile(string filePath, string entryName = null)
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

            if (entryName == null)
                entryName = Path.GetFileName(filePath);
            
        byte[] data = File.ReadAllBytes(filePath);

        // Remove existing entry if present
        if (_entryMap.ContainsKey(entryName))
        {
            RemoveEntry(entryName);
        }

        return AddEntry(entryName, data);
    }

    #endregion

    #region IEnumerable Implementation

        public IEnumerator<IMGEntry> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
            Dispose(true, syncBeforeClose: true);
        GC.SuppressFinalize(this);
    }

        /// <summary>
        /// Closes the archive without saving any pending changes.
        /// </summary>
        public void CloseWithoutSync()
        {
            Dispose(true, syncBeforeClose: false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing, bool syncBeforeClose = true)
    {
        if (_disposed)
            return;

        if (disposing)
            {
                if (syncBeforeClose)
        {
            try
            {
                Sync();
            }
            catch
            {
                // Ignore sync errors during disposal
                    }
            }

            if (_ownsStreams)
            {
                    if (_imgStream != _dirStream && _dirStream != null)
                {
                        _dirStream.Dispose();
                }
                    if (_imgStream != null)
                    {
                        _imgStream.Dispose();
                    }
            }

            _imgStream = null;
            _dirStream = null;
        }

        _disposed = true;
    }

    ~IMGArchive()
    {
        Dispose(false);
    }

    #endregion

    #region Private Methods

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
                throw new ObjectDisposedException("IMGArchive");
        }
    }

    private void ThrowIfReadOnly()
    {
        if ((_mode & IMGMode.ReadWrite) == 0)
        {
            throw new IMGException("Attempt to modify a read-only IMG archive!");
        }
    }

    private void ReadHeader()
    {
        if (_dirStream == null)
        {
            throw new IMGException("DIR stream is null");
        }

            BinaryReader reader = new BinaryReader(_dirStream, Encoding.ASCII);

        // Check for VER2 magic
        byte[] firstBytes = new byte[4];
        int bytesRead = _dirStream.Read(firstBytes, 0, 4);

        if (bytesRead == 0)
        {
            // Empty file - assume VER1
            _version = IMGVersion.VER1;
            _headerReservedSpace = 1;
            return;
        }

        if (bytesRead < 4)
        {
            throw new IMGException("Premature end of file");
        }

        _entries.Clear();
        _entryMap.Clear();

        bool sorted = true;
        uint lastOffset = 0;

        if (firstBytes[0] == 'V' && firstBytes[1] == 'E' && firstBytes[2] == 'R' && firstBytes[3] == '2')
        {
            // VER2 archive
            _version = IMGVersion.VER2;
            uint numEntries = reader.ReadUInt32();

            for (int i = 0; i < numEntries; i++)
            {
                    IMGEntry entry = ReadEntry(reader);

                if (entry.Offset < lastOffset)
                {
                    sorted = false;
                }
                lastOffset = entry.Offset;

                    LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
                _entryMap[entry.Name.ToLowerInvariant()] = node;
            }
        }
        else
        {
            // VER1 archive
            _version = IMGVersion.VER1;

            // First bytes are part of the first entry
            _dirStream.Position = 0;

            while (_dirStream.Position < _dirStream.Length)
            {
                long startPos = _dirStream.Position;
                    IMGEntry entry = ReadEntry(reader);

                if (_dirStream.Position - startPos != EntrySize)
                {
                    if (_dirStream.Position >= _dirStream.Length)
                        break;

                        throw new IMGException(string.Format("Input isn't divided into {0}-byte blocks. Is this really a VER1 DIR file?", EntrySize));
                }

                if (entry.Offset < lastOffset)
                {
                    sorted = false;
                }
                lastOffset = entry.Offset;

                    LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
                _entryMap[entry.Name.ToLowerInvariant()] = node;
            }
        }

        // Sort entries by offset if not already sorted
        if (!sorted && _entries.Count > 1)
        {
                List<IMGEntry> sortedEntries = _entries.OrderBy(e => e.Offset).ToList();
            _entries.Clear();
            _entryMap.Clear();

                foreach (IMGEntry entry in sortedEntries)
            {
                    LinkedListNode<IMGEntry> node = _entries.AddLast(entry);
                _entryMap[entry.Name.ToLowerInvariant()] = node;
            }
        }

        // Set header reserved space
        if (_entries.Count > 0)
        {
                _headerReservedSpace = _entries.First.Value.Offset;
        }
        else
        {
            _headerReservedSpace = 1;
        }
    }

    private static IMGEntry ReadEntry(BinaryReader reader)
    {
            IMGEntry entry = new IMGEntry();
            entry.Offset = reader.ReadUInt32();
            entry.Size = reader.ReadUInt32();
            entry.NameBytes = reader.ReadBytes(IMGEntry.NameFieldSize);
        return entry;
    }

    private void RewriteHeaderSection()
    {
        ThrowIfReadOnly();
        ThrowIfDisposed();

            Stream outStream = _version == IMGVersion.VER1 ? _dirStream : _imgStream;

        outStream.Position = 0;

            BinaryWriter writer = new BinaryWriter(outStream, Encoding.ASCII);

        if (_version == IMGVersion.VER2)
        {
            writer.Write(Ver2Magic);
            writer.Write((uint)_entries.Count);
        }

            foreach (IMGEntry entry in _entries)
        {
            writer.Write(entry.Offset);
            writer.Write(entry.Size);
                byte[] nameBytes = entry.NameBytes;
                if (nameBytes == null)
                    nameBytes = new byte[IMGEntry.NameFieldSize];
                writer.Write(nameBytes);
        }

        outStream.Flush();

            // Truncate the header section to remove old entry data for VER1
            // (VER1 reads until EOF, so we must truncate to avoid reading stale entries)
            if (_version == IMGVersion.VER1)
            {
                long newLength = _entries.Count * EntrySize;
                _dirStream.SetLength(newLength);
            }
    }

    private uint GetDataEndOffset()
    {
        if (_entries.Count == 0)
            return _headerReservedSpace;

            IMGEntry lastEntry = _entries.Last.Value;
        return lastEntry.Offset + lastEntry.Size;
    }

    private bool ReserveHeaderSpace(uint numHeaders)
    {
        long bytesToReserve = numHeaders * EntrySize;

        if (_version == IMGVersion.VER2)
        {
            bytesToReserve += Ver2HeaderSize;
        }

        long reservedSize = BlocksToBytes(HeaderReservedSize);
        _headerReservedSpace = BytesToBlocks(bytesToReserve);

        if (reservedSize == 0 || bytesToReserve <= reservedSize)
        {
            return true;
        }

        // Need to move entries to make room for header expansion
        // For simplicity, we'll move all affected entries to the end

            List<IMGEntry> entriesToMove = new List<IMGEntry>();
            foreach (IMGEntry e in _entries)
            {
                if (BlocksToBytes(e.Offset) >= bytesToReserve)
                    break;
                entriesToMove.Add(e);
            }

        if (entriesToMove.Count == 0)
            return true;

        // Read data for entries to move
            Dictionary<string, byte[]> entryData = new Dictionary<string, byte[]>();
            foreach (IMGEntry entry in entriesToMove)
        {
            entryData[entry.Name] = ReadEntryData(entry);
        }

        // Remove entries from the list temporarily
            foreach (IMGEntry entry in entriesToMove)
        {
                LinkedListNode<IMGEntry> node = _entryMap[entry.Name.ToLowerInvariant()];
            _entries.Remove(node);
            _entryMap.Remove(entry.Name.ToLowerInvariant());
        }

        // Add them back at the end with new offsets
            foreach (IMGEntry entry in entriesToMove)
        {
                IMGEntry newEntry = entry;
            newEntry.Offset = GetDataEndOffset();

                LinkedListNode<IMGEntry> node = _entries.AddLast(newEntry);
            _entryMap[entry.Name.ToLowerInvariant()] = node;

            // Write data to new location
                _imgStream.Position = BlocksToBytes(newEntry.Offset);
                byte[] data = entryData[entry.Name];
            _imgStream.Write(data, 0, data.Length);

            int padding = (int)(BlocksToBytes(newEntry.Size) - data.Length);
            if (padding > 0)
            {
                _imgStream.Write(new byte[padding], 0, padding);
            }
        }

        return true;
    }

    private void ExpandSize(uint sizeInBlocks)
    {
        long targetSize = BlocksToBytes(sizeInBlocks);
            _imgStream.Position = _imgStream.Length;

        long bytesToAdd = targetSize - _imgStream.Length;
        if (bytesToAdd <= 0)
            return;

        byte[] buffer = new byte[Math.Min(bytesToAdd, 8192)];
        while (bytesToAdd > 0)
        {
            int toWrite = (int)Math.Min(bytesToAdd, buffer.Length);
            _imgStream.Write(buffer, 0, toWrite);
            bytesToAdd -= toWrite;
        }
    }

    #endregion
}

/// <summary>
/// A stream wrapper that limits reading to a specific length.
/// </summary>
internal class BoundedStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _startPosition;
    private readonly long _length;
    private long _position;

    public BoundedStream(Stream baseStream, long length)
    {
        _baseStream = baseStream;
        _startPosition = baseStream.Position;
        _length = length;
        _position = 0;
    }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return _baseStream.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _length; } }

    public override long Position
    {
            get { return _position; }
        set
        {
            if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException("value");
            _position = value;
            _baseStream.Position = _startPosition + value;
        }
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _length - _position;
        if (remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(count, remaining);
        _baseStream.Position = _startPosition + _position;
        int read = _baseStream.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
            long newPosition;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPosition = offset;
                    break;
                case SeekOrigin.Current:
                    newPosition = _position + offset;
                    break;
                case SeekOrigin.End:
                    newPosition = _length + offset;
                    break;
                default:
                    throw new ArgumentException("Invalid seek origin", "origin");
            }

        if (newPosition < 0 || newPosition > _length)
                throw new ArgumentOutOfRangeException("offset");

        _position = newPosition;
        return _position;
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
        }
    }
}
