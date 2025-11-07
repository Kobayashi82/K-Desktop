
#Region " Capturar Pantalla "

Public Class FCapturarPantalla

#Region " Variables "

    Dim WithEvents ColorSelector As New ColorSelectorCL
    Dim CerrarColorSelector, isFocused As Boolean

    Dim EditorProgram As String
    Dim GImagenes(Screen.AllScreens.Length - 1) As NSGroupBox
    Dim Imagenes(Screen.AllScreens.Length - 1) As PictureBox
    Dim Capturas(Screen.AllScreens.Length - 1) As Image
    Dim SelStart(Screen.AllScreens.Length - 1) As Point
    Dim SelStop(Screen.AllScreens.Length - 1) As Point
    Dim SelNow(Screen.AllScreens.Length - 1) As Point
    Dim Selecting(Screen.AllScreens.Length - 1) As Boolean
    Dim ShowSel(Screen.AllScreens.Length - 1) As Boolean
    Dim RecortePosition(Screen.AllScreens.Length - 1) As Point
    Dim RecorteSize(Screen.AllScreens.Length - 1) As Size
    Dim isDrawing As Boolean : Dim DrawColor As String
    Dim MousePath As New Drawing2D.GraphicsPath()
    Public Sonido As String

    Dim ImageWidth, ImageHeight, NewImageWidth, NewImageHeight As Integer

    ReadOnly UndoStack(Screen.AllScreens.Length - 1) As Stack(Of Bitmap)
    ReadOnly RedoStack(Screen.AllScreens.Length - 1) As Stack(Of Bitmap)

#End Region

#Region " Eventos "

#Region " Formulario "

#Region " Load "

    Private Sub FCaptura_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : TNombre.ReducedFont = True
    End Sub

    Private Sub FCaptura_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim NoImagen As Boolean
        Dim CTab As Integer = TabControl.SelectedIndex
        Opacity = 0 : TabControl.TabPages.Clear()
        For Cuenta = 0 To Screen.AllScreens.Count - 1
            NoImagen = False
            If IO.File.Exists(FMenu.KobaPathTemp + "Captura_" + Cuenta.ToString + ".png") = False Then My.Resources.NoImage.Save(FMenu.KobaPathTemp + "Captura_" + Cuenta.ToString + ".png", Imaging.ImageFormat.Png) : NoImagen = True
            UndoStack(Cuenta) = New Stack(Of Bitmap)
            RedoStack(Cuenta) = New Stack(Of Bitmap)
            TabControl.TabPages.Add("") : GImagenes(Cuenta) = New NSGroupBox : Imagenes(Cuenta) = New PictureBox : TabControl.TabPages(Cuenta).Tag = NoImagen
            TabControl.TabPages(Cuenta).Controls.Add(GImagenes(Cuenta)) : GImagenes(Cuenta).Controls.Add(Imagenes(Cuenta))
            GImagenes(Cuenta).Location = New Point(2, 0) : GImagenes(Cuenta).Size = New Size(TabControl.Size.Width - 26, TabControl.Height - 8) : GImagenes(Cuenta).Anchor = 15
            Imagenes(Cuenta).Location = New Point(3, 2) : Imagenes(Cuenta).Size = New Size(GImagenes(Cuenta).Size.Width - 6, GImagenes(Cuenta).Height - 5) : Imagenes(Cuenta).Anchor = 15 : Imagenes(Cuenta).SizeMode = PictureBoxSizeMode.StretchImage : Imagenes(Cuenta).AllowDrop = True
            AddHandler Imagenes(Cuenta).DoubleClick, AddressOf Imagen_DoubleClick
            AddHandler Imagenes(Cuenta).MouseUp, AddressOf Imagen_MouseUp
            AddHandler Imagenes(Cuenta).MouseDown, AddressOf Imagen_MouseDown
            AddHandler Imagenes(Cuenta).MouseMove, AddressOf Imagen_MouseMove
            AddHandler Imagenes(Cuenta).Paint, AddressOf Imagen_Paint
            AddHandler Imagenes(Cuenta).DragEnter, AddressOf Imagen_DragEnter
            AddHandler Imagenes(Cuenta).DragDrop, AddressOf Imagen_DragDrop

            Capturas(Cuenta) = Imagen.LoadImageClone(FMenu.KobaPathTemp + "Captura_" + Cuenta.ToString + ".png") : Imagenes(Cuenta).Image = Imagen.LoadImageClone(FMenu.KobaPathTemp + "Captura_" + Cuenta.ToString + ".png")
        Next
        If CTab <> -1 And CTab < TabControl.TabPages.Count Then TabControl.SelectedIndex = CTab
        If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False Else PTelegram.Enabled = True

        ImageDimension()
        Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
        If Width < 460 Then
            NewSizeVideo(New Size(ImageWidth, ImageHeight), 460 - 54, 0)
            Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
        End If
        MinimumSize = New Size(460, 300)
        Location = New Point((Screen.PrimaryScreen.WorkingArea.Width - Width) / 2, (Screen.PrimaryScreen.WorkingArea.Height - Height) / 2)

        Dim cFileSize As String = FileSize(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png") : Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
        Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString

        Dim Reg = My.Computer.Registry.ClassesRoot.CreateSubKey("SystemFileAssociations\image\shell\edit\command", True) : EditorProgram = Reg.GetValue("") : Reg.Close()
        If EditorProgram = "" Then
            Reg = My.Computer.Registry.ClassesRoot.CreateSubKey("SystemFileAssociations\image\shell\edit\command", True) : Reg.SetValue("", """" + "MSPaint"" ""%1""") : Reg.Close()
            EditorProgram = "MSPaint"
        Else
            If EditorProgram.Contains("""") = True Then EditorProgram = EditorProgram.ToString.Split("""")(1)
        End If
        TNombre.Focus() : HTexto.Focus() : ShowControls(Not TabControl.TabPages(TabControl.SelectedIndex).Tag)
        Application.DoEvents() : ShowInTaskbar = False : ShowInTaskbar = True
        Sonidos.Play(Sonido, "CapturarPantalla")
        TopMost = False : FMenu.FocusMe(Me) : If Visible = True Then Opacity = 1
        FMenu.FCapturaPantalla_Opening = False
        LWitdh.Value2 = Imagenes(TabControl.SelectedIndex).Image.Width.ToString : LWitdh.Invalidate()
        LHeight.Value2 = Imagenes(TabControl.SelectedIndex).Image.Height.ToString : LHeight.Invalidate()
    End Sub

    Private OldSize As Size

    Private Sub FCapturarPantalla_ResizeBegin(sender As Object, e As EventArgs) Handles Me.ResizeBegin
        OldSize = Imagenes(TabControl.SelectedIndex).Size
    End Sub

    Private Sub FCapturarPantalla_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        If Visible = False Then Exit Sub
        For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
            SLabel.EscalarTexto(OldSize)
        Next
        OldSize = Imagenes(TabControl.SelectedIndex).Size
    End Sub

    Private Sub FCaptura_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        Application.DoEvents()
    End Sub

    Private Sub Imagen_DoubleClick(sender As Object, e As EventArgs)
        OldSize = Imagenes(TabControl.SelectedIndex).Size
        If WindowState = FormWindowState.Normal Then WindowState = FormWindowState.Maximized Else WindowState = FormWindowState.Normal
        For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
            SLabel.EscalarTexto(OldSize)
        Next
    End Sub
    Private Sub BMaximizar_MouseDown(sender As Object, e As MouseEventArgs) Handles BMaximizar.MouseDown
        OldSize = Imagenes(TabControl.SelectedIndex).Size
    End Sub

    Private Sub BMaximizar_MouseClick(sender As Object, e As MouseEventArgs) Handles BMaximizar.MouseClick
        For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
            SLabel.EscalarTexto(OldSize)
        Next
    End Sub

#End Region

#Region " KeyPress "

    Private Sub FCaptura_KeyUp(sender As Object, e As KeyEventArgs) Handles Me.KeyUp
        If e.KeyCode = Keys.Delete Then : e.Handled = True
            For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
                If SLabel.isSelected = True Then SLabel.Eliminar() : Exit For
            Next
        End If
        If ModifierKeys = Keys.Control And e.KeyCode = Keys.Z Then PUndo_Click(sender, Nothing)
        If ModifierKeys = Keys.Control And e.KeyCode = Keys.Y Then PRedo_Click(sender, Nothing)
    End Sub

    Private Sub FCaptura_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then : e.Handled = True
            If isFocused = False Then Close() Else HTexto.Focus()
        End If
    End Sub

#End Region

#Region " Controles "

    Private Sub TabControl_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabControl.SelectedIndexChanged
        UpdateSize() : ShowControls(Not TabControl.TabPages(TabControl.SelectedIndex).Tag)
    End Sub

    Private Sub Controles_Click(sender As Object, e As MouseEventArgs) Handles Me.Click, FTheme.Click, BMaximizar.Click, BColor.Click, BCerrar.Click, PTelegram.Click, PCopiar.Click, PGuardar.Click, PCerrar.Click, LTamaño.Click, Tamaño.Click, LWitdh.Click, Lpor.Click, LHeight.Click, PTexto.Click
        HTexto.Focus()
        If sender.Name = "BColor" Then
            For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
                If SLabel.isSelected = True Then Exit Sub
            Next
        End If
        If Imagenes(TabControl.SelectedIndex).Controls.Count > 0 Then CType(Imagenes(TabControl.SelectedIndex).Controls(0), SelectableLabel).DeseleccionarTodo()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        e.Handled = True
    End Sub

#End Region

#End Region

#Region " Funciones "

#Region " Recortar / Dibujar "

    Public Sub Recortar()
        'For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
        '    SLabel.TempLocation.X = SLabel.Left - RecortePosition(TabControl.SelectedIndex).X
        '    SLabel.TempLocation.Y = SLabel.Top - RecortePosition(TabControl.SelectedIndex).Y
        'Next

        Dim bm As New Bitmap(RecorteSize(TabControl.SelectedIndex).Width, RecorteSize(TabControl.SelectedIndex).Height)
        Dim G As Graphics = Graphics.FromImage(bm) : G.DrawImage(Imagenes(TabControl.SelectedIndex).Image, New Rectangle(0, 0, RecorteSize(TabControl.SelectedIndex).Width, RecorteSize(TabControl.SelectedIndex).Height), New Rectangle(RecortePosition(TabControl.SelectedIndex).X, RecortePosition(TabControl.SelectedIndex).Y, RecorteSize(TabControl.SelectedIndex).Width, RecorteSize(TabControl.SelectedIndex).Height), GraphicsUnit.Pixel)
        Imagenes(TabControl.SelectedIndex).Image = bm : Capturas(TabControl.SelectedIndex) = bm : MousePath.Reset()
        UpdateSize()
        'Application.DoEvents()
        'For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
        '    Dim WPercent As Decimal = (Imagenes(TabControl.SelectedIndex).Width * 100 / RecorteSize(TabControl.SelectedIndex).Width) - 100
        '    Dim HPercent As Decimal = (Imagenes(TabControl.SelectedIndex).Height * 100 / RecorteSize(TabControl.SelectedIndex).Height) - 100

        '    'Dim WPercent As Decimal = (RecorteSize(TabControl.SelectedIndex).Width * 100 / Imagenes(TabControl.SelectedIndex).Width) - 100
        '    'Dim HPercent As Decimal = (RecorteSize(TabControl.SelectedIndex).Height * 100 / Imagenes(TabControl.SelectedIndex).Height) - 100


        '    SLabel.Left = CInt(SLabel.TempLocation.X * (1 + WPercent / 100))
        '    SLabel.Top = CInt(SLabel.TempLocation.Y * (1 + HPercent / 100))
        '    SLabel.Invalidate()

        '    'SLabel.Left = SLabel.TempLocation.X
        '    'SLabel.Top = SLabel.TempLocation.Y
        'Next
    End Sub

    Private prevPoint As Point
    Private drawing As Bitmap

    Private Sub Imagen_MouseDown(sender As Object, e As MouseEventArgs)
        If Imagenes(TabControl.SelectedIndex).Controls.Count > 0 Then CType(Imagenes(TabControl.SelectedIndex).Controls(0), SelectableLabel).DeseleccionarTodo()
        HTexto.Focus()
        If TabControl.TabPages(TabControl.SelectedIndex).Tag = True Then Exit Sub
        drawing = New Bitmap(Imagenes(TabControl.SelectedIndex).Image.Width, Imagenes(TabControl.SelectedIndex).Image.Height)
        drawing.MakeTransparent()
        If e.Button = MouseButtons.Left Then
            If Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Hand Then
                UndoStack(TabControl.SelectedIndex).Push(Imagenes(TabControl.SelectedIndex).Image.Clone) : RedoStack(TabControl.SelectedIndex).Clear()
                Recortar() : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
                OldSize = Imagenes(TabControl.SelectedIndex).Size
            Else
                prevPoint = e.Location
                UndoStack(TabControl.SelectedIndex).Push(Imagenes(TabControl.SelectedIndex).Image.Clone) : RedoStack(TabControl.SelectedIndex).Clear()
                isDrawing = True : MousePath.StartFigure()
                If SelStart(TabControl.SelectedIndex) = Nothing Then SelStart(TabControl.SelectedIndex) = New Point
                SelStart(TabControl.SelectedIndex).X = 10000 : SelStart(TabControl.SelectedIndex).Y = 10000
                SelNow(TabControl.SelectedIndex).X = 10000 : SelNow(TabControl.SelectedIndex).Y = 10000
                Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
            End If
        ElseIf e.Button = MouseButtons.Right Then
            isDrawing = False
            If Capturas(TabControl.SelectedIndex).Width < 200 And Capturas(TabControl.SelectedIndex).Height < 200 Then
                SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
            Else
                If Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Hand Then
                    'Recortar() : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
                    MousePath.Reset() : Selecting(TabControl.SelectedIndex) = False : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
                Else
                    Selecting(TabControl.SelectedIndex) = True
                    If Selecting(TabControl.SelectedIndex) Then
                        ShowSel(TabControl.SelectedIndex) = True : If SelStart(TabControl.SelectedIndex) = Nothing Then SelStart(TabControl.SelectedIndex) = New Point
                        SelStart(TabControl.SelectedIndex).X = e.X : SelStart(TabControl.SelectedIndex).Y = e.Y
                        SelNow(TabControl.SelectedIndex).X = e.X : SelNow(TabControl.SelectedIndex).Y = e.Y
                    End If
                End If
            End If : Imagenes(TabControl.SelectedIndex).Invalidate()
        End If
    End Sub

    Private Sub Imagen_MouseMove(sender As Object, e As MouseEventArgs)
        If SelStart(TabControl.SelectedIndex).X = -10000 Then Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default : Exit Sub
        If isDrawing = True Then
            Dim aX As Integer = (Imagenes(TabControl.SelectedIndex).Image.Width / Imagenes(TabControl.SelectedIndex).Width) * prevPoint.X
            Dim aY As Integer = (Imagenes(TabControl.SelectedIndex).Image.Height / Imagenes(TabControl.SelectedIndex).Height) * prevPoint.Y

            Dim cX As Integer = (Imagenes(TabControl.SelectedIndex).Image.Width / Imagenes(TabControl.SelectedIndex).Width) * e.X
            Dim cY As Integer = (Imagenes(TabControl.SelectedIndex).Image.Height / Imagenes(TabControl.SelectedIndex).Height) * e.Y
            MousePath.AddLine(cX, cY, cX, cY) ': Imagenes(TabControl.SelectedIndex).Invalidate()

            Using g As Graphics = Imagenes(TabControl.SelectedIndex).CreateGraphics()
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.DrawLine(New Pen(Color.FromArgb(255, BColor.BackColor), 1), prevPoint, e.Location)
            End Using
            'Using gc As Graphics = Graphics.FromImage(drawing)
            'gc.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            'gc.DrawLine(New Pen(Color.FromArgb(255, BColor.BackColor), 3), New Point(aX, aY), New Point(cX, cY))
            'End Using

            prevPoint = e.Location
        Else
            If Selecting(TabControl.SelectedIndex) = True Then
                If SelNow(TabControl.SelectedIndex) = Nothing Then SelNow(TabControl.SelectedIndex) = New Point
                If e.X > Imagenes(TabControl.SelectedIndex).Width Then SelNow(TabControl.SelectedIndex).X = Imagenes(TabControl.SelectedIndex).Width - 2 Else SelNow(TabControl.SelectedIndex).X = e.X
                If e.Y > Imagenes(TabControl.SelectedIndex).Height Then SelNow(TabControl.SelectedIndex).Y = Imagenes(TabControl.SelectedIndex).Height - 2 Else SelNow(TabControl.SelectedIndex).Y = e.Y
                Imagenes(TabControl.SelectedIndex).Invalidate()
            ElseIf e.X > SelStart(TabControl.SelectedIndex).X And e.X < SelStop(TabControl.SelectedIndex).X And e.Y > SelStart(TabControl.SelectedIndex).Y And e.Y < SelStop(TabControl.SelectedIndex).Y Then
                Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Hand
            Else
                Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
            End If
        End If
    End Sub

    Private Sub Imagen_MouseUp(sender As Object, e As MouseEventArgs)
        isDrawing = False : If e.Button = MouseButtons.Left Then
            Using g As Graphics = Graphics.FromImage(drawing)
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.DrawPath(New Pen(Color.FromArgb(255, BColor.BackColor), 1), MousePath)
            End Using

            MousePath.Reset() : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default

            Dim bmp3 As New Bitmap(Imagenes(TabControl.SelectedIndex).Image.Width, Imagenes(TabControl.SelectedIndex).Image.Height)
            Using g As Graphics = Graphics.FromImage(bmp3)
                g.DrawImage(Imagenes(TabControl.SelectedIndex).Image, New Point(0, 0))
                g.DrawImage(drawing, New Point(0, 0))
            End Using
            Imagenes(TabControl.SelectedIndex).Image = bmp3.Clone : Application.DoEvents()
            bmp3.Dispose()


            Imagenes(TabControl.SelectedIndex).Image.Save(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png", Imaging.ImageFormat.Png)
            Dim cFileSize As String = FileSize(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png") : Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
            Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString
        ElseIf e.Button = MouseButtons.Right Then
            If Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Hand Or SelStart(TabControl.SelectedIndex).X = -10000 Then Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default : Exit Sub
            Selecting(TabControl.SelectedIndex) = False : If Not Selecting(TabControl.SelectedIndex) Then
                Dim Temp As Integer : If SelStop(TabControl.SelectedIndex) = Nothing Then SelStop(TabControl.SelectedIndex) = New Point
                SelStop(TabControl.SelectedIndex).X = e.X : SelStop(TabControl.SelectedIndex).Y = e.Y
                If SelStop(TabControl.SelectedIndex).X < SelStart(TabControl.SelectedIndex).X Then Temp = SelStart(TabControl.SelectedIndex).X : SelStart(TabControl.SelectedIndex).X = SelStop(TabControl.SelectedIndex).X : SelStop(TabControl.SelectedIndex).X = Temp
                If SelStop(TabControl.SelectedIndex).Y < SelStart(TabControl.SelectedIndex).Y Then Temp = SelStart(TabControl.SelectedIndex).Y : SelStart(TabControl.SelectedIndex).Y = SelStop(TabControl.SelectedIndex).Y : SelStop(TabControl.SelectedIndex).Y = Temp
                If SelStart(TabControl.SelectedIndex).X < 0 Then SelStart(TabControl.SelectedIndex).X = 2
                If SelStart(TabControl.SelectedIndex).Y < 0 Then SelStart(TabControl.SelectedIndex).Y = 2
                If SelStop(TabControl.SelectedIndex).X > Imagenes(TabControl.SelectedIndex).Width Then SelStop(TabControl.SelectedIndex).X = Imagenes(TabControl.SelectedIndex).Width - 2
                If SelStop(TabControl.SelectedIndex).Y > Imagenes(TabControl.SelectedIndex).Height Then SelStop(TabControl.SelectedIndex).Y = Imagenes(TabControl.SelectedIndex).Height - 2
                Dim PercentX As Double = (Imagenes(TabControl.SelectedIndex).Width * 100) / Capturas(TabControl.SelectedIndex).Width
                Dim PercentY As Double = (Imagenes(TabControl.SelectedIndex).Height * 100) / Capturas(TabControl.SelectedIndex).Height
                RecortePosition(TabControl.SelectedIndex).X = (100 * SelStart(TabControl.SelectedIndex).X) / PercentX
                RecortePosition(TabControl.SelectedIndex).Y = (100 * SelStart(TabControl.SelectedIndex).Y) / PercentY
                RecorteSize(TabControl.SelectedIndex).Width = (100 * (SelStop(TabControl.SelectedIndex).X - SelStart(TabControl.SelectedIndex).X)) / PercentX
                RecorteSize(TabControl.SelectedIndex).Height = (100 * (SelStop(TabControl.SelectedIndex).Y - SelStart(TabControl.SelectedIndex).Y)) / PercentY
                Imagenes(TabControl.SelectedIndex).Invalidate()
            End If
        End If
    End Sub

    Private Sub Imagen_Paint(sender As Object, e As PaintEventArgs)
        If isDrawing = True Then
            Dim g As Graphics = Graphics.FromImage(Imagenes(TabControl.SelectedIndex).Image) : g.DrawPath(New Pen(Color.FromArgb(255, BColor.BackColor), 1), MousePath) ' Alpha, Color, Width
        Else
            If ShowSel(TabControl.SelectedIndex) = False Then Exit Sub
            Dim _rRectangle As New Rectangle : Dim _penNew As New Pen(Color.Red, 1)
            If Selecting(TabControl.SelectedIndex) Then
                If SelNow(TabControl.SelectedIndex).X < SelStart(TabControl.SelectedIndex).X Then
                    If SelNow(TabControl.SelectedIndex).X < 0 Then _rRectangle.X = 2 Else _rRectangle.X = SelNow(TabControl.SelectedIndex).X
                    _rRectangle.Width = SelStart(TabControl.SelectedIndex).X - _rRectangle.X
                Else
                    If SelStart(TabControl.SelectedIndex).X < 0 Then _rRectangle.X = 2 Else _rRectangle.X = SelStart(TabControl.SelectedIndex).X
                    _rRectangle.Width = SelNow(TabControl.SelectedIndex).X - _rRectangle.X
                End If
                If SelNow(TabControl.SelectedIndex).Y < SelStart(TabControl.SelectedIndex).Y Then
                    If SelNow(TabControl.SelectedIndex).Y < 0 Then _rRectangle.Y = 2 Else _rRectangle.Y = SelNow(TabControl.SelectedIndex).Y
                    _rRectangle.Height = SelStart(TabControl.SelectedIndex).Y - _rRectangle.Y
                Else
                    If SelStart(TabControl.SelectedIndex).Y < 0 Then _rRectangle.Y = 2 Else _rRectangle.Y = SelStart(TabControl.SelectedIndex).Y
                    _rRectangle.Height = SelNow(TabControl.SelectedIndex).Y - _rRectangle.Y
                End If
            Else
                If SelStart(TabControl.SelectedIndex).X <> -10000 Then
                    Dim PercentX As Double = (Imagenes(TabControl.SelectedIndex).Width * 100) / Capturas(TabControl.SelectedIndex).Width
                    Dim PercentY As Double = (Imagenes(TabControl.SelectedIndex).Height * 100) / Capturas(TabControl.SelectedIndex).Height
                    _rRectangle.X = (PercentX * RecortePosition(TabControl.SelectedIndex).X) / 100
                    _rRectangle.Y = (PercentY * RecortePosition(TabControl.SelectedIndex).Y) / 100
                    _rRectangle.Width = (PercentX * RecorteSize(TabControl.SelectedIndex).Width) / 100
                    _rRectangle.Height = (PercentY * RecorteSize(TabControl.SelectedIndex).Height) / 100
                    SelStart(TabControl.SelectedIndex).X = (PercentX * RecortePosition(TabControl.SelectedIndex).X) / 100
                    SelStart(TabControl.SelectedIndex).Y = (PercentY * RecortePosition(TabControl.SelectedIndex).Y) / 100
                    If SelStart(TabControl.SelectedIndex).X + ((PercentX * RecorteSize(TabControl.SelectedIndex).Width) / 100) > Imagenes(TabControl.SelectedIndex).Width Then SelStop(TabControl.SelectedIndex).X = Imagenes(TabControl.SelectedIndex).Width - 2 Else SelStop(TabControl.SelectedIndex).X = SelStart(TabControl.SelectedIndex).X + ((PercentX * RecorteSize(TabControl.SelectedIndex).Width) / 100)
                    If SelStart(TabControl.SelectedIndex).Y + ((PercentY * RecorteSize(TabControl.SelectedIndex).Height) / 100) > Imagenes(TabControl.SelectedIndex).Height Then SelStop(TabControl.SelectedIndex).Y = Imagenes(TabControl.SelectedIndex).Height - 2 Else SelStop(TabControl.SelectedIndex).Y = SelStart(TabControl.SelectedIndex).Y + ((PercentY * RecorteSize(TabControl.SelectedIndex).Height) / 100)
                End If
            End If : _penNew.DashStyle = Drawing2D.DashStyle.Dash : e.Graphics.DrawRectangle(_penNew, _rRectangle)
        End If
    End Sub

#End Region

#Region " Save JPEG "

    Private Sub SaveJpeg(Path As String, Img As Image, Quality As Long)
        Try : Dim QualityParam As New Imaging.EncoderParameter(Imaging.Encoder.Quality, Quality)
            Dim jpegCodec As Imaging.ImageCodecInfo = GetEncoderInfo("image/jpeg")
            Dim EncoderParams As New Imaging.EncoderParameters(1)
            EncoderParams.Param(0) = QualityParam : Img.Save(Path, jpegCodec, EncoderParams)
        Catch : End Try
    End Sub

    Private Function GetEncoderInfo(MimeType As String) As Imaging.ImageCodecInfo
        Try : Dim Codecs As Imaging.ImageCodecInfo() = Imaging.ImageCodecInfo.GetImageEncoders()
            For i As Integer = 0 To Codecs.Length - 1
                If (Codecs(i).MimeType = MimeType) Then Return Codecs(i)
            Next i
        Catch : End Try : Return Nothing
    End Function

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

#Region " Update Size "

    'Private Sub UpdateSize()
    '    'Suspend the layout of the TabControl to improve performance
    '    TabControl.SuspendLayout()

    '    'Set the size mode of the current image to normal
    '    Imagenes(TabControl.SelectedIndex).SizeMode = PictureBoxSizeMode.Normal

    '    'Get the full size of the current image
    '    Dim fullSizeImg As Image = Imagenes(TabControl.SelectedIndex).Image
    '    ImageWidth = fullSizeImg.Width : ImageHeight = fullSizeImg.Height
    '    'Check if the image is wider or taller
    '    If ImageWidth > ImageHeight Then
    '        'Resize the image to a specific width
    '        NewSizeVideo(New Size(ImageWidth, ImageHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
    '    Else
    '        'Resize the image to a specific height
    '        NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
    '    End If
    '    'Set the minimum size of the form
    '    MinimumSize = New Size(400, 300)
    '    'Set the size of the form to the new calculated size
    '    Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
    '    'Check if the form's height is bigger than the screen's height
    '    If Height > Screen.PrimaryScreen.WorkingArea.Height Then
    '        'If it is, resize the image to a specific height
    '        NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
    '        'Set the size of the form to the new calculated size
    '        Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
    '    End If
    '    'Check if the form's width is smaller than 460
    '    If Width < 460 Then
    '        'If it is, resize the image to a specific width
    '        NewSizeVideo(New Size(ImageWidth, ImageHeight), 460 - 54, 0)
    '        'Set the size of the form to the new calculated size
    '        Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
    '    End If
    '    'Check if the form's height is bigger than the screen's height
    '    If Height > Screen.PrimaryScreen.WorkingArea.Height Then
    '        'If it is, set the form's height to the screen's height
    '        Height = Screen.PrimaryScreen.WorkingArea.Height
    '    End If
    '    'Center the form on the screen
    '    Location = New Point((Screen.PrimaryScreen.WorkingArea.Width - Width) / 2, (Screen.PrimaryScreen.WorkingArea.Height - Height) / 2)
    '    'Set the minimum size of the form
    '    MinimumSize = New Size(460, 300)
    '    'Set the size mode of the current image to stretch
    '    Imagenes(TabControl.SelectedIndex).SizeMode = PictureBoxSizeMode.StretchImage

    '    'Save the current image to a file
    '    Imagenes(TabControl.SelectedIndex).Image.Save(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png", Imaging.ImageFormat.Png)
    '    'Get the file size of the saved image
    '    Dim cFileSize As String = FileSize(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png")
    '    'Get the size of the file
    '    Dim cTamaño As String = cFileSize.Split(" ")(0)
    '    'Get the file type
    '    Dim cTipo As String = cFileSize.Split(" ")(1)
    '    'Set the size value in the form
    '    Tamaño.Value1 = cTamaño.ToString + " "
    '    'Set the file type in the form
    '    Tamaño.Value2 = cTipo.ToString

    '    'Resume the layout of the TabControl
    '    TabControl.ResumeLayout()
    'End Sub

    'Private Sub ImageDimension()
    '    'Load the current image from the file
    '    Using fullSizeImg As Image = Image.FromFile(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png")
    '        'Get the width of the image
    '        ImageWidth = fullSizeImg.Width
    '        'Get the height of the image
    '        ImageHeight = fullSizeImg.Height
    '        'Check if the image is wider or taller
    '        If ImageWidth > ImageHeight Then
    '            'Resize the image to a specific width
    '            NewSizeVideo(New Size(ImageWidth, ImageHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
    '        Else
    '            'Resize the image to a specific height
    '            NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
    '        End If
    '    End Using
    'End Sub

    'Private Sub NewSizeVideo(OriginalSize As Size, newWidth As Integer, newHeight As Integer)
    '    'Create a new bitmap with the original size of the image
    '    Using bmp As New Bitmap(OriginalSize.Width, OriginalSize.Height)
    '        'Calculate the ratio of the image
    '        Dim ratio As Double = bmp.Width / bmp.Height
    '        'Set the new size of the image to the size of the bitmap
    '        Dim newSize As Size = bmp.Size
    '        'Check if the new width is greater than 0
    '        If newWidth > 0 Then
    '            'If it is, set the new size to the specific width
    '            newSize = New Size(newWidth, newWidth / ratio)
    '        ElseIf newHeight > 0 Then
    '            'If the new width is not greater than 0, check if the new height is greater than 0
    '            'If it is, set the new size to the specific height
    '            newSize = New Size(newHeight * ratio, newHeight)
    '        End If
    '        'Create a new bitmap with the new size
    '        Using b As New Bitmap(newSize.Width, newSize.Height)
    '            'Create a new Graphics object from the new bitmap
    '            Using g As Graphics = Graphics.FromImage(b)
    '                'Set the interpolation mode to high quality bicubic
    '                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
    '                'Draw the original image to the new bitmap with the new size
    '                g.DrawImage(bmp, 0, 0, newSize.Width, newSize.Height)
    '            End Using
    '            'Set the width of the new image
    '            NewImageWidth = b.Width.ToString
    '            'Set the height of the new image
    '            NewImageHeight = b.Height
    '        End Using
    '    End Using
    'End Sub


#End Region

#Region " Update Size OLD"

    Private Sub UpdateSize()
        TabControl.SuspendLayout()
        Imagenes(TabControl.SelectedIndex).SizeMode = PictureBoxSizeMode.Normal

        Dim FullSizeImg As Image = Imagenes(TabControl.SelectedIndex).Image
        ImageWidth = FullSizeImg.Width : ImageHeight = FullSizeImg.Height
        If ImageWidth > ImageHeight Then
            NewSizeVideo(New Size(ImageWidth, ImageHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
        Else
            NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
        End If
        MinimumSize = New Size(400, 300)
        Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
        If Height > Screen.PrimaryScreen.WorkingArea.Height Then
            NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
            Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
        End If
        If Width < 460 Then
            NewSizeVideo(New Size(ImageWidth, ImageHeight), 460 - 54, 0)
            Size = New Size(NewImageWidth + 54, NewImageHeight + 89)
        End If
        If Height > Screen.PrimaryScreen.WorkingArea.Height Then Height = Screen.PrimaryScreen.WorkingArea.Height
        Location = New Point((Screen.PrimaryScreen.WorkingArea.Width - Width) / 2, (Screen.PrimaryScreen.WorkingArea.Height - Height) / 2) : MinimumSize = New Size(460, 300)
        Imagenes(TabControl.SelectedIndex).SizeMode = PictureBoxSizeMode.StretchImage

        Imagenes(TabControl.SelectedIndex).Image.Save(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png", Imaging.ImageFormat.Png)
        Dim cFileSize As String = FileSize(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png") : Dim cTamaño As String = cFileSize.Split(" ")(0) : Dim cTipo As String = cFileSize.Split(" ")(1)
        Tamaño.Value1 = cTamaño.ToString + " " : Tamaño.Value2 = cTipo.ToString

        TabControl.ResumeLayout()
        LWitdh.Value2 = Imagenes(TabControl.SelectedIndex).Image.Width.ToString : LWitdh.Invalidate()
        LHeight.Value2 = Imagenes(TabControl.SelectedIndex).Image.Height.ToString : LHeight.Invalidate()
    End Sub

    Private Sub ImageDimension()
        Dim FullSizeImg As Image = Imagen.LoadImageClone(FMenu.KobaPathTemp + "Captura_" + TabControl.SelectedIndex.ToString + ".png")
        ImageWidth = FullSizeImg.Width : ImageHeight = FullSizeImg.Height
        If ImageWidth > ImageHeight Then
            NewSizeVideo(New Size(ImageWidth, ImageHeight), (Screen.PrimaryScreen.WorkingArea.Width / 4) * 3, 0)
        Else
            NewSizeVideo(New Size(ImageWidth, ImageHeight), 0, (Screen.PrimaryScreen.WorkingArea.Height / 4) * 3)
        End If
    End Sub

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
        NewImageWidth = b.Width.ToString : NewImageHeight = b.Height
    End Sub

#End Region

#Region " Show Controls "

    Private Sub ShowControls(Show As Boolean)
        If Show = True Then
            If TNombre.Text <> "" And TNombre.Text <> "Escriba un nombre" Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
            BColor.Enabled = True : PUndo.Enabled = True : PRedo.Enabled = True : PUndo.Image = My.Resources.Undo_Off : PRedo.Image = My.Resources.Redo_Off
            PEditar.Enabled = True : PCopiar.Enabled = True : PGuardar.Enabled = True : PEditar.Image = My.Resources.Editar_Imagen_Off : PCopiar.Image = My.Resources.Copiar_Imagen_Off : PGuardar.Image = My.Resources.Save_Off
            If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True : PTelegram.Image = My.Resources.Telegram_Off
        Else
            PTexto.Enabled = False : BColor.Enabled = False : PUndo.Enabled = False : PRedo.Enabled = False : PUndo.Image = My.Resources.Undo_No : PRedo.Image = My.Resources.Redo_No : Tamaño.Value1 = "0 " : Tamaño.Value2 = " KB" : LWitdh.Value2 = "0 " : LHeight.Value2 = "0"
            TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) : PTelegram.Enabled = False : PEditar.Enabled = False : PCopiar.Enabled = False : PGuardar.Enabled = False : PTelegram.Image = My.Resources.Telegram_No : PEditar.Image = My.Resources.Editar_Imagen_No : PCopiar.Image = My.Resources.Copiar_Imagen_No : PGuardar.Image = My.Resources.Save_No
        End If
    End Sub

#End Region

#End Region

#End Region

#Region " Controles "

#Region " TNombre "

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If e.KeyChar = "/" Or e.KeyChar = "\" Or e.KeyChar = ":" Or e.KeyChar = "*" Or e.KeyChar = "?" Or e.KeyChar = """" Or e.KeyChar = "<" Or e.KeyChar = ">" Or e.KeyChar = "|" Then e.Handled = True
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TNombre_GotFocus(sender As Object, e As EventArgs) Handles TNombre.GotFocus
        isFocused = True : If TNombre.ForeColor1 = Color.FromArgb(72, 72, 72) Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = ""
        If TabControl.TabPages(TabControl.SelectedIndex).Tag = False Then TNombre.ForeColor1 = Color.FromArgb(102, 102, 102)
    End Sub

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        isFocused = False : TNombre.SelectionStart = 0 : TNombre.SelectionLength = 0 : TNombre.Text = TNombre.Text.Trim
        If TNombre.Text = "" Or TNombre.Text = "Escriba un nombre" Then TNombre.Text = "Escriba un nombre" : TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
        If TabControl.TabPages(TabControl.SelectedIndex).Tag = True Then TNombre.ForeColor1 = Color.FromArgb(72, 72, 72)
    End Sub

#End Region

#Region " Botones "

    Private Function FusionarTextos(Imagen As Image) As Image
        If Imagenes(TabControl.SelectedIndex).Controls.Count = 0 Then Return Imagen
        Dim TImagenFinal As New Bitmap(Imagenes(TabControl.SelectedIndex).Image.Width, Imagenes(TabControl.SelectedIndex).Image.Height)
        Dim WPercent As Decimal = (TImagenFinal.Width * 100 / Imagenes(TabControl.SelectedIndex).Width) - 100
        Dim HPercent As Decimal = (TImagenFinal.Height * 100 / Imagenes(TabControl.SelectedIndex).Height) - 100
        Using G As Graphics = Graphics.FromImage(TImagenFinal)
            G.DrawImage(Imagenes(TabControl.SelectedIndex).Image, New Point(0, 0))
            For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
                Dim TImagen As Image = SLabel.GetTexto(TImagenFinal.Size)
                If TImagen IsNot Nothing Then G.DrawImage(TImagen, CInt(SLabel.Left * (1 + WPercent / 100)), CInt(SLabel.Top * (1 + HPercent / 100)))
            Next
        End Using : Return TImagenFinal
    End Function

#Region " Undo "

    Private Sub PUndo_Click(sender As Object, e As MouseEventArgs) Handles PUndo.Click, PUndo.DoubleClick
        If (e IsNot Nothing AndAlso e.Button <> MouseButtons.Left) Or UndoStack(TabControl.SelectedIndex).Count = 0 Then Exit Sub
        RedoStack(TabControl.SelectedIndex).Push(Imagenes(TabControl.SelectedIndex).Image.Clone)
        Imagenes(TabControl.SelectedIndex).Image = UndoStack(TabControl.SelectedIndex).Pop()
        Capturas(TabControl.SelectedIndex) = Imagenes(TabControl.SelectedIndex).Image
        MousePath.Reset() : Selecting(TabControl.SelectedIndex) = False : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
        If UndoStack(TabControl.SelectedIndex).Count = 0 Then PUndo.Image = My.Resources.Undo_Off
        UpdateSize()
    End Sub

    Private Sub PUndo_MouseEnter(sender As Object, e As EventArgs) Handles PUndo.MouseEnter
        If PUndo.Enabled = True AndAlso UndoStack(TabControl.SelectedIndex).Count > 0 Then PUndo.Image = My.Resources.Undo_On
    End Sub

    Private Sub PUndo_MouseLeave(sender As Object, e As EventArgs) Handles PUndo.MouseLeave
        PUndo.Image = My.Resources.Undo_Off
    End Sub

#End Region

#Region " Redo "

    Private Sub PRedo_Click(sender As Object, e As MouseEventArgs) Handles PRedo.Click, PRedo.DoubleClick
        If (e IsNot Nothing AndAlso e.Button <> MouseButtons.Left) Or RedoStack(TabControl.SelectedIndex).Count = 0 Then Exit Sub
        UndoStack(TabControl.SelectedIndex).Push(Imagenes(TabControl.SelectedIndex).Image.Clone)
        Imagenes(TabControl.SelectedIndex).Image = RedoStack(TabControl.SelectedIndex).Pop()
        Capturas(TabControl.SelectedIndex) = Imagenes(TabControl.SelectedIndex).Image
        MousePath.Reset() : Selecting(TabControl.SelectedIndex) = False : SelStart(TabControl.SelectedIndex).X = -10000 : Imagenes(TabControl.SelectedIndex).Cursor = Cursors.Default
        If RedoStack(TabControl.SelectedIndex).Count = 0 Then PRedo.Image = My.Resources.Redo_Off
        UpdateSize()
    End Sub

    Private Sub PRedo_MouseEnter(sender As Object, e As EventArgs) Handles PRedo.MouseEnter
        If PRedo.Enabled = True AndAlso RedoStack(TabControl.SelectedIndex).Count > 0 Then PRedo.Image = My.Resources.Redo_On
    End Sub

    Private Sub PRedo_MouseLeave(sender As Object, e As EventArgs) Handles PRedo.MouseLeave
        PRedo.Image = My.Resources.Redo_Off
    End Sub

#End Region

#Region " Texto "

    Private Sub PTexto_Click(sender As Object, e As MouseEventArgs) Handles PTexto.Click, PTexto.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Imagenes(TabControl.SelectedIndex).Controls.Add(New SelectableLabel With {.Name = "SLabel" + Imagenes(TabControl.SelectedIndex).Controls.Count.ToString, .Text = "Dobleclick para editar", .Location = New Point(50, 50), .Size = New Size(400, 40), .ContextMenuStrip = MenuTexto, .ForeColor = BColor.BackColor})
        AddHandler Imagenes(TabControl.SelectedIndex).Controls(Imagenes(TabControl.SelectedIndex).Controls.Count - 1).DoubleClick, AddressOf SLabel_DoubleClick
        CType(Imagenes(TabControl.SelectedIndex).Controls(Imagenes(TabControl.SelectedIndex).Controls.Count - 1), SelectableLabel).Seleccionar()
    End Sub

    Private Sub PTexto_MouseEnter(sender As Object, e As EventArgs) Handles PTexto.MouseEnter
        If PTexto.Enabled = True AndAlso TabControl.TabPages(TabControl.SelectedIndex).Tag = False Then PTexto.Image = My.Resources.Copiar_Imagen_On
    End Sub

    Private Sub PTexto_MouseLeave(sender As Object, e As EventArgs) Handles PTexto.MouseLeave
        If PTexto.Enabled = True Then PTexto.Image = My.Resources.Copiar_Imagen_Off
    End Sub

    Private Sub PTexto_MouseDown(sender As Object, e As MouseEventArgs) Handles PTexto.MouseDown
        PTexto.AllowDrop = PTexto.Enabled
    End Sub

    Private Sub PTexto_MouseMove(sender As Object, e As MouseEventArgs) Handles PTexto.MouseMove
        If e.Button = MouseButtons.Left Then
            sender.DoDragDrop(sender.Image, DragDropEffects.Move)
        End If
    End Sub

    Private Sub Imagen_DragEnter(sender As Object, e As DragEventArgs) Handles PTexto.DragEnter
        e.Effect = DragDropEffects.Move
    End Sub

    Private Sub Imagen_DragDrop(sender As Object, e As DragEventArgs)
        If e.Effect = DragDropEffects.Move Then
            Dim CursorLocation As Point = sender.PointToClient(Cursor.Position)
            Imagenes(TabControl.SelectedIndex).Controls.Add(New SelectableLabel With {.Name = "SLabel" + Imagenes(TabControl.SelectedIndex).Controls.Count.ToString, .Text = "Dobleclick para editar", .Location = New Point(CursorLocation.X - 200, CursorLocation.Y - 20), .Size = New Size(400, 40), .ContextMenuStrip = MenuTexto, .ForeColor = BColor.BackColor})
            AddHandler Imagenes(TabControl.SelectedIndex).Controls(Imagenes(TabControl.SelectedIndex).Controls.Count - 1).DoubleClick, AddressOf SLabel_DoubleClick
            CType(Imagenes(TabControl.SelectedIndex).Controls(Imagenes(TabControl.SelectedIndex).Controls.Count - 1), SelectableLabel).Seleccionar()
        End If
    End Sub

#Region " Menu "

#Region " Eventos "

    Private Sub MenuTexto_Opening(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles MenuTexto.Opening
        MenuTexto.Tag = CType(sender, ContextMenuStrip).SourceControl
    End Sub

    Private Sub MenuTexto_MItem_MouseEnter(sender As Object, e As EventArgs) Handles MTexto_Texto.MouseEnter, MTexto_Fuente.MouseEnter, MTexto_Eliminar.MouseEnter
        CType(sender, ToolStripMenuItem).ForeColor = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub MenuTexto_MItem_MouseLeave(sender As Object, e As EventArgs) Handles MTexto_Texto.MouseLeave, MTexto_Fuente.MouseLeave, MTexto_Eliminar.MouseLeave
        CType(sender, ToolStripMenuItem).ForeColor = Color.FromArgb(130, 130, 130)
    End Sub

#End Region

#Region " Texto "

    Private Sub SLabel_DoubleClick(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim Texto As String = InputBox("Escriba un texto", "Label Test", "Escriba un texto")
        If Texto.Trim <> "" Then sender.Text = Texto
    End Sub

    Private Sub MTexto_Texto_Click(sender As Object, e As EventArgs) Handles MTexto_Texto.Click
        Dim SLabel As SelectableLabel = CType(sender, ToolStripMenuItem).Owner.Tag
        Dim Texto As String = InputBox("Escriba un texto", "Label Test", "Escriba un texto")
        If Texto.Trim <> "" Then SLabel.Text = Texto
    End Sub

#End Region

#Region " Fuente "

    Private Sub MTexto_Fuente_Click(sender As Object, e As EventArgs) Handles MTexto_Fuente.Click
        CType(sender, ToolStripMenuItem).Owner.Tag.CambiarFuente()
    End Sub

#End Region

#Region " Eliminar "

    Private Sub MTexto_Eliminar_Click(sender As Object, e As EventArgs) Handles MTexto_Eliminar.Click
        CType(sender, ToolStripMenuItem).Owner.Tag.Eliminar
    End Sub

#End Region

#End Region

#End Region

#Region " Color "

    Private Sub BColor_Click(sender As Object, e As MouseEventArgs) Handles BColor.Click, BColor.DoubleClick
        'BColor.Enabled = False : BColor.Invalidate() : BColor.Enabled = True
        If BColor.Enabled = False Then Exit Sub
        If e.Button = MouseButtons.Left Then
            If CerrarColorSelector = False Then ColorSelector = New ColorSelectorCL(PointToScreen(sender.Location).X + (sender.Width / 2), PointToScreen(sender.Location).Y) : CerrarColorSelector = True : BColor.Cursor = Cursors.Default Else ColorSelector.Close() : CerrarColorSelector = False : BColor.Cursor = Cursors.Hand
        ElseIf e.Button = MouseButtons.Right Then
            BColor.BackColor = Color.FromArgb(205, 150, 0) : CerrarColorSelector = False : BColor.Cursor = Cursors.Hand
            For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
                If SLabel.isSelected = True Then SLabel.ForeColor = BColor.BackColor : SLabel.Invalidate() : Exit For
            Next
        Else
            CerrarColorSelector = False
        End If
    End Sub

    Private Sub ColorSelectorCL_ColorChanged(NewColor As Color, DialogBox As Boolean) Handles ColorSelector.ColorChanged
        If DialogBox = True Then
            Activate() : Dim ColorDialog1 As New ColorDialog With {.AllowFullOpen = True}
            If ColorDialog1.ShowDialog = DialogResult.OK Then BColor.BackColor = ColorDialog1.Color
        Else
            BColor.BackColor = NewColor
        End If
        For Each SLabel As SelectableLabel In Imagenes(TabControl.SelectedIndex).Controls
            If SLabel.isSelected = True Then SLabel.ForeColor = BColor.BackColor : SLabel.Invalidate() : Exit For
        Next
    End Sub

    Private Sub BColor_MouseEnter(sender As Object, e As EventArgs) Handles BColor.MouseEnter
        If ColorSelector.Visible = True Then CerrarColorSelector = True : BColor.Cursor = Cursors.Default Else CerrarColorSelector = False : BColor.Cursor = Cursors.Hand
    End Sub

#End Region

#Region " Telegram "

    Private Sub PTelegram_Click(sender As Object, e As MouseEventArgs) Handles PTelegram.Click, PTelegram.DoubleClick
        If e.Button <> MouseButtons.Left AndAlso IO.File.Exists(Rutas.TelegramPath) = True Then Exit Sub
        Dim FileName As String = "Pantalla " + (TabControl.SelectedIndex + 1).ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
        If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text + ".png"
        IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        FusionarTextos(Imagenes(TabControl.SelectedIndex).Image).Save(FMenu.KobaPathTemp + FileName, Imaging.ImageFormat.Png)
        FMenu.CreateKPS() : RunProcess.RunAsStandar(FMenu.KobaPathTemp + "KPS.exe", "Ejecutar Aplicacion|-|" + Rutas.TelegramPath + "|-|-sendpath " + "|||" + FMenu.KobaPathTemp + FileName + "||||-||-|Normal|-|Normal|-|False|-|False") : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
        Try : AppActivate("Telegram") : Catch : End Try : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
    End Sub

    Private Sub PTelegram_MouseEnter(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter
        If IO.File.Exists(Rutas.TelegramPath) = False Or TabControl.TabPages(TabControl.SelectedIndex).Tag = True Then PTelegram.Enabled = False : PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Enabled = True : PTelegram.Image = My.Resources.Telegram_On
    End Sub

    Private Sub PTelegram_MouseLeave(sender As Object, e As EventArgs) Handles PTelegram.MouseLeave
        If IO.File.Exists(Rutas.TelegramPath) = False Or TabControl.TabPages(TabControl.SelectedIndex).Tag = True Then Exit Sub
        PTelegram.Image = My.Resources.Telegram_Off
    End Sub

#End Region

#Region " Editar "

    Private Sub PEditar_Click(sender As Object, e As MouseEventArgs) Handles PEditar.Click, PEditar.DoubleClick
        If e.Button = MouseButtons.Left AndAlso (EditorProgram = "" Or TabControl.TabPages(TabControl.SelectedIndex).Tag = True) Then Exit Sub
        Select Case e.Button
            Case MouseButtons.Left
                Dim FileName As String = "Pantalla " + TabControl.SelectedIndex.ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
                If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text + ".png"
                IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
                'Imagenes(TabControl.SelectedIndex).Image.Save(FMenu.KobaPathTemp + FileName, Imaging.ImageFormat.Png)
                FusionarTextos(Imagenes(TabControl.SelectedIndex).Image).Save(FMenu.KobaPathTemp + FileName, Imaging.ImageFormat.Png)
                Process.Start(EditorProgram, """" + FMenu.KobaPathTemp + FileName + """")
            Case MouseButtons.Right
                HTexto.Focus()
                Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True : Abrir.Title = "Seleccionar programa..." : Abrir.Filter = "Archivos Ejecutables|*.exe;*.bat;*.lnk|Todos los Archivos|*.*"
                If Abrir.ShowDialog <> DialogResult.Cancel Then
                    Dim Reg = My.Computer.Registry.ClassesRoot.CreateSubKey("SystemFileAssociations\image\shell\edit\command", True) : Reg.SetValue("", """" + Abrir.FileName + """ ""%1""") : Reg.Close()
                    EditorProgram = Abrir.FileName
                End If
            Case MouseButtons.Middle
                Dim Reg = My.Computer.Registry.ClassesRoot.CreateSubKey("SystemFileAssociations\image\shell\edit\command", True) : Reg.SetValue("", """" + "MSPaint"" ""%1""") : Reg.Close()
                EditorProgram = "MSPaint"
                FMenu.ShowMessageBox("Editor predeterminado establecido a 'MS Paint'", FMenu.Version)
        End Select
    End Sub

    Private Sub PEditar_MouseEnter(sender As Object, e As EventArgs) Handles PEditar.MouseEnter
        If PEditar.Enabled = True AndAlso EditorProgram <> "" AndAlso TabControl.TabPages(TabControl.SelectedIndex).Tag = False Then PEditar.Image = My.Resources.Editar_Imagen_On
    End Sub

    Private Sub PEditar_MouseLeave(sender As Object, e As EventArgs) Handles PEditar.MouseLeave
        If PEditar.Enabled = True Then PEditar.Image = My.Resources.Editar_Imagen_Off
    End Sub

#End Region

#Region " Copiar "

    Private Sub PCopiar_Click(sender As Object, e As MouseEventArgs) Handles PCopiar.Click, PCopiar.DoubleClick
        If e.Button = MouseButtons.Left Then
            Clipboard.SetImage(FusionarTextos(Imagenes(TabControl.SelectedIndex).Image))
        ElseIf e.Button = MouseButtons.Right Then
            Dim FileName As String = "Pantalla " + (TabControl.SelectedIndex + 1).ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
            If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text + ".png"
            IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
            FusionarTextos(Imagenes(TabControl.SelectedIndex).Image).Save(FMenu.KobaPathTemp + FileName, Imaging.ImageFormat.Png)
            Dim DataObject As New DataObject() : Dim file(0) As String : file(0) = FMenu.KobaPathTemp + FileName
            DataObject.SetData(DataFormats.FileDrop, True, file) : Clipboard.SetDataObject(DataObject)
        End If
    End Sub

    Private Sub PCopiar_MouseEnter(sender As Object, e As EventArgs) Handles PCopiar.MouseEnter
        If PCopiar.Enabled = True AndAlso TabControl.TabPages(TabControl.SelectedIndex).Tag = False Then PCopiar.Image = My.Resources.Copiar_Imagen_On
    End Sub

    Private Sub PCopiar_MouseLeave(sender As Object, e As EventArgs) Handles PCopiar.MouseLeave
        If PCopiar.Enabled = True Then PCopiar.Image = My.Resources.Copiar_Imagen_Off
    End Sub

#End Region

#Region " Guardar "

    Private Sub PGuardar_Click(sender As Object, e As MouseEventArgs) Handles PGuardar.Click, PGuardar.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim FileName As String = "Pantalla " + (TabControl.SelectedIndex + 1).ToString + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
        If TNombre.Text <> "Escriba un nombre" And TNombre.Text <> "" Then FileName = TNombre.Text + ".png"
        Dim Guardar As New SaveFileDialog : Guardar.CheckPathExists = True : Guardar.FileName = FileName : Guardar.OverwritePrompt = True : Guardar.AddExtension = True
        Guardar.Title = "Guardar captura de pantalla..." : Guardar.Filter = "Archivo de Imagen (*.png)|*.png|Archivo de Imagen (*.jpg)|*.jpg|Archivo de Imagen (*.bmp)|*.bmp" : Guardar.DefaultExt = "*.png"
        If Guardar.ShowDialog <> DialogResult.Cancel Then
            IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
            Select Case IO.Path.GetExtension(Guardar.FileName).ToLower
                Case ".jpg"
                    SaveJpeg(Guardar.FileName, FusionarTextos(Imagenes(TabControl.SelectedIndex).Image), 100)
                Case ".bmp"
                    FusionarTextos(Imagenes(TabControl.SelectedIndex).Image).Save(Guardar.FileName, Imaging.ImageFormat.Bmp)
                Case ".png"
                    FusionarTextos(Imagenes(TabControl.SelectedIndex).Image).Save(Guardar.FileName, Imaging.ImageFormat.Png)
            End Select
        End If
    End Sub

    Private Sub PGuardar_MouseEnter(sender As Object, e As EventArgs) Handles PGuardar.MouseEnter
        If PGuardar.Enabled = True AndAlso TabControl.TabPages(TabControl.SelectedIndex).Tag = False Then PGuardar.Image = My.Resources.Save_On
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

    Private Sub Controles_Click(sender As Object, e As EventArgs) Handles Tamaño.Click, PTelegram.Click, PGuardar.Click, PCopiar.Click, PCerrar.Click, MyBase.Click, LTamaño.Click, FTheme.Click, BMaximizar.Click, BColor.Click, BCerrar.Click, LWitdh.Click, Lpor.Click, LHeight.Click, PTexto.Click

    End Sub

    Private Sub BColor_Click(sender As Object, e As EventArgs) Handles BColor.DoubleClick

    End Sub

    Private Sub PCerrar_Click(sender As Object, e As EventArgs) Handles PCerrar.DoubleClick

    End Sub

    Private Sub PTelegram_Click(sender As Object, e As EventArgs) Handles PTelegram.DoubleClick, PTexto.DoubleClick

    End Sub

    Private Sub PGuardar_Click(sender As Object, e As EventArgs) Handles PGuardar.DoubleClick

    End Sub

    Private Sub PUndo_Click(sender As Object, e As EventArgs) Handles PUndo.DoubleClick, PUndo.Click

    End Sub

    Private Sub PRedo_Click(sender As Object, e As EventArgs) Handles PRedo.DoubleClick, PRedo.Click

    End Sub

    Private Sub PCopiar_Click(sender As Object, e As EventArgs) Handles PCopiar.DoubleClick

    End Sub

    Private Sub PEditar_Click(sender As Object, e As EventArgs) Handles PEditar.DoubleClick, PEditar.Click

    End Sub

    Private Sub CapturaPantalla_LHelp_Click(sender As Object, e As EventArgs) Handles CapturaPantalla_LHelp.DoubleClick, CapturaPantalla_LHelp.Click

    End Sub

    Private Sub CambiarColorToolStripMenuItem_Click(sender As Object, e As EventArgs)

    End Sub

    Private Sub EliminarToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles MTexto_Eliminar.Click

    End Sub

#End Region

#End Region

#Region " Help "

    Private Sub CapturaPantalla_LHelp_Click(sender As Object, e As MouseEventArgs) Handles CapturaPantalla_LHelp.Click, CapturaPantalla_LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Capturar Pantalla" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub CapturaPantalla_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles CapturaPantalla_LHelp.MouseEnter
        CapturaPantalla_LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub CapturaPantalla_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles CapturaPantalla_LHelp.MouseLeave
        CapturaPantalla_LHelp.ForeColor1 = Color.FromArgb(85, 85, 85)
    End Sub

#End Region

#End Region

#Region " Tooltips "

    Private Sub CapturarPantalla_Tooltips(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter, PEditar.MouseEnter, PCopiar.MouseEnter, PGuardar.MouseEnter, PCerrar.MouseEnter, PUndo.MouseEnter, PRedo.MouseEnter, BColor.MouseEnter, PTexto.MouseEnter
        Select Case sender.Name
            Case "PUndo" : If PUndo.Enabled = True AndAlso UndoStack(TabControl.SelectedIndex).Count <> 0 Then FMenu.Show_Tooltip(sender, "Deshacer última acción") Else FMenu.Show_Tooltip(Nothing, "")
            Case "PRedo" : If PRedo.Enabled = True AndAlso RedoStack(TabControl.SelectedIndex).Count <> 0 Then FMenu.Show_Tooltip(sender, "Rehacer última acción") Else FMenu.Show_Tooltip(Nothing, "")
            Case "PTexto" : If PTexto.Enabled = True Then FMenu.Show_Tooltip(sender, "Inserta un texto en la captura")
            Case "BColor" : If BColor.Enabled = True Then FMenu.Show_Tooltip(sender, "Color con el que se dibujará" + vbCrLf + vbCrLf + "Color actual: (R:" + BColor.BackColor.R.ToString + ", G:" + BColor.BackColor.G.ToString + ", B:" + BColor.BackColor.B.ToString)

            Case "PTelegram" : If PTelegram.Enabled = True Then FMenu.Show_Tooltip(sender, "Envía la captura por Telegram")
            Case "PEditar" : If PEditar.Enabled = True Then FMenu.Show_Tooltip(sender, "Click Izquierdo: Abre la captura en el editor predeterminado" + vbCrLf + "Click Derecho: Selecciona el programa de edición pedeterminado" + vbCrLf + "Click Central: Establece 'MsPaint' como editor predeterminado")
            Case "PCopiar" : If PCopiar.Enabled = True Then FMenu.Show_Tooltip(sender, "Click Izquierdo: Copia la captura en el portapapeles de Windows" + vbCrLf + "Click Derecho: Copia la captura como archivo en el portapapeles de Windows")
            Case "PGuardar" : If PGuardar.Enabled = True Then FMenu.Show_Tooltip(sender, "Guarda la captura en disco")
            Case "PCerrar" : FMenu.Show_Tooltip(sender, "Cierra la ventana")
        End Select
    End Sub

#End Region

End Class

#End Region