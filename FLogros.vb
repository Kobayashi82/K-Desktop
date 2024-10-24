
#Region " FLogros "

Public Class FLogros

#Region " Variables "

    Dim Combo_Dropped, Buscar_Focused As Boolean : Dim Combo_TempValue As String
    Dim Filtro As String = "FechaUp"
    Dim Show_Logros() As Logros.LogroST

#End Region

#Region " Formulario "

#Region " Load "

    Private Sub FLogros_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top) : CNotificaciones.Checked = Logros.NotificacionesLogros
    End Sub

    Private Sub FLogros_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me) : CLogros.SelectedIndex = 0 : Actualizar_Estadisticas()
    End Sub

#End Region

#Region " KeyPress "

    Private Sub FLogros_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 Then
            If Combo_Dropped = True Then CLogros.DroppedDown = False : Combo_Dropped = False : Exit Sub
            If Buscar_Focused = True Then Buscar_Focused = False : HTexto.Focus() : e.Handled = True : TBuscar2.Visible = False : PBuscar.Image = My.Resources.Search_Off : BDummy.Visible = False : BLogros.Location = New Point(8, 35) : BLogros.Size = New Size(337, 21) : CLogros.Location = New Point(9, 35) : CLogros.Size = New Size(336, 21) : Exit Sub
            e.Handled = True : Close()
        End If
    End Sub

#End Region

#Region " Controles "

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True
    End Sub

    Private Sub Controles_Click(sender As Object, e As EventArgs) Handles FTheme.Click, BEstadisticas.Click, Panel1.Click, Panel2.Click, Label1.Click
        HTexto.Focus() : If Panel1.Visible = True Then Panel1.Focus()
    End Sub

#End Region

#End Region

#Region " Logros "

#Region " Cargar "

    Private Sub BLogros_Click(sender As Object, e As MouseEventArgs) Handles BLogros.Click, BLogros.DoubleClick
        If e IsNot Nothing AndAlso e.Button <> MouseButtons.Left Then Exit Sub
        If Control.ModifierKeys = Keys.Control Then
            If FMenu.ShowMessageBox("¿Resetar todos los logros?", FMenu.Version, MsgBoxStyle.YesNo,) = DialogResult.OK Then
                FMenu.ShowMessageBox("Los logros se han resetado", FMenu.Version,)
                Logros.Reset() : Actualizar_Logros()
            End If : Exit Sub
        End If
        If e IsNot Nothing AndAlso GLogros.Visible = True Then
            CLogros.DroppedDown = True : CLogros.Focus()
            Exit Sub
        ElseIf e IsNot Nothing AndAlso GLogros.Visible = False Then
            BLogros.ForeColor1 = Color.FromArgb(205, 150, 0)
            BEstadisticas.ForeColor1 = Color.FromArgb(102, 102, 102)
            GEstadisticas.Visible = False
            GLogros.Visible = True
            Exit Sub
        End If
        If e Is Nothing AndAlso PBuscar.Visible = True Then Panel1.Focus()
        Actualizar_Logros()
    End Sub

    Private Sub Add_Group(Indice As Integer, Position As Point)
        Dim Logro_Group As New NSGroupBox With {.Size = New Size(260, 67), .Location = Position, .Name = "Group" + Indice.ToString, .SubTitle = "", .Title = "", .Text = ""}
        Dim Logro_Imagen As New PictureBox With {.Size = New Size(72, 50), .Location = New Point(8, 9), .Name = "Imagen" + Indice.ToString, .SizeMode = PictureBoxSizeMode.StretchImage, .BorderStyle = BorderStyle.FixedSingle, .Image = Show_Logros(Indice).Imagen}
        Dim Logro_Texto As New Label With {.Size = New Size(166, 36), .Location = New Point(86, 9), .Name = "Texto" + Indice.ToString, .AutoSize = False, .TextAlign = ContentAlignment.MiddleCenter, .Font = TLogro.Font, .Text = Show_Logros(Indice).Texto, .ForeColor = TLogro.ForeColor}
        Dim Logro_LNombre As New NSLabel With {.Size = New Size(166, 17), .Location = New Point(Position.X + 8, Position.Y - 10), .Name = "LNombred" + Indice.ToString, .Font = LNombre.Font, .Text = Show_Logros(Indice).Nombre, .ForeColor = LNombre.ForeColor, .ForeColor1 = LNombre.ForeColor1, .Value1 = "", .Value2 = ""}

        AddHandler Logro_Group.Click, AddressOf Controles_Click
        AddHandler Logro_Imagen.Click, AddressOf Controles_Click
        AddHandler Logro_Texto.Click, AddressOf Controles_Click
        AddHandler Logro_LNombre.Click, AddressOf Controles_Click

        If Show_Logros(Indice).Desbloqueado = True Then
            Dim Logro_LDesbloqueado As New Label With {.Size = New Size(166, 36), .AutoSize = True, .Location = New Point(92, 47), .Name = "LDesbloqueado" + Indice.ToString, .Font = LDesbloqueadoEl.Font, .Text = "Desbloqueado el día:", .ForeColor = LDesbloqueadoEl.ForeColor}
            Dim Logro_LFecha As New Label With {.Size = New Size(166, 36), .AutoSize = True, .Location = New Point(189, 47), .Name = "LFechaD" + Indice.ToString, .Font = LFecha.Font, .Text = Show_Logros(Indice).Fecha, .ForeColor = LFecha.ForeColor}
            AddHandler Logro_LDesbloqueado.Click, AddressOf Controles_Click
            AddHandler Logro_LFecha.Click, AddressOf Controles_Click
            Logro_Group.Controls.Add(Logro_LDesbloqueado)
            Logro_Group.Controls.Add(Logro_LFecha)
        Else
            Logro_LNombre.ForeColor1 = Logro_Texto.ForeColor
            Logro_Texto.Height = 50
            GrayScale(Logro_Imagen)
        End If
        Application.DoEvents()
        Logro_Group.Controls.Add(Logro_Imagen)
        Logro_Group.Controls.Add(Logro_Texto)
        Logro_LNombre.Width = Rutas.GetTextSize(Logro_LNombre).Width
        Panel1.Controls.Add(Logro_LNombre)
        Panel1.Controls.Add(Logro_Group)
    End Sub

#Region " Combo "

    Private Sub CLogros_DropDownClosed(sender As Object, e As EventArgs) Handles CLogros.DropDownClosed
        Combo_TempValue = ""
    End Sub

    Private Sub CLogros_LostFocus(sender As Object, e As EventArgs) Handles CLogros.LostFocus
        Combo_Dropped = False
    End Sub

    Private Sub CLogros_DropDown(sender As Object, e As EventArgs) Handles CLogros.DropDown
        Combo_Dropped = True : Combo_TempValue = BLogros.Tag
        If CLogros.Items.Contains(BLogros.Tag) = True Then CLogros.SelectedItem = BLogros.Tag Else CLogros.SelectedIndex = 0
    End Sub

    Private Sub CLogros_SelectedIndexChanged(sender As Object, e As EventArgs) Handles CLogros.SelectedIndexChanged
        If Combo_TempValue = "" Then
            If BLogros.Tag <> CLogros.SelectedItem.ToString Then
                BLogros.Tag = CLogros.SelectedItem.ToString
                BLogros.Text = CLogros.SelectedItem.ToString
                BLogros.Invalidate()
                BLogros_Click(sender, Nothing)
            End If
        End If
    End Sub

#End Region

#End Region

#Region " Filtrar "

    Private Sub Filtrar()
        Dim cLogros(0) As Logros.LogroST
        For Cuenta = 0 To Show_Logros.Length - 1 : Show_Logros(Cuenta).Ignorar = False : Next
        Select Case Filtro
            Case "FechaUp"
                Dim ListaDate As New List(Of DateTime)
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = True AndAlso Show_Logros(Cuenta).Fecha <> "" Then ListaDate.Add(Show_Logros(Cuenta).Fecha)
                Next
                ListaDate.Sort()
                Dim CContador As Integer
                For Cuenta = ListaDate.Count - 1 To 0 Step -1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = True AndAlso Show_Logros(Cuenta2).Ignorar = False AndAlso Show_Logros(Cuenta2).Fecha = ListaDate(Cuenta).ToString("dd/MM/yyyy") Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Show_Logros(Cuenta2).Ignorar = True
                        End If
                    Next
                Next
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False Then
                        ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta) : CContador += 1
                    End If
                Next
            Case "FechaDown"
                Dim CContador As Integer
                Dim ListaDate As New List(Of DateTime)
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = True AndAlso Show_Logros(Cuenta).Fecha <> "" Then ListaDate.Add(Show_Logros(Cuenta).Fecha)
                Next
                ListaDate.Sort()
                For Cuenta = 0 To ListaDate.Count - 1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = True AndAlso Show_Logros(Cuenta2).Ignorar = False AndAlso Show_Logros(Cuenta2).Fecha = ListaDate(Cuenta).ToString("dd/MM/yyyy") Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Show_Logros(Cuenta2).Ignorar = True
                        End If
                    Next
                Next
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False Then
                        ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta) : CContador += 1
                    End If
                Next
            Case "NombreUp"
                Dim CContador As Integer
                Dim ListaDate As New List(Of String)
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = True Then ListaDate.Add(Show_Logros(Cuenta).Nombre)
                Next
                ListaDate.Sort()
                For Cuenta = 0 To ListaDate.Count - 1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = True AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Exit For
                        End If
                    Next
                Next
                ListaDate.Clear()
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False Then ListaDate.Add(Show_Logros(Cuenta).Nombre)
                Next
                ListaDate.Sort()
                For Cuenta = 0 To ListaDate.Count - 1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = False AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Exit For
                        End If
                    Next
                Next
            Case "NombreDown"
                Dim CContador As Integer
                Dim ListaDate As New List(Of String)
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = True Then
                        ListaDate.Add(Show_Logros(Cuenta).Nombre)
                    End If
                Next
                ListaDate.Sort()
                For Cuenta = ListaDate.Count - 1 To 0 Step -1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = True AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Exit For
                        End If
                    Next
                Next
                ListaDate.Clear()
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False Then ListaDate.Add(Show_Logros(Cuenta).Nombre)
                Next
                ListaDate.Sort()
                For Cuenta = ListaDate.Count - 1 To 0 Step -1
                    For Cuenta2 = 0 To Show_Logros.Length - 1
                        If Show_Logros(Cuenta2).Desbloqueado = False AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                            ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                            Exit For
                        End If
                    Next
                Next
        End Select
        Show_Logros = cLogros
    End Sub

#End Region

#Region " Filtrar Texto "

    Private Sub Filtrar_Texto()
        Show_Logros = Logros.Logros
        If TBuscar2.Text = "" Then Actualizar_Logros(False) : Exit Sub
        Dim cLogros(0) As Logros.LogroST
        Dim CContador As Integer
        Dim ListaDate As New List(Of String)
        For Cuenta = 0 To Show_Logros.Length - 1
            If Show_Logros(Cuenta).Desbloqueado = True AndAlso (Show_Logros(Cuenta).Nombre.ToLower.Contains(TBuscar2.Text.ToLower) = True Or Show_Logros(Cuenta).Texto.ToLower.Contains(TBuscar2.Text.ToLower) = True) Then ListaDate.Add(Show_Logros(Cuenta).Nombre)
        Next
        ListaDate.Sort()
        For Cuenta = 0 To ListaDate.Count - 1
            For Cuenta2 = 0 To Show_Logros.Length - 1
                If Show_Logros(Cuenta2).Desbloqueado = True AndAlso (Show_Logros(Cuenta2).Nombre.ToLower.Contains(TBuscar2.Text.ToLower) = True Or Show_Logros(Cuenta2).Texto.ToLower.Contains(TBuscar2.Text.ToLower) = True) AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                    ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                    Exit For
                End If
            Next
        Next
        ListaDate.Clear()
        For Cuenta = 0 To Show_Logros.Length - 1
            If Show_Logros(Cuenta).Desbloqueado = False AndAlso (Show_Logros(Cuenta).Nombre.ToLower.Contains(TBuscar2.Text.ToLower) = True Or Show_Logros(Cuenta).Texto.ToLower.Contains(TBuscar2.Text.ToLower) = True) Then ListaDate.Add(Show_Logros(Cuenta).Nombre)
        Next
        ListaDate.Sort()
        For Cuenta = 0 To ListaDate.Count - 1
            For Cuenta2 = 0 To Show_Logros.Length - 1
                If Show_Logros(Cuenta2).Desbloqueado = False AndAlso (Show_Logros(Cuenta2).Nombre.ToLower.Contains(TBuscar2.Text.ToLower) = True Or Show_Logros(Cuenta2.ToString).Texto.ToLower.Contains(TBuscar2.Text.ToLower) = True) AndAlso Show_Logros(Cuenta2).Nombre = ListaDate(Cuenta) Then
                    ReDim Preserve cLogros(CContador) : cLogros(CContador) = Show_Logros(Cuenta2) : CContador += 1
                    Exit For
                End If
            Next
        Next
        Show_Logros = cLogros
        Actualizar_Logros(False)
    End Sub

#End Region

#Region " Actualizar Logros "

    Public Sub Actualizar_Logros(Optional Reset As Boolean = True)
        If Reset = True Then Show_Logros = Logros.Logros
        If TBuscar2.Text <> "" AndAlso Reset = True Then Filtrar_Texto() Else Filtrar()
        If Show_Logros.Length <= 2 And Show_Logros(0).Nombre = "" Then
            BLogros.Text = BLogros.Tag + " - (0)" : BLogros.Invalidate() : Application.DoEvents()
            Panel1.Controls.Clear()
            Exit Sub
        End If
        Dim Contador As Integer : Select Case BLogros.Tag
            Case "Logros (Todos)"
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False AndAlso Show_Logros(Cuenta).Oculto = True Then Continue For
                    Contador += 1
                Next
            Case "Logros (Bloqueados)"
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = False AndAlso Show_Logros(Cuenta).Oculto = False Then Contador += 1
                Next
            Case "Logros (Desbloqueados)"
                For Cuenta = 0 To Show_Logros.Length - 1
                    If Show_Logros(Cuenta).Desbloqueado = True Then Contador += 1
                Next
        End Select : BLogros.Text = BLogros.Tag + " - (" + Contador.ToString + ")" : BLogros.Invalidate() : Application.DoEvents()
        Panel1.AutoScroll = False
        Panel1.SuspendLayout()
        Panel1.Controls.Clear()
        For Each P As Control In Panel1.Controls : P.Visible = False : Next
        LEspere.Visible = True
        Dim XPos, YPos As Integer
        Dim Col As Integer
        XPos = 60 : YPos = 20
        For Cuenta = 0 To Show_Logros.Length - 1
            Select Case BLogros.Tag
                Case "Logros (Todos)"
                    If Show_Logros(Cuenta).Desbloqueado = False AndAlso Show_Logros(Cuenta).Oculto = True Then Continue For
                    Application.DoEvents()
                    Add_Group(Cuenta, New Point(XPos, YPos))
                    Application.DoEvents()
                    Select Case Col
                        Case 0 : Col = 1 : XPos = 420
                        Case 1 : Col = 0 : XPos = 60 : YPos += 100
                    End Select
                Case "Logros (Bloqueados)"
                    If Show_Logros(Cuenta).Desbloqueado = False AndAlso Show_Logros(Cuenta).Oculto = False Then
                        Application.DoEvents()
                        Add_Group(Cuenta, New Point(XPos, YPos))
                        Application.DoEvents()
                        Select Case Col
                            Case 0 : Col = 1 : XPos = 420
                            Case 1 : Col = 0 : XPos = 60 : YPos += 100
                        End Select
                    End If
                Case "Logros (Desbloqueados)"
                    If Show_Logros(Cuenta).Desbloqueado = True Then
                        Application.DoEvents()
                        Add_Group(Cuenta, New Point(XPos, YPos))
                        Application.DoEvents()
                        Select Case Col
                            Case 0 : Col = 1 : XPos = 420
                            Case 1 : Col = 0 : XPos = 60 : YPos += 100
                        End Select
                    End If
            End Select
        Next
        Panel1.ResumeLayout()
        Panel1.AutoScroll = True
        LEspere.Visible = False
        If GLogros.Visible = True And PBuscar.Visible = True Then Panel1.Focus()
    End Sub

#End Region

#Region " Ordenar "

    Private Sub PSort_Nombre_Click(sender As Object, e As MouseEventArgs) Handles PSort_Nombre.Click
        HTexto.Focus() : If Panel1.Visible = False Or LEspere.Visible = True Then Exit Sub
        If Filtro = "NombreUp" Then Filtro = "NombreDown" Else Filtro = "NombreUp"
        Actualizar_Logros(False)
        If Panel1.Visible = True Then Panel1.Focus()
    End Sub

    Private Sub PSort_Fecha_Click(sender As Object, e As MouseEventArgs) Handles PSort_Fecha.Click
        HTexto.Focus() : If Panel1.Visible = False Or LEspere.Visible = True Then Exit Sub
        If Filtro = "FechaUp" Then Filtro = "FechaDown" Else Filtro = "FechaUp"
        Actualizar_Logros(False)
        If Panel1.Visible = True Then Panel1.Focus()
    End Sub

    Private Sub PSort_MouseEnter(sender As Object, e As EventArgs) Handles PSort_Nombre.MouseEnter, PSort_Fecha.MouseEnter
        If Panel1.Controls.Count = 0 Or Panel1.Visible = False Then Exit Sub
        Select Case sender.Name
            Case "PSort_Nombre" : If Filtro = "NombreUp" Then sender.Image = My.Resources.Logros_Sort_Nombre_On Else sender.Image = My.Resources.Logros_Sort_Nombre_On
            Case "PSort_Fecha" : If Filtro = "FechaUp" Then sender.Image = My.Resources.Logros_Sort_Fecha_On Else sender.Image = My.Resources.Logros_Sort_Fecha_On
        End Select
    End Sub

    Private Sub PSort_MouseLeave(sender As Object, e As EventArgs) Handles PSort_Nombre.MouseLeave, PSort_Fecha.MouseLeave
        Select Case sender.Name
            Case "PSort_Nombre" : If Filtro = "NombreUp" Then sender.Image = My.Resources.Logros_Sort_Nombre_Off Else sender.Image = My.Resources.Logros_Sort_Nombre_Off
            Case "PSort_Fecha" : If Filtro = "FechaUp" Then sender.Image = My.Resources.Logros_Sort_Fecha_Off Else sender.Image = My.Resources.Logros_Sort_Fecha_Off
        End Select
    End Sub
#End Region

#Region " Buscar "

    Private Sub TBuscar_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TBuscar2.KeyPress
        If Asc(e.KeyChar) = 13 Then
            e.Handled = True : TBuscar2.Tag = TBuscar2.Text : HTexto.Focus() : Filtrar_Texto()
            If Panel1.Visible = True Then Panel1.Focus()
        End If
    End Sub

    Private Sub TBuscar2_GotFocus(sender As Object, e As EventArgs) Handles TBuscar2.GotFocus
        Buscar_Focused = True
    End Sub

    Private Sub TBuscar2_LostFocus(sender As Object, e As EventArgs) Handles TBuscar2.LostFocus
        Buscar_Focused = False
        TBuscar2.Text = TBuscar2.Tag
    End Sub

    Private Sub PBuscar_Click(sender As Object, e As MouseEventArgs) Handles PBuscar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TBuscar2.Visible = False Then
            TBuscar2.Visible = True
            BDummy.Visible = True
            BLogros.Location = New Point(130, 35) : BLogros.Size = New Size(215, 21)
            CLogros.Location = New Point(131, 35) : CLogros.Size = New Size(213, 21)
            TBuscar2.Focus()
        Else
            TBuscar2.Visible = False
            BDummy.Visible = False
            BLogros.Location = New Point(8, 35) : BLogros.Size = New Size(337, 21)
            CLogros.Location = New Point(9, 35) : CLogros.Size = New Size(335, 21)
        End If
    End Sub

    Private Sub PBuscar_MouseEnter(sender As Object, e As EventArgs) Handles PBuscar.MouseEnter
        PBuscar.Image = My.Resources.Search_On
    End Sub

    Private Sub PBuscar_MouseLeave(sender As Object, e As EventArgs) Handles PBuscar.MouseLeave
        If TBuscar2.Visible = False Then PBuscar.Image = My.Resources.Search_Off
    End Sub

#End Region

#Region " Checks "

    Private Sub Notificaciones_Click(sender As Object, e As MouseEventArgs) Handles CNotificaciones.Click, CNotificaciones.DoubleClick, LNotificaciones.Click, LNotificaciones.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CNotificaciones.Checked = True Then CNotificaciones.Checked = False Else CNotificaciones.Checked = True
        Registro.SetRegKey(FMenu.RegPath + "\Opciones", "LogrosNotif", CNotificaciones.Checked) : Logros.NotificacionesLogros = CNotificaciones.Checked
    End Sub

#End Region

#Region " GrayScale "

    Private Sub GrayScale(ByRef PImagen As PictureBox)
        Dim X, Y, clr As Integer
        Dim bm As New Bitmap(PImagen.Image)
        For X = 0 To bm.Width - 1
            For Y = 0 To bm.Height - 1
                clr = (CInt(bm.GetPixel(X, Y).R) + bm.GetPixel(X, Y).G + bm.GetPixel(X, Y).B) \ 3
                bm.SetPixel(X, Y, Color.FromArgb(clr, clr, clr))
            Next
        Next : PImagen.Image = bm
    End Sub

#End Region

#End Region

#Region " Estadisticas "

#Region " Cargar "

    Private Sub BEstadisticas_Click(sender As Object, e As MouseEventArgs) Handles BEstadisticas.Click, BEstadisticas.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If Control.ModifierKeys = Keys.Control Then
            If FMenu.ShowMessageBox("¿Resetar todas las estadisticas?", FMenu.Version, MsgBoxStyle.YesNo,) = DialogResult.OK Then
                FMenu.ShowMessageBox("Las estadisticas se han resetado", FMenu.Version,)
                Estadisticas.Reset() : Actualizar_Estadisticas()
            End If : Exit Sub
        End If
        BLogros.ForeColor1 = Color.FromArgb(102, 102, 102)
        BEstadisticas.ForeColor1 = Color.FromArgb(205, 150, 0)
        GLogros.Visible = False
        GEstadisticas.Visible = True
    End Sub

    Private Sub LEstadisticas_MouseEnter(sender As Object, e As EventArgs) Handles LComandos_Creados.MouseEnter, LComandos_Creados.MouseEnter, LComandos_Eliminados.MouseEnter, LComandos_Ejecutados.MouseEnter, LAcciones_Creadas.MouseEnter, LAcciones_Eliminadas.MouseEnter, LAcciones_Ejecutadas.MouseEnter, LKBoard_Creados.MouseEnter, LKBoard_Eliminados.MouseEnter, LKBoard_Ejecutados.MouseEnter, LQuickMenus_Creados.MouseEnter, LQuickMenus_Eliminados.MouseEnter, LQuickMenus_Abiertos.MouseEnter, LMacros_Grabadas.MouseEnter, LMacros_Ejecutadas.MouseEnter, LIconos_Modificados.MouseEnter, LSalidasAudio.MouseEnter, LTopMost_Aplicados.MouseEnter, LLogros_Desbloqueados.MouseEnter, LCapturas_Pantalla.MouseEnter, LGrabaciones_Pantalla.MouseEnter, LGrabaciones_Audio.MouseEnter, LPapelera_Vaciada.MouseEnter, LApagado_Equipo.MouseEnter, LEnviado_Telegram.MouseEnter, LDesactivado_En.MouseEnter, LArchivos_Ocultados.MouseEnter, LSonidos_Reproducidos.MouseEnter, LAplicaciones_Ejecutadas.MouseEnter, LPaginas_Webs.MouseEnter, LCarpetas_Abiertas.MouseEnter, LCMD_Ejecutados.MouseEnter, LBAT_Ejecutados.MouseEnter, LUDP_Ejecutados.MouseEnter, LAcciones_Unicas.MouseEnter, LKDesktop_Iniciado.MouseEnter, LKDesktop_Instalado.MouseEnter
        Dim Separador As Object = GEstadisticas.Controls.Find("S" + sender.Name.Substring(1), True).FirstOrDefault() : Separador.LineColor = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub LEstadisticas_MouseLeave(sender As Object, e As EventArgs) Handles LComandos_Creados.MouseLeave, LComandos_Creados.MouseLeave, LComandos_Eliminados.MouseLeave, LComandos_Ejecutados.MouseLeave, LAcciones_Creadas.MouseLeave, LAcciones_Eliminadas.MouseLeave, LAcciones_Ejecutadas.MouseLeave, LKBoard_Creados.MouseLeave, LKBoard_Eliminados.MouseLeave, LKBoard_Ejecutados.MouseLeave, LQuickMenus_Creados.MouseLeave, LQuickMenus_Eliminados.MouseLeave, LQuickMenus_Abiertos.MouseLeave, LMacros_Grabadas.MouseLeave, LMacros_Ejecutadas.MouseLeave, LIconos_Modificados.MouseLeave, LSalidasAudio.MouseLeave, LTopMost_Aplicados.MouseLeave, LLogros_Desbloqueados.MouseLeave, LCapturas_Pantalla.MouseLeave, LGrabaciones_Pantalla.MouseLeave, LGrabaciones_Audio.MouseLeave, LPapelera_Vaciada.MouseLeave, LApagado_Equipo.MouseLeave, LEnviado_Telegram.MouseLeave, LDesactivado_En.MouseLeave, LArchivos_Ocultados.MouseLeave, LSonidos_Reproducidos.MouseLeave, LAplicaciones_Ejecutadas.MouseLeave, LPaginas_Webs.MouseLeave, LCarpetas_Abiertas.MouseLeave, LCMD_Ejecutados.MouseLeave, LBAT_Ejecutados.MouseLeave, LUDP_Ejecutados.MouseLeave, LAcciones_Unicas.MouseLeave, LKDesktop_Iniciado.MouseLeave, LKDesktop_Instalado.MouseLeave
        Dim Separador As Object = GEstadisticas.Controls.Find("S" + sender.Name.Substring(1), True).FirstOrDefault() : Separador.LineColor = Color.FromArgb(35, 35, 35)
    End Sub

#End Region

#Region " Actualizar "

    Public Sub Actualizar_Estadisticas()
        TComandos_Creados.Text = Estadisticas.Estadisticas.Comandos_Creados.ToString
        TComandos_Eliminados.Text = Estadisticas.Estadisticas.Comandos_Eliminados.ToString
        TComandos_Ejecutados.Text = Estadisticas.Estadisticas.Comandos_Ejecutados.ToString

        TAcciones_Creadas.Text = Estadisticas.Estadisticas.Acciones_Creadas.ToString
        TAcciones_Eliminadas.Text = Estadisticas.Estadisticas.Acciones_Eliminadas.ToString
        TAcciones_Ejecutadas.Text = Estadisticas.Estadisticas.Acciones_Ejecutadas.ToString

        TKBoard_Creados.Text = Estadisticas.Estadisticas.KBoard_Creados.ToString
        TKBoard_Eliminados.Text = Estadisticas.Estadisticas.KBoard_Eliminados.ToString
        TKBoard_Ejecutados.Text = Estadisticas.Estadisticas.KBoard_Ejecutados.ToString

        TQuickMenus_Creados.Text = Estadisticas.Estadisticas.QuickMenus_Creados.ToString
        TQuickMenus_Eliminados.Text = Estadisticas.Estadisticas.QuickMenus_Eliminados.ToString
        TQuickMenus_Abiertos.Text = Estadisticas.Estadisticas.QuickMenus_Abiertos.ToString

        TMacros_Grabadas.Text = Estadisticas.Estadisticas.Macros_Grabadas.ToString
        TMacros_Ejecutadas.Text = Estadisticas.Estadisticas.Macros_Ejecutadas.ToString

        TIconos_Modificados.Text = Estadisticas.Estadisticas.Iconos_Modificados.ToString
        TSalidasAudio.Text = Estadisticas.Estadisticas.Salidas_Audio.ToString
        TTopMost_Aplicados.Text = Estadisticas.Estadisticas.TopMost_Ejecutados.ToString
        Dim Contador, Contador2 As Integer : For Cuenta = 0 To Logros.Logros.Length - 1
            If Logros.Logros(Cuenta).Desbloqueado = True Then Contador += 1
        Next
        For Cuenta = 0 To Logros.Logros.Length - 1
            If Logros.Logros(Cuenta).Desbloqueado = False AndAlso Logros.Logros(Cuenta).Oculto = True Then Continue For
            Contador2 += 1
        Next : TLogros_Desbloqueados.Text = Contador.ToString + " de  " + Contador2.ToString

        TCapturas_Pantalla.Text = Estadisticas.Estadisticas.Capturas_Pantalla.ToString
        TGrabaciones_Pantalla.Text = Estadisticas.Estadisticas.Grabaciones_Pantalla.ToString
        TGrabaciones_Audio.Text = Estadisticas.Estadisticas.Grabaciones_Audio.ToString

        TPapelera_Vaciada.Text = Estadisticas.Estadisticas.Papelera_Vaciada.ToString
        TApagado_Equipo.Text = Estadisticas.Estadisticas.Apagado_Equipo.ToString
        TEnviado_Telegram.Text = Estadisticas.Estadisticas.Enviado_Telegram.ToString

        TDesactivado_En.Text = Estadisticas.Estadisticas.DesactivadoEn.ToString
        TArchivos_Ocultados.Text = Estadisticas.Estadisticas.Archivos_Ocultados.ToString
        TSonidos_Reproducidos.Text = Estadisticas.Estadisticas.Sonidos_Reproducidos.ToString

        TAplicaciones_Ejecutadas.Text = Estadisticas.Estadisticas.Aplicaciones_Ejecutadas.ToString
        TPaginas_Webs.Text = Estadisticas.Estadisticas.Paginas_Webs.ToString
        TCarpetas_Abiertas.Text = Estadisticas.Estadisticas.Carpetas_Abiertas.ToString

        TCMD_Ejecutados.Text = Estadisticas.Estadisticas.CMD_Ejecutados.ToString
        TBAT_Ejecutados.Text = Estadisticas.Estadisticas.BAT_Ejecutados.ToString
        TUDP_Ejecutados.Text = Estadisticas.Estadisticas.Comandos_UDP.ToString

        Contador = 0 : For Cuenta = 0 To FMenu.Comandos_Accion_CAccion.Items.Count - 1
            If FMenu.Comandos_Accion_CAccion.Items(Cuenta).ToString.Substring(0, 1) <> "@" Then Contador += 1
        Next : TAcciones_Unicas.Text = Estadisticas.Estadisticas.Acciones_Unicas.ToString.Length.ToString + " de " + Contador.ToString
        TKDesktop_Iniciado.Text = Estadisticas.Estadisticas.KDesktop_Iniciado.ToString
        TKDesktop_Instalado.Text = FMenu.KDesktop_Instalado.ToString("dd/MM/yyyy")
    End Sub

#End Region

#End Region

#Region " Tooltips "

    Private Sub Logros_Tooltips(sender As Object, e As EventArgs) Handles PBuscar.MouseEnter, PSort_Nombre.MouseEnter, PSort_Fecha.MouseEnter, CNotificaciones.MouseEnter, LNotificaciones.MouseEnter
        Select Case sender.Name
            Case "PBuscar" : FMenu.Show_Tooltip(sender, "Permite buscar logros por nombre")
            Case "PSort_Nombre" : FMenu.Show_Tooltip(sender, "Ordenar logros por nombre (Ascendente / Descendente)")
            Case "PSort_Fecha" : FMenu.Show_Tooltip(sender, "Ordenar logros por fecha (Ascendente / Descendente)")
            Case "CNotificaciones", "LNotificaciones" : FMenu.Show_Tooltip(sender, "Mostrar una notificación cuando se desbloquea un logro")
        End Select
    End Sub

#End Region

End Class

#End Region