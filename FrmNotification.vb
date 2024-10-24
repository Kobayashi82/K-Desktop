
#Region " Notification "

Public Class FrmNotification

#Region " Variables "

    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function GetForegroundWindow() As IntPtr : End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean : End Function

    Private Shared OpenForms As New List(Of FrmNotification)
    Private BloquearSonido As Boolean
    Private CurrentForegroundWindow As IntPtr
    Private StartTimer As New Timer With {.Enabled = False, .Interval = 2500}
    Private LifeTimer As New Timer With {.Enabled = False, .Interval = 100}

#Region " Constructor "

    Public Sub New(LifeTime As Integer, Nombre As String, Imagen As Image, Texto As String, Optional WaitTime As Integer = 2500)
        CurrentForegroundWindow = GetForegroundWindow()
        InitializeComponent()
        SetForegroundWindow(CurrentForegroundWindow)
        StartTimer.Interval = WaitTime : If WaitTime = 0 Then StartTimer.Interval = 10
        LifeTimer.Interval = LifeTime
        LNombre.Text = Nombre
        PLogro.Image = Imagen
        TLogro.Text = Texto
        Opacity = 0
        AddHandler StartTimer.Tick, AddressOf StartTimer_Tick
        AddHandler LifeTimer.Tick, AddressOf LifeTimer_Tick
    End Sub

#End Region

#End Region

#Region " Formulario "
    Private Sub NotificationForm_Load(sender As Object, e As EventArgs) Handles Me.Load
        BloquearSonido = True
    End Sub

    Public Shadows Sub Show()
        SetForegroundWindow(CurrentForegroundWindow)
        MyBase.Show()
    End Sub

    Private Sub NotificationForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        SetForegroundWindow(CurrentForegroundWindow)
        StartTimer.Enabled = True
    End Sub

    Private Sub NotificationForm_FormClosed(sender As Object, e As FormClosedEventArgs) Handles Me.FormClosed
        For Each openForm As FrmNotification In OpenForms
            If openForm Is Me Then Exit For
            openForm.Top += Height + 5
        Next : OpenForms.Remove(Me) : BloquearSonido = False
    End Sub

    Private Sub StartTimer_Tick(sender As Object, e As EventArgs)
        StartTimer.Enabled = False
        Location = New Point(Screen.PrimaryScreen.WorkingArea.Width - Width - 5, Screen.PrimaryScreen.WorkingArea.Height - Height - 5)
        If OpenForms.Count = 0 Or BloquearSonido = False Then My.Computer.Audio.Play(My.Resources.Logro, AudioPlayMode.Background)
        For Each openForm As FrmNotification In OpenForms : openForm.Top -= Height + 5 : Next
        OpenForms.Add(Me)
        Do Until Opacity = 1 : Opacity += 0.1 : Threading.Thread.Sleep(10) : Application.DoEvents() : Loop
        LifeTimer.Start()
    End Sub

    Private Sub LifeTimer_Tick(sender As Object, e As EventArgs)
        Close()
    End Sub

    Private Sub NotificationForm_Click(sender As Object, e As MouseEventArgs) Handles Me.Click, LNombre.Click, TLogro.Click, PLogro.Click, Separador1.Click, Separador2.Click, Separador3.Click, Separador4.Click, Me.DoubleClick, LNombre.DoubleClick, TLogro.DoubleClick, PLogro.DoubleClick, Separador1.DoubleClick, Separador2.DoubleClick, Separador3.DoubleClick, Separador4.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FLogros.Show() : FLogros.Activate()
    End Sub

#End Region

End Class

#End Region
