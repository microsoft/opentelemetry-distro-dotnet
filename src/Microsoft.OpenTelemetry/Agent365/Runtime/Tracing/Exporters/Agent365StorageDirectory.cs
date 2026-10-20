// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Exporters
{
    internal static class Agent365StorageDirectory
    {
        private const uint PrivateMode = 0x1C0; // 0700
        private const uint AccessModeMask = 0x1FF;
        private const uint FileTypeMask = 0xF000;
        private const uint DirectoryType = 0x4000;
        private const uint SymbolicLinkType = 0xA000;
        private const uint StickyBit = 0x200;
        private const int AlreadyExists = 17;

        internal static string Create(string rootDirectory)
        {
            var applicationDirectory = Path.Combine(rootDirectory, "Microsoft", "Agent365");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Directory.CreateDirectory(applicationDirectory);
                return applicationDirectory;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                throw new PlatformNotSupportedException("Secure Agent365 offline storage requires Windows, Linux, or macOS.");
            }

            var userId = GetEffectiveUserId();
            var root = EnsureDirectory(Path.GetFullPath(rootDirectory), userId, requirePrivate: false);
            var microsoftDirectory = EnsureDirectory(Path.Combine(root, "Microsoft"), userId, requirePrivate: true);
            return EnsureDirectory(Path.Combine(microsoftDirectory, "Agent365"), userId, requirePrivate: true);
        }

        private static string EnsureDirectory(string path, uint userId, bool requirePrivate)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                path = Path.Combine(EnsureDirectory(parent!, userId, requirePrivate: false), Path.GetFileName(path));
            }

            // mkdir applies 0700 at creation, including on netstandard2.0. Creating first and
            // chmod-ing later would expose the cache during the intervening permission window.
            if (MakeDirectory(path, PrivateMode) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != AlreadyExists)
                {
                    throw new IOException($"Could not create Agent365 storage directory '{path}'.", new Win32Exception(error));
                }
            }

            var (mode, owner) = ReadStatus(path);
            if ((mode & FileTypeMask) == SymbolicLinkType && !requirePrivate && owner == 0)
            {
                // macOS uses root-owned system links such as /var -> /private/var.
                // Use only the validated canonical path for subsequent directory creation and writes.
                var resolved = ResolvePath(path, IntPtr.Zero);
                if (resolved == IntPtr.Zero)
                {
                    throw new IOException($"Could not resolve storage ancestor '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));
                }

                try
                {
                    return EnsureDirectory(Marshal.PtrToStringAnsi(resolved)!, userId, requirePrivate: false);
                }
                finally
                {
                    Free(resolved);
                }

            }

            var isDirectory = (mode & FileTypeMask) == DirectoryType;
            var trustedOwner = owner == userId || (!requirePrivate && owner == 0);
            var safePermissions = requirePrivate
                ? (mode & AccessModeMask) == PrivateMode
                : (mode & 0x12) == 0 || (mode & StickyBit) != 0;

            if (!isDirectory || !trustedOwner || !safePermissions)
            {
                throw new UnauthorizedAccessException(
                    $"Agent365 storage directory '{path}' has unsafe ownership, permissions, or a symbolic link. "
                    + "Use an application-owned private directory; existing permissions are not changed automatically.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ValidateMacOsAccessControl(path);
            }

            return path;
        }

        private static void ValidateMacOsAccessControl(string path)
        {
            var acl = DarwinGetAcl(path, 0x100); // ACL_TYPE_EXTENDED
            if (acl == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                // Darwin reports ENOENT when an existing directory has no extended ACL.
                if (error == 2)
                {
                    return;
                }

                throw new IOException($"Could not inspect storage ACL '{path}'.", new Win32Exception(error));
            }

            try
            {
                // Darwin returns 0 for entries and -1/EINVAL at end, unlike Linux's ACL API.
                var entryId = 0; // ACL_FIRST_ENTRY
                while (DarwinGetAclEntry(acl, entryId, out var entry) == 0)
                {
                    if (DarwinGetAclTag(entry, out var tag) != 0)
                    {
                        throw new IOException($"Could not inspect storage ACL '{path}'.",
                            new Win32Exception(Marshal.GetLastWin32Error()));
                    }

                    if (tag != 2) // ACL_EXTENDED_DENY
                    {
                        throw new UnauthorizedAccessException(
                            $"Agent365 storage directory '{path}' has an extended allow or unsupported ACL. "
                            + "Use a directory without extended allow ACLs; existing ACLs are not changed automatically.");
                    }

                    entryId = -1; // ACL_NEXT_ENTRY
                }

                var error = Marshal.GetLastWin32Error();
                if (error != 22)
                {
                    throw new IOException($"Could not inspect storage ACL '{path}'.", new Win32Exception(error));
                }
            }
            finally
            {
                DarwinFreeAcl(acl);
            }
        }

        private static (uint Mode, uint Owner) ReadStatus(string path)
        {
            // statx has an architecture-independent ABI. Darwin's 64-bit stat layout is
            // shared by x64 and arm64, but x64 uses the INODE64 symbol.
            var status = new byte[256];
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const uint requestedFields = 0xB; // STATX_TYPE | STATX_MODE | STATX_UID
                if (Statx(-100, path, 0x100, requestedFields, status) != 0)
                {
                    throw new IOException($"Could not inspect storage directory '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));
                }

                if ((BitConverter.ToUInt32(status, 0) & requestedFields) != requestedFields)
                {
                    throw new IOException($"Ownership and permissions are unavailable for storage directory '{path}'.");
                }

                return (BitConverter.ToUInt16(status, 28), BitConverter.ToUInt32(status, 20));
            }

            var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? DarwinLStat64(path, status)
                : DarwinLStat(path, status);
            if (result != 0)
            {
                throw new IOException($"Could not inspect storage directory '{path}'.", new Win32Exception(Marshal.GetLastWin32Error()));
            }

            return (BitConverter.ToUInt16(status, 4), BitConverter.ToUInt32(status, 16));
        }

        [DllImport("libc", EntryPoint = "mkdir", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int MakeDirectory(string path, uint mode);

        [DllImport("libc", EntryPoint = "geteuid")]
        private static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "statx", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int Statx(int directory, string path, int flags, uint mask, [Out] byte[] status);

        [DllImport("libc", EntryPoint = "lstat$INODE64", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int DarwinLStat64(string path, [Out] byte[] status);

        [DllImport("libc", EntryPoint = "lstat", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int DarwinLStat(string path, [Out] byte[] status);

        [DllImport("libc", EntryPoint = "acl_get_link_np", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr DarwinGetAcl(string path, int type);

        [DllImport("libc", EntryPoint = "acl_get_entry", SetLastError = true)]
        private static extern int DarwinGetAclEntry(IntPtr acl, int entryId, out IntPtr entry);

        [DllImport("libc", EntryPoint = "acl_get_tag_type", SetLastError = true)]
        private static extern int DarwinGetAclTag(IntPtr entry, out int tag);

        [DllImport("libc", EntryPoint = "acl_free")]
        private static extern int DarwinFreeAcl(IntPtr acl);

        [DllImport("libc", EntryPoint = "realpath", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr ResolvePath(string path, IntPtr resolved);

        [DllImport("libc", EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);
    }
}
