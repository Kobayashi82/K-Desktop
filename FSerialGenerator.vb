
#Region " Serial Generator "

Public Class FSerialGenerator

#Region " Variables "

    Private enc As Text.UTF8Encoding
    Private encryptor As Security.Cryptography.ICryptoTransform
    'Private decryptor As Security.Cryptography.ICryptoTransform
    Public Serial As String
    Public Aplicado As Boolean
    Public Cancelado As Boolean = True

#End Region

#Region " Eventos "

#Region " Formulario "

    Private Sub FSerialGenerator_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.FromControl(FSerial).Bounds.Width - Width) / 2 + Screen.FromControl(FSerial).Bounds.Left, (Screen.FromControl(FSerial).Bounds.Height - Height) / 2 + Screen.FromControl(FSerial).Bounds.Top)
    End Sub

    Private Sub FMenu_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim KEY_128 As Byte() = {42, 1, 52, 67, 231, 13, 94, 101, 123, 6, 0, 12, 32, 91, 4, 111, 31, 70, 21, 141, 123, 142, 234, 82, 95, 129, 187, 162, 12, 55, 98, 23}
        Dim IV_128 As Byte() = {234, 12, 52, 44, 214, 222, 200, 109, 2, 98, 45, 76, 88, 53, 23, 78}
        Dim symmetricKey As New Security.Cryptography.RijndaelManaged()
        symmetricKey.Mode = Security.Cryptography.CipherMode.CBC
        enc = New Text.UTF8Encoding
        encryptor = symmetricKey.CreateEncryptor(KEY_128, IV_128)
        'decryptor = symmetricKey.CreateDecryptor(KEY_128, IV_128)
        TEmail.Focus() : TSerial.Focus() : HTexto.Focus() : Opacity = 1 : ShowInTaskbar = True : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FSerialGenerator_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 Then Close()
    End Sub

    Private Sub THidden_KeyPress(sender As Object, e As KeyPressEventArgs) Handles HTexto.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True
    End Sub

    Private Sub Controls_Click(sender As Object, e As EventArgs) Handles FTheme.Click, LEmail.Click, LValidez.Click, LValidez.Click, BGenerar.Click, BAplicar.Click, BCerrarc.Click, TFecha.Click
        HTexto.Focus()
    End Sub

#End Region

#Region " Textos "

#Region " TEmail "

    Private Sub TEmail_TextChanged(sender As Object, e As EventArgs) Handles TEmail.TextChanged
        If TEmail.BorderColor = Color.Red Then TEmail.BorderColor = Color.FromArgb(55, 55, 55)
    End Sub

    Private Sub TEmail_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TEmail.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus() : If ValidarEMail(TEmail.Text) = True Then GenerateKey() Else TEmail.BorderColor = Color.Red : TEmail.Focus() : TSerial.Text = ""
    End Sub

#End Region

#Region " TFecha "

    Private Sub TFecha_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TFecha.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus() : If ValidarEMail(TEmail.Text) = True Then GenerateKey() Else TEmail.BorderColor = Color.Red : TEmail.Focus() : TSerial.Text = ""
    End Sub

    Private Sub TFecha_KeyUp(sender As Object, e As KeyEventArgs) Handles TFecha.KeyUp
        TFecha.Refresh()
    End Sub

    Private Sub TFecha_KeyDown(sender As Object, e As KeyEventArgs) Handles TFecha.KeyDown
        TFecha.Refresh()
    End Sub

#End Region

#Region " TSerial "

    Private Sub TSerial_KeyPress(sender As Object, e As KeyPressEventArgs) Handles TSerial.KeyPress
        If Asc(e.KeyChar) = 13 Then e.Handled = True : HTexto.Focus()
    End Sub

#End Region

#End Region

#Region " Botones "

#Region " Generar "

    Private Sub BGenerar_Click(sender As Object, e As MouseEventArgs) Handles BGenerar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If ValidarEMail(TEmail.Text) = True Then GenerateKey() Else TEmail.BorderColor = Color.Red : TEmail.Focus() : TSerial.Text = ""
    End Sub

    Function ValidarEMail(email As String) As Boolean
        Return New Text.RegularExpressions.Regex("^[_a-zA-Z0-9-]+(.[a-zA-Z0-9-]+)@[a-zA-Z0-9-]+(.[a-zA-Z0-9-]+)*(.[a-zA-Z]{2,4})$").IsMatch(email)
    End Function

    Private Sub GenerateKey()
        Try : Dim sPlainText As String = TEmail.Text.Replace(" ", "") + "|" + Now.ToString("dd/MM/yyyy") + "|" + TFecha.Value.ToString("dd/MM/yyyy")
            Dim memoryStream As New IO.MemoryStream()
            Dim cryptoStream As New Security.Cryptography.CryptoStream(memoryStream, encryptor, Security.Cryptography.CryptoStreamMode.Write)
            cryptoStream.Write(enc.GetBytes(sPlainText), 0, sPlainText.Length) : cryptoStream.FlushFinalBlock()
            TSerial.Text = Convert.ToBase64String(memoryStream.ToArray()).Replace("+", "*") : memoryStream.Close() : cryptoStream.Close()
        Catch : End Try
    End Sub

#End Region

#Region " Aplicar "

    Private Sub BAplicar_Click(sender As Object, e As MouseEventArgs) Handles BAplicar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If ValidarEMail(TEmail.Text) = True Then GenerateKey() Else TEmail.BorderColor = Color.Red : TEmail.Focus() : TSerial.Text = "" : Exit Sub
        Serial = TSerial.Text : Aplicado = True : Cancelado = False : Close()
    End Sub

#End Region

#Region " Cerrar "

    Private Sub BCerrar_Click(sender As Object, e As MouseEventArgs) Handles BCerrarc.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

#End Region

#End Region

#Region " Tooltips "

    Private Sub SerialGenerator_Tooltips(sender As Object, e As EventArgs) Handles LEmail.MouseEnter, LValidez.MouseEnter, LSerial.MouseEnter
        Select Case sender.Name.Replace("_C", "_").Replace("_L", "_")
            Case "LEmail" : FMenu.Show_Tooltip(sender, "Dirección de correo electronico")
            Case "LValidez" : FMenu.Show_Tooltip(sender, "Fecha de caducidad de la licencia")
            Case "LSerial" : FMenu.Show_Tooltip(sender, "Licencia de K-Desktop")
        End Select
    End Sub

#End Region

#End Region

End Class

#End Region