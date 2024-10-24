
#Region " Debug "

Public Class FDebug

#Region " Variables "

#Region " Declaraciones "

    <Runtime.InteropServices.DllImportAttribute("user32.dll")> Public Shared Function SendMessage(hWnd As IntPtr, Msg As Integer, wParam As Integer, lParam As Integer) As Integer : End Function
    <Runtime.InteropServices.DllImportAttribute("user32.dll")> Public Shared Function ReleaseCapture() As Boolean : End Function

#End Region

    Dim isFocused As Boolean

#End Region

#Region " Formulario "

#Region " Load "

    Private Sub FDebug_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True
        If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Image = My.Resources.Telegram_Off
        Width = Math.Max(TError.Width + 90, 450) : Height = Math.Max(TError.Height + 143, 156)
        Location = New Point((Screen.FromControl(Me).Bounds.Width - Width) / 2, (Screen.FromControl(Me).Bounds.Height - Height) / 2)
    End Sub

    Private Sub FCaptura_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        TComentarios.Focus() : HTexto.Focus()
        Application.DoEvents() : ShowInTaskbar = False : ShowInTaskbar = True
        Sonidos.Play("Error", "Error")
        TopMost = False : FMenu.FocusMe(Me) : If Visible = True Then Opacity = 1
    End Sub

#End Region

#Region " KeyPress "

    Private Sub TFecha_MouseDown(sender As Object, e As MouseEventArgs) Handles TFecha.MouseDown
        If e.Button = Windows.Forms.MouseButtons.Left Then ReleaseCapture() : SendMessage(Handle, &HA1, &H2, 0)
    End Sub

    Private Sub FCaptura_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then : e.Handled = True
            If isFocused = False Then Close() Else HTexto.Focus()
        End If
    End Sub

#End Region

#Region " Controles "

    Private Sub Controles_Click(sender As Object, e As MouseEventArgs) Handles Me.Click, FTheme.Click, BCerrar.Click, PTelegram.Click, PCopiar.Click, PGuardar.Click, PCerrar.Click, TFecha.Click, LVersion.Click, TVersion.Click, LWindows.Click, TWindows.Click, LMemoria.Click, TMemoria.Click, LError.Click, TError.Click
        HTexto.Focus()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        e.Handled = True
    End Sub

#End Region

#End Region

#Region " Controles "

#Region " Comentarios "

    Private Sub TComentarios_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TComentarios.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

    Private Sub TComentarios_GotFocus(sender As Object, e As EventArgs) Handles TComentarios.GotFocus
        If TComentarios.ForeColor1 = Color.FromArgb(72, 72, 72) Or TComentarios.Text = "Escriba aquí sus comentarios" Then TComentarios.Text = "" : TComentarios.ForeColor1 = Color.FromArgb(102, 102, 102)
        TComentarios.SelectionLength = 0 : isFocused = True
    End Sub

    Private Sub TComentarios_LostFocus(sender As Object, e As EventArgs) Handles TComentarios.LostFocus
        isFocused = False : TComentarios.SelectionStart = 0 : TComentarios.SelectionLength = 0 : TComentarios.Text = TComentarios.Text.Trim
        If TComentarios.Text = "" Or TComentarios.Text = "Escriba aquí sus comentarios" Then TComentarios.Text = "Escriba aquí sus comentarios" : TComentarios.ForeColor1 = Color.FromArgb(72, 72, 72)
    End Sub

#End Region

#Region " Botones "

#Region " Telegram "

    Private Sub PTelegram_Click(sender As Object, e As MouseEventArgs) Handles PTelegram.Click
        If e.Button <> MouseButtons.Left Or IO.File.Exists(Rutas.TelegramPath) = False Then Exit Sub
        PTelegram.Image = My.Resources.Telegram_Off : IO.Directory.CreateDirectory(FMenu.KobaPathTemp)
        Dim FileName As String = "Error de " + FMenu.Version + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
        Using bmp = New Bitmap(ActiveForm.Width, ActiveForm.Height)
            ActiveForm.DrawToBitmap(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
            bmp.Save(FMenu.KobaPathTemp + FileName, Imaging.ImageFormat.Png)
        End Using
        PTelegram.Image = My.Resources.Telegram_On
        FMenu.CreateKPS() : RunProcess.RunAsStandar(FMenu.KobaPathTemp + "KPS.exe", "Ejecutar Aplicacion|-|" + Rutas.TelegramPath + "|-|-sendpath " + "|||" + FMenu.KobaPathTemp + FileName + "||||-||-|Normal|-|Normal|-|False|-|False") : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
        Try : AppActivate("Telegram") : Catch : End Try : Estadisticas.Estadisticas.Enviado_Telegram += 1 : Estadisticas.Guardar()
    End Sub

#End Region

#Region " Copiar "

    Private Sub PCopiar_Click(sender As Object, e As MouseEventArgs) Handles PCopiar.Click
        If e.Button = MouseButtons.Left Then
            PCopiar.Image = My.Resources.Copiar_Imagen_Off
            Using bmp = New Bitmap(ActiveForm.Width, ActiveForm.Height)
                ActiveForm.DrawToBitmap(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
                Clipboard.SetImage(bmp)
            End Using
            PCopiar.Image = My.Resources.Copiar_Imagen_On
        ElseIf e.Button = MouseButtons.Right Then
            My.Computer.Clipboard.Clear()
            My.Computer.Clipboard.SetText(TFecha.Text + vbCrLf + vbCrLf + "Version: " + TVersion.Text + vbCrLf + "Windows: " + TWindows.Text + vbCrLf + "Memoria: " + TMemoria.Text + vbCrLf + vbCrLf + "Comentarios: " + TComentarios.Text + vbCrLf + vbCrLf + TError.Text + vbCrLf, TextDataFormat.Text)
        End If
    End Sub

#End Region

#Region " Guardar "

    Private Sub PGuardar_Click(sender As Object, e As MouseEventArgs) Handles PGuardar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim FileName As String = "Error de " + FMenu.Version + " - (" + DateTime.Now.ToString("dd-MM-yyyy HH.mm.ss") + ").png"
        Dim Guardar As New SaveFileDialog : Guardar.CheckPathExists = True : Guardar.FileName = FileName : Guardar.OverwritePrompt = True : Guardar.AddExtension = True
        Guardar.Title = "Guardar error de " + FMenu.Version + "..." : Guardar.Filter = "Archivo de Imagen (*.png)|*.png|Archivo de Texto (*.txt)|*.txt"
        If Guardar.ShowDialog <> DialogResult.Cancel Then
            If IO.File.Exists(Guardar.FileName) = True Then Try : IO.File.Delete(Guardar.FileName) : Catch : End Try
            If IO.Path.GetExtension(Guardar.FileName) = ".png" Then
                Using bmp = New Bitmap(ActiveForm.Width, ActiveForm.Height)
                    ActiveForm.DrawToBitmap(bmp, New Rectangle(0, 0, bmp.Width, bmp.Height))
                    bmp.Save(Guardar.FileName, Imaging.ImageFormat.Png)
                End Using
            Else
                Dim Escribir As New IO.StreamWriter(Guardar.FileName)
                Escribir.Write(TFecha.Text + vbCrLf + vbCrLf + "Version: " + TVersion.Text + vbCrLf + "Windows: " + TWindows.Text + vbCrLf + "Memoria: " + TMemoria.Text + vbCrLf + vbCrLf + "Comentarios: " + TComentarios.Text + vbCrLf + vbCrLf + TError.Text + vbCrLf)
                Escribir.Close()
            End If
        End If
    End Sub

#End Region

#Region " Cerrar "

    Private Sub PCerrar_Click(sender As Object, e As MouseEventArgs) Handles PCerrar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Close()
    End Sub

#End Region

#Region " MouseMove "

    Private Sub PBotones_MouseEnter(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter, PCopiar.MouseEnter, PGuardar.MouseEnter, PCerrar.MouseEnter
        Select Case sender.Name
            Case "PTelegram" : If IO.File.Exists(Rutas.TelegramPath) = False Then PTelegram.Image = My.Resources.Telegram_No Else PTelegram.Image = My.Resources.Telegram_On
            Case "PCopiar" : PCopiar.Image = My.Resources.Copiar_Imagen_On
            Case "PGuardar" : PGuardar.Image = My.Resources.Save_On
            Case "PCerrar" : PCerrar.Image = My.Resources.Cerrar_On
        End Select
    End Sub

    Private Sub PBotones_MouseLeave(sender As Object, e As EventArgs) Handles PTelegram.MouseLeave, PCopiar.MouseLeave, PGuardar.MouseLeave, PCerrar.MouseLeave
        Select Case sender.Name
            Case "PTelegram" : If IO.File.Exists(Rutas.TelegramPath) = True Then PTelegram.Image = My.Resources.Telegram_Off
            Case "PCopiar" : PCopiar.Image = My.Resources.Copiar_Imagen_Off
            Case "PGuardar" : PGuardar.Image = My.Resources.Save_Off
            Case "PCerrar" : PCerrar.Image = My.Resources.Cerrar_Off
        End Select
    End Sub

#End Region

#End Region

#End Region

#Region " Tooltips "

    Private Sub Debug_Tooltips(sender As Object, e As EventArgs) Handles PTelegram.MouseEnter, PCopiar.MouseEnter, PGuardar.MouseEnter, PCerrar.MouseEnter
        Select Case sender.Name
            Case "PTelegram" : FMenu.Show_Tooltip(sender, "Envía los datos como imágen por Telegram")
            Case "PCopiar" : FMenu.Show_Tooltip(sender, "Click Izquierdo: Copia los datos como imágen" + vbCrLf + "Click Derecho: Copia los datos como texto")
            Case "PGuardar" : FMenu.Show_Tooltip(sender, "Guarda los datos como imágen o texto")
            Case "PCerrar" : FMenu.Show_Tooltip(sender, "Cierra la ventana")
        End Select
    End Sub

#End Region

End Class

#End Region