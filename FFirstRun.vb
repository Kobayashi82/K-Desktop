
#Region " FFirstRun "

Public Class FFirstRun

#Region " Variables "

    Public Cancelado As Boolean = True
    Public Serial As String
    Private enc As Text.UTF8Encoding
    Private encryptor As Security.Cryptography.ICryptoTransform
    'Private decryptor As Security.Cryptography.ICryptoTransform

#End Region

#Region " Eventos "

#Region " Formulario "

    Private Sub FFirstRun_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : Location = New Point((Screen.PrimaryScreen.Bounds.Width - Width) / 2, (Screen.PrimaryScreen.Bounds.Height - Height) / 2)
    End Sub

    Private Sub FFirstRun_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim KEY_128 As Byte() = {42, 1, 52, 67, 231, 13, 94, 101, 123, 6, 0, 12, 32, 91, 4, 111, 31, 70, 21, 141, 123, 142, 234, 82, 95, 129, 187, 162, 12, 55, 98, 23}
        Dim IV_128 As Byte() = {234, 12, 52, 44, 214, 222, 200, 109, 2, 98, 45, 76, 88, 53, 23, 78}
        Dim symmetricKey As New Security.Cryptography.RijndaelManaged()
        symmetricKey.Mode = Security.Cryptography.CipherMode.CBC
        enc = New Text.UTF8Encoding
        encryptor = symmetricKey.CreateEncryptor(KEY_128, IV_128)
        'decryptor = symmetricKey.CreateDecryptor(KEY_128, IV_128)
        Opacity = 1 : TopMost = False : FMenu.FocusMe(Me)
        My.Computer.Audio.Play(My.Resources._Error, AudioPlayMode.Background)
    End Sub

    Private Sub FFirstRun_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 Then Close()
    End Sub

    Private Sub FFirstRun_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If Cancelado = True Then
            My.Computer.Audio.Play(My.Resources._Error, AudioPlayMode.Background)
            If FMenu.ShowMessageBox("Si cierra la ventana '" + FMenu.Version + "' se cerrará", FMenu.Version, MsgBoxStyle.YesNo, "Salir").ToString = "OK" Then
                FMenu.NotifyIcon.Visible = False : FMenu.NotifyIcon.Dispose()
            Else : e.Cancel = True : End If
        End If
    End Sub

    Private Sub BTrial_Click(sender As Object, e As MouseEventArgs) Handles BTrial.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        Try : Dim sPlainText As String = "trial" + "|" + Now.ToString("dd/MM/yyyy") + "|" + Now.AddDays(15).ToString("dd/MM/yyyy")
            Dim memoryStream As New IO.MemoryStream()
            Dim cryptoStream As New Security.Cryptography.CryptoStream(memoryStream, encryptor, Security.Cryptography.CryptoStreamMode.Write)
            cryptoStream.Write(enc.GetBytes(sPlainText), 0, sPlainText.Length) : cryptoStream.FlushFinalBlock()
            Serial = Convert.ToBase64String(memoryStream.ToArray()).Replace("+", "*") : memoryStream.Close() : cryptoStream.Close()
            Cancelado = False : Close()
        Catch : End Try
    End Sub

    Private Sub BRegistrar_Click(sender As Object, e As MouseEventArgs) Handles BRegistrar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        FSerial.Editado = False : FSerial.FirstRun = True : FSerial.TNombre.Text = ""
        FSerial.ShowDialog() : If FSerial.Cancelado = "No" Then Serial = FSerial.Serial : Cancelado = False : Close()
    End Sub

#End Region

#End Region

End Class

#End Region