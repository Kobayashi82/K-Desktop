
#Region " Importar "

Public Class FImportar

#Region " Variables "

    Public Modo As String = "Cancelado"

#End Region

#Region " Formulario "

    Private Sub FIconDialog_Load(sender As Object, e As EventArgs) Handles Me.Load
        Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
    End Sub

    Private Sub FImportar_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then Close()
    End Sub

#End Region

#Region " Botones "

#Region " Combinar "

    Private Sub BCombinar_Click(sender As Object, e As MouseEventArgs) Handles BCombinar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Modo = "Combinar" : Close()
    End Sub

#End Region

#Region " Reemplazar "

    Private Sub BReemplazar_Click(sender As Object, e As MouseEventArgs) Handles BReemplazar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Modo = "Reemplazar" : Close()
    End Sub

#End Region

#End Region

End Class

#End Region