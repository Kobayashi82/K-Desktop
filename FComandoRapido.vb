
#Region " Comando Rapido "

Public Class FComandoRapido

#Region " Variables "

    Public Nombre, Tipo As String
    Public Cancelado As Boolean = True
    Public isDefault As Boolean = True
    Public isURL As Boolean
    Dim Combo_TempValue As String
    Dim Combo_IgnoreOne, Combo_Dropped As Boolean
    Dim PlayTimer As New Timer With {.Interval = 100, .Enabled = False}

#End Region

#Region " Formulario "

    Private Sub FComando_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        AddHandler PlayTimer.Tick, AddressOf PlayTimer_Tick : ComboSonidos_Cargar()
        TNombre.Text = Nombre : TNombre.Focus() : If Nombre = "" Then HTexto.Focus()
        CAdministrador.Checked = False
        TRuta.Tag = "" : TRuta.Text = "" : ValidarAccion()
        TParametros.Text = ""
        TModo.Text = "Normal"
        TPrioridad.Text = "Normal"
        TSonido.Text = "Sin Sonido" : LCSonido.Text = "Sin Sonido"
        PIcono.Image = My.Resources.Accion_Aplicacion.ToBitmap
    End Sub

    Private Sub FComando_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        TNombre.Focus() : HTexto.Focus() : TRuta.Focus()
        Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FComandoRapido_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If GInformacion_LoadingCircle.Visible = True Then e.Cancel = True
    End Sub

    Private Sub FComando_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 Then
            If Combo_Dropped = True Then DroppedDown() : Combo_Dropped = False : HTexto.Focus() : Exit Sub
            If Opacity = 1 Then e.Handled = True : Close()
        End If
    End Sub

    Private Sub DroppedDown()
        CSonido.DroppedDown = False
        CModo.DroppedDown = False
        CPrioridad.DroppedDown = False
    End Sub

#Region " Controles "

    Private Sub FTheme_Click(sender As Object, e As EventArgs) Handles FTheme.Click ' Añadir demas controles
        HTexto.Focus()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True
    End Sub

#End Region

#End Region

#Region " Icono "

    Private Sub PIcono_Click(sender As Object, e As MouseEventArgs) Handles PIcono.Click
        If e.Button = MouseButtons.Left Then NombreIcono_Menu_Seleccionar_Click(sender, Nothing)
    End Sub

    Private Sub NombreIcono_Menu_Opening(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles ComandoRapido_Menu.Opening
        ComandoRapido_Menu.Height = 110
    End Sub

    Private Sub NombreIcono_Menu_Seleccionar_Click(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Seleccionar.Click
        FIconDialog.Cancelado = True : FIconDialog.ShowDialog()
        If FIconDialog.Cancelado = False Then PIcono.Image = FIconDialog.Icono.ToBitmap : isDefault = False
    End Sub

    Private Sub NombreIcono_Menu_Examinar_Click(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Examinar.Click
        Try : Dim ImageListSmall, ImageListLarge As ImageList : Dim Icono As Bitmap = Nothing
            Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True
            Abrir.Title = "Seleccionar icono..." : Abrir.Filter = "Archivos de Iconos|*.ico;*.exe;*.dll;*.lnk|Todos los Archivos|*.*"
            If Abrir.ShowDialog <> DialogResult.Cancel Then
                If Abrir.FileName.ToLower.EndsWith(".lnk") Then ImageListLarge = FIconDialog.GetImageList(Rutas.GetTargetPath(Abrir.FileName)) Else ImageListLarge = FIconDialog.GetImageList(Abrir.FileName)
                ImageListSmall = ImageListLarge : If ImageListSmall IsNot Nothing Then
                    For Cuenta = 0 To ImageListSmall.Images.Count - 1
                        If ImageListSmall.Images(Cuenta) IsNot Nothing Then Icono = ImageListSmall.Images(Cuenta) : Exit For
                    Next
                Else
                    If Abrir.FileName.ToLower.EndsWith(".lnk") Then Abrir.FileName = Rutas.GetTargetPath(Abrir.FileName)
                    Icono = Icon.ExtractAssociatedIcon(Abrir.FileName).ToBitmap
                End If
                If Icono Is Nothing Then Exit Sub
                PIcono.Image = Icono
                isDefault = False
            End If
        Catch : End Try
    End Sub

    Private Sub NombreIcono_Menu_Default_Click(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Default.Click
        If GInformacion_PIcono.Image Is Nothing Then PIcono.Image = My.Resources.Accion_Aplicacion.ToBitmap Else PIcono.Image = GInformacion_PIcono.Image
        isDefault = True
    End Sub

    Private Sub NombreIcono_Menu_Eliminar_Click(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Eliminar.Click
        PIcono.Image = Nothing : isDefault = False
    End Sub

    Private Sub MenuIcono_MItem_MouseEnter(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Seleccionar.MouseEnter, ComandoRapido_Menu_Examinar.MouseEnter, ComandoRapido_Menu_Default.MouseEnter, ComandoRapido_Menu_Eliminar.MouseEnter
        Dim QMItem As ToolStripMenuItem = CType(sender, ToolStripMenuItem) : QMItem.ForeColor = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub MenuIcono_MItem_MouseLeave(sender As Object, e As EventArgs) Handles ComandoRapido_Menu_Seleccionar.MouseLeave, ComandoRapido_Menu_Examinar.MouseLeave, ComandoRapido_Menu_Default.MouseLeave, ComandoRapido_Menu_Eliminar.MouseLeave
        Dim QMItem As ToolStripMenuItem = CType(sender, ToolStripMenuItem) : QMItem.ForeColor = Color.FromArgb(130, 130, 130)
    End Sub

#End Region

#Region " Nombre "

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        If TNombre.Text.Trim = "" Then TNombre.Text = "" : Exit Sub
        'TNombre.Text = TNombre.Text.Trim.Substring(0, 1).ToUpper + TNombre.Text.Trim.Substring(1).Trim()
    End Sub

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

#End Region

#Region " Sonido "

    Private Sub LCSonido_TextChanged(sender As Object, e As EventArgs) Handles LCSonido.TextChanged
        TSonido.Enabled = False
        LCSonido.Text = Rutas.Acortar_Ruta(TSonido.Text, LCSonido)
        Dim TextSize As Single = 6.75 : Do Until TextRenderer.MeasureText(LCSonido.Text, New Font(LCSonido.Font.Name, TextSize, FontStyle.Regular)).Width < LCSonido.Width Or TextSize < 6 : TextSize -= 1 : Loop : LCSonido.Font = New Font(LCSonido.Font.Name, TextSize, FontStyle.Regular)
    End Sub

    Private Sub LCSonido_Click(sender As Object, e As MouseEventArgs) Handles LCSonido.Click, TSonido.Click
        If e.Button = MouseButtons.Left Then
            CSonido.DroppedDown = True : CSonido.Focus()
        ElseIf e.Button = MouseButtons.Right Then
            LCSonido.Visible = False : Combo_IgnoreOne = True : TSonido.ReadOnly = False : TSonido.Enabled = True : TSonido.Cursor = Cursors.IBeam : TSonido.Focus()
        Else : HTexto.Focus()
        End If
    End Sub

    Private Sub TSonido_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TSonido.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TSonido_KeyDown(sender As Object, e As KeyEventArgs) Handles TSonido.KeyDown
        If e.KeyCode = Keys.Down Or e.KeyCode = Keys.Up Then e.Handled = True : HTexto.Focus() : CSonido.DroppedDown = True : CSonido.Focus()
    End Sub

    Private Sub TSonido_LostFocus(sender As Object, e As EventArgs) Handles TSonido.LostFocus
        If Combo_IgnoreOne = True Then Combo_IgnoreOne = False : Exit Sub
        If LCSonido.Visible = False Then LCSonido.Visible = True : TSonido.ReadOnly = True : TSonido.Enabled = False : TSonido.Cursor = Cursors.Hand
        If TSonido.Text.Trim = "" Then TSonido.Text = "Sin Sonido"
        For Cuenta = 0 To CSonido.Items.Count - 1
            If TSonido.Text.ToLower = CSonido.Items(Cuenta).ToLower Then TSonido.Text = CSonido.Items(Cuenta)
        Next : TSonido.Text = TSonido.Text.Trim : TSonido.SelectionLength = 0 : LCSonido.Text = TSonido.Text
    End Sub

    Private Sub TSonido_MouseEnter(sender As Object, e As EventArgs) Handles TSonido.MouseEnter, CSonido.MouseEnter
        CSonido.State = NSComboBox.MouseState.None
    End Sub

#Region " Combo Sonido "

    Private Sub CSonido_DropDown(sender As Object, e As EventArgs) Handles CSonido.DropDown
        TSonido.Enabled = True
        CSonido.MaxDropDownItems = CSonido.Items.Count : Combo_Dropped = True : Combo_TempValue = TSonido.Text
        If CSonido.Items.Contains(TSonido.Text) = True Then CSonido.SelectedItem = TSonido.Text Else CSonido.SelectedIndex = 0
    End Sub

    Private Sub CSonido_DropDownClosed(sender As Object, e As EventArgs) Handles CSonido.DropDownClosed
        Combo_TempValue = ""
    End Sub

    Private Sub CSonido_LostFocus(sender As Object, e As EventArgs) Handles CSonido.LostFocus
        Combo_Dropped = False
    End Sub

    Private Sub SelectedIndexChanged(sender As Object, e As EventArgs) Handles CSonido.SelectedIndexChanged
        If Combo_TempValue = "" Then TSonido.Text = CSonido.SelectedItem.Trim : LCSonido.Text = TSonido.Text : HTexto.Focus()
    End Sub

    Private Sub CSonido_KeyPress(sender As Object, e As KeyPressEventArgs) Handles CSonido.KeyPress
        If Asc(e.KeyChar) = 13 Then TSonido.Text = CSonido.SelectedItem.Trim : LCSonido.Text = TSonido.Text : HTexto.Focus() : e.Handled = True
    End Sub

    Private Sub CSonido_ItemHover(Index As Integer) Handles CSonido.ItemHover
        PlayTimer.Stop() : PlayTimer.Start()
    End Sub

#End Region

#Region " Botones "

    Private Sub BPlay_Click(sender As Object, e As MouseEventArgs) Handles BPlay.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TSonido.Text = "Sin Sonido" Then My.Computer.Audio.Stop() Else Sonidos.Play_Block(TSonido.Text)
    End Sub

    Private Sub BExaminar_Sonido_Click(sender As Object, e As MouseEventArgs) Handles BExaminar_Sonido.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True
        Abrir.Title = "Seleccionar audio..." : Abrir.Filter = "Archivos de Audio|*.wav;*.mid|Todos los Archivos|*.*"
        If Abrir.ShowDialog <> DialogResult.Cancel Then TSonido.Text = Abrir.FileName : LCSonido.Text = TSonido.Text : Sonidos.Play_Block(TSonido.Text)
        TSonido.SelectionStart = TSonido.Text.Length : TSonido.SelectionLength = 0
        BExaminar_Sonido.State = NSButton.MouseState.None : BExaminar_Sonido.Invalidate() : HTexto.Focus()
    End Sub

    Private Sub BExaminar_Sonido_MouseEnter(sender As Object, e As EventArgs) Handles BExaminar_Sonido.MouseEnter
        CSonido.State = NSComboBox.MouseState.None
    End Sub

#End Region

#Region " PlayTimer "

    Private Sub PlayTimer_Tick()
        If CSonido.DroppedDown = True Then If CSonido.SelectedItem.Trim = "Sin Sonido" Then My.Computer.Audio.Stop() : PlayTimer.Stop() Else Sonidos.Play_Block(CSonido.SelectedItem.Trim) : PlayTimer.Stop()
    End Sub

    Private Sub ComboSonidos_Cargar()
        CSonido.Items.Clear() : CSonido.Items.AddRange(FMenu.Comandos_Sonido_CSonido.Items.Cast(Of Object).ToArray())
    End Sub

#End Region

#End Region

#Region " Ruta "

#Region " Check Controls "

    Private Sub CheckControls()
        If Internet.ValidarURL(TRuta.Text) = True Then
            TParametros.ReadOnly = True : TParametros.Text = ""
            TParametros.ForeColor1 = Color.FromArgb(18, 18, 18) : TParametros.ForeColor2 = Color.FromArgb(18, 18, 18)
            CModo.Enabled = False : TModo.Enabled = False : LCModo.Enabled = False
            TModo.Text = "Normal" : LCModo.Text = "Normal"
            TModo.Cursor = Cursors.Default : LCModo.Cursor = Cursors.Default
            CPrioridad.Enabled = False : TPrioridad.Enabled = False : LCPrioridad.Enabled = False
            TPrioridad.Cursor = Cursors.Default : LCPrioridad.Cursor = Cursors.Default
            TPrioridad.Text = "Normal" : LCPrioridad.Text = "Normal"
            Exit Sub
        ElseIf IO.Directory.Exists(TRuta.Text) = True Or TRuta.Text.ToLower = "explorer" Then
            TParametros.ReadOnly = False : TParametros.ForeColor1 = TRuta.ForeColor1 : TParametros.ForeColor2 = TRuta.ForeColor2
            CModo.Enabled = True : TModo.Enabled = True : LCModo.Enabled = True
            TModo.Cursor = Cursors.Hand : LCModo.Cursor = Cursors.Hand
            CPrioridad.Enabled = False : TPrioridad.Enabled = False : LCPrioridad.Enabled = False
            TPrioridad.Cursor = Cursors.Default : LCPrioridad.Cursor = Cursors.Default
            TPrioridad.Text = "Normal" : LCPrioridad.Text = "Normal"
            Exit Sub
        ElseIf TRuta.Text.ToLower = "cmd" Then
            TParametros.ReadOnly = False : TParametros.ForeColor1 = TRuta.ForeColor1 : TParametros.ForeColor2 = TRuta.ForeColor2
            CModo.Enabled = True : TModo.Enabled = True : LCModo.Enabled = True
            TModo.Cursor = Cursors.Hand : LCModo.Cursor = Cursors.Hand
            CPrioridad.Enabled = False : TPrioridad.Enabled = False : LCPrioridad.Enabled = False
            TPrioridad.Cursor = Cursors.Default : LCPrioridad.Cursor = Cursors.Default
            TPrioridad.Text = "Normal" : LCPrioridad.Text = "Normal"
        Else
            TParametros.ReadOnly = False : TParametros.ForeColor1 = TRuta.ForeColor1 : TParametros.ForeColor2 = TRuta.ForeColor2
            CModo.Enabled = True : TModo.Enabled = True : LCModo.Enabled = True
            TModo.Cursor = Cursors.Hand : LCModo.Cursor = Cursors.Hand
            CPrioridad.Enabled = True : TPrioridad.Enabled = True : LCPrioridad.Enabled = True
            TPrioridad.Cursor = Cursors.Hand : LCPrioridad.Cursor = Cursors.Hand
        End If
    End Sub

#End Region

#Region " Validad Accion "

    Private Sub ValidarAccion()
        If TRuta.Tag = "" Then Accion_Info(TRuta.Tag) : GInformacion.Tag = "" : Exit Sub
        If IO.Path.GetDirectoryName(TRuta.Tag) <> "" Or IO.File.Exists(TRuta.Tag) = True Or IO.Directory.Exists(TRuta.Tag) = True Then
            TRuta.Text = Rutas.Acortar_Ruta(TRuta.Tag, TRuta) : Accion_Info(TRuta.Tag) : Exit Sub
        Else
            If TRuta.Text.ToLower.StartsWith("www.") = True Then TRuta.Text = "http://" + Replace(TRuta.Text, "www.", "", 1, -1, vbTextCompare)
            If TRuta.Text.ToLower.StartsWith("https://") = False And TRuta.Text.ToLower.StartsWith("http://") = False Then If Internet.ValidarURL(TRuta.Text) = True Then TRuta.Text = "http://" + TRuta.Text
            If TRuta.Text.ToLower.StartsWith("https://") = True Or TRuta.Text.ToLower.StartsWith("http://") = True Then If Internet.ValidarURL(TRuta.Text) = False Then If TRuta.Text.ToLower.StartsWith("https://") = True Then TRuta.Text = TRuta.Text.Substring(8) Else TRuta.Text = TRuta.Text.Substring(7)
            TRuta.Text = Replace(TRuta.Text, "http://", "http://", 1, -1, vbTextCompare)
            TRuta.Text = Replace(TRuta.Text, "https://", "https://", 1, -1, vbTextCompare)
            TRuta.Text = Replace(TRuta.Text, "http://www.", "http://", 1, -1, vbTextCompare)
            TRuta.Text = Replace(TRuta.Text, "https://www.", "https://", 1, -1, vbTextCompare)
            TRuta.Text = Replace(TRuta.Text, "ftp://", "ftp://", 1, -1, vbTextCompare)
            TRuta.Tag = TRuta.Text : Accion_Info(TRuta.Tag)
        End If
    End Sub

#End Region

    Private Sub TRuta_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TRuta.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : GInformacion.Tag = "" : HTexto.Focus()
    End Sub

    Private Sub TRuta_GotFocus(sender As Object, e As EventArgs) Handles TRuta.GotFocus
        If TRuta.Tag.ToString.Length = 2 And TRuta.Tag.ToString.EndsWith(":") Then TRuta.Tag = TRuta.Tag.ToString.ToUpper + "\"
        If TRuta.Tag.ToString.EndsWith("\") And TRuta.Tag.ToString.Length > 3 Then TRuta.Tag = TRuta.Tag.ToString.Substring(0, TRuta.Tag.ToString.Length - 1)
        If TRuta.Tag = "" Then TRuta.Text = "" Else TRuta.Text = TRuta.Tag
    End Sub

    Private Sub TRuta_LostFocus(sender As Object, e As EventArgs) Handles TRuta.LostFocus
        TRuta.Text = TRuta.Text.Trim
        If TRuta.Text = "" Then
            TRuta.Tag = ""
            TRuta.Text = ""
            GInformacion_LNombre.Value1 = "" : GInformacion_LNombre.Value2 = ""
            GInformacion_LCompany.Text = "" : GInformacion_LCompany.Refresh()
            GInformacion_LTipo.Text = "" : GInformacion_LTipo.Refresh()
            GInformacion_PIcono.Image = Nothing : GInformacion_PIcono.Refresh()
            GInformacion_SinInformacion.Visible = True
            GInformacion.Tag = ""
        Else
            TRuta.Text = FMenu.ReplaceSpecialFolder(TRuta.Text)
            TRuta.Text = FMenu.CheckSpecialFolder(TRuta.Text)
            If TRuta.Text.Length = 2 And TRuta.Text.EndsWith(":") Then TRuta.Text = TRuta.Text.ToUpper + "\"
            If TRuta.Text.EndsWith("\") And TRuta.Text.Length > 3 Then TRuta.Text = TRuta.Text.Substring(0, TRuta.Text.Length - 1)
            If TRuta.Text.ToLower.StartsWith("http") = False Then TRuta.Text = TRuta.Text.Substring(0, 1).ToUpper + TRuta.Text.Substring(1)
            If TRuta.Text.ToLower = "cmd" Then TRuta.Text = "CMD"
            TRuta.Tag = TRuta.Text : TRuta.SelectionLength = 0 : ValidarAccion() : Set_Nombre(FMenu.ReplaceSpecialFolder(TRuta.Tag)) : Set_Nombre(FMenu.CheckSpecialFolder(TRuta.Tag))
        End If : CheckControls()
    End Sub

    Private Sub Set_Nombre(Ruta As String)
        If TNombre.Text.Trim <> "" Then Exit Sub
        If IO.File.Exists(Ruta) = True Then
            TNombre.Text = IO.Path.GetFileNameWithoutExtension(Ruta)
        ElseIf IO.Directory.Exists(Ruta) = True Then
            If Ruta.ToString.Length = 3 AndAlso Ruta.ToString.EndsWith("\") Then TNombre.Text = Ruta Else TNombre.Text = Ruta.ToString.Split("\").Last
        ElseIf Ruta.ToString.ToLower.StartsWith("www.") Or Ruta.ToString.ToLower.StartsWith("http") Or Ruta.ToString.ToLower.StartsWith("ftp") Then
            If Ruta.ToString.Contains("//") = True Then TNombre.Text = Split(Ruta.ToString, "//",, CompareMethod.Text)(1)
        Else
            Ruta = Rutas.GetTargetPath(Ruta)
            If IO.File.Exists(Ruta) = True Then
                Dim Titulo As String = FileVersionInfo.GetVersionInfo(Ruta).FileDescription : If Titulo Is Nothing Then Titulo = ""
                If Titulo = "" Then
                    If IO.Directory.Exists(Ruta) = True Then
                        If Ruta.ToString.Length = 3 AndAlso Ruta.ToString.EndsWith("\") Then TNombre.Text = Ruta Else TNombre.Text = Ruta.ToString.Split("\").Last
                    Else
                        TNombre.Text = IO.Path.GetFileNameWithoutExtension(Ruta)
                    End If
                Else
                    TNombre.Text = Titulo
                End If
            End If
        End If
        If TNombre.Text <> "" Then TNombre.Text = TNombre.Text.Substring(0, 1).ToUpper + TNombre.Text.Substring(1)
    End Sub

    Private Sub BExaminar_Ruta_Click(sender As Object, e As MouseEventArgs) Handles BExaminar_Ruta.Click
        HTexto.Focus() : If e.Button = MouseButtons.Left Then
            Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True : Abrir.Title = "Seleccionar programa..." : Abrir.Filter = "Archivos Ejecutables|*.exe;*.bat;*.lnk|Todos los Archivos|*.*"
            If Abrir.ShowDialog <> DialogResult.Cancel Then TRuta.Tag = Abrir.FileName : TRuta.Text = Rutas.Acortar_Ruta(Abrir.FileName, TRuta)
            BExaminar_Ruta.State = NSButton.MouseState.None : BExaminar_Ruta.Invalidate()
        ElseIf e.Button = Windows.Forms.MouseButtons.Right Then
            Dim Abrir As New FolderBrowserDialog : Abrir.ShowNewFolderButton = True : If IO.Directory.Exists(TRuta.Text) Then Abrir.SelectedPath = IO.Path.GetDirectoryName(TRuta.Text)
            Abrir.Description = "Seleccionar carpeta..." : If Abrir.ShowDialog <> DialogResult.Cancel Then TRuta.Tag = Abrir.SelectedPath : TRuta.Text = Rutas.Acortar_Ruta(Abrir.SelectedPath, TRuta)
            BExaminar_Ruta.State = NSButton.MouseState.None : BExaminar_Ruta.Invalidate()
        End If
        ValidarAccion() : Set_Nombre(FMenu.ReplaceSpecialFolder(TRuta.Tag)) : Set_Nombre(FMenu.CheckSpecialFolder(TRuta.Tag))
        TRuta.SelectionStart = TRuta.Text.Length : TRuta.SelectionLength = 0
        CheckControls()
    End Sub

#End Region

#Region " Parametros "

    Private Sub TParametros_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TParametros.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TParametros_GotFocus(sender As Object, e As EventArgs) Handles TParametros.GotFocus
        If TParametros.ReadOnly = True Then HTexto.Focus() : Exit Sub
    End Sub

    Private Sub TParametros_LostFocus(sender As Object, e As EventArgs) Handles TParametros.LostFocus
        TParametros.Text = TParametros.Text.Trim : TParametros.SelectionLength = 0
    End Sub

#End Region

#Region " Modo "

    Private Sub TModo_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TModo.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TModo_TextChanged(sender As Object, e As EventArgs) Handles TModo.TextChanged
        LCModo.Text = TModo.Text.Trim
    End Sub

    Private Sub TModo_GotFocus(sender As Object, e As EventArgs) Handles TModo.GotFocus
        TModo.Text = TModo.Text.Trim : TModo.SelectionLength = 0 : HTexto.Focus()
    End Sub

    Private Sub TModo_LostFocus(sender As Object, e As EventArgs) Handles TModo.LostFocus
        TModo.Text = TModo.Text.Trim : TModo.SelectionLength = 0
    End Sub

    Private Sub TModo_Click(sender As Object, e As MouseEventArgs) Handles TModo.Click, LCModo.Click
        HTexto.Focus() : If LCModo.Enabled = False Then Exit Sub
        If e.Button = MouseButtons.Left Then CModo.DroppedDown = True : CModo.Focus()
    End Sub

    Private Sub PModo_Click(sender As Object, e As MouseEventArgs) Handles PModo.Click, PModo.DoubleClick
        HTexto.Focus() : If CModo.Enabled = False Then Exit Sub
        If e.Button = MouseButtons.Left Then CModo.DroppedDown = True : CModo.Focus()
    End Sub

    Private Sub PModo_MouseEnter(sender As Object, e As EventArgs) Handles PModo.MouseEnter
        If CModo.Enabled = False Then Exit Sub
        PModo.Image = My.Resources.CComandos_On
    End Sub

    Private Sub PModo_MouseLeave(sender As Object, e As EventArgs) Handles PModo.MouseLeave
        PModo.Image = My.Resources.CComandos_Off
    End Sub

#Region " Combo "

    Private Sub CModo_DropDown(sender As Object, e As EventArgs) Handles CModo.DropDown
        CModo.MaxDropDownItems = CModo.Items.Count : Combo_Dropped = True : Combo_TempValue = TModo.Text : CModo.SelectedItem = TModo.Text
        If CModo.Items.Contains(TModo.Text) = True Then CModo.SelectedItem = TModo.Text Else CModo.SelectedIndex = 0
    End Sub

    Private Sub CModo_DropDownClosed(sender As Object, e As EventArgs) Handles CModo.DropDownClosed
        Combo_TempValue = ""
    End Sub

    Private Sub CModo_LostFocus(sender As Object, e As EventArgs) Handles CModo.LostFocus
        Combo_Dropped = False
    End Sub

    Private Sub CModo_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CModo.SelectedIndexChanged
        If Combo_TempValue = "" Then TModo.Text = CModo.SelectedItem.Trim : LCModo.Text = TModo.Text : HTexto.Focus()
    End Sub

    Private Sub CModo_KeyPress(sender As Object, e As KeyPressEventArgs) Handles CModo.KeyPress
        If Asc(e.KeyChar) = 13 Then TModo.Text = CModo.SelectedItem.Trim : LCModo.Text = TModo.Text : e.Handled = True : HTexto.Focus()
    End Sub

#End Region

#End Region

#Region " Prioridad "

    Private Sub TPrioridad_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TPrioridad.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TPrioridad_TextChanged(sender As Object, e As EventArgs) Handles TPrioridad.TextChanged
        LCPrioridad.Text = TPrioridad.Text.Trim
    End Sub

    Private Sub TPrioridad_GotFocus(sender As Object, e As EventArgs) Handles TPrioridad.GotFocus
        TPrioridad.Text = TPrioridad.Text.Trim : TPrioridad.SelectionLength = 0 : HTexto.Focus()
    End Sub

    Private Sub TPrioridad_LostFocus(sender As Object, e As EventArgs) Handles TPrioridad.LostFocus
        TPrioridad.Text = TPrioridad.Text.Trim : TPrioridad.SelectionLength = 0
    End Sub

    Private Sub TPrioridad_Click(sender As Object, e As MouseEventArgs) Handles TPrioridad.Click, LCPrioridad.Click
        HTexto.Focus() : If LCPrioridad.Enabled = False Then Exit Sub
        If e.Button = MouseButtons.Left Then CPrioridad.DroppedDown = True : CPrioridad.Focus()
    End Sub

    Private Sub PPrioridad_Click(sender As Object, e As MouseEventArgs) Handles PPrioridad.Click, PPrioridad.DoubleClick
        HTexto.Focus() : If CPrioridad.Enabled = False Then Exit Sub
        If e.Button = MouseButtons.Left Then CPrioridad.DroppedDown = True : CPrioridad.Focus()
    End Sub

    Private Sub PPrioridad_MouseEnter(sender As Object, e As EventArgs) Handles PPrioridad.MouseEnter
        If CPrioridad.Enabled = False Then Exit Sub
        PPrioridad.Image = My.Resources.CComandos_On
    End Sub

    Private Sub PPrioridad_MouseLeave(sender As Object, e As EventArgs) Handles PPrioridad.MouseLeave
        PPrioridad.Image = My.Resources.CComandos_Off
    End Sub

#Region " Combo "

    Private Sub CPrioridad_DropDown(sender As Object, e As EventArgs) Handles CPrioridad.DropDown
        CPrioridad.MaxDropDownItems = CPrioridad.Items.Count : Combo_Dropped = True : Combo_TempValue = TPrioridad.Text : CPrioridad.SelectedItem = TPrioridad.Text
        If CPrioridad.Items.Contains(TPrioridad.Text) = True Then CPrioridad.SelectedItem = TPrioridad.Text Else CPrioridad.SelectedIndex = 0
    End Sub

    Private Sub CPrioridad_DropDownClosed(sender As Object, e As EventArgs) Handles CPrioridad.DropDownClosed
        Combo_TempValue = ""
    End Sub

    Private Sub CPrioridad_LostFocus(sender As Object, e As EventArgs) Handles CPrioridad.LostFocus
        Combo_Dropped = False
    End Sub

    Private Sub CPrioridad_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CPrioridad.SelectedIndexChanged
        If Combo_TempValue = "" Then TPrioridad.Text = CPrioridad.SelectedItem.Trim : LCPrioridad.Text = TPrioridad.Text : HTexto.Focus()
    End Sub

    Private Sub CPrioridad_KeyPress(sender As Object, e As KeyPressEventArgs) Handles CPrioridad.KeyPress
        If Asc(e.KeyChar) = 13 Then TPrioridad.Text = CPrioridad.SelectedItem.Trim : LCPrioridad.Text = TPrioridad.Text : e.Handled = True : HTexto.Focus()
    End Sub

#End Region

#End Region

#Region " Administrador "

    Private Sub Administrador_Click(sender As Object, e As MouseEventArgs) Handles LAdministrador.Click, LAdministrador.DoubleClick, CAdministrador.DoubleClick, CAdministrador.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CAdministrador.Checked = True Then CAdministrador.Checked = False Else CAdministrador.Checked = True
    End Sub

#End Region

#Region " Informacion "

    Private Sub GInformacion_PIcono_Click(sender As Object, e As MouseEventArgs) Handles GInformacion_PIcono.Click
        If e.Button <> MouseButtons.Right Then Exit Sub
        'EjecutarAccion(Lista.SelectedItems(0).Tag, Comandos_Accion_ListaAccion.SelectedItems(0).Tag)
    End Sub

    Dim Thread_Info As New Threading.Thread(AddressOf Accion_Info)
    Public Sub Accion_Info(Ruta As String)
        If Ruta = "" Then
            GInformacion_LNombre.Value1 = "" : GInformacion_LNombre.Value2 = ""
            GInformacion_LCompany.Text = "" : GInformacion_LCompany.Refresh()
            GInformacion_LTipo.Text = "" : GInformacion_LTipo.Refresh()
            GInformacion_PIcono.Image = Nothing : GInformacion_PIcono.Refresh()
            If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
            GInformacion_SinInformacion.Visible = True
            GInformacion.Tag = ""
            isURL = False
            Exit Sub
        End If
        GInformacion_SinInformacion.Visible = False
        If GInformacion.Tag = Ruta.ToLower Then Exit Sub Else GInformacion.Tag = Ruta.ToLower
        If Internet.ValidarURL(Ruta) = True Then
            GInformacion_LNombre.Value1 = "" : GInformacion_LNombre.Value2 = ""
            GInformacion_LCompany.Text = "" : GInformacion_LCompany.Refresh()
            GInformacion_LTipo.Text = "" : GInformacion_LTipo.Refresh()
            GInformacion_PIcono.Image = Nothing : GInformacion_PIcono.Refresh()
            If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
            GInformacion_LoadingCircle.Active = True : GInformacion_LoadingCircle.Visible = True
        End If
Repite: If Thread_Info.ThreadState = Threading.ThreadState.Unstarted Or Thread_Info.ThreadState = Threading.ThreadState.Aborted Or Thread_Info.ThreadState = Threading.ThreadState.Stopped Then
            Thread_Info = New Threading.Thread(AddressOf ThreadSub_Info)
            Thread_Info.SetApartmentState(Threading.ApartmentState.STA)
            Thread_Info.IsBackground = True
            Thread_Info.Start(Ruta) : Application.DoEvents()
        ElseIf Thread_Info.ThreadState = Threading.ThreadState.Running Then
            Thread_Info.Abort() : Application.DoEvents() : GoTo Repite
        Else
            GoTo Repite
        End If
    End Sub

    Private Sub ThreadSub_Info(Ruta As String)
        Dim Titulo As String
Repite: Titulo = "" : Ruta = FMenu.ReplaceSpecialFolder(Ruta)
        If IO.File.Exists(Ruta) = False And IO.Directory.Exists(Ruta) = False Then
            Ruta = Replace(Ruta, "http", "http", 1, -1, vbTextCompare) : If Internet.ValidarURL(Ruta) = True Then
                If New Uri(Ruta).HostNameType = UriHostNameType.Dns Then
                    Dim URLTitulo As String = "" : Dim cURLTitulo() As String = New Uri(Ruta).Host.Split(".") : For Cuenta = 0 To cURLTitulo.Length - 2 : URLTitulo += cURLTitulo(Cuenta) + "." : Next
                    URLTitulo = URLTitulo.Substring(0, URLTitulo.Length - 1)

                    Dim Company As String = "" : Try : Company = Net.Dns.GetHostAddresses(New Uri(Ruta).Host)(0).ToString() : Catch : End Try : GInformacion_LCompany.Refresh()
                    Dim Icono As Image = Nothing : If IO.File.Exists(FMenu.KobaPathTemp + "icono.ico") = True Then IO.File.Delete(FMenu.KobaPathTemp + "icono.ico")
                    Try : My.Computer.Network.DownloadFile("https://t2.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=http://www." + New Uri(Ruta).Host.ToString + "&size=32", FMenu.KobaPathTemp + "icono.ico", "", "", False, 500, True) : Catch : Company = "" : GoTo Salta1 : End Try
                    Dim Contador As Integer : Do Until IO.File.Exists(FMenu.KobaPathTemp + "icono.ico") = True Or Contador = 100 : Contador += 1 : Threading.Thread.Sleep(10) : Loop
Salta1:             If IO.File.Exists(FMenu.KobaPathTemp + "icono.ico") = True Then Icono = Imagen.LoadImageClone(FMenu.KobaPathTemp + "icono.ico") : IO.File.Delete(FMenu.KobaPathTemp + "icono.ico") Else Icono = Nothing

                    GInformacion_LNombre.Text = URLTitulo : Rutas.SetFontSize(GInformacion_LNombre, 8) : GInformacion_LNombre.Text = ""
                    If URLTitulo.Length > 1 Then GInformacion_LNombre.Value1 = URLTitulo.Substring(0, 1).ToUpper : GInformacion_LNombre.Value2 = URLTitulo.Substring(1) Else GInformacion_LNombre.Value1 = URLTitulo.ToUpper : GInformacion_LNombre.Value2 = ""
                    If Company = "" Then Company = "Offline"
                    GInformacion_LCompany.Text = Company : GInformacion_LCompany.Refresh()
                    GInformacion_LTipo.Text = "Website" : GInformacion_LTipo.Refresh()

                    If Icono Is Nothing AndAlso IO.File.Exists(FMenu.KobaPath + "Iconos\WebIcons\" + Ruta.Replace("\", "_").Replace("/", "_").Replace("?", "_").Replace(".", "_").Replace(":", "").Replace(";", "") + ".ico") = True Then
                        Icono = Iconos.Cargar(FMenu.KobaPath + "Iconos\WebIcons\" + Ruta.Replace("\", "_").Replace("/", "_").Replace("?", "_").Replace(".", "_").Replace(":", "").Replace(";", "") + ".ico").ToBitmap
                    ElseIf Icono Is Nothing Then
                        Icono = My.Resources.WebPage.ToBitmap
                    Else
                        Iconos.Guardar(Icono, FMenu.KobaPath + "Iconos\WebIcons\" + Ruta.Replace("\", "_").Replace("/", "_").Replace("?", "_").Replace(".", "_").Replace(":", "").Replace(";", "") + ".ico")
                    End If : GInformacion_PIcono.Image = Icono : GInformacion_PIcono.Refresh()

                    If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
                    GInformacion_LoadingCircle.Visible = False : GInformacion_LoadingCircle.Active = False
                    isURL = True
                    Exit Sub
                End If
            Else
                Dim cRuta = Rutas.GetTargetPath(Ruta) : If IO.File.Exists(cRuta) = True Then
                    Ruta = cRuta : GoTo Salta2
                Else
                    Dim cTitulo As String = Ruta : GInformacion_LNombre.Text = cTitulo : Rutas.SetFontSize(GInformacion_LNombre, 8, 7) : GInformacion_LNombre.Text = ""
                    If IO.Path.GetDirectoryName(Ruta) <> "" Then cTitulo = Rutas.Acortar_Ruta(Ruta, GInformacion_LNombre)
                    If cTitulo.Length > 1 Then GInformacion_LNombre.Value1 = cTitulo.Substring(0, 1).ToUpper : GInformacion_LNombre.Value2 = cTitulo.Substring(1) Else GInformacion_LNombre.Value1 = cTitulo.ToUpper : GInformacion_LNombre.Value2 = ""
                    GInformacion_LCompany.Text = "" : GInformacion_LCompany.Refresh()
                    If Ruta.Length = 3 AndAlso Ruta.EndsWith(":\") Then GInformacion_LTipo.Text = "Unidad Desconocida" Else GInformacion_LTipo.Text = "Aplicacion Desconocida"
                    Rutas.SetFontSize(GInformacion_LTipo, 7, 4) : GInformacion_LTipo.Refresh()
                    GInformacion_PIcono.Image = My.Resources.Accion_Aplicacion.ToBitmap : GInformacion_PIcono.Refresh()
                    If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
                    GInformacion_LoadingCircle.Visible = False : GInformacion_LoadingCircle.Active = False
                    Exit Sub
                End If
            End If
        End If
Salta2: If IO.File.Exists(Ruta) = True Then Titulo = FileVersionInfo.GetVersionInfo(Ruta).FileDescription
        If Titulo Is Nothing Then Titulo = ""
        If Titulo = "" Then
            If IO.Directory.Exists(Ruta) = True Then
                Titulo = Rutas.Acortar_Ruta(Ruta, GInformacion_LNombre)
            Else
                If IO.Path.GetDirectoryName(Ruta) <> "" Then Titulo = Rutas.Acortar_Ruta(Ruta, GInformacion_LNombre) Else Titulo = IO.Path.GetFileNameWithoutExtension(Ruta)
            End If
        End If
        If Titulo.ToLower = "procesador de comandos de windows" Then Titulo = "Comando de MS-DOS"
        GInformacion_LNombre.Text = Titulo : Rutas.SetFontSize(GInformacion_LNombre, 8) : GInformacion_LNombre.Text = ""
        If Titulo.Length > 1 Then GInformacion_LNombre.Value1 = Titulo.Substring(0, 1).ToUpper : GInformacion_LNombre.Value2 = Titulo.Substring(1) Else GInformacion_LNombre.Value1 = Titulo.ToUpper : GInformacion_LNombre.Value2 = ""
        Dim Compañia As String = "" : If IO.File.Exists(Ruta) = True Then Compañia = FileVersionInfo.GetVersionInfo(Ruta).CompanyName
        If Compañia Is Nothing Then Compañia = ""
        GInformacion_LCompany.Text = Compañia : GInformacion_LCompany.Refresh()
        GInformacion_LTipo.Text = FileType(Ruta) : Rutas.SetFontSize(GInformacion_LTipo, 7, 4) : GInformacion_LTipo.Refresh()

        If GInformacion_LTipo.Text = "Archivo" Then If IO.Directory.Exists(Ruta) = True Then GInformacion_LTipo.Text = "Carpeta" : GInformacion_LTipo.Refresh() : GInformacion_LCompany.Text = "Microsoft Corporation" : GInformacion_LCompany.Refresh()
        Select Case GInformacion_LTipo.Text
            Case "Carpeta"
                GInformacion_PIcono.Image = My.Resources.Accion_AbrirCarpeta.ToBitmap
                If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
            Case "Disco local"
                GInformacion_LCompany.Text = "Microsoft Corporation" : GInformacion_LCompany.Refresh()
                GInformacion_PIcono.Image = My.Resources.DiscoLocal.ToBitmap
                If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
            Case "Acceso directo"
                Ruta = Rutas.GetTargetPath(Ruta) : GoTo Repite
            Case "Archivo por lotes de Windows"
                GInformacion_LCompany.Text = "Microsoft Corporation" : GInformacion_LCompany.Refresh()
                GInformacion_PIcono.Image = My.Resources.Accion_BAT.ToBitmap
                If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
            Case Else
                GInformacion_PIcono.Image = Iconos.ExtraerIcono(Ruta, My.Resources.Accion_Aplicacion.ToBitmap)
                If isDefault = True Then PIcono.Image = GInformacion_PIcono.Image
        End Select
        GInformacion_LNombre.Visible = True : GInformacion_LCompany.Visible = True : GInformacion_LTipo.Visible = True : GInformacion_PIcono.Visible = True
        GInformacion_LoadingCircle.Visible = False : GInformacion_LoadingCircle.Active = False
        End Sub

#End Region

#Region " Help "

    Private Sub LHelp_Click(sender As Object, e As MouseEventArgs) Handles LHelp.Click, LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Comandos Rápidos" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub LHelp_MouseEnter(sender As Object, e As EventArgs) Handles LHelp.MouseEnter
        LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub LHelp_MouseLeave(sender As Object, e As EventArgs) Handles LHelp.MouseLeave
        LHelp.ForeColor1 = Color.Gray
    End Sub

    Private Sub PModo_Click(sender As Object, e As EventArgs) Handles PModo.DoubleClick, PModo.Click

    End Sub

    Private Sub PPrioridad_Click(sender As Object, e As EventArgs) Handles PPrioridad.DoubleClick, PPrioridad.Click

    End Sub

    Private Sub BPlay_Click(sender As Object, e As EventArgs) Handles BPlay.Click

    End Sub

    Private Sub PIcono_Click(sender As Object, e As EventArgs) Handles PIcono.Click

    End Sub

    Private Sub LHelp_Click(sender As Object, e As EventArgs) Handles LHelp.DoubleClick, LHelp.Click

    End Sub

    Private Sub BAceptar_Click(sender As Object, e As EventArgs) Handles BAceptar.Click

    End Sub

    Private Sub BCancelar_Click(sender As Object, e As EventArgs) Handles BCancelar.Click

    End Sub

    Private Sub BExaminar_Sonido_Click(sender As Object, e As EventArgs) Handles BExaminar_Sonido.Click

    End Sub

    Private Sub BExaminar_Ruta_Click(sender As Object, e As EventArgs) Handles BExaminar_Ruta.Click

    End Sub

    Private Sub LCSonido_Click(sender As Object, e As EventArgs) Handles TSonido.Click, LCSonido.Click

    End Sub

    Private Sub GInformacion_PIcono_Click(sender As Object, e As EventArgs) Handles GInformacion_PIcono.Click

    End Sub

    Private Sub Administrador_Click(sender As Object, e As EventArgs) Handles LAdministrador.DoubleClick, LAdministrador.Click, CAdministrador.DoubleClick, CAdministrador.Click

    End Sub

    Private Sub TPrioridad_Click(sender As Object, e As EventArgs) Handles TPrioridad.Click, LCPrioridad.Click

    End Sub

    Private Sub TModo_Click(sender As Object, e As EventArgs) Handles TModo.Click, LCModo.Click

    End Sub

#End Region

#Region " Aceptar / Cancelar "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        If GInformacion_LoadingCircle.Visible = True Then Exit Sub
        If TNombre.Text.Trim = "" Then TNombre.Focus() : Exit Sub
        If e.Button = MouseButtons.Left Then
            If IO.File.Exists(TRuta.Tag) = False And IO.Directory.Exists(TRuta.Tag) = False AndAlso Internet.ValidarURL(TRuta.Tag) = True Then
                Tipo = "Pagina Web"
            ElseIf IO.File.Exists(TRuta.Tag) = False And IO.Directory.Exists(TRuta.Tag) = True Then
                Tipo = "Abrir Carpeta"
            Else
                Tipo = "Ejecutar Aplicacion"
            End If
            Cancelado = False : Close()
        End If
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If GInformacion_LoadingCircle.Visible = True Then Exit Sub
        If e.Button = MouseButtons.Left Then Close()
    End Sub

#End Region

#Region " Tooltips "

    Private Sub ComandoRapido_Tooltips(sender As Object, e As EventArgs) Handles LRuta.MouseEnter, BExaminar_Ruta.MouseEnter, LParametros.MouseEnter, LModo.MouseEnter, LPrioridad.MouseEnter, CAdministrador.MouseEnter, LAdministrador.MouseEnter, GInformacion_LNombre.MouseEnter, LNombre.MouseEnter, PIcono.MouseEnter, LSonido.MouseEnter, BPlay.MouseEnter, BExaminar_Sonido.MouseEnter, LCModo.MouseEnter, LCPrioridad.MouseEnter, LCSonido.MouseEnter
        Select Case sender.Name
            Case "LNombre" : FMenu.Show_Tooltip(sender, "Nombre del comando")
            Case "PIcono" : FMenu.Show_Tooltip(sender, "Icono que se mostrará en el comando y QuickMenu" + vbCrLf + vbCrLf + "Click Izquierdo: Seleccionar un icono" + vbCrLf + "Click Derecho: Mostrar el menú desplegable" + vbCrLf + "Click Central: Establecer icono predeterminado")
            Case "LSonido" : FMenu.Show_Tooltip(sender, "Sonido que se reproduce al ejecutarse el comando")
            Case "LCSonido" : FMenu.Show_Tooltip(sender, "Sonido que se reproduce al ejecutarse el comando" + vbCrLf + vbCrLf + "Click Derecho: Editar manualmente")
            Case "BPlay" : FMenu.Show_Tooltip(sender, "Reproduce el sonido seleccionado")
            Case "BExaminar_Sonido" : FMenu.Show_Tooltip(sender, "Seleccionar un archivo de audio")
            Case "LRuta" : FMenu.Show_Tooltip(sender, "Archivo, carpeta o página web que se abrirá")
            Case "BExaminar_Ruta" : FMenu.Show_Tooltip(sender, "Click Izquierdo: Seleccionar un archivo" + vbCrLf + "Click Derecho: Seleccionar una carpeta")
            Case "LParametros" : FMenu.Show_Tooltip(sender, "Parámetros con el que se ejecutará la aplicación")
            Case "CAdministrador", "LAdministrador" : FMenu.Show_Tooltip(sender, "Ejecuta la aplicación con privilegios de administrador")
            Case "LModo", "LCModo" : FMenu.Show_Tooltip(sender, "Modo en el que se ejecutará la aplicación" + vbCrLf + "No todas las aplicaciones lo soportan")
            Case "LPrioridad", "LCPrioridad" : FMenu.Show_Tooltip(sender, "Prioridad de CPU que se le asignará a la aplicación")
            Case "GInformacion_LNombre" : If (TRuta.Tag <> "" AndAlso IO.Path.GetDirectoryName(TRuta.Tag) <> "") And TRuta.Text <> TRuta.Tag AndAlso TRuta.Tag <> GInformacion_LNombre.Value1 + GInformacion_LNombre.Value2 Then FMenu.Show_Tooltip(sender, TRuta.Tag.ToString)
        End Select
    End Sub

#End Region

End Class

#End Region