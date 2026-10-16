using System.Runtime.InteropServices;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>
/// The Wi-Fi and Bluetooth switches of Windows' quick settings (Windows.Devices.Radios). The WinRT calls are made by
/// hand through their v-tables, so K-Desktop doesn't need the Windows SDK projection (some 25 MB more in the exe).
/// </summary>
public static unsafe class Radios
{
    private enum RadioState { Unknown, On, Off, Disabled }

    private enum RadioKind { Other, WiFi, MobileBroadband, Bluetooth, FM }

    private enum AsyncStatus { Started, Completed, Canceled, Error }

    private static readonly Guid IidRadioStatics = new("5fb6a12e-67cb-46ae-aae9-65919f86eff4");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-c000-000000000046");

    /// <summary>
    /// Turns the radio on, off or toggles it. Returns the new state (true = on); null when this PC has no such radio or
    /// Windows refused.
    /// </summary>
    public static bool? Switch(RadioDevice device, SwitchMode mode) => OnRadioThread(device, mode);

    /// <summary>Whether the radio is on now (null: there is none, or it is disabled). Changes nothing.</summary>
    public static bool? IsOn(RadioDevice device) => OnRadioThread(device, null);

    private static bool? OnRadioThread(RadioDevice device, SwitchMode? mode)
    {
        bool? result = null;
        Exception? error = null;
        // On a thread of its own, initialised for WinRT (the thread pool's aren't, for these calls).
        var thread = new Thread(() =>
        {
            var init = RoInitialize(1 /* RO_INIT_MULTITHREADED */);
            try { result = SwitchCore(device == RadioDevice.WiFi ? RadioKind.WiFi : RadioKind.Bluetooth, mode); }
            catch (Exception ex) { error = ex; }
            finally
            {
                if (init >= 0) RoUninitialize();
            }
        }) { IsBackground = true, Name = "Radios" };
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Windows no respondió al cambiar la radio");
        if (error is not null) throw error;
        return result;
    }

    private static bool? SwitchCore(RadioKind kind, SwitchMode? mode)
    {
        var statics = Factory("Windows.Devices.Radios.Radio", IidRadioStatics);
        try
        {
            // IRadioStatics.RequestAccessAsync (9): desktop apps are allowed, but it must be asked once.
            IntPtr access;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(statics, 9))(statics, &access));
            Await(access);
            Release(access);

            // IRadioStatics.GetRadiosAsync (6) → IVectorView<Radio>
            IntPtr operation;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(statics, 6))(statics, &operation));
            var radios = AwaitObject(operation);
            try
            {
                uint count;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)VTable(radios, 7))(radios, &count)); // get_Size
                for (uint i = 0; i < count; i++)
                {
                    IntPtr radio;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(radios, 6))(radios, i, &radio)); // GetAt
                    try
                    {
                        int radioKind, state;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(radio, 11))(radio, &radioKind)); // get_Kind
                        if ((RadioKind)radioKind != kind) continue;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(radio, 9))(radio, &state)); // get_State
                        if ((RadioState)state == RadioState.Disabled) return null;
                        if (mode is null) return (RadioState)state == RadioState.On;
                        var on = mode switch
                        {
                            SwitchMode.On => true,
                            SwitchMode.Off => false,
                            _ => (RadioState)state != RadioState.On,
                        };
                        // SetStateAsync (6) → IAsyncOperation<RadioAccessStatus>
                        IntPtr set;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)VTable(radio, 6))(radio, (int)(on ? RadioState.On : RadioState.Off), &set));
                        var status = AwaitInt(set);
                        if (status != 1 /* Allowed */)
                        {
                            Log.Warn($"Windows no deja cambiar la radio {kind} (acceso {status})");
                            return null;
                        }
                        return on;
                    }
                    finally { Release(radio); }
                }
                return null;
            }
            finally { Release(radios); }
        }
        finally { Release(statics); }
    }

    // ── WinRT by hand ────────────────────────────────────────────────────

    private static IntPtr Factory(string className, Guid iid)
    {
        Check(WindowsCreateString(className, className.Length, out var hstring));
        try
        {
            Check(RoGetActivationFactory(hstring, ref iid, out var factory));
            return factory;
        }
        finally { WindowsDeleteString(hstring); }
    }

    private static IntPtr VTable(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero) Marshal.Release(instance);
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>Waits for an IAsyncOperation/IAsyncAction (polling IAsyncInfo.Status).</summary>
    private static void Await(IntPtr operation)
    {
        var iid = IidAsyncInfo;
        Check(Marshal.QueryInterface(operation, ref iid, out var info));
        try
        {
            var start = Environment.TickCount64;
            while (true)
            {
                int status;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(info, 7))(info, &status)); // get_Status
                switch ((AsyncStatus)status)
                {
                    case AsyncStatus.Completed: return;
                    case AsyncStatus.Canceled: throw new OperationCanceledException();
                    case AsyncStatus.Error:
                        int code;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(info, 8))(info, &code)); // get_ErrorCode
                        Marshal.ThrowExceptionForHR(code);
                        return;
                }
                if (Environment.TickCount64 - start > 10_000) throw new TimeoutException();
                Thread.Sleep(15);
            }
        }
        finally { Release(info); }
    }

    /// <summary>IAsyncOperation&lt;object&gt;: waits and returns GetResults (8), releasing the operation.</summary>
    private static IntPtr AwaitObject(IntPtr operation)
    {
        try
        {
            Await(operation);
            IntPtr result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)VTable(operation, 8))(operation, &result));
            return result;
        }
        finally { Release(operation); }
    }

    /// <summary>IAsyncOperation&lt;enum&gt;: waits and returns GetResults (8), releasing the operation.</summary>
    private static int AwaitInt(IntPtr operation)
    {
        try
        {
            Await(operation);
            int result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)VTable(operation, 8))(operation, &result));
            return result;
        }
        finally { Release(operation); }
    }

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern void RoUninitialize();

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);
}
