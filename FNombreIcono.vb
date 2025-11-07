
#Region " Nombre Icono "

Public Class FNombreIcono

#Region " Variables "

    Public Nombre, Comando_ID As String
    Public isDefault As Boolean
    Public cItem As Object
    Public Cancelado As Boolean = True

#End Region

#Region " Formulario "

    Private Sub FComando_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        TNombre.Text = Nombre : TNombre.Focus() : If Nombre = "" Then HTexto.Focus()
    End Sub

    Private Sub FComando_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        TNombre.Focus() : HTexto.Focus() : If TNombre.Text = "" Then TNombre.Focus()
        Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FComando_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 Then
            If Opacity = 1 Then e.Handled = True : Close()
        End If
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

#Region " Nombre "

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        If TNombre.Text.Trim = "" Then TNombre.Text = "" : Exit Sub
        ' TNombre.Text = TNombre.Text.Trim.Substring(0, 1).ToUpper + TNombre.Text.Trim.Substring(1).Trim()
    End Sub

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

#End Region

#Region " Icono "

    Private Sub PIcono_Click(sender As Object, e As MouseEventArgs) Handles PIcono.Click
        If e.Button = MouseButtons.Left Then
            NombreIcono_Menu_Seleccionar_Click(sender, Nothing)
        ElseIf e.Button = MouseButtons.Middle Then
            NombreIcono_Menu_Default_Click(sender, Nothing)
        End If
    End Sub

    Private Sub NombreIcono_Menu_Opening(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles NombreIcono_Menu.Opening
        NombreIcono_Menu.Height = 110
    End Sub

    Private Sub NombreIcono_Menu_Seleccionar_Click(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Seleccionar.Click
        FIconDialog.Cancelado = True : FIconDialog.ShowDialog()
        If FIconDialog.Cancelado = False Then PIcono.Image = FIconDialog.Icono.ToBitmap : isDefault = False
    End Sub

    Private Sub NombreIcono_Menu_Examinar_Click(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Examinar.Click
        Try : Dim ImageListSmall, ImageListLarge As ImageList
            Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True
            Abrir.Title = "Seleccionar icono..." : Abrir.Filter = "Archivos de Iconos|*.ico;*.exe;*.dll;*.lnk|Todos los Archivos|*.*"
            If Abrir.ShowDialog <> DialogResult.Cancel Then
                If Abrir.FileName.ToLower.EndsWith(".lnk") Then ImageListLarge = FIconDialog.GetImageList(Rutas.GetTargetPath(Abrir.FileName)) Else ImageListLarge = FIconDialog.GetImageList(Abrir.FileName)
                ImageListSmall = ImageListLarge : If ImageListSmall IsNot Nothing Then
                    For Cuenta = 0 To ImageListSmall.Images.Count - 1
                        If ImageListSmall.Images(Cuenta) IsNot Nothing Then PIcono.Image = ImageListSmall.Images(Cuenta) : Exit For
                    Next
                End If
            End If : isDefault = False
        Catch : End Try
    End Sub

    Private Sub NombreIcono_Menu_Default_Click(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Default.Click
        cItem.Tag = Split(cItem.Tag, "|-|", , CompareMethod.Text)(0) + "|-|" + Split(cItem.Tag, "|-|", , CompareMethod.Text)(1) + "|-|" + Split(cItem.Tag, "|-|", , CompareMethod.Text)(2) + "|-|" + "[DEFAULT]"
        PIcono.Image = Iconos.DefaultIcon(cItem.Tag, True) : isDefault = True
    End Sub

    Private Sub NombreIcono_Menu_Eliminar_Click(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Eliminar.Click
        PIcono.Image = Nothing : isDefault = False
    End Sub

    Private Sub MenuIcono_MItem_MouseEnter(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Seleccionar.MouseEnter, NombreIcono_Menu_Examinar.MouseEnter, NombreIcono_Menu_Default.MouseEnter, NombreIcono_Menu_Eliminar.MouseEnter
        Dim QMItem As ToolStripMenuItem = CType(sender, ToolStripMenuItem) : QMItem.ForeColor = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub MenuIcono_MItem_MouseLeave(sender As Object, e As EventArgs) Handles NombreIcono_Menu_Seleccionar.MouseLeave, NombreIcono_Menu_Examinar.MouseLeave, NombreIcono_Menu_Default.MouseLeave, NombreIcono_Menu_Eliminar.MouseLeave
        Dim QMItem As ToolStripMenuItem = CType(sender, ToolStripMenuItem) : QMItem.ForeColor = Color.FromArgb(130, 130, 130)
    End Sub

#End Region

#Region " Aceptar / Cancelar "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        If TNombre.Text.Trim = "" AndAlso FMenu.Comando_Existe(Comando_ID) = False Then TNombre.Focus() : Exit Sub
        If TNombre.Text.Trim = "" Then TNombre.Text = ""
        If Comando_ID <> "" AndAlso Split(cItem.Tag, "|-|",, CompareMethod.Text)(2).ToLower = "[COMANDO]" AndAlso (FMenu.Comando_Get(Comando_ID).Nombre.ToLower = TNombre.Text.ToLower) Then TNombre.Text = ""
        If e.Button = MouseButtons.Left Then Cancelado = False : Close()
    End Sub

    Private Sub BAceptar_Click(sender As Object, e As EventArgs) Handles BAceptar.Click

    End Sub

    Private Sub BCancelar_Click(sender As Object, e As EventArgs) Handles BCancelar.Click

    End Sub

    Private Sub PIcono_Click(sender As Object, e As EventArgs) Handles PIcono.Click

    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

#End Region

#Region " Tooltips "

    Private Sub NombreIcono_Tooltips(sender As Object, e As EventArgs) Handles LNombre.MouseEnter, PIcono.MouseEnter
        Select Case sender.Name
            Case "LNombre" : FMenu.Show_Tooltip(sender, "Nombre que se mostrará")
            Case "PIcono" : FMenu.Show_Tooltip(sender, "Icono que se mostrará" + vbCrLf + vbCrLf + "Click Izquierdo: Seleccionar un icono" + vbCrLf + "Click Derecho: Mostrar el menú desplegable" + vbCrLf + "Click Central: Establecer icono predeterminado")
        End Select
    End Sub

#End Region

End Class

#End Region