using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>
/// The pipe is created by hand: .NET's PipeSecurity cannot set an integrity label without audit privileges, and an
/// elevated creator would otherwise leave it high-integrity, out of reach of the normal-user assistant.
/// </summary>
internal static partial class PipeSecurityNative
{
    public const uint PIPE_ACCESS_DUPLEX = 0x00000003;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
    public const uint PIPE_TYPE_BYTE = 0x00000000;
    public const uint PIPE_REJECT_REMOTE_CLIENTS = 0x00000008;
    public const uint SDDL_REVISION_1 = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances,
        uint outBufferSize, uint inBufferSize, uint defaultTimeout, in SECURITY_ATTRIBUTES attributes);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint size);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);
}
