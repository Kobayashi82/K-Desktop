
#Region " Video Recorder Editor "

Public Class FVideoRecorderEditor

#Region " Variables "

    Public Cancelado As Boolean = True
    Dim ForceCancel As Boolean
    Dim VideoPath As String = FVideoRecorder.Player1.URL
    Dim VideoTimer As New Timer With {.Interval = 500}
    Dim VideoWidth, VideoHeight, NewVideoWidth, NewVideoHeight As Integer
    Dim PlayerState As String = ""
    Dim PlayerPosition As Integer = -1
    Dim Started, MouseDowned As Boolean
    Dim _isMoved As Boolean
    Dim _x, _y As Integer
    Dim VLeft, VRight As Integer
    Dim Editing_Thread As New Threading.Thread(AddressOf Editing_Thread_Sub)

#End Region

#Region " Eventos "

#Region " Formulario "

    Private Sub FVideoRecorderTrim_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True
        PCropBottom.BackColor = Color.Black
        AddHandler VideoTimer.Tick, AddressOf VideoTimer_Tick
        Player1.URL = VideoPath
        Player1.stretchToFit = True
        Player1.enableContextMenu = False
        Player1.uiMode = "mini"
        Player1.Ctlenabled = True
        Player1.settings.setMode("loop", True)
        Player1.settings.autoStart = False
        Player1.settings.volume = 100
        VideoDimension()
        Size = New Size(NewVideoWidth + 31, NewVideoHeight + 92)
        If Height > Screen.PrimaryScreen.WorkingArea.Height Then Height = Screen.PrimaryScreen.WorkingArea.Height
        Location = New Point((Screen.FromControl(FVideoRecorder).Bounds.Width - Width) / 2 + Screen.FromControl(FVideoRecorder).Bounds.Left, (Screen.FromControl(FVideoRecorder).Bounds.Height - Height) / 2 + Screen.FromControl(FVideoRecorder).Bounds.Top)
    End Sub

    Private Sub VideoDimension()
        Dim objShell As New Shell32.Shell : Dim objFolder As Shell32.Folder = objShell.NameSpace(FMenu.KobaPathTemp)
        If objFolder IsNot Nothing Then
            Dim objFolderItem As Shell32.FolderItem : objFolderItem = objFolder.ParseName(IO.Path.GetFileName(Player1.URL))
            If objFolderItem IsNot Nothing Then VideoWidth = CInt(objFolder.GetDetailsOf(objFolderItem, 316)) : VideoHeight = CInt(objFolder.GetDetailsOf(objFolderItem, 314))
        End If
        If VideoWidth > VideoHeight Then
            NewSizeVideo(New Size(VideoWidth, VideoHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
        Else
            NewSizeVideo(New Size(VideoWidth, VideoHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
        End If
    End Sub

    Private Sub NewSizeVideo(OriginalSize As Size, newWidth As Integer, newHeight As Integer)
        Dim bmp As New Bitmap(OriginalSize.Width, OriginalSize.Height)
        Dim ratio As Double = bmp.Width / bmp.Height
        Dim newSize As Size = bmp.Size
        If newWidth > 0 Then
            newSize = New Size(newWidth, CInt(newWidth / ratio))
        ElseIf newHeight > 0 Then
            newSize = New Size(CInt(newHeight * ratio), newHeight)
        End If
        Dim b As New Bitmap(newSize.Width, newSize.Height)
        Using g As Graphics = Graphics.FromImage(b)
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
            g.DrawImage(bmp, 0, 0, newSize.Width, newSize.Height)
        End Using
        NewVideoWidth = b.Width.ToString : NewVideoHeight = b.Height
    End Sub

    Private Sub FVideoRecorderTrim_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        Player1.Visible = False : FMenu.WaitTime(500) : Player1.Visible = True : Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FVideoRecorder_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Asc(e.KeyChar) = 27 Then e.Handled = True : Close()
    End Sub

    Private Sub FVideoRecorderTrim_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles MyBase.Closing
        If Editing_Thread.IsAlive Then e.Cancel = True : Exit Sub
        Opacity = 0 : VideoTimer.Enabled = False : Player1.close()
    End Sub

#End Region

#Region " Funciones "

#Region " Croping "

    Private Sub SCropBottom_MouseDown(sender As Object, e As MouseEventArgs) Handles SCropTop.MouseDown, SCropBottom.MouseDown, SCropLeft.MouseDown, SCropRight.MouseDown
        _isMoved = True
        _x = e.Location.X
        _y = e.Location.Y
    End Sub

    Private Sub SetCropPos()
        'SCropTop
        SCropTop.Location = New Point(0, 1) : PCropTop.Location = New Point(0, -100)
        SCropTop.Size = New Size(Panel1.Width, 3) : PCropTop.Size = New Size(Panel1.Width, SCropTop.Top + 100)
        'SCropBottom
        SCropBottom.Location = New Point(0, Panel1.Height - 3) : PCropBottom.Location = New Point(0, SCropBottom.Top + 3)
        SCropBottom.Size = New Size(Panel1.Width, 3) : PCropBottom.Size = New Size(Panel1.Width, 100)
        'SCropLeft
        SCropLeft.Location = New Point(0, 4) : PCropLeft.Location = New Point(-100, 0)
        SCropLeft.Size = New Size(3, Panel1.Height - 4) : PCropLeft.Size = New Size(SCropLeft.Left + 100, Panel1.Height)
        'SCropRight
        SCropRight.Location = New Point(Panel1.Width - 3, 4) : PCropRight.Location = New Point(SCropRight.Left + 3, 0)
        SCropRight.Size = New Size(3, Panel1.Height - 4) : PCropRight.Size = New Size(SCropRight.Left + 100, Panel1.Height)
    End Sub

    Private Sub SCropBottom_MouseMove(sender As Object, e As MouseEventArgs) Handles SCropTop.MouseMove, SCropBottom.MouseMove, SCropLeft.MouseMove, SCropRight.MouseMove
        If _isMoved Then
            Dim cCropLine As NSSeperatorCrop = CType(sender, NSSeperatorCrop)
            Select Case cCropLine.Name
                Case "SCropTop"
                    SCropTop.Location = New Point(SCropTop.Location.X, Math.Max(Math.Min(SCropTop.Location.Y + (e.Location.Y - _y), SCropBottom.Top - 3), 0))
                    PCropTop.Size = New Size(PCropTop.Width, SCropTop.Top + 100)
                    SCropLeft.Location = New Point(SCropLeft.Left, SCropTop.Top + 3) : SCropLeft.Size = New Size(SCropLeft.Width, (SCropBottom.Top - SCropTop.Top) - 3)
                    SCropRight.Location = New Point(SCropRight.Left, SCropTop.Top + 3) : SCropRight.Size = New Size(SCropRight.Width, (SCropBottom.Top - SCropTop.Top) - 3)
                Case "SCropBottom"
                    SCropBottom.Location = New Point(SCropBottom.Location.X, Math.Max(Math.Min(SCropBottom.Location.Y + (e.Location.Y - _y), Panel1.Height - 3), SCropTop.Top + 3))
                    PCropBottom.Location = New Point(PCropBottom.Left, SCropBottom.Top) : PCropBottom.Size = New Size(PCropBottom.Width, (Panel1.Height - SCropBottom.Top) + 100)
                    SCropLeft.Size = New Size(SCropLeft.Width, (SCropBottom.Top - SCropTop.Top) - 3)
                    SCropRight.Size = New Size(SCropRight.Width, (SCropBottom.Top - SCropTop.Top) - 2)
                Case "SCropLeft"
                    SCropLeft.Location = New Point(Math.Max(Math.Min(SCropLeft.Location.X + (e.Location.X - _x), SCropRight.Left - 3), 0), SCropLeft.Top)
                    PCropLeft.Size = New Size(SCropLeft.Left + 100, PCropLeft.Height)
                    SCropTop.Location = New Point(SCropLeft.Left, SCropTop.Top) : SCropTop.Size = New Size((SCropRight.Left - SCropTop.Left) + 3, SCropTop.Height)
                    SCropBottom.Location = New Point(SCropLeft.Left, SCropBottom.Top) : SCropBottom.Size = New Size((SCropRight.Left - SCropBottom.Left) + 3, SCropBottom.Height)
                Case "SCropRight"
                    SCropRight.Location = New Point(Math.Max(Math.Min(SCropRight.Location.X + (e.Location.X - _x), Panel1.Width - 3), SCropLeft.Left + 3), SCropRight.Top)
                    PCropRight.Location = New Point(SCropRight.Left, PCropRight.Top) : PCropRight.Size = New Size((Panel1.Width - SCropRight.Left) + 100, PCropRight.Height)
                    SCropTop.Size = New Size(((SCropRight.Left - SCropTop.Left) + 3), SCropTop.Height)
                    SCropBottom.Size = New Size(((SCropRight.Left - SCropBottom.Left) + 3), SCropBottom.Height)
            End Select
        End If
    End Sub

    Private Sub SCropBottom_MouseUp(sender As Object, e As MouseEventArgs) Handles SCropTop.MouseUp, SCropBottom.MouseUp, SCropLeft.MouseUp, SCropRight.MouseUp
        _isMoved = False
    End Sub

    Private Sub Player1_MouseMoveEvent(sender As Object, e As AxWMPLib._WMPOCXEvents_MouseMoveEvent) Handles Player1.MouseMoveEvent
        Player1.Cursor = Cursors.Default
    End Sub

    Private Sub FVideoRecorderEditor_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        SetCropPos()
    End Sub

#End Region

#Region " Trim "

    Private Sub Trim_MouseDown(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseDown, Trim_Right.MouseDown
        If Editing_Thread.IsAlive Then Exit Sub
        _isMoved = True
        _x = e.Location.X
        _y = e.Location.Y
    End Sub

    Private Sub Trim_MouseMove(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseMove, Trim_Right.MouseMove
        If _isMoved Then
            Select Case sender.Name
                Case "Trim_Left"
                    Trim_Left.Location = New Point(Math.Max(Math.Min(Trim_Left.Location.X + (e.Location.X - _x), Trim_Right.Left - Trim_Left.Width), 26), Trim_Left.Top)
                Case "Trim_Right"
                    Trim_Right.Location = New Point(Math.Max(Math.Min(Trim_Right.Location.X + (e.Location.X - _x), (Progreso.Left + Progreso.Width) - 3), Trim_Left.Left + Trim_Left.Width), Trim_Right.Top)
            End Select
        End If
    End Sub

    Private Sub Trim_MouseUp(sender As Object, e As MouseEventArgs) Handles Trim_Left.MouseUp, Trim_Right.MouseUp
        _isMoved = False
        VLeft = ((((Trim_Left.Left - 26) * 100) / Progreso.Width) * Progreso.Maximum) / 100
        VRight = ((((Trim_Right.Left + 3 - 26) * 100) / Progreso.Width) * Progreso.Maximum) / 100

        If sender.name = "Trim_Left" Then Progreso.Value = VLeft Else If Progreso.Value > VRight Then Progreso.Value = VRight
        If Player1.playState.ToString = "wmppsPaused" Then
            Player1.Ctlcontrols.play()
            Player1.Ctlcontrols.currentPosition = Progreso.Value / 1000
            Player1.Ctlcontrols.pause()
        Else
            Player1.Ctlcontrols.currentPosition = Progreso.Value / 1000
        End If
    End Sub

#End Region

#End Region

#End Region

#Region " Metodos "

#Region " Player "

    Private Sub Player_PlayStateChange(sender As Object, e As AxWMPLib._WMPOCXEvents_PlayStateChangeEvent) Handles Player1.PlayStateChange
        Try : If Player1.playState.ToString = "wmppsMediaEnded" Then Progreso.Value = Progreso.Maximum : Exit Sub
            If Player1.playState.ToString = "wmppsTransitioning" Or Player1.playState.ToString = "wmppsReady" Then Exit Sub
            If Player1.playState.ToString = "wmppsStopped" Then Started = False : Player1.Ctlcontrols.play() : Exit Sub

            If Player1.currentMedia.duration <> 0 And Started = False Then
                VideoTimer.Enabled = False : Started = True : If Progreso.Value > Player1.currentMedia.duration * 1000 Then Progreso.Value = 0 : Progreso.Maximum = Player1.currentMedia.duration * 1000 : Progreso.Value = Progreso.Maximum Else Progreso.Maximum = Player1.currentMedia.duration * 1000
                VLeft = ((((Trim_Left.Left - 26) * 100) / Progreso.Width) * Progreso.Maximum) / 100
                VRight = ((((Trim_Right.Left + 3 - 26) * 100) / Progreso.Width) * Progreso.Maximum) / 100

                If PlayerPosition > 0 Then
                    Player1.Ctlcontrols.currentPosition = PlayerPosition : Progreso.Value = Player1.Ctlcontrols.currentPosition * 1000 : PlayerPosition = -1
                    If PlayerState = "wmppsPlaying" Then Player1.Ctlcontrols.play()
                    If PlayerState = "wmppsPaused" Then Player1.Ctlcontrols.pause()
                Else
                    Player1.Ctlcontrols.pause() : Player1.Ctlcontrols.currentPosition = 0 : Progreso.Value = 0
                End If : VideoTimer.Enabled = True
                If Player1.Tag = 0 And Opacity <> 1 Then
                    Player1.Visible = False : For Cuenta = 5 To 100 Step 5
                        If Cuenta = 30 Then Player1.Visible = True : Opacity = 1 : Exit For
                        Opacity = Cuenta / 100 : Refresh() : Application.DoEvents() : Threading.Thread.Sleep(10)
                    Next : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
                End If
            End If
            If Player1.playState.ToString = "wmppsPlaying" And BPlay.Text = "4" Then BPlay.Text = ";"
            If Player1.playState.ToString = "wmppsPaused" And BPlay.Text = ";" Then BPlay.Text = "4"
            If Player1.playState.ToString = "wmppsStopped" Then Started = False : Player1.Ctlcontrols.play()
        Catch : End Try
    End Sub

    Private Sub Player_Click(sender As Object, e As AxWMPLib._WMPOCXEvents_ClickEvent) Handles Player1.ClickEvent
        'HTexto.Focus()
    End Sub

#End Region

#Region " Timer "

    Private Sub VideoTimer_Tick(sender As Object, e As EventArgs)
        If MouseDowned = True Then Exit Sub
        If Player1.playState.ToString <> "wmppsPlaying" And Player1.playState.ToString = "wmppsPaused" Then Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 < VLeft Then Progreso.Value = VLeft : Player1.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 > VRight Then Progreso.Value = VRight : Player1.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Application.DoEvents() : Threading.Thread.Sleep(100) : Progreso.Value = VLeft : Player1.Ctlcontrols.currentPosition = Progreso.Value / 1000 : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 > Progreso.Maximum - 200 Then Progreso.Value = Progreso.Maximum : Exit Sub
        If Player1.Ctlcontrols.currentPosition * 1000 < 500 Then Progreso.Value = 0 : Exit Sub
        Progreso.Value = Player1.Ctlcontrols.currentPosition * 1000
    End Sub

#End Region

#Region " Progreso "

    Private Sub Progreso_MouseDown(sender As Object, e As MouseEventArgs) Handles Progreso.MouseDown
        Dim cProgreso As NSProgressBar = CType(sender, NSProgressBar)
        MouseDowned = True : ChangeProgress(cProgreso, e)
    End Sub

    Private Sub Progreso_MouseUp(sender As Object, e As MouseEventArgs) Handles Progreso.MouseUp
        Dim cProgreso As NSProgressBar = CType(sender, NSProgressBar)
        ChangeProgress(cProgreso, e) : MouseDowned = False
    End Sub

    Private Sub ChangeProgress(cProgreso As NSProgressBar, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Or Editing_Thread.IsAlive Then Exit Sub
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

    Private Sub BPlay_Click(sender As Object, e As EventArgs) Handles BPlay.Click
        If Editing_Thread.IsAlive Then Exit Sub
        If Player1.playState.ToString = "wmppsPlaying" Then Player1.Ctlcontrols.pause() Else If Player1.playState.ToString = "wmppsPaused" Then Player1.Ctlcontrols.play()
    End Sub

#End Region

#Region " Aceptar "

    Private Sub PAceptar_Click(sender As Object, e As MouseEventArgs) Handles PAceptar.Click
        If e.Button <> MouseButtons.Left Or Editing_Thread.IsAlive Then Exit Sub
        Dim Trim As String = "" : Dim Crop As String = ""

        If Trim_Left.Left > 26 Or Trim_Right.Left < (Progreso.Left + Progreso.Width) - 4 Then
            Dim cStart As Decimal = ((((VLeft * 100) / Progreso.Maximum) * (Player1.currentMedia.duration * 1000)) / 100) / 1000
            Dim cDuration As Decimal = ((Player1.currentMedia.duration * 1000) - (((((Progreso.Maximum - (VRight - VLeft)) * 100) / Progreso.Maximum) * (Player1.currentMedia.duration * 1000)) / 100)) / 1000
            If cDuration < 1 Then cDuration = 1
            If cStart + cDuration > Player1.currentMedia.duration Then cStart = Player1.currentMedia.duration - cDuration
            If cStart + cDuration > Player1.currentMedia.duration Or Player1.currentMedia.duration <= 1 Then Trim = "" Else Trim = " -ss " + cStart.ToString.Replace(",", ".") + " -t " + cDuration.ToString.Replace(",", ".") + " "
        End If

        If SCropLeft.Left > 0 Or SCropTop.Top > 0 Or SCropRight.Left < 966 Or SCropBottom.Top < 550 Then
            Dim cLeft As Integer = SCropLeft.Left
            Dim cTop As Integer = SCropTop.Top - 2
            Dim cWidth As Integer = (SCropRight.Left - SCropLeft.Left) + 3
            Dim cHeight As Integer = (SCropBottom.Top - SCropTop.Top) + 3

            Dim Percent_Left As Integer = Math.Max((cLeft * 100) / Player1.Width, 0)
            Dim Percent_Top As Integer = Math.Max((cTop * 100) / (Player1.Height - 55), 0)
            Dim Percent_Width As Integer = Math.Min((cWidth * 100) / Player1.Width, 100)
            Dim Percent_Height As Integer = Math.Min((cHeight * 100) / (Player1.Height - 55), 100)

            Dim New_Left As Integer = (Percent_Left * VideoWidth) / 100
            Dim New_Top As Integer = (Percent_Top * VideoHeight) / 100
            Dim New_Width As Integer = (Percent_Width * VideoWidth) / 100
            Dim New_Height As Integer = (Percent_Height * VideoHeight) / 100

            If New_Left + New_Width > VideoWidth Then New_Width = VideoWidth - New_Left
            If New_Top + New_Height > VideoHeight Then New_Height = VideoHeight - New_Top

            If New_Left Mod 2 <> 0 Then New_Left += 1
            : If New_Top Mod 2 <> 0 Then New_Top += 1
            If New_Width Mod 2 <> 0 Then New_Width += 1
            If New_Height Mod 2 <> 0 Then New_Height += 1

            Crop = "-filter:v ""crop=" + New_Width.ToString + ":" + New_Height.ToString + ":" + New_Left.ToString + ":" + New_Top.ToString + """"

            If New_Left < 10 AndAlso New_Top < 10 AndAlso New_Width > VideoWidth - 10 AndAlso New_Height > VideoHeight - 10 Then Crop = ""
        End If
        If Trim = "" And Crop = "" Then Close() : Exit Sub
        'BAceptar.State = NSButton.MouseState.None : BAceptar.Invalidate() : Application.DoEvents()
        EditingPanel.Visible = True : EditingLabel.Visible = True : BPlay.Enabled = False : Progreso.Enabled = False : Trim_Left.Enabled = False : Trim_Right.Enabled = False
        SCropLeft.Visible = False : SCropRight.Visible = False : SCropTop.Visible = False : SCropBottom.Visible = False
        PCropLeft.Visible = False : PCropRight.Visible = False : PCropTop.Visible = False : PCropBottom.Visible = False
        FVideoRecorder.EditingLabel.Left = (FVideoRecorder.EditingPanel.Left + (FVideoRecorder.EditingPanel.Width / 2) - (FVideoRecorder.EditingLabel.Width / 2))
        FVideoRecorder.EditingLabel.Top = (FVideoRecorder.EditingPanel.Top + (FVideoRecorder.EditingPanel.Height / 2) - (FVideoRecorder.EditingLabel.Height))
        FVideoRecorder.EditingPanel.Visible = True : FVideoRecorder.EditingLabel.Visible = True
        FVideoRecorder.Player1.close() : Player1.close()
        Editing_Thread.SetApartmentState(Threading.ApartmentState.STA) : Editing_Thread.IsBackground = True : Editing_Thread.Start(Trim + "|" + Crop)
        Do Until Editing_Thread.ThreadState = Threading.ThreadState.Aborted Or Editing_Thread.ThreadState = Threading.ThreadState.Stopped : Application.DoEvents() : Loop
        Cancelado = ForceCancel : Close()
    End Sub

    Private Sub Editing_Thread_Sub(Data As String)
        Try : Dim Trim As String = Data.Split("|")(0) : Dim Crop As String = Data.Split("|")(1)
            StartEditing(FMenu.KobaPathTemp + "VideoRecording.mp4", Trim, Crop)
        Catch : End Try
    End Sub

    Private Sub StartEditing(cVideoPath As String, Trim As String, Crop As String)
        Try : If IO.File.Exists(cVideoPath) = True Then
                IO.Directory.CreateDirectory(FMenu.KobaPathTemp) : If IO.File.Exists(FMenu.KobaPathTemp + "ffmpeg.exe") = False Then IO.File.WriteAllBytes(FMenu.KobaPathTemp + "ffmpeg.exe", My.Resources.ffmpeg)
                Dim Proceso As New Process : Proceso.StartInfo.FileName = FMenu.KobaPathTemp + "ffmpeg.exe"
                Proceso.StartInfo.UseShellExecute = False : Proceso.StartInfo.WindowStyle = ProcessWindowStyle.Hidden : Proceso.StartInfo.RedirectStandardInput = True : Proceso.StartInfo.CreateNoWindow = True
                If IO.File.Exists(FMenu.KobaPathTemp + "EditVideo.mp4") = True Then IO.File.Delete(FMenu.KobaPathTemp + "EditVideo.mp4")
                If Crop <> "" Then
                    Proceso.StartInfo.Arguments = "-i " + cVideoPath + Trim + " -vcodec libx264 -r 30 " + Crop + " -crf 20 -preset fast -acodec copy " + FMenu.KobaPathTemp + "EditVideo.mp4"
                ElseIf Trim <> "" Then
                    Proceso.StartInfo.Arguments = "-i " + cVideoPath + Trim + " -acodec copy -vcodec copy " + FMenu.KobaPathTemp + "EditVideo.mp4"
                Else
                    ForceCancel = True : Exit Sub
                End If
                Proceso.Start() : Proceso.WaitForExit() : IO.File.Copy(FMenu.KobaPathTemp + "EditVideo.mp4", cVideoPath, True)
            End If
        Catch ex As Exception : MsgBox(ex.Message) : ForceCancel = True : End Try
    End Sub

    Private Sub PAceptar_MouseEnter(sender As Object, e As EventArgs) Handles PAceptar.MouseEnter
        PAceptar.Image = My.Resources.Aceptar_On
    End Sub

    Private Sub PAceptar_MouseLeave(sender As Object, e As EventArgs) Handles PAceptar.MouseLeave
        PAceptar.Image = My.Resources.Aceptar_Off
    End Sub

    Private Sub GrabacionPantallaEditor_LHelp_Click(sender As Object, e As EventArgs) Handles GrabacionPantallaEditor_LHelp.DoubleClick, GrabacionPantallaEditor_LHelp.Click

    End Sub

    Private Sub PAceptar_Click(sender As Object, e As EventArgs) Handles PAceptar.Click

    End Sub

#End Region

#Region " Cancelar "

    Private Sub PCerrar_Click(sender As Object, e As MouseEventArgs) Handles PCerrar.Click
        If e.Button <> MouseButtons.Left Or Editing_Thread.IsAlive Then Exit Sub Else Close()
    End Sub

    Private Sub PCerrar_MouseEnter(sender As Object, e As EventArgs) Handles PCerrar.MouseEnter
        PCerrar.Image = My.Resources.Cerrar_On
    End Sub

    Private Sub PCerrar_MouseLeave(sender As Object, e As EventArgs) Handles PCerrar.MouseLeave
        PCerrar.Image = My.Resources.Cerrar_Off
    End Sub

#End Region

#Region " Help "

    Private Sub GrabacionPantallaEditor_LHelp_Click(sender As Object, e As MouseEventArgs) Handles GrabacionPantallaEditor_LHelp.Click, GrabacionPantallaEditor_LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Grabar Pantalla" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub GrabacionPantallaEditor_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles GrabacionPantallaEditor_LHelp.MouseEnter
        GrabacionPantallaEditor_LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub GrabacionPantallaEditor_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles GrabacionPantallaEditor_LHelp.MouseLeave
        GrabacionPantallaEditor_LHelp.ForeColor1 = Color.FromArgb(85, 85, 85)
    End Sub

#End Region

#End Region

#Region " Tooltips "

    Private Sub EditarVideo_Tooltips(sender As Object, e As EventArgs) Handles PAceptar.MouseEnter, PCerrar.MouseEnter
        Select Case sender.Name
            Case "PAceptar" : FMenu.Show_Tooltip(sender, "Aplica los cambios y cierra la ventana")
            Case "PCerrar" : FMenu.Show_Tooltip(sender, "Cierra la ventana")
        End Select
    End Sub

#End Region

End Class

#End Region