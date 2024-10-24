
#Region " K-Link "

Public Class FKLink

#Region " Variables "

    Dim RutaHardLink_Origen, RutaHardLink_Destino As String

#End Region

#Region " Eventos "

#Region " Formulario "

    Private Sub FKLink_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.PrimaryScreen.Bounds.Width - Width) / 2, (Screen.PrimaryScreen.Bounds.Height - Height) / 2)
    End Sub

    Private Sub FKLink_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        HTexto.Focus() : Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FKLink_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then e.Handled = True : Close()
    End Sub

    Private Sub HTexto_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True
    End Sub

    Private Sub Controles_Click(sender As Object, e As EventArgs) Handles FTheme.Click, BExaminar_Carpeta.Click, BExaminar_Guardar.Click, BAceptar.Click, BCancelar.Click, LCarpeta.Click, LGuardar.Click, LArchivo.Click
        HTexto.Focus()
    End Sub

#End Region

#Region " Funciones "

#Region " Check HardLink "

    Private Function Check_HardLink(Carpeta As String) As String
        Dim Carpetas As String() = Carpeta.Split("\")
        Dim CarpetaActual As String = Carpetas(0) + "\"
        For Cuenta As Integer = 1 To Carpetas.Length - 1
            CarpetaActual = IO.Path.Combine(CarpetaActual, Carpetas(Cuenta))
            Dim HardLink_Path As String = Check_HardLink_Folder(CarpetaActual)
            If Not String.IsNullOrEmpty(HardLink_Path) Then Return HardLink_Path
        Next : Return ""
    End Function

    Private Function Check_HardLink_Folder(Carpeta As String) As String
        Try : Dim process As New Process()
            process.StartInfo.FileName = "cmd.exe"
            process.StartInfo.Arguments = "/c fsutil reparsepoint query """ + Carpeta + """"
            process.StartInfo.UseShellExecute = False
            process.StartInfo.RedirectStandardOutput = True
            process.StartInfo.CreateNoWindow = True
            process.Start()
            Dim output As String = process.StandardOutput.ReadToEnd().ToLower
            process.WaitForExit()
            If output.Contains("print name:") Then
                Dim startIndex As Integer = output.IndexOf(vbCrLf + "print name:") + 17
                Dim endIndex As Integer = output.IndexOf(vbCrLf, startIndex)
                Return output.Substring(startIndex, endIndex - startIndex).Trim
            ElseIf output.Contains("nombre de impresi") Then
                Dim startIndex As Integer = output.IndexOf(vbCrLf + "nombre de impresi") + 23
                Dim endIndex As Integer = output.IndexOf(vbCrLf, startIndex)
                Return output.Substring(startIndex, endIndex - startIndex).Trim
            End If
        Catch : End Try : Return ""
    End Function

#End Region

#End Region

#End Region

#Region " Carpeta "

    Private Sub Textos_TextChanged(sender As Object, e As EventArgs) Handles TCarpeta.TextChanged, TGuardar.TextChanged

    End Sub

    Private Sub TCarpeta_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TCarpeta.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : TCarpeta.Tag = FMenu.ReplaceSpecialFolder(TCarpeta.Text) : HTexto.Focus()
    End Sub

    Private Sub TCarpeta_GotFocus(sender As Object, e As EventArgs) Handles TCarpeta.GotFocus
        If TCarpeta.Tag = "" Then TCarpeta.Text = "" Else TCarpeta.Text = TCarpeta.Tag
    End Sub

    Private Sub TCarpeta_LostFocus(sender As Object, e As EventArgs) Handles TCarpeta.LostFocus
        TCarpeta.Tag = FMenu.ReplaceSpecialFolder(TCarpeta.Tag) : TCarpeta.Text = Rutas.Acortar_Ruta(TCarpeta.Tag, TCarpeta)
    End Sub

    Private Sub BExaminar_Carpeta_Click(sender As Object, e As MouseEventArgs) Handles BExaminar_Carpeta.Click
        If e.Button = MouseButtons.Left Then
            Dim Abrir As New OpenFileDialog With {.CheckFileExists = True, .CheckPathExists = True, .Title = "Seleccione el archivo de origen"}
            If Abrir.ShowDialog <> DialogResult.Cancel Then TCarpeta.Tag = Abrir.FileName : TCarpeta.Text = Rutas.Acortar_Ruta(TCarpeta.Tag, TCarpeta)

        ElseIf e.Button = MouseButtons.Right Then
            Dim Abrir As New FolderBrowserDialog With {.ShowNewFolderButton = True, .Description = "Seleccione la carpeta origen"}
            If Abrir.ShowDialog < DialogResult.Cancel Then TCarpeta.Tag = Abrir.SelectedPath : TCarpeta.Text = Rutas.Acortar_Ruta(TCarpeta.Tag, TCarpeta)
        End If
    End Sub

#End Region

#Region " Guardar "

    Private Sub TGuardar_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TGuardar.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : TGuardar.Tag = FMenu.ReplaceSpecialFolder(TGuardar.Text) : HTexto.Focus()
    End Sub

    Private Sub TGuardar_GotFocus(sender As Object, e As EventArgs) Handles TGuardar.GotFocus
        If TGuardar.Tag = "" Then TGuardar.Text = "" Else TGuardar.Text = TGuardar.Tag
    End Sub

    Private Sub TGuardar_LostFocus(sender As Object, e As EventArgs) Handles TGuardar.LostFocus
        TGuardar.Tag = FMenu.ReplaceSpecialFolder(TGuardar.Tag) : TGuardar.Text = Rutas.Acortar_Ruta(TGuardar.Tag, TGuardar)
    End Sub

    Private Sub BExaminar_Guardar_Click(sender As Object, e As MouseEventArgs) Handles BExaminar_Guardar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If IO.File.Exists(TCarpeta.Tag) Then
            Dim Guardar As New SaveFileDialog With {.Title = "Guardar K-Link", .CheckPathExists = True, .Filter = "Todos los archivos|*.*", .FileName = IO.Path.GetFileName(TCarpeta.Tag)}
            If Guardar.ShowDialog <> DialogResult.Cancel Then
                TGuardar.Tag = Guardar.FileName
                If TGuardar.Tag.ToString.EndsWith(".") Then TGuardar.Tag = TGuardar.Tag.ToString.Substring(0, TGuardar.Tag.ToString.Length - 1)
                TGuardar.Text = Rutas.Acortar_Ruta(TGuardar.Tag, TGuardar)
            End If
        ElseIf IO.Directory.Exists(TCarpeta.Tag) Then
            Dim Guardar As New SaveFileDialog With {.Title = "Guardar K-Link", .CheckPathExists = True, .Filter = "Enlace especial (K-Link)|*."}
            Dim TempName As String = "" : If IO.Directory.Exists(TCarpeta.Tag) = True Then TempName = TCarpeta.Tag.Split("\")(TCarpeta.Tag.Split("\").Length - 1) : If TempName = "" Then TempName = "Drive " + TCarpeta.Tag.Substring(0, 1).ToUpper
            Guardar.FileName = TempName : If Guardar.ShowDialog <> DialogResult.Cancel Then
                TGuardar.Tag = Guardar.FileName
                If TGuardar.Tag.ToString.EndsWith(".") Then TGuardar.Tag = TGuardar.Tag.ToString.Substring(0, TGuardar.Tag.ToString.Length - 1)
                TGuardar.Text = Rutas.Acortar_Ruta(TGuardar.Tag, TGuardar)
            End If
        End If
    End Sub

#End Region

#Region " Botones "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        Try : If TCarpeta.Text.Trim = "" Or TGuardar.Text.Trim = "" Or IO.Path.GetFileNameWithoutExtension(TGuardar.Tag) = "" Or e.Button <> MouseButtons.Left Then Exit Sub
            If IO.File.Exists(TCarpeta.Tag) Then
                RutaHardLink_Origen = Check_HardLink(TCarpeta.Tag) : If RutaHardLink_Origen = "" Then RutaHardLink_Origen = TCarpeta.Tag
                RutaHardLink_Destino = Check_HardLink(TGuardar.Tag) : If RutaHardLink_Destino = "" Then RutaHardLink_Destino = TGuardar.Tag
                If (RutaHardLink_Origen.Substring(0, 1).ToLower <> RutaHardLink_Destino.Substring(0, 1).ToLower) Then
                    FMenu.ShowMessageBox("No se pueden crear enlances fisicos de archivos", FMenu.Version, MsgBoxStyle.Exclamation, "Aceptar", "que se encuentren en unidades diferentes")
                    Exit Sub
                End If
                If IO.Directory.Exists(IO.Path.GetDirectoryName(TGuardar.Tag)) = False Then IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(TGuardar.Tag))
                FMenu.RunCMD("MKLink /H " + """" + TGuardar.Tag + """ """ + TCarpeta.Tag + """")
                Close()
                Logros.Desbloquear("Especialista del enlace")
            ElseIf IO.Directory.Exists(TCarpeta.Tag) Then
                If IO.Directory.Exists(IO.Path.GetDirectoryName(TGuardar.Tag)) = False Then IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(TGuardar.Tag))
                FMenu.RunCMD("MKLink /H /J " + """" + TGuardar.Tag + """ """ + TCarpeta.Tag + """")
                Close()
                Logros.Desbloquear("Especialista del enlace")
            End If
        Catch : End Try
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

#End Region

#Region " Help "

    Private Sub KLink_LHelp_Click(sender As Object, e As MouseEventArgs) Handles LHelp.Click, LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Abrir Carpeta" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub KLink_LHelp_MouseEnter(sender As Object, e As EventArgs) Handles LHelp.MouseEnter
        LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub KLink_LHelp_MouseLeave(sender As Object, e As EventArgs) Handles LHelp.MouseLeave
        LHelp.ForeColor1 = Color.Gray
    End Sub

    Private Sub KLink_LHelp_Click(sender As Object, e As EventArgs) Handles LHelp.DoubleClick, LHelp.Click

    End Sub

#End Region

#Region " Tooltips "

    Private Sub KLink_Tooltips(sender As Object, e As EventArgs) Handles LCarpeta.MouseEnter, LGuardar.MouseEnter, BExaminar_Carpeta.MouseEnter, BExaminar_Guardar.MouseEnter, LArchivo.MouseEnter
        Select Case sender.Name
            Case "LCarpeta", "LArchivo" : FMenu.Show_Tooltip(sender, "Ruta de la carpeta de la que se va a crear el 'K-Link'")
            Case "BExaminar_Carpeta" : FMenu.Show_Tooltip(sender, "Click Izquierdo: Seleccionar un archivo" + vbCrLf + "Click Derecho: Seleccionar una carpeta")
            Case "LGuardar" : FMenu.Show_Tooltip(sender, "Ruta donde se creará el 'K-Link'")
            Case "BExaminar_Guardar" : FMenu.Show_Tooltip(sender, "Seleccionar donde se creará el 'K-Link'")
        End Select
    End Sub

#End Region

End Class

#End Region