
#Region " KPS "

Module Module1

#Region " Variables "

#Region " Declaraciones "

    <Runtime.InteropServices.DllImport("user32.dll")> Private Function GetWindowText(hwnd As Integer, lpString As System.Text.StringBuilder, cch As Integer) As Integer : End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Function GetForegroundWindow() As IntPtr : End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Private Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As IntPtr) As IntPtr : End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, X As Integer, Y As Integer, cx As Integer, cy As Integer, uFlags As SetWindowPosFlags) As Boolean : End Function : Public Enum SetWindowPosFlags As UInteger : IgnoreMove = &H2 : IgnoreResize = &H1 : End Enum

    Private Structure BrowserST : Dim Nombre As String : Dim Ruta As String : Dim Version As String : End Structure
    Private Declare Function SetForegroundWindow Lib "user32.dll" (hwnd As Integer) As Integer
    Private Declare Auto Function FindWindow Lib "user32.dll" (lpClassName As String, lpWindowName As String) As Integer
    Private Declare Function IsIconic Lib "user32.dll" (hwnd As Integer) As Boolean
    Private Declare Function ShowWindow Lib "user32.dll" (hwnd As Integer, nCmdShow As Integer) As Integer

#End Region

    Dim Comando As String
    Dim KobaPathTemp As String = IO.Path.GetTempPath + "KDesktop\"

#End Region

#Region " Main "

    Sub Main()
        If Environment.GetCommandLineArgs.Length = 0 Then Exit Sub
        Comando = String.Join(" ", Environment.GetCommandLineArgs).Replace("|||", """")
        Select Case Split(Comando, "|-|", , CompareMethod.Text)(0).ToLower
            Case "ejecutar aplicacion" : EjecutarAplicacion(Comando)
            Case "ejecutar bat" : EjecutarBAT(Comando)
            Case "probar bat" : ProbarBAT(Comando)
            Case "pagina web" : PaginaWeb(Comando)
            Case "eliminar" : Eliminar(Comando)
        End Select
    End Sub

#End Region

#Region " Funciones "

    Private Sub Eliminar(Settings As String)
        If Split(Settings, "|-|", , CompareMethod.Text).Length < 2 Then Exit Sub
        Dim Ruta As String = Split(Settings, "|-|", , CompareMethod.Text)(1)
        If Ruta = "" Then Exit Sub
        Do Until IO.File.Exists(Split(Settings, "|-|", , CompareMethod.Text)(1)) = False : Try : IO.File.Delete(Ruta) : Catch : End Try : Loop
    End Sub

    Private Function TelegramPath() As String
        Dim Telegram As String = ""
        Dim StartDrive As String = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 1)
        For Each Dir As String In IO.Directory.GetDirectories(StartDrive + ":\users")
            If IO.File.Exists(Dir + "\AppData\Roaming\Telegram Win (Unofficial)\Telegram.exe") = True Then Telegram = Dir + "\AppData\Roaming\Telegram Win (Unofficial)\Telegram.exe"
            If IO.File.Exists(Dir + "\AppData\Roaming\Telegram Desktop\Telegram.exe") = True Then Telegram = Dir + "\AppData\Roaming\Telegram Desktop\Telegram.exe"
            If Telegram <> "" Then Return Telegram
        Next : Return Telegram
    End Function

    Private Function GetRegKey(SubKey As String, Name As String, Optional DefaultValue As String = "") As String
        Try : My.Computer.Registry.CurrentUser.CreateSubKey("Software\" + SubKey) : Using Reg = My.Computer.Registry.CurrentUser.OpenSubKey("Software\" + SubKey, True)
                If Reg.GetValue(Name) = "" Then Reg.SetValue(Name, DefaultValue)
                Return Reg.GetValue(Name) : End Using
        Catch : Return "" : End Try
    End Function

#End Region

#Region " Ejecutar Aplicacion "

    Private Sub EjecutarAplicacion(Settings As String) ' (1) Ruta, (2) Parametros, (3) IniciarEn, (4) Modo, (5) Prioridad, (6) TopMost
        If Split(Settings, "|-|", , CompareMethod.Text).Length < 7 Or Split(Settings, "|-|", , CompareMethod.Text)(1).Trim = "" Then Exit Sub
        Dim P As New Process() : Dim WindowMode As String = "Normal"
        Try : Dim Ruta As String = Split(Settings, "|-|", , CompareMethod.Text)(1) : If Ruta.ToLower = "telegram" Then Ruta = TelegramPath()
            Dim Parametros As String = Split(Settings, "|-|", , CompareMethod.Text)(2)
            Dim IniciarEn As String = Split(Settings, "|-|", , CompareMethod.Text)(3)
            WindowMode = Split(Settings, "|-|", , CompareMethod.Text)(4)
            Dim Prioridad As String = Split(Settings, "|-|", , CompareMethod.Text)(5)
            Dim isDirectory As Boolean
            If IO.Directory.Exists(Ruta) = True Then
                isDirectory = True
            Else
                If IO.Directory.Exists(IniciarEn) = True Then
                    P.StartInfo.WorkingDirectory = IniciarEn
                Else
                    If IO.Directory.Exists(IO.Path.GetDirectoryName(Ruta)) = True Then P.StartInfo.WorkingDirectory = IO.Path.GetDirectoryName(Ruta)
                End If
            End If
            Select Case WindowMode
                Case "Minimizado" : P.StartInfo.WindowStyle = ProcessWindowStyle.Minimized
                Case "Maximizado" : P.StartInfo.WindowStyle = ProcessWindowStyle.Maximized
                Case "Oculto" : P.StartInfo.CreateNoWindow = True : P.StartInfo.WindowStyle = ProcessWindowStyle.Hidden
                Case Else : P.StartInfo.WindowStyle = ProcessWindowStyle.Normal
            End Select
            If Ruta.ToLower = "cmd" Then
                Dim ShowWindow As Boolean : If WindowMode <> "Oculto" Then ShowWindow = True Else ShowWindow = False
                P.StartInfo.CreateNoWindow = Not ShowWindow : P.StartInfo.Arguments = " " + If(ShowWindow, "/K", "/C") + " " + Parametros : P.StartInfo.FileName = Ruta : P.Start()
            Else
                P.StartInfo.Arguments = Parametros : P.StartInfo.FileName = Ruta : P.Start()
                Try : Select Case Prioridad
                        Case "Low" : P.PriorityClass = ProcessPriorityClass.Idle
                        Case "Below Normal" : P.PriorityClass = ProcessPriorityClass.BelowNormal
                        Case "Normal" : P.PriorityClass = ProcessPriorityClass.Normal
                        Case "Above Normal" : P.PriorityClass = ProcessPriorityClass.AboveNormal
                        Case "High" : P.PriorityClass = ProcessPriorityClass.High
                        Case "Real Time" : P.PriorityClass = ProcessPriorityClass.RealTime
                    End Select
                Catch : End Try
            End If
        Catch : End Try
        Try : If WindowMode = "Oculto" Then Exit Sub
            Dim TopMost As Boolean = CBool(Split(Settings, "|-|", , CompareMethod.Text)(6))
            If TopMost = True Then
                For Cuenta = 0 To 30
                    Threading.Thread.Sleep(25) : P.Refresh() : If P.MainWindowHandle <> IntPtr.Zero Then Exit For
                Next : Dim Titulo As String = P.MainWindowTitle : If P.MainWindowTitle Is Nothing Or Titulo = "" Then Titulo = GetTitulo()
                If Titulo = "" Then Titulo = P.ProcessName : If Titulo = "" Then Titulo = GetProcess() : If Titulo = "" Then Exit Sub
                If P.MainWindowHandle <> 0 Then SetWindowPos(P.MainWindowHandle, New IntPtr(-&H1), 0, 0, 0, 0, &H2 Or &H1)
            End If
            If Split(Settings, "|-|", , CompareMethod.Text)(1).ToLower = "telegram" Then
                For Cuenta = 0 To 30
                    Threading.Thread.Sleep(25) : P.Refresh() : If P.MainWindowHandle <> IntPtr.Zero Then Exit For
                Next : Dim Titulo As String = P.MainWindowTitle : If P.MainWindowTitle Is Nothing Or Titulo = "" Then Titulo = GetTitulo()
                If Titulo = "" Then Titulo = P.ProcessName : If Titulo = "" Then Titulo = GetProcess() : If Titulo = "" Then Exit Sub
                If P.MainWindowHandle <> 0 Then
                    SetWindowPos(P.MainWindowHandle, New IntPtr(-&H1), 0, 0, 0, 0, &H2 Or &H1)
                    Threading.Thread.Sleep(100)
                    SetWindowPos(P.MainWindowHandle, New IntPtr(-&H2), 0, 0, 0, 0, &H2 Or &H1)
                End If
                Threading.Thread.Sleep(500) : FocusWindow("Telegram")
            End If
        Catch : End Try
    End Sub

    Sub FocusWindow(strWindowCaption As String)
        Dim hWnd As Integer = FindWindow(Nothing, strWindowCaption)
        If hWnd > 0 Then SetForegroundWindow(hWnd) : If IsIconic(hWnd) Then ShowWindow(hWnd, 9) Else ShowWindow(hWnd, 5)
    End Sub

#Region " Procesos "

    Public Function GetProcess(Optional Handle As Boolean = False) As Object
        Try : Dim ProcIdXL As Integer = 0 : GetWindowThreadProcessId(GetForegroundWindow, ProcIdXL)
            If Handle = True Then Return Process.GetProcessById(ProcIdXL).MainWindowHandle Else Return Process.GetProcessById(ProcIdXL).ProcessName
        Catch : End Try : If Handle = False Then Return "" Else Return 0
    End Function

    Public Function GetProcess(hwnd As IntPtr, Optional Handle As Boolean = False) As Object
        Try : If hwnd = Nothing Then If Handle = False Then Return "" Else Return 0
            Dim ProcIdXL As Integer = 0 : GetWindowThreadProcessId(hwnd, ProcIdXL)
            If Handle = True Then Return Process.GetProcessById(ProcIdXL).MainWindowHandle Else Return Process.GetProcessById(ProcIdXL).ProcessName
        Catch : End Try : If Handle = False Then Return "" Else Return 0
    End Function

    Public Function GetTitulo() As String
        Try : Dim Titulo As New Text.StringBuilder(256) : GetWindowText(GetForegroundWindow, Titulo, Titulo.Capacity) : Return Titulo.ToString
        Catch : End Try : Return ""
    End Function

    Public Function GetTitulo(hwnd As IntPtr) As String
        Try
            If hwnd = Nothing Then Return ""
            Dim ProcIdXL As Integer = 0 : GetWindowThreadProcessId(hwnd, ProcIdXL) : Return Process.GetProcessById(ProcIdXL).MainWindowTitle
        Catch : End Try : Return ""
    End Function

#End Region

#End Region

#Region " Ejecutar BAT "

    Private Sub EjecutarBAT(Settings As String) ' (1) Nombre, (2) Ruta, (3) Modo
        If Split(Settings, "|-|", , CompareMethod.Text).Length < 4 Or Split(Settings, "|-|", , CompareMethod.Text)(1).Trim = "" Then Exit Sub
        Dim Ruta As String = Split(Settings, "|-|", , CompareMethod.Text)(2)
        Dim WindowMode As String = Split(Settings, "|-|", , CompareMethod.Text)(3)
        If IO.File.Exists(Ruta) = False Then Exit Sub
        Dim Texto_BAT As String : Dim Leer As New IO.StreamReader(Ruta) : Texto_BAT = Leer.ReadToEnd : Leer.Close() : If Texto_BAT.Trim = "" Then Exit Sub
        Dim P As New Process()
        Select Case WindowMode
            Case "Minimizado" : P.StartInfo.WindowStyle = ProcessWindowStyle.Minimized
            Case "Maximizado" : P.StartInfo.WindowStyle = ProcessWindowStyle.Maximized
            Case "Oculto" : P.StartInfo.CreateNoWindow = True : P.StartInfo.WindowStyle = ProcessWindowStyle.Hidden
            Case Else : P.StartInfo.WindowStyle = ProcessWindowStyle.Normal
        End Select
        P.StartInfo.FileName = Ruta : P.Start()
    End Sub

#Region " Probar BAT "

    Private Sub BAT_Añadir(Datos As String)

        If IO.File.Exists(KobaPathTemp + "ProbarBAT.txt") = False Then
            Dim Guardar As New IO.StreamWriter(KobaPathTemp + "ProbarBAT.txt")
            Guardar.Close()
        End If
        Dim Lines As List(Of String) = IO.File.ReadAllLines(KobaPathTemp + "ProbarBAT.txt").ToList
        Lines.Remove(Datos) : Lines.Add(Datos)
        IO.File.WriteAllLines(KobaPathTemp + "ProbarBAT.txt", Lines)
    End Sub

    Private Sub BAT_Eliminar(ID As String)
        If IO.File.Exists(KobaPathTemp + "ProbarBAT.txt") = False Then Exit Sub
        Dim Lines As List(Of String) = IO.File.ReadAllLines(KobaPathTemp + "ProbarBAT.txt").ToList
        For Cuenta = 0 To Lines.Count - 1
            If Lines(Cuenta).Contains(ID) Then Lines.RemoveAt(Cuenta)
        Next
        IO.File.WriteAllLines(KobaPathTemp + "ProbarBAT.txt", Lines)
    End Sub

    Private Function BAT_GetPID(ID As String) As Integer
        If IO.File.Exists(KobaPathTemp + "ProbarBAT.txt") = False Then Return 0
        Dim Lines As List(Of String) = IO.File.ReadAllLines(KobaPathTemp + "ProbarBAT.txt").ToList
        For Cuenta = 0 To Lines.Count - 1
            If Lines(Cuenta).Contains(ID) Then Return Lines(Cuenta).Split("|")(1)
        Next : Return 0
    End Function

    Private Sub ProbarBAT(Settings As String) ' (1) Boton, (2) Nombre, (3) Ruta, (4) Modo, (5) ID
        Try : If Split(Settings, "|-|", , CompareMethod.Text).Length < 6 Or Split(Settings, "|-|", , CompareMethod.Text)(3).Trim = "" Then Exit Sub
            Dim Boton As String = Split(Settings, "|-|", , CompareMethod.Text)(1)
            Dim Ruta As String = Split(Settings, "|-|", , CompareMethod.Text)(3)
            Dim WindowMode As String = Split(Settings, "|-|", , CompareMethod.Text)(4)

            IO.Directory.CreateDirectory(KobaPathTemp)

            Dim PID As Integer = BAT_GetPID(Split(Settings, "|-|", , CompareMethod.Text)(5))
            BAT_Eliminar(Split(Settings, "|-|", , CompareMethod.Text)(5))
            If PID <> 0 Then
                Dim Proceso As Process = Process.GetProcessById(PID)
                If Proceso IsNot Nothing Then
                    Try : If Proceso.HasExited = False Then Proceso.CloseMainWindow()
                    Catch : End Try
                End If
            End If
            If Boton <> "Left" Or IO.File.Exists(Ruta) = False Then Exit Sub
            Dim Texto_BAT As String : Dim Leer As New IO.StreamReader(Ruta) : Texto_BAT = Leer.ReadToEnd : Leer.Close() : If Texto_BAT.Trim = "" Then Exit Sub
            Dim EjecutarBAT_Proceso As New Process : EjecutarBAT_Proceso.StartInfo.FileName = Ruta
            Select Case WindowMode
                Case "Minimizado" : EjecutarBAT_Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Minimized
                Case "Maximizado" : EjecutarBAT_Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Maximized
                Case "Oculto" : EjecutarBAT_Proceso.StartInfo.UseShellExecute = False : EjecutarBAT_Proceso.StartInfo.CreateNoWindow = True : EjecutarBAT_Proceso.StartInfo.RedirectStandardInput = True : EjecutarBAT_Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Hidden
                Case Else : EjecutarBAT_Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Normal
            End Select
            EjecutarBAT_Proceso.Start()
            BAT_Añadir(Split(Settings, "|-|", , CompareMethod.Text)(5) + "|" + EjecutarBAT_Proceso.Id.ToString)
        Catch : End Try
    End Sub

#End Region

#End Region

#Region " Pagina Web "

    Private Sub PaginaWeb(Settings As String)  ' (1) URL, (2) Navegador, (3) Nueva_Ventana, (4) Incognito
        If Split(Settings, "|-|", , CompareMethod.Text).Length < 5 Or Split(Settings, "|-|", , CompareMethod.Text)(1).Trim = "" Then Exit Sub
        Dim URL As String = Split(Settings, "|-|", , CompareMethod.Text)(1)
        Dim Navegador As String = Split(Settings, "|-|", , CompareMethod.Text)(2)
        Dim NuevaVentana As Boolean = CBool(Split(Settings, "|-|", , CompareMethod.Text)(3))
        Dim VentanaIncognito As Boolean = CBool(Split(Settings, "|-|", , CompareMethod.Text)(4))
        Dim URLComando As String = ""
        Select Case Navegador.ToLower
            Case "google chrome"
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start("Chrome", URLComando + "-url " + URL)
            Case "mozilla firefox"
                If VentanaIncognito = True Then URLComando += "-private-window " Else If NuevaVentana = True Then URLComando += "-new-window "
                Dim Proceso As Process = Process.Start("Firefox", URLComando + URL)
            Case "microsoft edge"
                Dim Proceso As Process = Process.Start("Microsoft-edge:" + URL)
            Case "internet explorer"
                Dim Proceso As Process = Process.Start("iexplore", URL)
            Case "opera"
                Dim Ruta As String = Get_Navegador_Ruta("opera")
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start(Ruta, URLComando + "-url " + URL)
            Case "vivaldi"
                Dim Ruta As String = Get_Navegador_Ruta("vivaldi")
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start(Ruta, URLComando + "-url " + URL)
            Case "uc browser"
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start("ucbrowser", URLComando + "-url " + URL)
            Case "brave"
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start("brave", URLComando + "-url " + URL)
            Case Else
                Dim Ruta As String = Get_Navegador_Ruta(Navegador)
                If NuevaVentana = True Then URLComando += "--new-window "
                If VentanaIncognito = True Then URLComando += "-incognito "
                Dim Proceso As Process = Process.Start(Ruta, URLComando + "-url " + URL)
        End Select
    End Sub

    Private Function Get_Navegador_Ruta(Nombre As String) As String
        Try : Dim Browsers As List(Of BrowserST) = GetInstalledBrowsers()
            For Cuenta = 0 To Browsers.Count - 1
                If Browsers(Cuenta).Nombre.ToLower = Nombre.ToLower Then Return Browsers(Cuenta).Ruta
            Next
        Catch : End Try : Return ""
    End Function

#Region " Get Installed Browsers "

    Private Function GetInstalledBrowsers() As List(Of BrowserST)
        Dim Browsers As New List(Of BrowserST)
        Dim BrowserKeys = My.Computer.Registry.LocalMachine.OpenSubKey("SOFTWARE\WOW6432Node\Clients\StartMenuInternet")
        If BrowserKeys IsNot Nothing Then
            Dim BrowserNames As String() = BrowserKeys.GetSubKeyNames()
            For Cuenta As Integer = 0 To BrowserNames.Length - 1
                Dim Browser As New BrowserST : Dim BrowserKey = BrowserKeys.OpenSubKey(BrowserNames(Cuenta))
                Browser.Nombre = CStr(BrowserKey.GetValue(Nothing)).Replace(" Stable", "") : Dim BrowserKeyPath = BrowserKey.OpenSubKey("shell\open\command") : Browser.Ruta = BrowserKeyPath.GetValue(Nothing).ToString().Replace("""", "")
                If IO.File.Exists(Browser.Ruta) = True Then Browser.Version = FileVersionInfo.GetVersionInfo(Browser.Ruta).FileVersion Else Browser.Version = "Desconocido"
                If Browser.Nombre <> "" Then Browsers.Add(Browser)
            Next
        End If
        BrowserKeys = My.Computer.Registry.CurrentUser.OpenSubKey("SOFTWARE\Clients\StartMenuInternet")
        If BrowserKeys IsNot Nothing Then
            Dim BrowserNames As String() = BrowserKeys.GetSubKeyNames()
            For Cuenta As Integer = 0 To BrowserNames.Length - 1
                Dim Browser As New BrowserST : Dim BrowserKey = BrowserKeys.OpenSubKey(BrowserNames(Cuenta))
                Browser.Nombre = CStr(BrowserKey.GetValue(Nothing)).Replace(" Stable", "") : Dim BrowserKeyPath = BrowserKey.OpenSubKey("shell\open\command") : Browser.Ruta = BrowserKeyPath.GetValue(Nothing).ToString().Replace("""", "")
                If IO.File.Exists(Browser.Ruta) = True Then Browser.Version = FileVersionInfo.GetVersionInfo(Browser.Ruta).FileVersion Else Browser.Version = "Desconocido"
                If Browser.Nombre <> "" Then Browsers.Add(Browser)
            Next
        End If
        Dim EdgeVersion As String = GetRegKey("Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\Microsoft.MicrosoftEdge_8wekyb3d8bbwe\Schemas", "PackageFullName").Replace("""", "")
        If EdgeVersion <> "" Then
            Dim Result = System.Text.RegularExpressions.Regex.Match(EdgeVersion, "(((([0-9.])\d)+){1})")
            If Result.Success Then
                Dim EdgeBrowser As BrowserST
                EdgeBrowser.Nombre = "Microsoft Edge"
                EdgeBrowser.Version = Result.Value
                EdgeBrowser.Ruta = ""
                Browsers.Add(EdgeBrowser)
            End If
        End If
        Return Browsers
    End Function

#End Region

#End Region

End Module

#End Region