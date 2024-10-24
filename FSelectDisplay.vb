
#Region " FSelectDisplay "

Public Class FSelectDisplay

#Region " Estructuras "

    Private Structure LocST
        Dim Loc As Point
        Dim Num As Integer
    End Structure

#End Region

#Region " Formulario "

#Region " Load "

    Private Sub FSelectDisplay_Load(sender As Object, e As EventArgs) Handles Me.Load
        Dim Loc(Screen.AllScreens.Length - 1) As LocST
        Dim FLoc(Screen.AllScreens.Length - 1) As Integer
        For i As Integer = 0 To (Screen.AllScreens.Length - 1)
            Loc(i).Num = i + 1
            Loc(i).Loc = New Point(Screen.AllScreens(i).Bounds.Location.X, Screen.AllScreens(i).Bounds.Location.Y)
            FLoc(i) = Screen.AllScreens(i).Bounds.Location.X
        Next : Array.Sort(FLoc, Loc)

        LPantalla1.Text = Loc(0).Num.ToString
        LPantalla2.Text = Loc(1).Num.ToString
        If Loc.Length >= 3 Then LPantalla3.Text = Loc(2).Num.ToString
        If Loc.Length >= 4 Then LPantalla4.Text = Loc(3).Num.ToString

        Panel1.Visible = True : Panel2.Visible = True : Panel3.Visible = True : Panel4.Visible = True
        Select Case Screen.AllScreens.Count
            Case 2
                Width = 319 : Height = 177 : Panel3.Visible = False : Panel4.Visible = False
                CSonido.Location = New Point(5, 3) : LSonido.Location = New Point(64, 7) : CMicrofono.Location = New Point(5, 30) : LMicrofono.Location = New Point(64, 34)
                CDesktopDuplication.Location = New Point(127, 3) : LDesktopDuplication.Location = New Point(186, 7) : CMarcarClicks.Location = New Point(127, 30) : LMarcarClicks.Location = New Point(186, 34)
            Case 3
                Width = 472 : Height = 177 : Panel4.Visible = False
                CSonido.Location = New Point(94, 3) : LSonido.Location = New Point(153, 7) : CMicrofono.Location = New Point(94, 30) : LMicrofono.Location = New Point(153, 34)
                CDesktopDuplication.Location = New Point(216, 3) : LDesktopDuplication.Location = New Point(275, 7) : CMarcarClicks.Location = New Point(216, 30) : LMarcarClicks.Location = New Point(275, 34)
            Case 4
                Width = 625 : Height = 177
                CSonido.Location = New Point(14, 3) : LSonido.Location = New Point(73, 7) : CMicrofono.Location = New Point(132, 3) : LMicrofono.Location = New Point(191, 7)
                CDesktopDuplication.Location = New Point(320, 3) : LDesktopDuplication.Location = New Point(379, 7) : CMarcarClicks.Location = New Point(457, 3) : LMarcarClicks.Location = New Point(516, 7)
        End Select

        Location = New Point(Screen.FromPoint(Cursor.Position).Bounds.X + ((Screen.FromPoint(Cursor.Position).Bounds.Width / 2) - (Width / 2)), Screen.FromPoint(Cursor.Position).Bounds.Y + ((Screen.FromPoint(Cursor.Position).Bounds.Height / 6) - (Height / 2)))
    End Sub

#End Region

#End Region

#Region " Pantalla "

    Private Sub LPantalla_Click(sender As Object, e As MouseEventArgs) Handles LPantalla1.Click, LPantalla1.DoubleClick, LPantalla2.Click, LPantalla2.DoubleClick, LPantalla3.Click, LPantalla3.DoubleClick, LPantalla4.Click, LPantalla4.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FMenu.GGrabacionPantalla_Pantalla = CInt(sender.Text) - 1
        FMenu.GGrabacionPantalla_DesktopDuplication = CDesktopDuplication.Checked
        FMenu.GGrabacionPantalla_Sonido = CSonido.Checked
        FMenu.GGrabacionPantalla_Microfono = CMicrofono.Checked
        FMenu.GGrabacionPantalla_MarcarClicks = CMarcarClicks.Checked
        Close()
    End Sub

    Private Sub DesktopDuplication_Click(sender As Object, e As MouseEventArgs) Handles CDesktopDuplication.Click, CDesktopDuplication.DoubleClick, LDesktopDuplication.Click, LDesktopDuplication.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CDesktopDuplication.Checked = True Then CDesktopDuplication.Checked = False Else CDesktopDuplication.Checked = True
    End Sub

    Private Sub Sonido_Click(sender As Object, e As MouseEventArgs) Handles CSonido.Click, CSonido.DoubleClick, LSonido.Click, LSonido.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CSonido.Checked = True Then CSonido.Checked = False Else CSonido.Checked = True
    End Sub

    Private Sub Microfono_Click(sender As Object, e As MouseEventArgs) Handles CMicrofono.Click, CMicrofono.DoubleClick, LMicrofono.Click, LMicrofono.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CMicrofono.Checked = True Then CMicrofono.Checked = False Else CMicrofono.Checked = True
    End Sub

    Private Sub MarcarClicks_Click(sender As Object, e As MouseEventArgs) Handles CMarcarClicks.Click, CMarcarClicks.DoubleClick, LMarcarClicks.Click, LMarcarClicks.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If TypeOf sender Is NSLabel Then If CMarcarClicks.Checked = True Then CMarcarClicks.Checked = False Else CMarcarClicks.Checked = True
    End Sub

#Region " Mouse Move "

    Private Sub LPantalla_MouseEnter(sender As Object, e As EventArgs) Handles LPantalla1.MouseEnter, LPantalla2.MouseEnter, LPantalla3.MouseEnter, LPantalla4.MouseEnter
        BPanel.ForeColor = Color.White
        Select Case sender.Name
            Case "LPantalla1"
                LPantalla1.ForeColor = Color.FromArgb(205, 150, 0)
                Panel1.BackColor = LPantalla1.ForeColor
                Panel1.BringToFront() : LPantalla1.BringToFront()
            Case "LPantalla2"
                LPantalla2.ForeColor = Color.FromArgb(205, 150, 0)
                Panel2.BackColor = LPantalla2.ForeColor
                Panel2.BringToFront() : LPantalla2.BringToFront()
            Case "LPantalla3"
                LPantalla3.ForeColor = Color.FromArgb(205, 150, 0)
                Panel3.BackColor = LPantalla3.ForeColor
                Panel3.BringToFront() : LPantalla3.BringToFront()
            Case "LPantalla4"
                LPantalla4.ForeColor = Color.FromArgb(205, 150, 0)
                Panel4.BackColor = LPantalla4.ForeColor
                Panel4.BringToFront() : LPantalla4.BringToFront()
        End Select
    End Sub

    Private Sub LPantalla_MouseLeave(sender As Object, e As EventArgs) Handles LPantalla1.MouseLeave, LPantalla2.MouseLeave, LPantalla3.MouseLeave, LPantalla4.MouseLeave
        Select Case sender.Name
            Case "LPantalla1"
                LPantalla1.ForeColor = Color.White
                Panel1.BackColor = LPantalla1.ForeColor
                Panel1.BringToFront() : LPantalla1.BringToFront()
            Case "LPantalla2"
                LPantalla2.ForeColor = Color.White
                Panel2.BackColor = LPantalla2.ForeColor
                Panel2.BringToFront() : LPantalla2.BringToFront()
            Case "LPantalla3"
                LPantalla3.ForeColor = Color.White
                Panel3.BackColor = LPantalla3.ForeColor
                Panel3.BringToFront() : LPantalla3.BringToFront()
            Case "LPantalla4"
                LPantalla4.ForeColor = Color.White
                Panel4.BackColor = LPantalla4.ForeColor
                Panel4.BringToFront() : LPantalla4.BringToFront()
        End Select
    End Sub

    Private Sub BSubPanel_MouseEnter(sender As Object, e As EventArgs) Handles BSubPanel.MouseEnter, CSonido.MouseEnter, LSonido.MouseEnter, CMicrofono.MouseEnter, LMicrofono.MouseEnter, CMarcarClicks.MouseEnter, LMarcarClicks.MouseEnter, LDesktopDuplication.MouseEnter, CDesktopDuplication.MouseEnter
        BPanel.BackColor = Color.FromArgb(205, 150, 0) : BPanel.BringToFront()
    End Sub

    Private Sub BSubPanel_MouseLeave(sender As Object, e As EventArgs) Handles BSubPanel.MouseLeave, CSonido.MouseLeave, LSonido.MouseLeave, CMicrofono.MouseLeave, LMicrofono.MouseLeave, CDesktopDuplication.MouseLeave, LDesktopDuplication.MouseLeave, CMarcarClicks.MouseLeave, LMarcarClicks.MouseLeave
        BPanel.BackColor = Color.White : BPanel.BringToFront()
    End Sub

    Private Sub Microfono_Click(sender As Object, e As EventArgs) Handles LMicrofono.DoubleClick, LMicrofono.Click, CMicrofono.DoubleClick, CMicrofono.Click, CDesktopDuplication.DoubleClick, CDesktopDuplication.Click, LDesktopDuplication.DoubleClick, LDesktopDuplication.Click, CMarcarClicks.DoubleClick, CMarcarClicks.Click, LMarcarClicks.DoubleClick, LMarcarClicks.Click

    End Sub

#End Region

#End Region

#Region " Tooltips "

    Private Sub SelectDisplay_Tooltips(sender As Object, e As EventArgs) Handles CDesktopDuplication.MouseEnter, LDesktopDuplication.MouseEnter, CSonido.MouseEnter, LSonido.MouseEnter, CMicrofono.MouseEnter, LMicrofono.MouseEnter, CMarcarClicks.MouseEnter, LMarcarClicks.MouseEnter
        Select Case sender.Name
            Case "CSonido", "LSonido" : FMenu.Show_Tooltip(sender, "Graba el sonido del ordenador")
            Case "CMicrofono", "LMicrofono" : FMenu.Show_Tooltip(sender, "Graba el sonido del micrófono")
            Case "CDesktopDuplication", "LDesktopDuplication" : FMenu.Show_Tooltip(sender, "Graba usando el modo 'Desktop Duplication'" + vbCrLf + vbCrLf + "Nota: En este modo puede grabar en aplicaciones" + vbCrLf + " que usan pantalla completa")
            Case "CMarcarClicks", "LMarcarClicks" : FMenu.Show_Tooltip(sender, "Marca los clicks del ratón en la grabación")
        End Select
    End Sub

#End Region

End Class

#End Region