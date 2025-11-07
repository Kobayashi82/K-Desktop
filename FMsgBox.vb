
#Region " FMsgBox "

Public Class FMsgBox

#Region " Variables "

    Public Modo As MsgBoxStyle
    Public Respuesta As MsgBoxResult = MsgBoxResult.Cancel

#End Region

#Region " Formulario "

    Private Sub FMsgBox_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        Text = FTheme.Text : TopMost = True
        'Location = New Point(Screen.FromPoint(Cursor.Position).Bounds.X + ((Screen.FromPoint(Cursor.Position).Bounds.Width / 2) - (Width / 2)), Screen.FromPoint(Cursor.Position).Bounds.Y + ((Screen.FromPoint(Cursor.Position).Bounds.Height / 6) - (Height / 2)))
        BAceptar.ForeColor1 = Color.FromArgb(205, 150, 0)
        Select Case Modo
            Case MsgBoxStyle.YesNo
                Dim TextSize As Single = 8 : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto.Font = New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)
                If TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width > LTexto.Width Then Dim TempText As String = LTexto.Text : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width : TempText = TempText.Substring(0, TempText.Length - 1) : LTexto.Text = TempText + "..." : Loop
                If LTexto2.Text <> "" Then
                    TextSize = 8 : Do Until TextRenderer.MeasureText(LTexto2.Text, New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto2.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto2.Font = New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)
                    LTexto2.Visible = True
                Else
                    LTexto2.Visible = False
                End If
                BAceptar.Visible = True : BAceptar.Location = New Point(74, 96)
                BCancelar.Visible = True : BCancelar.Location = New Point(217, 96)
                BNo.Visible = False
            Case MsgBoxStyle.YesNoCancel
                Dim TextSize As Single = 8 : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto.Font = New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)
                If TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width > LTexto.Width Then Dim TempText As String = LTexto.Text : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width : TempText = TempText.Substring(0, TempText.Length - 1) : LTexto.Text = TempText + "..." : Loop
                If LTexto2.Text <> "" Then
                    TextSize = 8 : Do Until TextRenderer.MeasureText(LTexto2.Text, New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto2.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto2.Font = New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)
                    LTexto2.Visible = True
                Else
                    LTexto2.Visible = False
                End If
                BAceptar.Visible = True : BAceptar.Location = New Point(28, 96)
                BCancelar.Visible = True : BCancelar.Location = New Point(272, 96)
                BNo.Visible = True
            Case Else
                Dim TextSize As Single = 8 : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto.Font = New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)
                If TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width > LTexto.Width Then Dim TempText As String = LTexto.Text : Do Until TextRenderer.MeasureText(LTexto.Text, New Font(LTexto.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto.Width : TempText = TempText.Substring(0, TempText.Length - 1) : LTexto.Text = TempText + "..." : Loop
                If LTexto2.Text <> "" Then
                    TextSize = 8 : Do Until TextRenderer.MeasureText(LTexto2.Text, New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)).Width < LTexto2.Width Or TextSize < 7 : TextSize -= 1 : Loop : LTexto2.Font = New Font(LTexto2.Font.Name, TextSize, FontStyle.Regular)
                    LTexto2.Visible = True
                Else
                    LTexto2.Visible = False
                End If
                BAceptar.Visible = True : BAceptar.Location = New Point(151, 96)
                BCancelar.Visible = False : BNo.Visible = False
        End Select
    End Sub

    Private Sub FMsgBox_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Opacity = 1 : FMenu.FocusMe(Me) : TopMost = False
    End Sub

    Private Sub FMsgBox_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 13 Then Respuesta = MsgBoxResult.Ok : Close()
        If Asc(e.KeyChar) = 27 Then Respuesta = MsgBoxResult.Cancel : Close()
    End Sub

#End Region

#Region " Botones "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        If e.Button = MouseButtons.Left Then Respuesta = MsgBoxResult.Ok : Close()
    End Sub

    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Respuesta = MsgBoxResult.Cancel : Close()
    End Sub

    Private Sub BNO_Click(sender As Object, e As MouseEventArgs) Handles BNo.Click
        If e.Button = MouseButtons.Left Then Respuesta = MsgBoxResult.No : Close()
    End Sub

    Private Sub BCancelarBNo_MouseEnter(sender As Object, e As EventArgs) Handles BCancelar.MouseEnter, BNo.MouseEnter
        If BAceptar.ForeColor1 = Color.FromArgb(205, 150, 0) Then BAceptar.ForeColor1 = Color.FromArgb(102, 102, 102)
    End Sub

    Private Sub BCancelarBNo_MouseLeave(sender As Object, e As EventArgs) Handles BCancelar.MouseLeave, BNo.MouseLeave
        If BAceptar.ForeColor1 = Color.FromArgb(102, 102, 102) Then BAceptar.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

#End Region

End Class

#End Region