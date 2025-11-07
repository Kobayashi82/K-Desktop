
#Region " FSerial "

Public Class FSerial

#Region " Variables "

    Public FirstRun As Boolean = False
    Public Cancelado As String = "Si"
    Public Editado As Boolean = False
    Public Serial As String

#End Region

#Region " Formulario "

    Private Sub FSerial_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        If FFirstRun.Visible = True Then LHelp.Visible = False Else LHelp.Visible = True
    End Sub

    Private Sub FSerial_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me) : HTexto.Focus()
    End Sub

    Private Sub FSerial_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If Cancelado = "Ending" Then Exit Sub
        If Cancelado <> "No" And Editado = False And FirstRun = False And Visible = True Then
            Sonidos.Play_Block("Error")
            If FMenu.ShowMessageBox("Si cierra la ventana '" + FMenu.Version + "' se cerrará", FMenu.Version, MsgBoxStyle.YesNo, "Salir").ToString = "OK" Then
                FMenu.NotifyIcon.Visible = False : FMenu.NotifyIcon.Dispose() : FTheme.Refresh() : FTheme.Dispose()
            Else : e.Cancel = True : End If
        End If
    End Sub

    Private Sub FSerial_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then e.Handled = True : Close()
    End Sub

    Private Sub THidden_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True
    End Sub

    Private Sub Controls_Click(sender As Object, e As EventArgs) Handles FTheme.Click, TNombre.Click, BAceptar.Click, BCancelar.Click, LHelp.Click
        HTexto.Focus()
    End Sub

#End Region

#Region " TNombre "

    Private Sub TNombre_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TNombre.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : BAceptar.Focus()
    End Sub

#End Region

#Region " Aceptar / Cancelar "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        Try : If e.Button <> MouseButtons.Left Then Exit Sub
            If TNombre.Text.Replace(" ", "") = "" Then Exit Sub
            Dim DSerial As String = FMenu.Serial_Decrypt(TNombre.Text.Replace(" ", ""))
            Dim Email As String = "" : Dim Validez As String = ""
            If DSerial.Contains("|") Then
                If DSerial.Split("|").Length = 3 Then Email = DSerial.Split("|")(0) : Validez = DSerial.Split("|")(2)
            End If
            If IsDate(Validez) Then
                If Validez <> "01/01/0001" Then
                    Dim Fecha As Date = Validez
                    If CDate(Now.ToShortDateString) > CDate(Fecha.ToShortDateString) Then
                        Sonidos.Play_Block("Chord") : FMenu.ShowMessageBox("El número de serie ha caducado", FMenu.Version, MsgBoxStyle.OkOnly, "Aceptar")
                        TNombre.SelectionStart = 0 : TNombre.SelectionLength = TNombre.Text.Length : TNombre.Focus() : Exit Sub
                    End If
                    Cancelado = "No"
                    If Serial <> TNombre.Text.Replace(" ", "") Then
                        Serial = TNombre.Text.Replace(" ", "") : Close()
                        Sonidos.Play_Block("Calendar")
                        FMenu.ShowMessageBox("Número de serie válido hasta el (" + Fecha.ToShortDateString + ")", FMenu.Version, MsgBoxStyle.OkOnly, "Aceptar")
                    Else
                        Serial = TNombre.Text.Replace(" ", "") : Close()
                    End If
                    Logros.Logro_Serial = True
                    Exit Sub
                End If
            End If
        Catch : End Try
        Sonidos.Play_Block("Chord") : FMenu.ShowMessageBox("El número de serie introducido no es válido", FMenu.Version, MsgBoxStyle.OkOnly, "Aceptar")
        TNombre.SelectionStart = 0 : TNombre.SelectionLength = TNombre.Text.Length : TNombre.Focus()
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

    Private Sub LSerialEspecial_Click(sender As Object, e As MouseEventArgs) Handles LSerialEspecial.Click
        Try : If e.Button <> MouseButtons.Left Then Exit Sub
            If Control.ModifierKeys = Keys.Shift Then
                Dim SerialOro As String = "4fOzWxq9kxMNDtTUMSjsKoe*6IVb8ydnHgac6w7u9kbFlqNPt6Q*DYv3MXxqBQZE"
                Dim DSerialOro As String = FMenu.Serial_Decrypt(SerialOro) : Dim ValidezOro As Date
                If DSerialOro.Contains("|") AndAlso DSerialOro.Split("|").Length = 3 Then ValidezOro = DSerialOro.Split("|")(2)
                Cancelado = "No"
                If Serial <> SerialOro Then
                    Serial = SerialOro : Close()
                    Sonidos.Play_Block("Calendar") : FMenu.ShowMessageBox("Número de serie válido hasta el fin de los tiempos", FMenu.Version, MsgBoxStyle.OkOnly, "Aceptar")
                Else
                    Serial = SerialOro : Close()
                End If
                Logros.Logro_Serial = True
                Exit Sub
            End If
            If Control.ModifierKeys = Keys.Control Then
                If TNombre.Text <> "" Then
                    Dim DSerialG As String = FMenu.Serial_Decrypt(TNombre.Text) : If DSerialG.Contains("|") Then
                        FSerialGenerator.TEmail.Text = DSerialG.Split("|")(0)
                        FSerialGenerator.TFecha.Value = DSerialG.Split("|")(2)
                        FSerialGenerator.TSerial.Text = TNombre.Text
                    End If
                End If : FSerialGenerator.ShowDialog()
                If FSerialGenerator.Cancelado = False Then TNombre.Text = FSerialGenerator.Serial
                Exit Sub
            End If
        Catch : End Try
    End Sub

#End Region

#Region " Help "

    Private Sub Serial_LHelp_Click(sender As Object, e As MouseEventArgs) Handles LHelp.Click, LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Licencia de KDesktop" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub Serial_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles LHelp.MouseEnter
        LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub Serial_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles LHelp.MouseLeave
        LHelp.ForeColor1 = Color.FromArgb(85, 85, 85)
    End Sub

    Private Sub Serial_LHelp_Click(sender As Object, e As EventArgs) Handles LHelp.DoubleClick

    End Sub

    Private Sub LSerialEspecial_Click(sender As Object, e As EventArgs) Handles LSerialEspecial.Click

    End Sub

#End Region

End Class

#End Region
