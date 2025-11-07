
#Region " Audio Recorder "

Public Class FAudioRecorder

#Region " Variables "

    Dim Started, MouseDowned, isFocused As Boolean
    Dim AudioTimer As New Timer
    Dim AudioPath As String
    Dim Player As New AxWMPLib.AxWindowsMediaPlayer
    Dim PlayerPosition As Integer = -1
    Dim PlayerState As String = ""
    Dim _isMoved As Boolean
    Dim _x, _y As Integer
    Dim VLeft, VRight As Integer

#End Region

#Region " Eventos "

#Region " Formulario "

#Region " Load "

    Private Sub FAudioRecorder_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        'If IO.File.Exists(Application.StartupPath + "\NAudio.Lame.dll") = False Then Trim_Left.Visible = False : Trim_Right.Visible = False
        DoubleBuffered = True : Opacity = 0 : TopMost = True : Location = New Point((Screen.PrimaryScreen.Bounds.Width - Width) / 2, (Screen.PrimaryScreen.Bounds.Height - Height) / 2)
        AudioTimer.Interval = 500 : AudioTimer.Enabled = True
        AddHandler AudioTimer.Tick, AddressOf AudioTimer_Tick
        AddHandler Player.PlayStateChange, AddressOf Player_PlayStateChange
        FTheme.Controls.Add(Player)
        Player.Location = New Point(-1000, 0) : Player.Size = New Size(100, 100)
        Player.enableContextMenu = False : Player.settings.autoStart = False : Player.Ctlenabled = True
        Player.settings.volume = 100
        Player.uiMode = "mini" : Player.settings.setMode("loop", True)
        AudioPath = FMenu.KobaPathTemp + "AudioS.mp3"
        If Not IO.File.Exists(FMenu.KobaPathTemp + "AudioS.mp3") Then GSonido.Enabled = False : CSonido.Checked = False
        If Not IO.File.Exists(FMenu.KobaPathTemp + "AudioM.mp3") Then GMicrofono.Enabled = False : CMicrofono.Checked = False
        If Not IO.File.Exists(FMenu.KobaPathTemp + "AudioA.mp3") And GSonido.Enabled = False And GMicrofono.Enabled = True Then AudioPath = FMenu.KobaPathTemp + "AudioM.mp3"
        If IO.File.Exists(FMenu.KobaPathTemp + "AudioA.mp3") And GSonido.Enabled = False Then CSonido.Checked = True : CMicrofono.Checked = True : AudioPath = FMenu.KobaPathTemp + "AudioA.mp3"
        If IO.File.Exists(AudioPath) = True Then Player.URL = AudioPath : Player.Ctlcontrols.play() Else Player.Visible = False : ShowControls(False)
    End Sub

    Private Sub FAudioRecorder_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        TNombre.Focus() : HTexto.Focus()
        If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True
        If Player.Visible = False AndAlso Visible = True Then Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
        FMenu.FGrabacionAudio_Opening = False
    End Sub

    Private Sub FAudioRecorder_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles MyBase.Closing
        AudioTimer.Enabled = False
    End Sub

#End Region

#Region " KeyPress "

    Private Sub FAudioRecorder_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Asc(e.KeyChar) = 27 Then : e.Handled = True
            If isFocused = False Then Close() Else HTexto.Focus()
        ElseIf Asc(e.KeyChar) = 32 Then : e.Handled = True
            BPlay_Click(sender, Nothing)
        End If
    End Sub

#End Region

#Region " Controles "

    Private Sub Controles_Click(sender As Object, e As MouseEventArgs) Handles Me.Click, LMicrofono.Click, GSonido.Click, GMicrofono.Click, FTheme.Click, CMicrofono.Click, BClose.Click, LSonido.Click, GSonido.Click, CSonido.Click, Progreso.Click, BPlay.Click, Trim_Left.Click, Trim_Right.Click, PTelegram.Click, PGuardar.Click, PCerrar.Click, LDuracion.Click, Duracion.Click, LTamaño.Click, Tamaño.Click
        HTexto.Focus()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        e.Handled = True
    End Sub

#End Region

#End Region

#Region " Metodos "

#Region " Player "

    Private Sub Player_PlayStateChange(sender As Object, e As AxWMPLib._WMPOCXEvents_PlayStateChangeEvent)
        Try
            If Player.playState.ToString = "wmppsMediaEnded" Then Progreso.Value = Progreso.Maximum : Exit Sub
            If Player.playState.ToString = "wmppsTransitioning" Or Player.playState.ToString = "wmppsReady" Then Exit Sub
            If Player.playState.ToString = "wmppsStopped" Then Started = False : Player.Ctlcontrols.play() : Exit Sub

            If Player.currentMedia.duration <> 0 And Started = False Then
                AudioTimer.Enabled = False : UpdateInfo() : Started = True : If Progreso.Value > Player.currentMedia.duration * 1000 Then Progreso.Value = 0 : Progreso.Maximum = Player.currentMedia.duration * 1000 : Progreso.Value = Progreso.Maximum Else Progreso.Maximum = Player.currentMedia.duration * 1000
                VLeft = ((((Trim_Left.Left - 23) * 100) / Progreso.Width) * Progreso.Maximum) / 100
                VRight = ((((Trim_Right.Left + 3 - 23) * 100) / Progreso.Width) * Progreso.Maximum) / 100

                Dim Percent As Integer = ((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum
                Dim cTime As Integer = (Player.currentMedia.duration * 1000) - ((Percent * (Player.currentMedia.duration * 1000)) / 100)
                Dim cDuracion As String : If cTime / 1000 > 0 Then cDuracion = New DateTime(TimeSpan.FromSeconds(cTime / 1000).Ticks).ToString(("mm:ss")) Else cDuracion = "00:01"
                Duracion.Value1 = cDuracion.Split(":")(0) + " :" : Duracion.Value2 = cDuracion.Split(":")(1)

                Dim cFileSize As String = FormatSize(My.Computer.FileSystem.GetFileInfo(AudioPath).Length - ((Percent * My.Computer.FileSystem.GetFileInfo(AudioPath).Length) / 100))
                Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
                Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString

                If PlayerPosition > 0 Then
                    Player.Ctlcontrols.currentPosition = PlayerPosition : Progreso.Value = Player.Ctlcontrols.currentPosition * 1000 : PlayerPosition = -1
                    If PlayerState = "wmppsPlaying" Then Player.Ctlcontrols.play()
                    If PlayerState = "wmppsPaused" Then Player.Ctlcontrols.pause()
                Else
                    Player.Ctlcontrols.pause() : Player.Ctlcontrols.currentPosition = 0 : Progreso.Value = 0
                End If : AudioTimer.Enabled = True : Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
            End If
            If Player.playState.ToString = "wmppsPlaying" And BPlay.Text = "4" Then BPlay.Text = ";"
            If Player.playState.ToString = "wmppsPaused" And BPlay.Text = ";" Then BPlay.Text = "4"
        Catch : End Try
    End Sub

#End Region

#Region " Timer "

    Private Sub AudioTimer_Tick(sender As Object, e As EventArgs)
        If MouseDowned = True Then Exit Sub
        If Player.playState.ToString <> "wmppsPlaying" And Player.playState.ToString = "wmppsPaused" Then Exit Sub
        If Player.Ctlcontrols.currentPosition * 1000 < VLeft Then Progreso.Value = VLeft : Player.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Exit Sub
        If Player.Ctlcontrols.currentPosition * 1000 > VRight Then Progreso.Value = VRight : Player.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Application.DoEvents() : Threading.Thread.Sleep(100) : Progreso.Value = VLeft : Player.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Exit Sub
        If Player.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum - 200 Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player.Ctlcontrols.currentPosition * 1000 < 500 Then Progreso.Value = 0 : Exit Sub
        Progreso.Value = Player.Ctlcontrols.currentPosition * 1000
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
        If cProgreso.Value < VLeft Then cProgreso.Value = VLeft
        If cProgreso.Value > VRight Then cProgreso.Value = VRight
        If Player.playState.ToString = "wmppsPaused" Then
            Player.Ctlcontrols.play()
            Player.Ctlcontrols.currentPosition = cProgreso.Value / 1000
            Player.Ctlcontrols.pause()
        Else
            Player.Ctlcontrols.currentPosition = cProgreso.Value / 1000
        End If
    End Sub

#End Region

#Region " BPlay "

    Private Sub BPlay_Click(sender As Object, e As MouseEventArgs) Handles BPlay.Click
        HTexto.Focus()
        If e IsNot Nothing AndAlso e.Button <> MouseButtons.Left Then Exit Sub
        If Player.playState.ToString = "wmppsPlaying" Then Player.Ctlcontrols.pause() Else If Player.playState.ToString = "wmppsPaused" Then Player.Ctlcontrols.play()
    End Sub

#End Region

#End Region

#Region " Funciones "

#Region " UpdateInfo "

    Private Sub UpdateInfo()
        Dim cDuracion As String : If Player.playState.ToString = "wmppsTransitioning" Or Player.playState.ToString = "wmppsReady" Or Player.playState.ToString = "wmppsStopped" Then cDuracion = "00:00" Else cDuracion = New DateTime(TimeSpan.FromSeconds(Player.currentMedia.duration).Ticks).ToString(("mm:ss"))
        Duracion.Value1 = cDuracion.Split(":")(0) + " :" : Duracion.Value2 = cDuracion.Split(":")(1)
        Dim cFileSize As String = FileSize(AudioPath) : Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
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
                    DoubleBytes = TheSize ' bytes
                    Return FormatNumber(DoubleBytes, 0) & " bytes"
                Case Else
                    Return "0 KB"
            End Select
        Catch : Return "0 KB" : End Try
    End Function

#End Region

#Region " Trim "

    Private Sub Trim_MouseDown(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseDown, Trim_Right.MouseDown
        _isMoved = True
        _x = e.Location.X
        _y = e.Location.Y
    End Sub

    Private Sub Trim_MouseMove(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseMove, Trim_Right.MouseMove
        If _isMoved Then
            Select Case sender.Name
                Case "Trim_Left"
                    Trim_Left.Location = New Point(Math.Max(Math.Min(Trim_Left.Location.X + (e.Location.X - _x), Trim_Right.Left - Trim_Left.Width), 24), Trim_Left.Top)
                Case "Trim_Right"
                    Trim_Right.Location = New Point(Math.Max(Math.Min(Trim_Right.Location.X + (e.Location.X - _x), (Progreso.Left + Progreso.Width) - 3), Trim_Left.Left + Trim_Left.Width), Trim_Right.Top)
            End Select
        End If
    End Sub

    Private Sub Trim_MouseUp(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseUp, Trim_Right.MouseUp
        _isMoved = False
        VLeft = ((((Trim_Left.Left - 23) * 100) / Progreso.Width) * Progreso.Maximum) / 100
        VRight = ((((Trim_Right.Left + 3 - 23) * 100) / Progreso.Width) * Progreso.Maximum) / 100

        Dim Percent As Integer = ((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum
        Dim cTime As Integer = (Player.currentMedia.duration * 1000) - ((Percent * (Player.currentMedia.duration * 1000)) / 100)
        Dim cDuracion As String : If cTime / 1000 > 0 Then cDuracion = New DateTime(TimeSpan.FromSeconds(cTime / 1000).Ticks).ToString(("mm:ss")) Else cDuracion = "00:01"
        Duracion.Value1 = cDuracion.Split(":")(0) + " :" : Duracion.Value2 = cDuracion.Split(":")(1)

        Dim cFileSize As String = FormatSize(My.Computer.FileSystem.GetFileInfo(AudioPath).Length - ((Percent * My.Computer.FileSystem.GetFileInfo(AudioPath).Length) / 100))
        Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
        Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString
        If sender.name = "Trim_Left" Then Progreso.Value = VLeft Else If Progreso.Value > VRight Then Progreso.Value = VRight
        If Player.playState.ToString = "wmppsPaused" Then
            Player.Ctlcontrols.play()
            Player.Ctlcontrols.currentPosition = Progreso.Value / 1000
            Player.Ctlcontrols.pause()
        Else
            Player.Ctlcontrols.currentPosition = Progreso.Value / 1000
        End If
    End Sub

    Private Sub TrimMP3(OriginalFile As String, DestinationFile As String, StartTime As Integer, EndTime As Integer)
        Using MP3FileReader = New NAudio.Wave.Mp3FileReader(OriginalFile)
            Using Writer = IO.File.Create(DestinationFile)
                Dim StartPostion = TimeSpan.FromMilliseconds(StartTime)
                Dim EndPostion = TimeSpan.FromMilliseconds(EndTime)
                MP3FileReader.CurrentTime = StartPostion

                While MP3FileReader.CurrentTime < EndPostion
                    Application.DoEvents()
                    Dim Frame = MP3FileReader.ReadNextFrame()
                    If Frame Is Nothing Then Exit While
                    Writer.Write(Frame.RawData, 0, Frame.RawData.Length)
                End While
            End Using
        End Using
    End Sub

    Private Sub TrimWav(OriginalFile As String, DestinationFile As String, StartTime As Integer, EndTime As Integer)
        Dim FilePath As String = FMenu.KobaPathTemp + "TempAudio.mp3" : If IO.File.Exists(FilePath) = True Then IO.File.Delete(FilePath)

        Using MP3FileReader = New NAudio.Wave.Mp3FileReader(OriginalFile)
            Using Writer = IO.File.Create(FilePath)
                Dim StartPostion = TimeSpan.FromMilliseconds(StartTime)
                Dim EndPostion = TimeSpan.FromMilliseconds(EndTime)
                MP3FileReader.CurrentTime = StartPostion

                While MP3FileReader.CurrentTime < EndPostion
                    Application.DoEvents()
                    Dim Frame = MP3FileReader.ReadNextFrame()
                    If Frame Is Nothing Then Exit While
                    Writer.Write(Frame.RawData, 0, Frame.RawData.Length)
                End While
            End Using
        End Using
        Using Reader As New NAudio.Wave.Mp3FileReader(FilePath)
            Using pcmStream As NAudio.Wave.WaveStream = NAudio.Wave.WaveFormatConversionStream.CreatePcmStream(Reader)
                NAudio.Wave.WaveFileWriter.CreateWaveFile(DestinationFile, pcmStream)
            End Using
        End Using
    End Sub

#End Region

#Region " Show Controls "

    Private Sub ShowControls(Show As Boolean)
        If Show = True Then
            BPlay.Enabled = True : Progreso.Enabled = True : Trim_Left.Visible = True : Trim_Right.Visible = True
            If TNombre.Text <> "" And TNombre.Text <> "Escriba un nombre" Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
            PCopiar.Enabled = True : PGuardar.Enabled = True : PCopiar.Image = My.Resources.Copiar_Imagen_Off : PGuardar.Image = My.Resources.Save_Off
            If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True : PTelegram.Image = My.Resources.Telegram_Off
        Else
            Progreso.Value = 0 : BPlay.Enabled = False : Progreso.Enabled = False : Trim_Left.Visible = False : Trim_Right.Visible = False
            TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) : PTelegram.Enabled = False : PCopiar.Enabled = False : PGuardar.Enabled = False : PTelegram.Image = My.Resources.Telegram_No : PCopiar.Image = My.Resources.Copiar_Imagen_No : PGuardar.Image = My.Resources.Save_No
            Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = " KB"
        End If
    End Sub

#End Region

#End Region

#End Region

#Region " Controles "

#Region " CSonido, CMicrofono "

    Private Sub LSonido_Click(sender As Object, e As MouseEventArgs) Handles LSonido.Click, LSonido.DoubleClick, CSonido.Click, CSonido.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then
            If CSonido.Checked = True And CMicrofono.Checked = False Then Exit Sub
            If CSonido.Checked = True Then CSonido.Checked = False Else CSonido.Checked = True
        ElseIf TypeOf sender Is NSOnOffBox Then
            'If CSonido.Checked = False And CMicrofono.Checked = False Then CSonido.Checked = True : Exit Sub
        End If : ChangeAudio()
    End Sub

    Private Sub CSonido_MouseEnter(sender As Object, e As EventArgs) Handles CSonido.MouseEnter
        If CSonido.Checked = True And CMicrofono.Checked = False Then CSonido.IgnoreChecked = True Else CSonido.IgnoreChecked = False
    End Sub

    Private Sub LMicrofono_Click(sender As Object, e As MouseEventArgs) Handles LMicrofono.Click, LMicrofono.DoubleClick, CMicrofono.Click, CMicrofono.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then
            If CMicrofono.Checked = True And CSonido.Checked = False Then Exit Sub
            If CMicrofono.Checked = True Then CMicrofono.Checked = False Else CMicrofono.Checked = True
        ElseIf TypeOf sender Is NSOnOffBox Then
            'If CMicrofono.Checked = False And CSonido.Checked = False Then CMicrofono.Checked = True : Exit Sub
        End If : ChangeAudio()
    End Sub

    Private Sub CMicrofono_MouseEnter(sender As Object, e As EventArgs) Handles CMicrofono.MouseEnter
        If CMicrofono.Checked = True And CSonido.Checked = False Then CMicrofono.IgnoreChecked = True Else CMicrofono.IgnoreChecked = False
    End Sub

    Private Sub ChangeAudio()
        Try : PlayerPosition = Player.Ctlcontrols.currentPosition : PlayerState = Player.playState.ToString
            If CSonido.Checked = True Then
                If CMicrofono.Checked = True Then
                    If IO.File.Exists(FMenu.KobaPathTemp + "AudioA.mp3") = True Then Player.URL = FMenu.KobaPathTemp + "AudioA.mp3" Else Player.Ctlcontrols.pause() : Player.URL = "" : ShowControls(False) : AudioPath = "" : Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = "KB" : Exit Sub
                Else
                    If IO.File.Exists(FMenu.KobaPathTemp + "AudioS.mp3") = True Then Player.URL = FMenu.KobaPathTemp + "AudioS.mp3" Else Player.Ctlcontrols.pause() : Player.URL = "" : ShowControls(False) : AudioPath = "" : Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = "KB" : Exit Sub
                End If
            Else
                If CMicrofono.Checked = True Then
                    If IO.File.Exists(FMenu.KobaPathTemp + "AudioM.mp3") = True Then Player.URL = FMenu.KobaPathTemp + "AudioM.mp3" : ShowControls(True) Else Player.Ctlcontrols.pause() : Player.URL = "" : ShowControls(False) : AudioPath = "" : Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = "KB" : Exit Sub
                Else
                    Player.Ctlcontrols.pause() : Player.URL = "" : ShowControls(False) : AudioPath = "" : Duracion.Value1 = "00 :" : Duracion.Value2 = "00" : Tamaño.Value1 = "0 " : Tamaño.Value2 = "KB" : Exit Sub
                End If
            End If : ShowControls(True) : AudioPath = Player.URL : Started = False : Player.Ctlcontrols.play()
        Catch : End Try
    End Sub

#End Region

#Region " TNombre "

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If e.KeyChar = "/" Or e.KeyChar = "\" Or e.KeyChar = ":" Or e.KeyChar = "*" Or e.KeyChar = "?" Or e.KeyChar = """" Or e.KeyChar = "<" Or e.KeyChar = ">" Or e.KeyChar = "|" Then e.Handled = True
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TNombre_GotFocus(sender As Object, e As EventArgs) Handles TNombre.GotFocus
        isFocused = True : If TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = ""
        TNombre.SelectionLength = 0 : If IO.File.Exists(AudioPath) = True Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
    End Sub

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        isFocused = False : TNombre.SelectionStart = 0 : TNombre.SelectionLength = 0 : TNombre.Text = TNombre.Text.Trim
        If TNombre.Text = "" Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = "Escriba un nombre" : TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
        If IO.File.Exists(AudioPath) = False Then TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
    End Sub

#End Region

#Region " Botones "

#Region " Telegram "

    Private Sub PTelegram_Click(sender As Object, e As MouseEventArgs) Handles PTelegram.Click
        If e.Button <> MouseButtons.Left Or IO.File.Exists(AudioPath) = False Or IO.File.Exists(Rutas.TelegramPath) = False Then Exit Sub
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        Dim FileName As String = "" : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then
            FileName = TNombre.Text + ".mp3"
        Else
            If CSonido.Checked = True And CMicrofono.Checked = False Then FileName = "Sonido - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
            If CSonido.Checked = False And CMicrofono.Checked = True Then FileName = "Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
            If CSonido.Checked = True And CMicrofono.Checked = True Then FileName = "Sonido + Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
        End If
        If Trim_Left.Left = 24 And Trim_Right.Left = (Progreso.Left + Progreso.Width) - 4 Then
            IO.File.Copy(AudioPath, FMenu.KobaPathTemp + FileName, True)
        Else
            If IO.File.Exists(FMenu.KobaPathTemp + FileName) = True Then IO.File.Delete(FMenu.KobaPathTemp + FileName)
            Dim cStart As Decimal = (((VLeft * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100
            Dim cDuration As Decimal = (Player.currentMedia.duration * 1000) - (((((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100)
            If cStart + cDuration > Player.currentMedia.duration * 1000 Then cStart = (Player.currentMedia.duration * 1000) - cDuration
            If cStart + cDuration > Player.currentMedia.duration * 1000 Or Player.currentMedia.duration <= 1 Then cStart = -1

            If cStart = -1 Then
                IO.File.Copy(AudioPath, FMenu.KobaPathTemp + FileName, True)
            Else
                TrimMP3(AudioPath, FMenu.KobaPathTemp + FileName, cStart, cStart + cDuration)
            End If
        End If
        FMenu.CreateKPS() : RunProcess.RunAsStandar(FMenu.KobaPathTemp + "KPS.exe", "Ejecutar Aplicacion|-|" + Rutas.TelegramPath + "|-|-sendpath " + "|||" + FMenu.KobaPathTemp + FileName + "||||-||-|Normal|-|Normal|-|False|-|False") : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
        Try : AppActivate("Telegram") : Catch : End Try
    End Sub

    Private Sub PTelegram_MouseEnter(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter
        If IO.File.Exists(Rutas.TelegramPath) = False Or IO.File.Exists(Player.URL) = False Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True : PTelegram.Image = My.Resources.Telegram_On
    End Sub

    Private Sub PTelegram_MouseLeave(sender As Object, e As EventArgs) Handles PTelegram.MouseLeave
        If IO.File.Exists(Rutas.TelegramPath) = False Or IO.File.Exists(Player.URL) = False Then Exit Sub
        PTelegram.Image = My.Resources.Telegram_Off
    End Sub

#End Region

#Region " Copiar "

    Private Sub PCopiar_Click(sender As Object, e As MouseEventArgs) Handles PCopiar.Click, PCopiar.DoubleClick
        If e.Button <> MouseButtons.Left Or IO.File.Exists(AudioPath) = False Then Exit Sub
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        Dim FileName As String = "" : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then
            FileName = TNombre.Text + ".mp3"
        Else
            If CSonido.Checked = True And CMicrofono.Checked = False Then FileName = "Sonido - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
            If CSonido.Checked = False And CMicrofono.Checked = True Then FileName = "Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
            If CSonido.Checked = True And CMicrofono.Checked = True Then FileName = "Sonido + Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").mp3"
        End If
        If Trim_Left.Left = 24 And Trim_Right.Left = (Progreso.Left + Progreso.Width) - 4 Then
            IO.File.Copy(AudioPath, FMenu.KobaPathTemp + FileName, True)
        Else
            If IO.File.Exists(FMenu.KobaPathTemp + FileName) = True Then IO.File.Delete(FMenu.KobaPathTemp + FileName)
            Dim cStart As Decimal = (((VLeft * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100
            Dim cDuration As Decimal = (Player.currentMedia.duration * 1000) - (((((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100)
            If cStart + cDuration > Player.currentMedia.duration * 1000 Then cStart = (Player.currentMedia.duration * 1000) - cDuration
            If cStart + cDuration > Player.currentMedia.duration * 1000 Or Player.currentMedia.duration <= 1 Then cStart = -1

            If cStart = -1 Then
                IO.File.Copy(AudioPath, FMenu.KobaPathTemp + FileName, True)
            Else
                TrimMP3(AudioPath, FMenu.KobaPathTemp + FileName, cStart, cStart + cDuration)
            End If
        End If
        Dim DataObject As New DataObject() : Dim file(0) As String : file(0) = FMenu.KobaPathTemp + FileName
        DataObject.SetData(DataFormats.FileDrop, True, file) : Clipboard.SetDataObject(DataObject)
    End Sub

    Private Sub PCopiar_MouseEnter(sender As Object, e As EventArgs) Handles PCopiar.MouseEnter
        If PCopiar.Enabled = True AndAlso IO.File.Exists(Player.URL) = True Then PCopiar.Image = My.Resources.Copiar_Imagen_On
    End Sub

    Private Sub PCopiar_MouseLeave(sender As Object, e As EventArgs) Handles PCopiar.MouseLeave
        If PCopiar.Enabled = True Then PCopiar.Image = My.Resources.Copiar_Imagen_Off
    End Sub

#End Region

#Region " Guardar "

    Private Sub PGuardar_Click(sender As Object, e As MouseEventArgs) Handles PGuardar.Click
        If e.Button <> MouseButtons.Left Or IO.File.Exists(AudioPath) = False Then Exit Sub
        Dim FileName As String = "" : If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then
            FileName = TNombre.Text.Substring(0, 1).ToUpper + TNombre.Text.Substring(1)
        Else
            If CSonido.Checked = True And CMicrofono.Checked = False Then FileName = "Sonido - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
            If CSonido.Checked = False And CMicrofono.Checked = True Then FileName = "Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
            If CSonido.Checked = True And CMicrofono.Checked = True Then FileName = "Sonido + Microfono - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ")"
        End If
        Dim Guardar As New SaveFileDialog : Guardar.CheckPathExists = True : Guardar.FileName = FileName : Guardar.OverwritePrompt = True : Guardar.AddExtension = True
        Guardar.Title = "Guardar grabacion de audio..." : Guardar.Filter = "Archivo de Audio (*.mp3)|*.mp3|Archivo de Audio (*.wav)|*.wav|Todos los Archivos (*.*)|*.*" : Guardar.DefaultExt = "*.mp3"
        If Guardar.ShowDialog <> DialogResult.Cancel Then
            If Trim_Left.Left = 24 And Trim_Right.Left = (Progreso.Left + Progreso.Width) - 4 Then
                If IO.Path.GetExtension(Guardar.FileName) = ".wav" Then IO.File.Copy(FMenu.KobaPathTemp + IO.Path.GetFileNameWithoutExtension(AudioPath) + ".wav", Guardar.FileName, True) Else IO.File.Copy(AudioPath, Guardar.FileName, True)
            Else
                If IO.File.Exists(Guardar.FileName) = True Then IO.File.Delete(Guardar.FileName)
                Dim cStart As Decimal = (((VLeft * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100
                Dim cDuration As Decimal = (Player.currentMedia.duration * 1000) - (((((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum) * (Player.currentMedia.duration * 1000)) / 100)
                If cStart + cDuration > Player.currentMedia.duration * 1000 Then cStart = (Player.currentMedia.duration * 1000) - cDuration
                If cStart + cDuration > Player.currentMedia.duration * 1000 Or Player.currentMedia.duration <= 1 Then cStart = -1

                If IO.Path.GetExtension(Guardar.FileName) = ".wav" Then
                    If cStart = -1 Then
                        IO.File.Copy(FMenu.KobaPathTemp + IO.Path.GetFileNameWithoutExtension(AudioPath) + ".wav", Guardar.FileName, True)
                    Else
                        TrimWav(AudioPath, Guardar.FileName, cStart, cStart + cDuration)
                    End If
                Else
                    If cStart = -1 Then
                        IO.File.Copy(AudioPath, Guardar.FileName, True)
                    Else
                        TrimMP3(AudioPath, Guardar.FileName, cStart, cStart + cDuration)
                    End If
                End If
            End If
        End If
    End Sub

    Private Sub PGuardar_MouseEnter(sender As Object, e As EventArgs) Handles PGuardar.MouseEnter
        If PGuardar.Enabled = True AndAlso IO.File.Exists(Player.URL) = True Then PGuardar.Image = My.Resources.Save_On
    End Sub

    Private Sub PGuardar_MouseLeave(sender As Object, e As EventArgs) Handles PGuardar.MouseLeave
        If PGuardar.Enabled = True Then PGuardar.Image = My.Resources.Save_Off
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

    Private Sub GrabacionAudio_LHelp_Click(sender As Object, e As MouseEventArgs) Handles GrabacionAudio_LHelp.Click, GrabacionAudio_LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Grabar Audio" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub GrabacionAudio_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles GrabacionAudio_LHelp.MouseEnter
        GrabacionAudio_LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub GrabacionAudio_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles GrabacionAudio_LHelp.MouseLeave
        GrabacionAudio_LHelp.ForeColor1 = Color.FromArgb(85, 85, 85)
    End Sub

#End Region

#Region " Tooltips "

    Private Sub GrabarAudio_Tooltips(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter, PCopiar.MouseEnter, PGuardar.MouseEnter, PCerrar.MouseEnter, CSonido.MouseEnter, LSonido.MouseEnter, CMicrofono.MouseEnter, LMicrofono.MouseEnter
        Select Case sender.Name
            Case "CSonido", "LSonido" : If CSonido.Enabled = True Then FMenu.Show_Tooltip(sender, "Sonido del ordenador")
            Case "CMicrofono", "LMicrofono" : If CMicrofono.Enabled = True Then FMenu.Show_Tooltip(sender, "Sonido del micrófono")

            Case "PTelegram" : If PTelegram.Enabled = True Then FMenu.Show_Tooltip(sender, "Envía la grabación por Telegram")
            Case "PCopiar" : If PCopiar.Enabled = True Then FMenu.Show_Tooltip(sender, "Copia la grabación como archivo en el portapapeles de Windows")
            Case "PGuardar" : If PGuardar.Enabled = True Then FMenu.Show_Tooltip(sender, "Guarda la grabación en disco")
            Case "PCerrar" : FMenu.Show_Tooltip(sender, "Cierra la ventana")
        End Select
    End Sub

#End Region

#End Region

End Class

#End Region