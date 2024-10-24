
#Region " Video Recorder "

Public Class FVideoRecorder

#Region " Variables "

#Region " Estructuras "

#End Region

    Dim PlayerState As String = ""
    Dim PlayerPosition As Integer = -1

    Dim VideoPath As String = FMenu.KobaPathTemp + "VideoRecording.mp4"

    Dim Started, MouseDowned, isFocused As Boolean
    Dim VideoWidth, VideoHeight, NewVideoWidth, NewVideoHeight As Integer
    Dim VideoTimer As New Timer With {.Interval = 500, .Enabled = False}

    Public cFVideoRecorderEditor As FVideoRecorderEditor
    Public Monitor As Integer

#End Region

#Region " Eventos "

#Region " Formulario "

#Region " Load "

    Private Sub FVideoRecorder_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Opacity = 0 : TopMost = True : Started = False
        AddHandler VideoTimer.Tick, AddressOf VideoTimer_Tick

        LProgreso.BringToFront()
        BPlay.BringToFront()
        Progreso.BringToFront()
        NoVideoImage.BringToFront()

        Player1.stretchToFit = True
        Player1.enableContextMenu = False
        Player1.uiMode = "mini"
        Player1.Ctlenabled = True
        Player1.settings.setMode("loop", True)
        Player1.settings.autoStart = False
        Player1.settings.volume = 100
        If IO.File.Exists(VideoPath) = False OrElse FileSize(VideoPath) = "0 KB" Then
            Player1.Visible = False
            ShowControls(False)
        Else
            Player1.URL = VideoPath
            Player1.Ctlcontrols.play()
        End If
        If FMenu.Opciones_OSD.Showing = True Then FMenu.Opciones_OSD.Close()

        If Player1.URL <> "" Then
            If VideoDimension() = False Then
                Player1.Visible = False
                ShowControls(False)
                GoTo Salta
            End If
            Size = New Size(NewVideoWidth + 32, NewVideoHeight + 104)
            If Width < 583 Then
                NewSizeVideo(New Size(VideoWidth, VideoHeight), 583 - 32, 0)
                Size = New Size(NewVideoWidth + 32, NewVideoHeight + 104)
            End If
        End If
Salta:  If Height > Screen.PrimaryScreen.WorkingArea.Height Then Height = Screen.PrimaryScreen.WorkingArea.Height
        Location = New Point((Screen.PrimaryScreen.WorkingArea.Width - Width) / 2, (Screen.PrimaryScreen.WorkingArea.Height - Height) / 2) : MinimumSize = New Size(583, 413)
    End Sub

    Dim DobleRow As String

    Private Sub FVideoRecorder_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        TNombre.Focus() : HTexto.Focus()
        If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False Else PTelegram.Enabled = True
        If Visible = True Then
            Player1.Visible = False : For Cuenta = 5 To 100 Step 5
                If Cuenta = 30 Then Player1.Visible = True : Opacity = 1 : Exit For
                Opacity = Cuenta / 100 : Refresh() : Application.DoEvents() : Threading.Thread.Sleep(10)
            Next : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
        End If
        FMenu.FGrabacionPantalla_Opening = False
    End Sub

    Private Sub FVideoRecorder_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles MyBase.Closing
        Opacity = 0 : VideoTimer.Enabled = False
    End Sub

#End Region

#Region " KeyPress "

    Private Sub FVideoRecorder_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Asc(e.KeyChar) = 27 Then : e.Handled = True
            If isFocused = False Then Close() Else HTexto.Focus()
        End If
    End Sub

#End Region

#Region " Controles "

    Private Sub Controles_Click(sender As Object, e As MouseEventArgs) Handles Me.Click, FTheme.Click, BMaximizar.Click, BClose.Click, GGif.Click, CGif.Click, LGif.Click, GSonido.Click, CSonido.Click, LSonido.Click, LDuracion.Click, Duracion.Click, LTamaño.Click, Tamaño.Click, PTelegram.Click, PGuardar.Click, PCerrar.Click, GPlayer.Click, Panel1.Click
        HTexto.Focus()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        e.Handled = True
    End Sub

#End Region

#End Region

#Region " Metodos "

#Region " Player "

    Private Sub Player_PlayStateChange(sender As Object, e As AxWMPLib._WMPOCXEvents_PlayStateChangeEvent) Handles Player1.PlayStateChange
        Try : If Player1.playState.ToString = "wmppsMediaEnded" Then Progreso.Value = Progreso.Maximum : Exit Sub
            If Player1.playState.ToString = "wmppsTransitioning" Or Player1.playState.ToString = "wmppsReady" Then Exit Sub
            If Player1.playState.ToString = "wmppsStopped" Then Started = False : Player1.Ctlcontrols.play() : Exit Sub

            If Player1.currentMedia.duration <> 0 And Started = False Then
                VideoTimer.Enabled = False : UpdateInfo() : Started = True : If Progreso.Value > Player1.currentMedia.duration * 1000 Then Progreso.Value = 0 : Progreso.Maximum = Player1.currentMedia.duration * 1000 : Progreso.Value = Progreso.Maximum Else Progreso.Maximum = Player1.currentMedia.duration * 1000
                If PlayerPosition > 0 Then
                    Player1.Ctlcontrols.currentPosition = PlayerPosition : Progreso.Value = Player1.Ctlcontrols.currentPosition * 1000 : PlayerPosition = -1
                    If PlayerState = "wmppsPlaying" Then Player1.Ctlcontrols.play()
                    If PlayerState = "wmppsPaused" Then Player1.Ctlcontrols.pause()
                Else
                    Player1.Ctlcontrols.pause() : Player1.Ctlcontrols.currentPosition = 0 : Progreso.Value = 0
                End If : VideoTimer.Enabled = True
            End If
            If Player1.playState.ToString = "wmppsPlaying" And BPlay.Text = "4" Then BPlay.Text = ";" : BPlay.Top -= 1
            If Player1.playState.ToString = "wmppsPaused" And BPlay.Text = ";" Then BPlay.Text = "4" : BPlay.Top += 1
            If Player1.playState.ToString = "wmppsStopped" Then Started = False : Player1.Ctlcontrols.play()
        Catch : End Try
    End Sub

    Private Sub Player_Click(sender As Object, e As AxWMPLib._WMPOCXEvents_ClickEvent) Handles Player1.ClickEvent
        HTexto.Focus()
    End Sub

#End Region

#Region " Timer "

    Private Sub VideoTimer_Tick(sender As Object, e As EventArgs)
        If CGif.Checked = True Then
            GSonido.Enabled = False : Player1.settings.mute = CGif.Checked
        Else
            GSonido.Enabled = True : Player1.settings.mute = Not CSonido.Checked
        End If
        If MouseDowned = True Then Exit Sub
        If Player1.playState.ToString <> "wmppsPlaying" And Player1.playState.ToString = "wmppsPaused" Then Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum - 200 Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 < 500 Then Progreso.Value = 0 : Exit Sub
        Progreso.Value = Player1.Ctlcontrols.currentPosition * 1000
    End Sub

#End Region

#Region " Progreso "

    Private Sub Progreso_MouseDown(sender As Object, e As MouseEventArgs) Handles Progreso.MouseDown
        HTexto.Focus() : MouseDowned = True : ChangeProgress(Progreso, e)
    End Sub

    Private Sub Progreso_MouseUp(sender As Object, e As MouseEventArgs) Handles Progreso.MouseUp
        ChangeProgress(Progreso, e) : MouseDowned = False
    End Sub

    Private Sub ChangeProgress(cProgreso As NSProgressBar, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim value = CInt(cProgreso.Minimum + (cProgreso.Maximum - cProgreso.Minimum) * Math.Min(Math.Max(e.X, 0), cProgreso.ClientSize.Width) / cProgreso.ClientSize.Width)
        If Math.Abs(cProgreso.Value - value) < 1000 Then
            If value < cProgreso.Value And value < 1000 Then
                cProgreso.Value = 0
            ElseIf value > cProgreso.Value And value > cProgreso.Maximum - 1000 Then
                cProgreso.Value = cProgreso.Maximum
            Else
                Exit Sub
            End If
        Else
            cProgreso.Value = value
        End If
        If Player1.playState.ToString = "wmppsPaused" Then
            Player1.Ctlcontrols.play()
            Player1.Ctlcontrols.currentPosition = cProgreso.Value / 1000
            Player1.Ctlcontrols.pause()
        Else
            Player1.Ctlcontrols.currentPosition = cProgreso.Value / 1000
        End If
    End Sub

#End Region

#Region " BPlay "

    Private Sub BPlay_Click(sender As Object, e As MouseEventArgs) Handles BPlay.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        HTexto.Focus() : If Player1.playState.ToString = "wmppsPlaying" Then Player1.Ctlcontrols.pause() Else If Player1.playState.ToString = "wmppsPaused" Then Player1.Ctlcontrols.play()
    End Sub

#End Region

#End Region

#Region " Funciones "

#Region " UpdateInfo "

    Private Sub UpdateInfo()
        Dim cDuracion As String : If Player1.playState.ToString = "wmppsTransitioning" Or Player1.playState.ToString = "wmppsReady" Or Player1.playState.ToString = "wmppsStopped" Then cDuracion = "00:00" Else cDuracion = New DateTime(TimeSpan.FromSeconds(Player1.currentMedia.duration).Ticks).ToString(("mm:ss"))
        Duracion.Value1 = cDuracion.Split(":")(0) + " :" : Duracion.Value2 = cDuracion.Split(":")(1)
        Dim cFileSize As String = FileSize(VideoPath) : Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
        Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString
    End Sub

#End Region

#Region " File Size "

    Public Function FileSize(FilePath As String) As String
        If Not IO.File.Exists(FilePath) Then Return "0 KB"
        Return FormatSize(My.Computer.FileSystem.GetFileInfo(FilePath).Length)
    End Function

    Private Function FormatSize(TheSize As Long) As String
        Dim DoubleBytes As Double
        Try : Select Case TheSize
                Case Is >= 1099511627776
                    DoubleBytes = CDbl(TheSize / 1099511627776) 'TB
                    Return FormatNumber(DoubleBytes, 2) & " TB"
                Case 1073741824 To 1099511627775
                    DoubleBytes = CDbl(TheSize / 1073741824) 'GB
                    Return FormatNumber(DoubleBytes, 2) & " GB"
                Case 1048576 To 1073741823
                    DoubleBytes = CDbl(TheSize / 1048576) 'MB
                    Return FormatNumber(DoubleBytes, 1) & " MB"
                Case 1024 To 1048575
                    DoubleBytes = CDbl(TheSize / 1024) 'KB
                    Return FormatNumber(DoubleBytes, 0) & " KB"
                Case 1 To 1023
                    'DoubleBytes = TheSize ' bytes
                    'Return FormatNumber(DoubleBytes, 0) & " bytes"
                    Return "0 KB"
                Case Else
                    Return "0 KB"
            End Select
        Catch : Return "0 KB" : End Try
    End Function

#End Region

#Region " Video Dimension "

    Private Function VideoDimension() As Boolean
        Dim SVideoWidth, SVideoHeight As String
        Dim objShell As New Shell32.Shell : Dim objFolder As Shell32.Folder = objShell.NameSpace(FMenu.KobaPathTemp)
        If objFolder IsNot Nothing Then
            Dim objFolderItem As Shell32.FolderItem : objFolderItem = objFolder.ParseName(IO.Path.GetFileName(VideoPath))
            If objFolderItem IsNot Nothing Then
                SVideoWidth = objFolder.GetDetailsOf(objFolderItem, 316) : SVideoHeight = objFolder.GetDetailsOf(objFolderItem, 314)
                If SVideoWidth = "" Or SVideoWidth = "0" Or SVideoHeight = "" Or SVideoHeight = "0" Then Return False
                VideoWidth = CInt(SVideoWidth) : VideoHeight = CInt(SVideoHeight)
            End If
        End If
        If VideoWidth > VideoHeight Then
            NewSizeVideo(New Size(VideoWidth, VideoHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
        Else
            NewSizeVideo(New Size(VideoWidth, VideoHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
        End If
        Return True
    End Function

    Private Sub NewSizeVideo(OriginalSize As Size, newWidth As Integer, newHeight As Integer)
        Dim bmp As New Bitmap(OriginalSize.Width, OriginalSize.Height)
        Dim ratio As Double = bmp.Width / bmp.Height
        Dim newSize As Size = bmp.Size
        If newWidth > 0 Then
            newSize = New Size(newWidth, newWidth / ratio)
        ElseIf newHeight > 0 Then
            newSize = New Size(newHeight * ratio, newHeight)
        End If
        Dim b As New Bitmap(newSize.Width, newSize.Height)
        Using g As Graphics = Graphics.FromImage(b)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(bmp, 0, 0, newSize.Width, newSize.Height)
        End Using
        NewVideoWidth = b.Width.ToString : NewVideoHeight = b.Height
    End Sub

#End Region

#Region " Remove Audio "

    Private Sub RemoveAudio(Ruta As String)
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp) : If IO.File.Exists(FMenu.KobaPathTemp + "ffmpeg.exe") = False Then IO.File.WriteAllBytes(FMenu.KobaPathTemp + "ffmpeg.exe", My.Resources.ffmpeg)
        Dim Proceso As New Process : Proceso.StartInfo.FileName = FMenu.KobaPathTemp + "ffmpeg.exe"
        Proceso.StartInfo.UseShellExecute = False : Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Hidden : Proceso.StartInfo.RedirectStandardInput = True : Proceso.StartInfo.CreateNoWindow = True
        If IO.File.Exists(FMenu.KobaPathTemp + "EditVideo.mp4") = True Then IO.File.Delete(FMenu.KobaPathTemp + "EditVideo.mp4")
        Proceso.StartInfo.Arguments = "-i " + FMenu.KobaPathTemp + "VideoRecording.mp4 -c copy -an """ + Ruta + """"
        Proceso.Start() : Proceso.WaitForExit()
    End Sub

#End Region

#Region " ShowControls "

    Private Sub ShowControls(Show As Boolean)
        If Show = True Then
            BPlay.Visible = True : Progreso.Visible = True : Player1.Visible = True : NoVideoImage.Visible = False
            If TNombre.Text <> "" And TNombre.Text <> "Escriba un nombre" Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
            PEditar.Enabled = True : PCopiar.Enabled = True : PGuardar.Enabled = True : PEditar.Image = My.Resources.Editar_Imagen_Off : PCopiar.Image = My.Resources.Copiar_Imagen_Off : PGuardar.Image = My.Resources.Save_Off
            If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Image = My.Resources.Telegram_Off
            GSonido.Enabled = True : GGif.Enabled = True
        Else
            BPlay.Visible = False : Progreso.Visible = False : Player1.Visible = False : NoVideoImage.Visible = True : VideoTimer.Enabled = False
            TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) : PEditar.Enabled = False : PCopiar.Enabled = False : PGuardar.Enabled = False : PTelegram.Image = My.Resources.Telegram_No : PEditar.Image = My.Resources.Editar_Imagen_No : PCopiar.Image = My.Resources.Copiar_Imagen_No : PGuardar.Image = My.Resources.Save_No
            Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = " KB"
            GSonido.Enabled = False : GGif.Enabled = False
        End If
    End Sub

#End Region

#End Region

#End Region

#Region " Controles "

#Region " CGif, CSonido "

    Private Sub LGif_Click(sender As Object, e As MouseEventArgs) Handles LGif.Click, LGif.DoubleClick, CGif.Click, CGif.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CGif.Checked = True Then CGif.Checked = False Else CGif.Checked = True
        If CGif.Checked = True Then GSonido.Enabled = False : Player1.settings.mute = CGif.Checked Else GSonido.Enabled = True : Player1.settings.mute = Not CSonido.Checked
    End Sub

    Private Sub LSonido_Click(sender As Object, e As MouseEventArgs) Handles LSonido.Click, LSonido.DoubleClick, CSonido.Click, CSonido.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CSonido.Checked = True Then CSonido.Checked = False Else CSonido.Checked = True
        Player1.settings.mute = Not CSonido.Checked
    End Sub

    Private Sub GSonidoGif_MouseEnter(sender As Object, e As EventArgs) Handles GSonido.MouseEnter, GGif.MouseEnter, FTheme.MouseEnter, GPlayer.MouseEnter
        If IO.File.Exists(Player1.URL) = False Then ShowControls(False)
    End Sub
#End Region

#Region " TNombre "

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If e.KeyChar = "/" Or e.KeyChar = "\" Or e.KeyChar = ":" Or e.KeyChar = "*" Or e.KeyChar = "?" Or e.KeyChar = """" Or e.KeyChar = "<" Or e.KeyChar = ">" Or e.KeyChar = "|" Then e.Handled = True
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TNombre_GotFocus(sender As Object, e As EventArgs) Handles TNombre.GotFocus
        isFocused = True : If TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = ""
        TNombre.SelectionLength = 0 : If IO.File.Exists(VideoPath) = True Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
    End Sub

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        isFocused = False : TNombre.SelectionStart = 0 : TNombre.SelectionLength = 0 : TNombre.Text = TNombre.Text.Trim
        If TNombre.Text = "" Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = "Escriba un nombre" : TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
        If IO.File.Exists(VideoPath) = False Then TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
    End Sub

#End Region

#Region " Botones "

#Region " Editar "

    Private Sub PEditar_Click(sender As Object, e As MouseEventArgs) Handles PEditar.Click, PEditar.DoubleClick
        If e.Button <> MouseButtons.Left Or IO.File.Exists(Player1.URL) = False Then Exit Sub
        Try : Player1.Ctlcontrols.pause()
            cFVideoRecorderEditor = New FVideoRecorderEditor
            cFVideoRecorderEditor.ShowDialog()
            If cFVideoRecorderEditor.Cancelado = False Then
                EditingPanel.Visible = False : EditingLabel.Visible = False
                If IO.File.Exists(VideoPath) = False Then
                    Player1.Visible = False
                    ShowControls(False)
                    GoTo Salta
                Else
                    Player1.newMedia(FMenu.KobaPathTemp + "VideoRecording.mp4")
                    Player1.URL = FMenu.KobaPathTemp + "VideoRecording.mp4"
                    Player1.Ctlcontrols.play()
                End If
                If VideoDimension() = False Then
                    Player1.Visible = False
                    ShowControls(False)
                    GoTo Salta
                End If
                Size = New Size(NewVideoWidth + 32, NewVideoHeight + 111)
                If Width < 375 Then
                    NewSizeVideo(New Size(VideoWidth, VideoHeight), 375 - 32, 0)
                    Size = New Size(NewVideoWidth + 32, NewVideoHeight + 111)
                End If

Salta:          If Height > Screen.PrimaryScreen.WorkingArea.Height Then Height = Screen.PrimaryScreen.WorkingArea.Height
                Location = New Point((Screen.PrimaryScreen.WorkingArea.Width - Width) / 2, (Screen.PrimaryScreen.WorkingArea.Height - Height) / 2) : MinimumSize = New Size(583, 413)
            End If
        Catch : End Try
    End Sub

    Private Sub PEditar_MouseEnter(sender As Object, e As EventArgs) Handles PEditar.MouseEnter
        If PEditar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PEditar.Image = My.Resources.GrabacionVideo_Editar Else ShowControls(False)
    End Sub

    Private Sub PEditar_MouseLeave(sender As Object, e As EventArgs) Handles PEditar.MouseLeave
        If PEditar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PEditar.Image = My.Resources.GrabacionVideo_Editar_Off
    End Sub

#End Region

#Region " Telegram "

    Private Sub PTelegram_Click(sender As Object, e As MouseEventArgs) Handles PTelegram.Click, PTelegram.DoubleClick
        If e.Button <> MouseButtons.Left Or IO.File.Exists(VideoPath) = False Or IO.File.Exists(Rutas.TelegramPath) = False Then Exit Sub
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        Dim FileName As String : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text Else FileName = "Pantalla " + Monitor.ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
        If CGif.Checked = True Then FileName += ".gif" Else FileName += ".mp4"
        If CGif.Checked = True Or CSonido.Checked = False Then RemoveAudio(FMenu.KobaPathTemp + FileName) Else IO.File.Copy(VideoPath, FMenu.KobaPathTemp + FileName, True)
        FMenu.CreateKPS() : RunProcess.RunAsStandar(FMenu.KobaPathTemp + "KPS.exe", "Ejecutar Aplicacion|-|" + Rutas.TelegramPath + "|-|-sendpath " + "|||" + FMenu.KobaPathTemp + FileName + "||||-||-|Normal|-|Normal|-|False|-|False") : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
        Try : AppActivate("Telegram") : Catch : End Try
    End Sub

    Private Sub PTelegram_MouseEnter(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter
        If IO.File.Exists(Player1.URL) = False Then ShowControls(False)
        If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True : PTelegram.Image = My.Resources.Telegram_On
    End Sub

    Private Sub PTelegram_MouseLeave(sender As Object, e As EventArgs) Handles PTelegram.MouseLeave
        If IO.File.Exists(Rutas.TelegramPath) = False Or IO.File.Exists(Player1.URL) = False Then Exit Sub
        PTelegram.Image = My.Resources.Telegram_Off
    End Sub

#End Region

#Region " Copiar "

    Private Sub PCopiar_Click(sender As Object, e As MouseEventArgs) Handles PCopiar.Click, PCopiar.DoubleClick
        If e.Button <> MouseButtons.Left Or IO.File.Exists(VideoPath) = False Then Exit Sub
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        Dim FileName As String : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text Else FileName = "Pantalla " + Monitor.ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
        If CGif.Checked = True Then FileName += ".gif" Else FileName += ".mp4"
        If CGif.Checked = True Or CSonido.Checked = False Then RemoveAudio(FMenu.KobaPathTemp + FileName) Else IO.File.Copy(VideoPath, FMenu.KobaPathTemp + FileName, True)
        Dim DataObject As New DataObject() : Dim file(0) As String : file(0) = FMenu.KobaPathTemp + FileName
        DataObject.SetData(DataFormats.FileDrop, True, file) : Clipboard.SetDataObject(DataObject)
    End Sub

    Private Sub PCopiar_MouseEnter(sender As Object, e As EventArgs) Handles PCopiar.MouseEnter
        If PCopiar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PCopiar.Image = My.Resources.Copiar_Imagen_On Else ShowControls(False)
    End Sub

    Private Sub PCopiar_MouseLeave(sender As Object, e As EventArgs) Handles PCopiar.MouseLeave
        If PCopiar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PCopiar.Image = My.Resources.Copiar_Imagen_Off
    End Sub

#End Region

#Region " Guardar "

    Private Sub PGuardar_Click(sender As Object, e As MouseEventArgs) Handles PGuardar.Click, PGuardar.DoubleClick
        If e.Button <> MouseButtons.Left Or IO.File.Exists(VideoPath) = False Then Exit Sub
        Dim FileName As String : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text Else FileName = "Pantalla " + Monitor.ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
        If CGif.Checked = True Then FileName += ".gif" Else FileName += ".mp4"
        Dim Guardar As New SaveFileDialog : Guardar.CheckPathExists = True : Guardar.FileName = FileName : Guardar.OverwritePrompt = True : Guardar.AddExtension = True
        Guardar.Title = "Guardar grabacion de pantalla..." : Guardar.Filter = "Archivo de Video (*.mp4)|*.mp4|Todos los Archivos (*.*)|*.*" : Guardar.DefaultExt = "*.mp4"
        If Guardar.ShowDialog <> DialogResult.Cancel Then
            If CGif.Checked = True Or CSonido.Checked = False Then RemoveAudio(Guardar.FileName) Else IO.File.Copy(VideoPath, Guardar.FileName, True)
        End If
    End Sub

    Private Sub PGuardar_MouseEnter(sender As Object, e As EventArgs) Handles PGuardar.MouseEnter
        If PGuardar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PGuardar.Image = My.Resources.Save_On Else ShowControls(False)
    End Sub

    Private Sub PGuardar_MouseLeave(sender As Object, e As EventArgs) Handles PGuardar.MouseLeave
        If PGuardar.Enabled = True AndAlso IO.File.Exists(Player1.URL) = True Then PGuardar.Image = My.Resources.Save_Off
    End Sub

#End Region

#Region " Salir "

    Private Sub PCerrar_Click(sender As Object, e As MouseEventArgs) Handles PCerrar.Click, PCerrar.DoubleClick
        If e.Button = MouseButtons.Left Then Close()
    End Sub

    Private Sub PCerrar_MouseEnter(sender As Object, e As EventArgs) Handles PCerrar.MouseEnter
        PCerrar.Image = My.Resources.Cerrar_On
    End Sub

    Private Sub PCerrar_MouseLeave(sender As Object, e As EventArgs) Handles PCerrar.MouseLeave
        PCerrar.Image = My.Resources.Cerrar_Off
    End Sub

#End Region

#End Region

#Region " Help "

    Private Sub GrabacionPantalla_LHelp_Click(sender As Object, e As MouseEventArgs) Handles GrabacionPantalla_LHelp.Click, GrabacionPantalla_LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Grabar Pantalla" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub GrabacionPantalla_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles GrabacionPantalla_LHelp.MouseEnter
        GrabacionPantalla_LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub GrabacionPantalla_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles GrabacionPantalla_LHelp.MouseLeave
        GrabacionPantalla_LHelp.ForeColor1 = Color.FromArgb(85, 85, 85)
    End Sub

    Private Sub Controles_Click(sender As Object, e As EventArgs) Handles Tamaño.Click, MyBase.Click, LTamaño.Click, LSonido.Click, LGif.Click, LDuracion.Click, GSonido.Click, GGif.Click, FTheme.Click, Duracion.Click, CSonido.Click, CGif.Click, BMaximizar.Click, BClose.Click, Panel1.Click, GPlayer.Click, PTelegram.Click, PGuardar.Click, PCerrar.Click

    End Sub

#End Region

#Region " Tooltips "

    Private Sub GrabarVideo_Tooltips(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter, PEditar.MouseEnter, PCopiar.MouseEnter, PGuardar.MouseEnter, PCerrar.MouseEnter, CSonido.MouseEnter, LSonido.MouseEnter, CGif.MouseEnter, LGif.MouseEnter
        Select Case sender.Name
            Case "CSonido", "LSonido" : If CSonido.Enabled = True Then FMenu.Show_Tooltip(sender, "Sonido de la grabación")
            Case "CGif", "LGif" : If CGif.Enabled = True Then FMenu.Show_Tooltip(sender, "Convertir el vídeo en 'GIF'" + vbCrLf + vbCrLf + "Los archivos 'GIF' no tienen sonido")

            Case "PTelegram" : If PTelegram.Enabled = True Then FMenu.Show_Tooltip(sender, "Envía la grabación por Telegram")
            Case "PEditar" : If PEditar.Enabled = True Then FMenu.Show_Tooltip(sender, "Edita la grabación para recortar su duración (Trim) o sus bordes (Crop)")
            Case "PCopiar" : If PCopiar.Enabled = True Then FMenu.Show_Tooltip(sender, "Copia la grabación como archivo en el portapapeles de Windows")
            Case "PGuardar" : If PGuardar.Enabled = True Then FMenu.Show_Tooltip(sender, "Guarda la grabación en disco")
            Case "PCerrar" : FMenu.Show_Tooltip(sender, "Cierra la ventana")
        End Select
    End Sub

#End Region

#End Region

End Class

#End Region