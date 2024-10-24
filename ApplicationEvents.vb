
#Region " MyApplication "

Namespace My

    Partial Friend Class MyApplication

#Region " Variables "

        Private WithEvents Domain As AppDomain = AppDomain.CurrentDomain
        Private Delegate Sub SafeApplicationThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)

#End Region

#Region " Startup "

        Private Sub MyApplication_Startup(sender As Object, e As ApplicationServices.StartupEventArgs) Handles Me.Startup
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, New ResolveEventHandler(AddressOf DomainCheck)
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf AppDomain_UnhandledException
            AddHandler Windows.Forms.Application.ThreadException, AddressOf App_ThreadException
        End Sub

#End Region

#Region " Domain Check "

        Private Function DomainCheck(sender As Object, e As ResolveEventArgs) As Reflection.Assembly Handles Domain.AssemblyResolve
            Try ' If e.Name.Substring(0, e.Name.IndexOf(",")) = "ScreenRecorderLib" Then Return Reflection.Assembly.Load(Resources.ScreenRecorderLib)
                ' If e.Name.Substring(0, e.Name.IndexOf(",")) = "NAudio.Lame" Then Return Reflection.Assembly.Load(Resources.NAudio_Lame)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "NAudio" Then Return Reflection.Assembly.Load(Resources.NAudio)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "AxInterop.WMPLib" Then Return Reflection.Assembly.Load(Resources.AxInterop_WMPLib)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "Interop.WMPLib" Then Return Reflection.Assembly.Load(Resources.Interop_WMPLib)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "AudioDeviceCmdlets" Then Return Reflection.Assembly.Load(Resources.AudioDeviceCmdlets)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "System.Management.Automation" Then Return Reflection.Assembly.Load(Resources.System_Management_Automation)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "IconLib" Then Return Reflection.Assembly.Load(Resources.IconLib)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "Interop.NetFwTypeLib" Then Return Reflection.Assembly.Load(Resources.Interop_NetFwTypeLib)
                If e.Name.Substring(0, e.Name.IndexOf(",")) = "Microsoft.Win32.TaskScheduler" Then Return Reflection.Assembly.Load(Resources.Microsoft_Win32_TaskScheduler)
            Catch : End Try : Return Nothing
        End Function

#End Region

#Region " Debug Errors "

        Private Sub ShowDebug(ex As Exception)
            Try
                FDebug.TFecha.Text = DateTime.Now.ToString("dd/MM/yyyy - HH:mm:ss")
                FDebug.TVersion.Text = FMenu.Version
                FDebug.TWindows.Text = Computer.Info.OSFullName + " " + IIf(Environment.Is64BitOperatingSystem, "x64", "x86") + " (" + Computer.Info.OSVersion + ")"
                FDebug.TMemoria.Text = Rutas.FormatSize(Computer.Info.AvailablePhysicalMemory) + " disponible de " + Rutas.FormatSize(Computer.Info.TotalPhysicalMemory)
            Catch : End Try
            If ex.ToString.Trim = "" Then FDebug.TError.Text = "No se han encontrado errores" Else FDebug.TError.Text = ex.ToString
            FDebug.Show()
        End Sub

        Private Sub App_ThreadException(sender As Object, e As Threading.ThreadExceptionEventArgs)
            If MainForm.InvokeRequired Then MainForm.Invoke(New SafeApplicationThreadException(AddressOf App_ThreadException), New Object() {sender, e}) Else ShowDebug(e.Exception)
        End Sub

        Private Sub AppDomain_UnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
            ShowDebug(DirectCast(e.ExceptionObject, Exception))
        End Sub

        Private Sub MyApplication_UnhandledException(sender As Object, e As ApplicationServices.UnhandledExceptionEventArgs) Handles Me.UnhandledException
            ShowDebug(e.Exception)
        End Sub

#End Region

    End Class

End Namespace

#End Region