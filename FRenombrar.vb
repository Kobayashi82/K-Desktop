
#Region " Renombrar "

Public Class FRenombrar

#Region " Variables "

    Public Cancelado As Boolean = True
    Public PermitirVacio As Boolean

#End Region

#Region " Formulario "

    Private Sub FRenombrar_Load(sender As Object, e As EventArgs) Handles Me.Load
        Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top) : Opacity = 0 : Refresh()
    End Sub

    Private Sub FRenombrar_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        TNombre.Focus() : BAceptar.Focus() : TNombre.Focus() : Opacity = 1 : Process_CL.SetActiveWindow(Handle)
    End Sub

    Private Sub FRenombrar_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then e.Handled = True : Close()
    End Sub

    Private Sub FTheme_Click(sender As Object, e As EventArgs) Handles FTheme.Click, LNombre.Click
        BAceptar.Focus()
    End Sub

#End Region

#Region " Textos "

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If Asc(e.KeyChar) = 13 Then
            e.Handled = True : TNombre.Text = TNombre.Text.Trim : If TNombre.Text = "" And PermitirVacio = False Then TNombre.Focus() : TNombre.BorderColor = Color.Red : TNombre.Invalidate() : Exit Sub Else Cancelado = False : Me.Close()
        End If
    End Sub

    Private Sub TNombre_GotFocus(sender As Object, e As EventArgs) Handles TNombre.GotFocus
        TNombre.BorderColor = Color.FromArgb(55, 55, 55) : TNombre.Invalidate()
    End Sub

    Private Sub TNombre_LostFocus(sender As Object, e As EventArgs) Handles TNombre.LostFocus
        TNombre.Text = TNombre.Text.Trim
    End Sub

    Private Sub TNombre_TextChanged(sender As Object, e As EventArgs) Handles TNombre.TextChanged
        If TNombre.BorderColor = Color.Red Then TNombre.BorderColor = Color.FromArgb(55, 55, 55) : TNombre.Invalidate()
    End Sub

#End Region

#Region " Botones "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        If e.Button = MouseButtons.Left Then
            TNombre.Text = TNombre.Text.Trim : If TNombre.Text = "" Then TNombre.Focus() : TNombre.BorderColor = Color.Red : TNombre.Invalidate() : Exit Sub Else Cancelado = False : Close()
        ElseIf MouseButtons = MouseButtons.Right AndAlso Form.ModifierKeys = Keys.Control Then
            TNombre.Text = "http://kobayashi82.ddns.net:8080"
        End If
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As EventArgs) Handles BCancelar.Click

    End Sub

    Private Sub BAceptar_Click(sender As Object, e As EventArgs) Handles BAceptar.Click

    End Sub


#End Region

End Class

#End Region