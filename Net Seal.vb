
#Region " Net Seal "

#Region " Theme Module "

Module ThemeModule

    Friend G As Graphics

    Sub New()
        TextBitmap = New Bitmap(1, 1)
        TextGraphics = Graphics.FromImage(TextBitmap)
    End Sub

    Private TextBitmap As Bitmap
    Private TextGraphics As Graphics

    Friend Function MeasureString(text As String, font As Font) As SizeF
        Return TextGraphics.MeasureString(text, font)
    End Function

    Friend Function MeasureString(text As String, font As Font, width As Integer) As SizeF
        Return TextGraphics.MeasureString(text, font, width, StringFormat.GenericTypographic)
    End Function

    Private CreateRoundPath As Drawing2D.GraphicsPath
    Private CreateRoundRectangle As Rectangle

    Friend Function CreateRound(x As Integer, y As Integer, width As Integer, height As Integer, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundRectangle = New Rectangle(x, y, width, height)
        Return CreateRound(CreateRoundRectangle, slope)
    End Function

    Friend Function CreateRound(r As Rectangle, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundPath = New Drawing2D.GraphicsPath(Drawing2D.FillMode.Winding)
        CreateRoundPath.AddArc(r.X, r.Y, slope, slope, 180.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Y, slope, slope, 270.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Bottom - slope, slope, slope, 0.0F, 90.0F)
        CreateRoundPath.AddArc(r.X, r.Bottom - slope, slope, slope, 90.0F, 90.0F)
        CreateRoundPath.CloseFigure()
        Return CreateRoundPath
    End Function

End Module

#End Region

#Region "Theme"

Class NSTheme : Inherits ThemeContainer154

    Private _AccentOffset As Integer = 42
    Public Property AccentOffset() As Integer
        Get
            Return _AccentOffset
        End Get
        Set(value As Integer)
            _AccentOffset = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.FromArgb(102, 102, 102)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Sub New()
        Header = 30
        BackColor = Color.FromArgb(50, 50, 50)

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(60, 60, 60))

        B1 = New SolidBrush(Color.FromArgb(50, 50, 50))
    End Sub

    Protected Overrides Sub ColorHook()

    End Sub

    Private R1 As Rectangle

    Private P1, P2 As Pen
    Private B1 As SolidBrush

    Private Pad As Integer

    Protected Overrides Sub PaintHook()
        G.Clear(BackColor)
        DrawBorders(P2, 1)

        G.DrawLine(P1, 0, 26, Width, 26)
        G.DrawLine(P2, 0, 25, Width, 25)

        Pad = Math.Max(Measure().Width + 20, 80)
        R1 = New Rectangle(Pad + 20, 17, Width - ((Pad + 15) * 2) + _AccentOffset, 8)

        G.DrawRectangle(P2, R1)
        G.DrawRectangle(P1, R1.X + 1, R1.Y + 1, R1.Width - 2, R1.Height)

        G.DrawLine(P1, 0, 29, Width, 29)
        G.DrawLine(P2, 0, 30, Width, 30)

        G.DrawIcon(FindForm.Icon, New Rectangle(3, 3, 24, 24))
        DrawText(New SolidBrush(ForeColor2), HorizontalAlignment.Left, 29, 1)
        DrawText(New SolidBrush(ForeColor1), HorizontalAlignment.Left, 28, 0)

        G.FillRectangle(B1, 0, 27, Width, 2)
        DrawBorders(Pens.Black)
    End Sub

End Class

Class NSThemeLogros : Inherits ThemeContainer154

    Private _AccentOffset As Integer = 42
    Public Property AccentOffset() As Integer
        Get
            Return _AccentOffset
        End Get
        Set(value As Integer)
            _AccentOffset = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.FromArgb(102, 102, 102)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Sub New()
        Header = 30
        BackColor = Color.FromArgb(50, 50, 50)

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(60, 60, 60))

        B1 = New SolidBrush(Color.FromArgb(50, 50, 50))
    End Sub

    Protected Overrides Sub ColorHook()

    End Sub

    Private R1 As Rectangle

    Private P1, P2 As Pen
    Private B1 As SolidBrush

    Protected Overrides Sub PaintHook()
        G.Clear(BackColor)
        DrawBorders(P2, 1)

        G.DrawLine(P1, 0, 26, Width, 26)
        G.DrawLine(P2, 0, 25, Width, 25)

        G.DrawRectangle(P2, R1)
        G.DrawRectangle(P1, R1.X + 1, R1.Y + 1, R1.Width - 2, R1.Height)

        G.DrawLine(P1, 0, 29, Width, 29)
        G.DrawLine(P2, 0, 30, Width, 30)

        DrawText(New SolidBrush(ForeColor2), HorizontalAlignment.Left, 29, 1)
        DrawText(New SolidBrush(ForeColor1), HorizontalAlignment.Left, 28, 0)

        G.FillRectangle(B1, 0, 27, Width, 2)
        DrawBorders(Pens.Black)
    End Sub

End Class

#End Region

#Region "Button"

Class NSButton : Inherits Control

#Region " Variables "

#Region " Mouse States "

    Public Enum STImageMode
        Normal = 0
        Fill = 1
    End Enum

    Public Enum MouseState As Byte
        None = 0
        Hover = 1
        Down = 2
        Block = 3
    End Enum

    Public State As MouseState = MouseState.None

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If e.Button = Windows.Forms.MouseButtons.Left AndAlso IsDisposed = False Then If ClientRectangle.Contains(PointToClient(MousePosition)) Then State = MouseState.Hover Else State = MouseState.None
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = Windows.Forms.MouseButtons.Left Then State = MouseState.Down : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        If Name.Length > 6 Then
            If Name.Substring(0, 6) = "BClose" Or Name.Substring(0, 6) = "BMaxEd" Then BorderColor2 = Color.Green
            If Name.Substring(0, 6) = "BExami" Then BorderColor3 = Color.Green
        End If
        State = MouseState.Hover : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        State = MouseState.None : Invalidate()
    End Sub

#End Region

#Region " Colors "

#Region " BaseColor "

    Private _BackColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property BackColor As Color
        Get
            Return _BackColor
        End Get
        Set(value As Color)
            _BackColor = value
            Invalidate()
        End Set
    End Property

    Private _BaseColor1 As Color = Color.FromArgb(35, 35, 35)
    Property BaseColor1 As Color
        Get
            Return _BaseColor1
        End Get
        Set(value As Color)
            _BaseColor1 = value
            Invalidate()
        End Set
    End Property

    Private _BaseColor2 As Color = Color.Empty
    Property BaseColor2 As Color
        Get
            Return _BaseColor2
        End Get
        Set(value As Color)
            _BaseColor2 = value
            Invalidate()
        End Set
    End Property

    Private _BaseColor3 As Color = Color.Empty
    Property BaseColor3 As Color
        Get
            Return _BaseColor3
        End Get
        Set(value As Color)
            _BaseColor3 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " ForeColor "

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.White
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor3 As Color = Color.White
    Property ForeColor3 As Color
        Get
            Return _ForeColor3
        End Get
        Set(value As Color)
            _ForeColor3 = value
            Invalidate()
        End Set
    End Property

    Private _FirstLetterColor As Color = Color.White
    Property FirstLetterColor As Color
        Get
            Return _FirstLetterColor
        End Get
        Set(value As Color)
            _FirstLetterColor = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " BorderColor "

    Private _BorderColor1 As Color = Color.FromArgb(65, 65, 65)
    Property BorderColor1 As Color
        Get
            Return _BorderColor1
        End Get
        Set(value As Color)
            _BorderColor1 = value
            Invalidate()
        End Set
    End Property

    Private _BorderColor2 As Color = Color.Red
    Property BorderColor2 As Color
        Get
            Return _BorderColor2
        End Get
        Set(value As Color)
            _BorderColor2 = value
            Invalidate()
        End Set
    End Property

    Private _BorderColor3 As Color = Color.Empty
    Property BorderColor3 As Color
        Get
            Return _BorderColor3
        End Get
        Set(value As Color)
            _BorderColor3 = value
            Invalidate()
        End Set
    End Property

    Private _MarkedSetColor As Color = Color.FromArgb(205, 150, 0)
    Property MarkedSetColor As Color
        Get
            Return _MarkedSetColor
        End Get
        Set(value As Color)
            _MarkedSetColor = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " Gradient "

    Private _GradientColor1 As Color = Color.FromArgb(60, 60, 60)
    Property GradientColor1 As Color
        Get
            Return _GradientColor1
        End Get
        Set(value As Color)
            _GradientColor1 = value
            Invalidate()
        End Set
    End Property

    Private _GradientColor2 As Color = Color.FromArgb(55, 55, 55)
    Property GradientColor2 As Color
        Get
            Return _GradientColor2
        End Get
        Set(value As Color)
            _GradientColor2 = value
            Invalidate()
        End Set
    End Property

    Private _GradientAngle As Integer = 90
    Property GradientAngle As Integer
        Get
            Return _GradientAngle
        End Get
        Set(value As Integer)
            If value > 360 Then value = 360
            If value < 1 Then value = 1
            _GradientAngle = value
            Invalidate()
        End Set
    End Property

    Private _GradientTransparency As Integer = 0
    Property GradientTransparency As Integer
        Get
            Return _GradientTransparency
        End Get
        Set(value As Integer)
            If value > 255 Then value = 255
            If value < 0 Then value = 0
            _GradientTransparency = value
            Invalidate()
        End Set
    End Property

#End Region

#End Region

#Region " Images "

    Private _BackgroundImageLayout As ImageLayout
    <ComponentModel.Browsable(False)> Overrides Property BackgroundImageLayout As ImageLayout
        Get
            Return _BackgroundImageLayout
        End Get
        Set(value As ImageLayout)
            _BackgroundImageLayout = value
        End Set
    End Property

    Private _BackgroundImage As Image
    <ComponentModel.Browsable(False)> Overrides Property BackgroundImage As Image
        Get
            Return _BackgroundImage
        End Get
        Set(value As Image)
            _BackgroundImage = value
        End Set
    End Property

    Private _Image As Image = Nothing
    Property Image As Image
        Get
            Return _Image
        End Get
        Set(value As Image)
            _Image = value
            Invalidate()
        End Set
    End Property

    Private _ImageSize As New Size(19, 19)
    Property ImageSize As Size
        Get
            Return _ImageSize
        End Get
        Set(value As Size)
            _ImageSize = value
            Invalidate()
        End Set
    End Property

    Private _ImageAlign As ContentAlignment = ContentAlignment.MiddleLeft
    Property ImageAlign() As ContentAlignment
        Get
            Return _ImageAlign
        End Get
        Set(value As ContentAlignment)
            _ImageAlign = value
            Invalidate()
        End Set
    End Property

    Private _ImageMode As STImageMode = STImageMode.Normal
    Property ImageMode() As STImageMode
        Get
            Return _ImageMode
        End Get
        Set(value As STImageMode)
            _ImageMode = value
            Invalidate()
        End Set
    End Property

    Private _ImageCode As String = ""
    Property ImageCode As String
        Get
            Return _ImageCode
        End Get
        Set(value As String)
            _ImageCode = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " Others "

    Private _TextAlign As ContentAlignment = ContentAlignment.MiddleCenter
    Property TextAlign() As ContentAlignment
        Get
            Return _TextAlign
        End Get
        Set(value As ContentAlignment)
            _TextAlign = value
            Invalidate()
        End Set
    End Property

    Private _DoubleText As Boolean = False
    Property DoubleText As Boolean
        Get
            Return _DoubleText
        End Get
        Set(value As Boolean)
            _DoubleText = value
        End Set
    End Property

    Private _Rounded As Integer = 7
    Property Rounded As Integer
        Get
            Return _Rounded
        End Get
        Set(value As Integer)
            If value > 30 Then value = 30
            If value < 1 Then value = 1
            _Rounded = value
            Invalidate()
        End Set
    End Property

    Private _Marked As Boolean = False
    Property Marked As Boolean
        Get
            Return _Marked
        End Get
        Set(value As Boolean)
            _Marked = value
        End Set
    End Property

    Private _MarkedHovered As Boolean = True
    Property MarkedHovered As Boolean
        Get
            Return _MarkedHovered
        End Get
        Set(value As Boolean)
            _MarkedHovered = value
        End Set
    End Property

    Private _Marked_Set As Boolean = False
    Property Marked_Set As Boolean
        Get
            Return _Marked_Set
        End Get
        Set(value As Boolean)
            _Marked_Set = value
        End Set
    End Property

    Private _CTRL As Boolean = False
    Property CTRL As Boolean
        Get
            Return _CTRL
        End Get
        Set(value As Boolean)
            _CTRL = value
        End Set
    End Property

    Private _SHIFT As Boolean = False
    Property SHIFT As Boolean
        Get
            Return _SHIFT
        End Get
        Set(value As Boolean)
            _SHIFT = value
        End Set
    End Property

    Private _ALT As Boolean = False
    Property ALT As Boolean
        Get
            Return _ALT
        End Get
        Set(value As Boolean)
            _ALT = value
        End Set
    End Property

    Private _WIN As Boolean = False
    Property WIN As Boolean
        Get
            Return _WIN
        End Get
        Set(value As Boolean)
            _WIN = value
        End Set
    End Property

    Public Tag2 As Object
    Public isCtrl, isShift, isAlt, isWin, HoverMenu As Boolean

#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.SupportsTransparentBackColor, True)
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)
        DoubleBuffered = True

        Font = New Font("Verdana", 8)
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim G As Graphics = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        G.Clear(Parent.BackColor)

        Dim PB1 As Drawing2D.PathGradientBrush
        Dim GB1 As Drawing2D.LinearGradientBrush
        'Dim GP1 As Drawing2D.GraphicsPath = CreateRound(0, 0, Width - 1, Height - 1, Rounded)
        'Dim GP2 As Drawing2D.GraphicsPath = CreateRound(1, 1, Width - 3, Height - 3, Rounded)
        Dim GP1 As New Drawing2D.GraphicsPath : GP1.AddRectangle(New RectangleF(0, 0, Width - 1, Height - 1))
        Dim GP2 As New Drawing2D.GraphicsPath : GP2.AddRectangle(New RectangleF(1, 1, Width - 3, Height - 3))

        If FMenu.PanelKBoard1.Visible = True AndAlso FMenu.KBoard_MenuTecla.Visible = True AndAlso HoverMenu = True AndAlso Name <> "KBoard_Nombre_BAdd" AndAlso Name <> "KBoard_Nombre_BDelete" AndAlso Name <> "KBoard_BProcesoAdd" AndAlso Name <> "KBoard_BProcesoEliminar" AndAlso Name <> "KBoard_BProcesoExaminar" Then State = MouseState.Hover

        Select Case State
            Case MouseState.None
                GB1 = New Drawing2D.LinearGradientBrush(ClientRectangle, GradientColor1, GradientColor2, GradientAngle)
                G.FillPath(GB1, GP1)
                If Marked_Set = True Then
                    G.DrawPath(New Pen(Color.FromArgb(BaseColor2.ToArgb)), GP1)
                    G.DrawPath(New Pen(Color.FromArgb(MarkedSetColor.ToArgb)), GP2)
                Else
                    If Marked = True Then
                        G.DrawPath(New Pen(Color.FromArgb(BaseColor2.ToArgb)), GP1)
                        G.DrawPath(New Pen(Color.FromArgb(BorderColor2.ToArgb)), GP2)
                    Else
                        G.DrawPath(New Pen(Color.FromArgb(BaseColor1.ToArgb)), GP1)
                        G.DrawPath(New Pen(Color.FromArgb(BorderColor1.ToArgb)), GP2)
                    End If
                End If
                If Name.Length > 6 Then
                    If Name.Substring(0, 6) = "BClose" Then
                        Dim ClosePath As New Drawing2D.GraphicsPath
                        Dim x = 4
                        Dim y = 3
                        ClosePath.AddLine(x + 1, y, x + 3, y)
                        ClosePath.AddLine(x + 5, y + 2, x + 7, y)
                        ClosePath.AddLine(x + 9, y, x + 10, y + 1)
                        ClosePath.AddLine(x + 7, y + 4, x + 7, y + 5)
                        ClosePath.AddLine(x + 10, y + 8, x + 9, y + 9)
                        ClosePath.AddLine(x + 7, y + 9, x + 5, y + 7)
                        ClosePath.AddLine(x + 3, y + 9, x + 1, y + 9)
                        ClosePath.AddLine(x + 0, y + 8, x + 3, y + 5)
                        ClosePath.AddLine(x + 3, y + 4, x + 0, y + 1)
                        G.FillPath(New SolidBrush(ForeColor1), ClosePath)
                        G.DrawPath(New Pen(ForeColor2), ClosePath)
                        GoTo Fin
                    End If
                End If
                If Name.Length > 6 Then
                    If Name.Substring(0, 4) = "BMax" Then
                        G.DrawRectangle(New Pen(ForeColor1, 2), 5, 5, 10, 6)
                        G.DrawRectangle(New Pen(ForeColor2), 3, 3, 13, 9)
                        G.DrawRectangle(New Pen(ForeColor2), 6, 6, 7, 3)
                        GoTo Fin
                    End If
                End If
                If DoubleText = True Then DrawText(G, Text, Font, ForeColor2, 1, 1, False, True)
                DrawText(G, Text, Font, ForeColor1)
            Case MouseState.Down
                PB1 = New Drawing2D.PathGradientBrush(GP1)
                PB1.CenterColor = GradientColor1
                PB1.SurroundColors = {GradientColor2}
                PB1.FocusScales = New PointF(0.8F, 0.5F)
                G.FillPath(PB1, GP1)
                G.DrawPath(New Pen(Color.FromArgb(BaseColor1.ToArgb)), GP1)
                If Marked_Set = True Then
                    G.DrawPath(New Pen(Color.FromArgb(MarkedSetColor.ToArgb)), GP2)
                Else
                    If Marked = True Then
                        G.DrawPath(New Pen(Color.FromArgb(BorderColor2.ToArgb)), GP2)
                    Else
                        If MarkedHovered = True Then G.DrawPath(New Pen(Color.FromArgb(BorderColor3.ToArgb)), GP2) Else G.DrawPath(New Pen(Color.FromArgb(BorderColor1.ToArgb)), GP2)
                    End If
                End If
                If Name.Length > 6 Then
                    If Name.Substring(0, 6) = "BClose" Then
                        Dim ClosePath As New Drawing2D.GraphicsPath
                        Dim x = 4
                        Dim y = 3
                        ClosePath.AddLine(x + 1, y, x + 3, y)
                        ClosePath.AddLine(x + 5, y + 2, x + 7, y)
                        ClosePath.AddLine(x + 9, y, x + 10, y + 1)
                        ClosePath.AddLine(x + 7, y + 4, x + 7, y + 5)
                        ClosePath.AddLine(x + 10, y + 8, x + 9, y + 9)
                        ClosePath.AddLine(x + 7, y + 9, x + 5, y + 7)
                        ClosePath.AddLine(x + 3, y + 9, x + 1, y + 9)
                        ClosePath.AddLine(x + 0, y + 8, x + 3, y + 5)
                        ClosePath.AddLine(x + 3, y + 4, x + 0, y + 1)
                        G.FillPath(New SolidBrush(Color.DarkRed), ClosePath)
                        G.DrawPath(New Pen(ForeColor2), ClosePath)
                        GoTo Fin
                    End If
                End If
                If Name.Length > 4 Then
                    If Name.Substring(0, 4) = "BMax" Then
                        G.DrawRectangle(New Pen(Color.DarkRed, 2), 5, 5, 10, 6)
                        G.DrawRectangle(New Pen(ForeColor2), 3, 3, 13, 9)
                        G.DrawRectangle(New Pen(ForeColor2), 6, 6, 7, 3)
                        GoTo Fin
                    End If
                End If
                If DoubleText = True Then DrawText(G, Text, Font, ForeColor2, 2, 2, False, True)
                DrawText(G, Text, Font, ForeColor3, 1, 1, False, True)
            Case MouseState.Hover
                GB1 = New Drawing2D.LinearGradientBrush(ClientRectangle, GradientColor1, GradientColor2, GradientAngle)
                G.FillPath(GB1, GP1)
                G.DrawPath(New Pen(Color.FromArgb(BaseColor1.ToArgb)), GP1)
                If Marked_Set = True Then
                    G.DrawPath(New Pen(Color.FromArgb(MarkedSetColor.ToArgb)), GP2)
                Else
                    If Marked = True Then
                        G.DrawPath(New Pen(Color.FromArgb(BorderColor2.ToArgb)), GP2)
                    Else
                        If MarkedHovered = True Then G.DrawPath(New Pen(Color.FromArgb(BorderColor3.ToArgb)), GP2) Else G.DrawPath(New Pen(Color.FromArgb(BorderColor1.ToArgb)), GP2)
                    End If
                End If
                If Name.Length > 6 Then
                    If Name.Substring(0, 6) = "BClose" Then
                        Dim ClosePath As New Drawing2D.GraphicsPath
                        Dim x = 4
                        Dim y = 3
                        ClosePath.AddLine(x + 1, y, x + 3, y)
                        ClosePath.AddLine(x + 5, y + 2, x + 7, y)
                        ClosePath.AddLine(x + 9, y, x + 10, y + 1)
                        ClosePath.AddLine(x + 7, y + 4, x + 7, y + 5)
                        ClosePath.AddLine(x + 10, y + 8, x + 9, y + 9)
                        ClosePath.AddLine(x + 7, y + 9, x + 5, y + 7)
                        ClosePath.AddLine(x + 3, y + 9, x + 1, y + 9)
                        ClosePath.AddLine(x + 0, y + 8, x + 3, y + 5)
                        ClosePath.AddLine(x + 3, y + 4, x + 0, y + 1)
                        G.FillPath(New SolidBrush(BorderColor2), ClosePath)
                        G.DrawPath(New Pen(ForeColor2), ClosePath)
                        GoTo Fin
                    End If
                End If
                If Name.Length > 4 Then
                    If Name.Substring(0, 4) = "BMax" Then
                        G.DrawRectangle(New Pen(BorderColor2, 2), 5, 5, 10, 6)
                        G.DrawRectangle(New Pen(ForeColor2), 3, 3, 13, 9)
                        G.DrawRectangle(New Pen(ForeColor2), 6, 6, 7, 3)
                    End If
                End If
                If DoubleText = True Then DrawText(G, Text, Font, ForeColor2, 1, 1, False, True)
                DrawText(G, Text, Font, ForeColor3)
        End Select
Fin:
        Dim cTexto As String = ""
        If CTRL = True Then cTexto += "C "
        If ALT = True Then cTexto += "A "
        If SHIFT = True Then cTexto += "S "
        If WIN = True Then cTexto += "W"
        If cTexto.EndsWith(" ") Then cTexto = cTexto.Substring(0, cTexto.Length - 1)
        If cTexto <> "" Then
            Dim cFont As New Font("Tw Cen MT Condensed Extra Bold", 6)
            Dim TextLocation As PointF
            TextLocation.X = ((Width / 2) - (G.MeasureString(cTexto, cFont).Width / 2)) - 1
            'If DoubleText = True Then G.DrawString(cTexto, cFont, New SolidBrush(ForeColor2), TextLocation.X + 2, TextLocation.Y + 2)
            G.DrawString(cTexto, cFont, New SolidBrush(Color.Gray), TextLocation.X + 1, TextLocation.Y + 2)
        End If
        MyBase.OnPaint(e)
    End Sub

#Region " DrawText "

    Private Sub DrawText(ByRef G As Graphics, DText As String, DFont As Font, DColor As Color, Optional XOffset As Integer = 0, Optional YOffset As Integer = 0, Optional DrawImage As Boolean = True, Optional CalcImage As Boolean = False, Optional WOffset As Integer = 0, Optional HOffset As Integer = 0)
        Dim TextLocation As PointF
        Dim TextSize As New Size(G.MeasureString(DText, DFont).Width, G.MeasureString(DText, DFont).Height)
        If TextAlign = ContentAlignment.TopLeft Then TextLocation = New PointF(5 + WOffset + XOffset, 5 + HOffset + YOffset)
        If TextAlign = ContentAlignment.TopCenter Then TextLocation = New PointF(((((Width + WOffset) \ 2) - G.MeasureString(DText, DFont).Width / 2) + XOffset), 5 + HOffset + YOffset)
        If TextAlign = ContentAlignment.TopRight Then TextLocation = New PointF((Width - (G.MeasureString(DText, DFont).Width + 5)) + XOffset, 5 + HOffset + YOffset)
        If TextAlign = ContentAlignment.MiddleLeft Then TextLocation = New PointF(5 + WOffset + XOffset, ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)
        If TextAlign = ContentAlignment.MiddleCenter Then TextLocation = New PointF(((((Width + WOffset) \ 2) - G.MeasureString(DText, DFont).Width / 2) + XOffset), ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)
        If TextAlign = ContentAlignment.MiddleRight Then TextLocation = New PointF((Width - (G.MeasureString(DText, DFont).Width + 5)) + XOffset, ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)
        If TextAlign = ContentAlignment.BottomLeft Then TextLocation = New PointF(5 + WOffset + XOffset, (Height + HOffset) - (G.MeasureString(DText, DFont).Height + 5) + YOffset)
        If TextAlign = ContentAlignment.BottomCenter Then TextLocation = New PointF(((((Width + WOffset) \ 2) - G.MeasureString(DText, DFont).Width / 2) + XOffset), (Height + HOffset) - (G.MeasureString(DText, DFont).Height + 5) + YOffset)
        If TextAlign = ContentAlignment.BottomRight Then TextLocation = New PointF((Width - (G.MeasureString(DText, DFont).Width + 5)) + XOffset, (Height + HOffset) - (G.MeasureString(DText, DFont).Height + 5) + YOffset)

        Dim ImageLocation As PointF
        If ImageAlign = ContentAlignment.TopLeft Then ImageLocation = New PointF(3 + WOffset, 2 + HOffset)
        If ImageAlign = ContentAlignment.TopCenter Then ImageLocation = New PointF((((Width - WOffset) / 2) - (ImageSize.Width / 2) + WOffset), 2 + HOffset)
        If ImageAlign = ContentAlignment.TopRight Then ImageLocation = New PointF(Width - ImageSize.Width - 3, 2 + HOffset)
        If ImageAlign = ContentAlignment.MiddleLeft Then ImageLocation = New PointF(3 + WOffset, ((Height - 1) / 2) - (ImageSize.Height / 2))
        If ImageAlign = ContentAlignment.MiddleCenter Then ImageLocation = New PointF((((Width - WOffset) / 2) - (ImageSize.Width / 2) + WOffset), ((Height - 1) / 2) - (ImageSize.Height / 2))
        If ImageAlign = ContentAlignment.MiddleRight Then ImageLocation = New PointF(Width - ImageSize.Width - 3, ((Height - 1) / 2) - (ImageSize.Height / 2))
        If ImageAlign = ContentAlignment.BottomLeft Then ImageLocation = New PointF(3 + WOffset, (Height + HOffset) - (ImageSize.Height + 2))
        If ImageAlign = ContentAlignment.BottomCenter Then ImageLocation = New PointF((((Width - WOffset) / 2) - (ImageSize.Width / 2) + WOffset), (Height + HOffset) - (ImageSize.Height + 2))
        If ImageAlign = ContentAlignment.BottomRight Then ImageLocation = New PointF(Width - ImageSize.Width - 3, (Height + HOffset) - (ImageSize.Height + 2))

        If Image Is Nothing Or DrawImage = False And CalcImage = False Then
            G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
        ElseIf ImageMode = STImageMode.Fill Then
            G.DrawImage(Image, 1 + WOffset, 1 + HOffset, Width - 2 - WOffset, Height - 2 - HOffset)
            G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
        Else
            G.DrawImage(Image, ImageLocation.X, ImageLocation.Y, ImageSize.Width, ImageSize.Height)
            Select Case TextAlign
                Case ContentAlignment.TopLeft
                    If ImageAlign = ContentAlignment.TopLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X + ImageSize.Width + 4 + XOffset, TextLocation.Y)
                    If ImageAlign = ContentAlignment.TopCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.TopRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.TopCenter
                    If ImageAlign = ContentAlignment.TopLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.TopCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.TopRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.TopRight
                    If ImageAlign = ContentAlignment.TopLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.TopCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.TopRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X - TextSize.Width - 4 + XOffset, TextLocation.Y)
                Case ContentAlignment.MiddleLeft
                    If ImageAlign = ContentAlignment.MiddleLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X + ImageSize.Width + 4 + XOffset, TextLocation.Y)
                    If ImageAlign = ContentAlignment.MiddleCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.MiddleRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.MiddleCenter
                    If ImageAlign = ContentAlignment.MiddleLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.MiddleCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.MiddleRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.MiddleRight
                    If ImageAlign = ContentAlignment.MiddleLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.MiddleCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.MiddleRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X - TextSize.Width - 4 + XOffset, TextLocation.Y)
                Case ContentAlignment.BottomLeft
                    If ImageAlign = ContentAlignment.BottomLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X + ImageSize.Width + 4 + XOffset, TextLocation.Y)
                    If ImageAlign = ContentAlignment.BottomCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.BottomRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.BottomCenter
                    If ImageAlign = ContentAlignment.BottomLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.BottomCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.BottomRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case ContentAlignment.BottomRight
                    If ImageAlign = ContentAlignment.BottomLeft Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.BottomCenter Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                    If ImageAlign = ContentAlignment.BottomRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X - TextSize.Width - 4 + XOffset, TextLocation.Y)
            End Select

            If TextAlign = ContentAlignment.TopLeft Or TextAlign = ContentAlignment.TopCenter Or TextAlign = ContentAlignment.TopRight Then If ImageAlign <> ContentAlignment.TopLeft And ImageAlign <> ContentAlignment.TopCenter And ImageAlign <> ContentAlignment.TopRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
            If TextAlign = ContentAlignment.MiddleLeft Or TextAlign = ContentAlignment.MiddleCenter Or TextAlign = ContentAlignment.MiddleRight Then If ImageAlign <> ContentAlignment.MiddleLeft And ImageAlign <> ContentAlignment.MiddleCenter And ImageAlign <> ContentAlignment.MiddleRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
            If TextAlign = ContentAlignment.BottomLeft Or TextAlign = ContentAlignment.BottomCenter Or TextAlign = ContentAlignment.BottomRight Then If ImageAlign <> ContentAlignment.BottomLeft And ImageAlign <> ContentAlignment.BottomCenter And ImageAlign <> ContentAlignment.BottomRight Then G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)

        End If
    End Sub

#End Region

#End Region

End Class

#End Region

#Region "ProgressBar"

Public Class NSProgressBar : Inherits Control

    Private _Minimum As Integer
    Property Minimum() As Integer
        Get
            Return _Minimum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Minimum = value
            If value > _Value Then _Value = value
            If value > _Maximum Then _Maximum = value
            Invalidate()
        End Set
    End Property

    Private _Maximum As Integer = 100
    Property Maximum() As Integer
        Get
            Return _Maximum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Maximum = value
            If value < _Value Then _Value = value
            If value < _Minimum Then _Minimum = value
            Invalidate()
        End Set
    End Property

    Private _Value As Integer
    Property Value() As Integer
        Get
            Return _Value
        End Get
        Set(value As Integer)
            If value > _Maximum OrElse value < _Minimum Then

            Else
                _Value = value
                Invalidate()
            End If
        End Set
    End Property

    'Private Sub Increment(amount As Integer)
    '    Value += amount
    'End Sub

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(55, 55, 55))
        B1 = New SolidBrush(Color.FromArgb(200, 160, 0))
    End Sub

    Private GP1, GP2, GP3 As Drawing2D.GraphicsPath

    Private R1, R2 As Rectangle

    Private P1, P2 As Pen
    Private B1 As SolidBrush
    Private GB1, GB2 As Drawing2D.LinearGradientBrush

    Private I1 As Integer

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = CreateRound(0, 0, Width - 1, Height - 1, 7)
        GP2 = CreateRound(1, 1, Width - 3, Height - 3, 7)

        R1 = New Rectangle(0, 2, Width - 1, Height - 1)
        GB1 = New Drawing2D.LinearGradientBrush(R1, Color.FromArgb(45, 45, 45), Color.FromArgb(50, 50, 50), 90.0F)

        G.SetClip(GP1)
        G.FillRectangle(GB1, R1)

        I1 = CInt((_Value - _Minimum) / (_Maximum - _Minimum) * (Width - 3))

        If I1 > 1 Then
            GP3 = CreateRound(1, 1, I1, Height - 3, 7)

            R2 = New Rectangle(1, 1, I1, Height - 3)
            GB2 = New Drawing2D.LinearGradientBrush(R2, Color.FromArgb(205, 150, 0), Color.FromArgb(150, 110, 0), 90.0F)

            G.FillPath(GB2, GP3)
            G.DrawPath(P1, GP3)

            G.SetClip(GP3)
            G.SmoothingMode = Drawing2D.SmoothingMode.None

            G.FillRectangle(B1, R2.X, R2.Y + 1, R2.Width, R2.Height \ 2)

            G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            G.ResetClip()
        End If

        G.DrawPath(P2, GP1)
        G.DrawPath(P1, GP2)
    End Sub

End Class

#End Region

#Region "Loading Circle"

Public Class NSLoadingCircle : Inherits Control

#Region " Variables "

    Private Const NumberOfDegreesInCircle As Double = 360
    Private Const NumberOfDegreesInHalfCircle As Double = NumberOfDegreesInCircle / 2
    Private Const DefaultInnerCircleRadius As Integer = 8
    Private Const DefaultOuterCircleRadius As Integer = 10
    Private Const DefaultNumberOfSpoke As Integer = 10
    Private Const DefaultSpokeThickness As Integer = 4
    Private ReadOnly DefaultColor As Color = Color.FromArgb(205, 150, 0)
    Private Const MacOSXInnerCircleRadius As Integer = 5
    Private Const MacOSXOuterCircleRadius As Integer = 11
    Private Const MacOSXNumberOfSpoke As Integer = 12
    Private Const MacOSXSpokeThickness As Integer = 2
    Private Const FireFoxInnerCircleRadius As Integer = 6
    Private Const FireFoxOuterCircleRadius As Integer = 7
    Private Const FireFoxNumberOfSpoke As Integer = 9
    Private Const FireFoxSpokeThickness As Integer = 4
    Private Const IE7InnerCircleRadius As Integer = 8
    Private Const IE7OuterCircleRadius As Integer = 9
    Private Const IE7NumberOfSpoke As Integer = 24
    Private Const IE7SpokeThickness As Integer = 4

#Region " Enumerators "

    Public Enum StylePresets
        MacOSX
        Firefox
        IE7
        Custom
    End Enum

#End Region

#Region " Properties "

    Private m_ProgressValue As Integer
    Private m_CenterPoint As PointF
    Private m_Colors As Color()
    Private m_Angles As Double()

    Private m_Color As Color
    <ComponentModel.TypeConverter("System.Drawing.ColorConverter"), ComponentModel.Category("LoadingCircle"), ComponentModel.Description("Sets the color of spoke.")>
    Public Property Color As Color
        Get
            Return m_Color
        End Get
        Set(value As Color)
            m_Color = value : GenerateColorsPallet() : Invalidate()
        End Set
    End Property

    Private m_OuterCircleRadius As Integer
    <ComponentModel.Description("Gets or sets the radius of outer circle."), ComponentModel.Category("LoadingCircle")>
    Public Property OuterCircleRadius As Integer
        Get
            If m_OuterCircleRadius = 0 Then m_OuterCircleRadius = DefaultOuterCircleRadius
            Return m_OuterCircleRadius
        End Get
        Set(value As Integer)
            m_OuterCircleRadius = value : Invalidate()
        End Set
    End Property

    Private m_InnerCircleRadius As Integer
    <ComponentModel.Description("Gets or sets the radius of inner circle."), ComponentModel.Category("LoadingCircle")>
    Public Property InnerCircleRadius As Integer
        Get
            If m_InnerCircleRadius = 0 Then m_InnerCircleRadius = DefaultInnerCircleRadius
            Return m_InnerCircleRadius
        End Get
        Set(value As Integer)
            m_InnerCircleRadius = value : Invalidate()
        End Set
    End Property

    Private m_NumberOfSpoke As Integer
    <ComponentModel.Description("Gets or sets the number of spoke."), ComponentModel.Category("LoadingCircle")>
    Public Property NumberSpoke As Integer
        Get
            If m_NumberOfSpoke = 0 Then m_NumberOfSpoke = DefaultNumberOfSpoke
            Return m_NumberOfSpoke
        End Get
        Set(value As Integer)
            If m_NumberOfSpoke <> value AndAlso m_NumberOfSpoke > 0 Then m_NumberOfSpoke = value : GenerateColorsPallet() : GetSpokesAngles() : Invalidate()
        End Set
    End Property

    Private m_IsTimerActive As Boolean
    <ComponentModel.Description("Gets or sets the number of spoke."), ComponentModel.Category("LoadingCircle")>
    Public Property Active As Boolean
        Get
            Return m_IsTimerActive
        End Get
        Set(value As Boolean)
            m_IsTimerActive = value : ActiveTimer()
        End Set
    End Property

    Private m_SpokeThickness As Integer
    <ComponentModel.Description("Gets or sets the thickness of a spoke."), ComponentModel.Category("LoadingCircle")>
    Public Property SpokeThickness As Integer
        Get
            If m_SpokeThickness <= 0 Then m_SpokeThickness = DefaultSpokeThickness
            Return m_SpokeThickness
        End Get
        Set(value As Integer)
            m_SpokeThickness = value : Invalidate()
        End Set
    End Property

    Private m_Timer As Timer
    <ComponentModel.Description("Gets or sets the rotation speed. Higher the slower."), ComponentModel.Category("LoadingCircle")>
    Public Property RotationSpeed As Integer
        Get
            Return m_Timer.Interval
        End Get
        Set(value As Integer)
            If value > 0 Then m_Timer.Interval = value
        End Set
    End Property

    Private m_StylePreset As StylePresets
    <ComponentModel.Category("LoadingCircle"), ComponentModel.Description("Quickly sets the style to one of these presets, or a custom style if desired"), ComponentModel.DefaultValue(GetType(StylePresets), "Custom")>
    Public Property StylePreset As StylePresets
        Get
            Return m_StylePreset
        End Get
        Set(value As StylePresets)
            m_StylePreset = value
            Select Case m_StylePreset
                Case StylePresets.MacOSX
                    SetCircleAppearance(MacOSXNumberOfSpoke, MacOSXSpokeThickness, MacOSXInnerCircleRadius, MacOSXOuterCircleRadius)
                Case StylePresets.Firefox
                    SetCircleAppearance(FireFoxNumberOfSpoke, FireFoxSpokeThickness, FireFoxInnerCircleRadius, FireFoxOuterCircleRadius)
                Case StylePresets.IE7
                    SetCircleAppearance(IE7NumberOfSpoke, IE7SpokeThickness, IE7InnerCircleRadius, IE7OuterCircleRadius)
                Case StylePresets.Custom
                    SetCircleAppearance(DefaultNumberOfSpoke, DefaultSpokeThickness, DefaultInnerCircleRadius, DefaultOuterCircleRadius)
            End Select
        End Set
    End Property

#End Region

#End Region

#Region " Constructor "

    Public Sub New()
        SetStyle(ControlStyles.UserPaint, True) : SetStyle(ControlStyles.OptimizedDoubleBuffer, True) : SetStyle(ControlStyles.ResizeRedraw, True) : SetStyle(ControlStyles.SupportsTransparentBackColor, True)
        m_Color = DefaultColor : GenerateColorsPallet() : GetSpokesAngles() : GetControlCenterPoint()
        m_Timer = New Timer() : AddHandler m_Timer.Tick, AddressOf aTimer_Tick : ActiveTimer() : AddHandler Resize, AddressOf LoadingCircle_Resize
    End Sub

#End Region

#Region " Events "

    Private Sub LoadingCircle_Resize(sender As Object, e As EventArgs)
        GetControlCenterPoint()
    End Sub

    Private Sub ATimer_Tick(sender As Object, e As EventArgs)
        m_ProgressValue = Threading.Interlocked.Increment(m_ProgressValue) Mod m_NumberOfSpoke : Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        If m_NumberOfSpoke > 0 Then
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.HighQuality
            Dim intPosition As Integer = m_ProgressValue
            For intCounter As Integer = 0 To m_NumberOfSpoke - 1
                intPosition = intPosition Mod m_NumberOfSpoke
                DrawLine(e.Graphics, GetCoordinate(m_CenterPoint, m_InnerCircleRadius, m_Angles(intPosition)), GetCoordinate(m_CenterPoint, m_OuterCircleRadius, m_Angles(intPosition)), m_Colors(intCounter), m_SpokeThickness)
                intPosition += 1
            Next
        End If : MyBase.OnPaint(e)
    End Sub

#End Region

#Region " Functions "

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        proposedSize.Width = (m_OuterCircleRadius + m_SpokeThickness) * 2 : Return proposedSize
    End Function

    Private Function Darken(_objColor As Color, _intPercent As Integer) As Color
        Dim intRed As Integer = _objColor.R : Dim intGreen As Integer = _objColor.G : Dim intBlue As Integer = _objColor.B
        Return Color.FromArgb(_intPercent, Math.Min(intRed, Byte.MaxValue), Math.Min(intGreen, Byte.MaxValue), Math.Min(intBlue, Byte.MaxValue))
    End Function

    Private Sub GenerateColorsPallet()
        m_Colors = GenerateColorsPallet(m_Color, Active, m_NumberOfSpoke)
    End Sub

    Private Function GenerateColorsPallet(_objColor As Color, _blnShadeColor As Boolean, _intNbSpoke As Integer) As Color()
        Dim objColors As Color() = New Color(NumberSpoke - 1) {}
        Dim bytIncrement As Byte = CByte((Byte.MaxValue / NumberSpoke))
        Dim PERCENTAGE_OF_DARKEN As Byte = 0
        For intCursor As Integer = 0 To NumberSpoke - 1
            If _blnShadeColor Then
                If intCursor = 0 OrElse intCursor < NumberSpoke - _intNbSpoke Then
                    objColors(intCursor) = _objColor
                Else
                    PERCENTAGE_OF_DARKEN += bytIncrement
                    If PERCENTAGE_OF_DARKEN > Byte.MaxValue Then PERCENTAGE_OF_DARKEN = Byte.MaxValue
                    objColors(intCursor) = Darken(_objColor, PERCENTAGE_OF_DARKEN)
                End If
            Else
                objColors(intCursor) = _objColor
            End If
        Next : Return objColors
    End Function

    Private Sub GetControlCenterPoint()
        m_CenterPoint = GetControlCenterPoint(Me)
    End Sub

    Private Function GetControlCenterPoint(_objControl As Control) As PointF
        Return New PointF(_objControl.Width / 2, _objControl.Height / 2 - 1)
    End Function

    Private Sub DrawLine(_objGraphics As Graphics, _objPointOne As PointF, _objPointTwo As PointF, _objColor As Color, _intLineThickness As Integer)
        Using objPen As New Pen(New SolidBrush(_objColor), _intLineThickness)
            objPen.StartCap = Drawing2D.LineCap.Round
            objPen.EndCap = Drawing2D.LineCap.Round
            _objGraphics.DrawLine(objPen, _objPointOne, _objPointTwo)
        End Using
    End Sub

    Private Function GetCoordinate(_objCircleCenter As PointF, _intRadius As Integer, _dblAngle As Double) As PointF
        Dim dblAngle As Double = Math.PI * _dblAngle / NumberOfDegreesInHalfCircle
        Return New PointF(_objCircleCenter.X + _intRadius * CSng(Math.Cos(dblAngle)), _objCircleCenter.Y + _intRadius * CSng(Math.Sin(dblAngle)))
    End Function

    Private Sub GetSpokesAngles()
        m_Angles = GetSpokesAngles(NumberSpoke)
    End Sub

    Private Function GetSpokesAngles(_intNumberSpoke As Integer) As Double()
        Dim Angles As Double() = New Double(_intNumberSpoke - 1) {}
        Dim dblAngle As Double = CDbl(NumberOfDegreesInCircle) / _intNumberSpoke
        For shtCounter As Integer = 0 To _intNumberSpoke - 1
            Angles(shtCounter) = (If(shtCounter = 0, dblAngle, Angles(shtCounter - 1) + dblAngle))
        Next : Return Angles
    End Function

    Private Sub ActiveTimer()
        If m_IsTimerActive Then m_Timer.Start() Else m_Timer.[Stop]() : m_ProgressValue = 0
        GenerateColorsPallet() : Invalidate()
    End Sub

    Public Sub SetCircleAppearance(_numberSpoke As Integer, _spokeThickness As Integer, _innerCircleRadius As Integer, _outerCircleRadius As Integer)
        NumberSpoke = _numberSpoke : SpokeThickness = _spokeThickness : InnerCircleRadius = _innerCircleRadius : OuterCircleRadius = _outerCircleRadius : Invalidate()
    End Sub

#End Region

End Class

#End Region

#Region "Label"

Class NSLabel : Inherits Control

#Region " Variables "

#Region " Properties "

    Private _TextAlign As HorizontalAlignment = HorizontalAlignment.Left
    Property TextAlign() As HorizontalAlignment
        Get
            Return _TextAlign
        End Get
        Set(value As HorizontalAlignment)
            _TextAlign = value
        End Set
    End Property

    Private _ForeColor1 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _Value1 As String = "NET"
    Public Property Value1() As String
        Get
            Return _Value1
        End Get
        Set(value As String)
            _Value1 = value
            Invalidate()
        End Set
    End Property

    Private _Value2 As String = "SEAL"
    Public Property Value2() As String
        Get
            Return _Value2
        End Get
        Set(value As String)
            _Value2 = value
            Invalidate()
        End Set
    End Property

#End Region

    Private PT1, PT2 As PointF
    Private SZ1, SZ2 As SizeF

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True) : SetStyle(ControlStyles.Selectable, False)
        Font = New Font("Segoe UI", 11.25F, FontStyle.Bold)
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        G.Clear(BackColor)
        PT1 = New PointF(0, 0)
        PT2 = New PointF(0, 0)
        SZ1.Width = 0
        If Text <> "" Then
            SZ1 = G.MeasureString(Text, Font, Width, StringFormat.GenericTypographic)
            If TextAlign = HorizontalAlignment.Left Then PT1 = New PointF(0, (Height \ 2) - (SZ1.Height / 2))
            If TextAlign = HorizontalAlignment.Center Then PT1 = New PointF((Width / 2) - (SZ1.Width / 2), (Height \ 2) - (SZ1.Height / 2))
            If TextAlign = HorizontalAlignment.Right Then PT1 = New PointF(Width - (SZ1.Width + 1), (Height \ 2) - (SZ1.Height / 2))
            G.DrawString(Text, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
            G.DrawString(Text, Font, New SolidBrush(ForeColor1), PT1.X, PT1.Y)
            Exit Sub
        End If
        If Enabled = True Then
            If Value1 <> "" Then
                Select Case TextAlign
                    Case HorizontalAlignment.Left
                        SZ1 = G.MeasureString(Value1, Font, Width, StringFormat.GenericTypographic)
                        PT1 = New PointF(0, Height \ 2 - SZ1.Height / 2)
                        G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                        G.DrawString(Value1, Font, Brushes.LightGray, PT1)
                        If Value2 <> "" Then
                            'SZ2 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                            PT2 = New PointF(SZ1.Width + 1, Height \ 2 - SZ1.Height / 2)
                            G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                            G.DrawString(Value2, Font, New SolidBrush(ForeColor1), PT2)
                        End If
                    Case HorizontalAlignment.Right
                        Dim SZTotal As SizeF
                        SZTotal = G.MeasureString(Value1 + Value2, Font, Width, StringFormat.GenericTypographic)
                        PT1 = New PointF(Width - 5 - SZTotal.Width, (Height / 2) - (SZTotal.Height / 2))
                        G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                        G.DrawString(Value1, Font, Brushes.LightGray, PT1)
                        If Value2 <> "" Then
                            SZ1 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                            PT2 = New PointF((Width - 5 - SZ1.Width) + 1, Height / 2 - SZ1.Height / 2)
                            G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                            G.DrawString(Value2, Font, New SolidBrush(ForeColor1), PT2)
                        End If
                    Case HorizontalAlignment.Center
                        SZ1 = G.MeasureString(Value1, Font, Width, StringFormat.GenericTypographic)
                        SZ2 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                        PT1 = New PointF((Width / 2) - ((SZ1.Width + SZ2.Width) / 2), (Height / 2) - (SZ1.Height / 2))
                        G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                        G.DrawString(Value1, Font, Brushes.LightGray, PT1)
                        If Value2 <> "" Then
                            PT2 = New PointF(((Width / 2) - ((SZ1.Width + SZ2.Width) / 2) + SZ1.Width) + 1, Height / 2 - SZ2.Height / 2)
                            G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                            G.DrawString(Value2, Font, New SolidBrush(ForeColor1), PT2)
                        End If
                End Select
            End If
        Else
            Select Case TextAlign
                Case HorizontalAlignment.Left
                    If Value1 <> "" Then
                        SZ1 = G.MeasureString(Value1, Font, Width, StringFormat.GenericTypographic)
                        PT1 = New PointF(0, Height \ 2 - SZ1.Height / 2)
                        G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                        G.DrawString(Value1, Font, Brushes.Gray, PT1)
                    End If
                    If Value2 <> "" Then
                        'SZ2 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                        PT2 = New PointF(SZ1.Width + 1, Height \ 2 - SZ1.Height / 2)
                        G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                        G.DrawString(Value2, Font, New SolidBrush(Color.FromArgb(150, 110, 0)), PT2)
                    End If
                Case HorizontalAlignment.Right
                    Dim SZTotal As SizeF
                    SZTotal = G.MeasureString(Value1 + Value2, Font, Width, StringFormat.GenericTypographic)
                    PT1 = New PointF(Width - 5 - SZTotal.Width, (Height / 2) - (SZTotal.Height / 2))
                    G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                    G.DrawString(Value1, Font, Brushes.Gray, PT1)
                    If Value2 <> "" Then
                        SZ1 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                        PT2 = New PointF((Width - 5 - SZ1.Width) + 1, Height / 2 - SZ1.Height / 2)
                        G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                        G.DrawString(Value2, Font, New SolidBrush(Color.FromArgb(150, 110, 0)), PT2)
                    End If
                Case HorizontalAlignment.Center
                    SZ1 = G.MeasureString(Value1, Font, Width, StringFormat.GenericTypographic)
                    SZ2 = G.MeasureString(Value2, Font, Width, StringFormat.GenericTypographic)
                    PT1 = New PointF((Width / 2) - ((SZ1.Width + SZ2.Width) / 2), (Height / 2) - (SZ1.Height / 2))
                    G.DrawString(Value1, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
                    G.DrawString(Value1, Font, Brushes.Gray, PT1)
                    If Value2 <> "" Then
                        PT2 = New PointF(((Width / 2) - ((SZ1.Width + SZ2.Width) / 2) + SZ1.Width) + 1, Height / 2 - SZ2.Height / 2)
                        G.DrawString(Value2, Font, Brushes.Black, PT2.X + 1, PT2.Y + 1)
                        G.DrawString(Value2, Font, New SolidBrush(Color.FromArgb(150, 110, 0)), PT2)
                    End If
            End Select
        End If
    End Sub

#End Region

End Class

#End Region

#Region " Selectable Label "

Public Class SelectableLabel : Inherits Label

#Region " Variables "

    Public isSelected As Boolean
    Private Ancho As Integer = 5
    Private MouseDownSize As Size
    Private MouseDownLocation, BottomRight As Point
    Private isMoving, isResizing As Boolean
    Private ResizinMode As String
    Private AbrirFuente As FontDialog

#Region " Constructor "

    Public Sub New()
        BackColor = Color.Transparent : AutoSize = False : BorderStyle = BorderStyle.None
        AbrirFuente = New FontDialog With {.ShowColor = True, .ShowEffects = True, .FontMustExist = True, .ShowApply = True, .Font = Font, .Color = ForeColor}
        AddHandler AbrirFuente.Apply, AddressOf CambiarFuente_Apply
    End Sub

#End Region

#End Region

#Region " Metodos "

#Region " Deseleccionar Todo "

    Public Sub DeseleccionarTodo()
        For Each SLabel As SelectableLabel In Parent.Controls
            SLabel.isSelected = False : SLabel.Invalidate()
        Next
    End Sub

#End Region

#Region " Seleccionar "

    Public Sub Seleccionar()
        For Each SLabel As SelectableLabel In Parent.Controls
            If SLabel Is Me Then isSelected = True : Invalidate() Else SLabel.isSelected = False : SLabel.Invalidate()
        Next : BringToFront()
    End Sub

#End Region

#Region " Eliminar "

    Public Sub Eliminar()
        Text = "" : Visible = False
        Parent.Controls.Remove(Me)
    End Sub

#End Region

#Region " Cambiar Fuente "

    Public Sub CambiarFuente()
        AbrirFuente.Font = Font : AbrirFuente.Color = ForeColor
        If AbrirFuente.ShowDialog() <> DialogResult.Cancel Then
            Font = AbrirFuente.Font
            ForeColor = AbrirFuente.Color
        End If
    End Sub

    Private Sub CambiarFuente_Apply(sender As Object, e As EventArgs)
        Font = sender.Font
        ForeColor = sender.Color
    End Sub

#End Region

    Public Function GetTexto(NewSize As Size) As Image
        If Text = "" Or Text = "Dobleclick para editar" Then Return Nothing
        Dim WPercent As Decimal = (NewSize.Width * 100 / Parent.Size.Width) - 100
        Dim HPercent As Decimal = (NewSize.Height * 100 / Parent.Size.Height) - 100

        Dim TImagen As New Bitmap(Math.Max(CInt(Width * (1 + WPercent / 100)), 15), Math.Max(CInt(Height * (1 + HPercent / 100)), 15))

        Dim Target As New Rectangle(5, 5, TImagen.Width - 10, TImagen.Height - 10)
        Dim TextPath As New Drawing2D.GraphicsPath
        TextPath.AddString(Text, Font.FontFamily, Font.Style, Target.Height, New PointF(0, 0), New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center})
        Dim text_rectf As RectangleF = TextPath.GetBounds()
        Dim target_pts() As PointF = {New PointF(Target.Left, Target.Top), New PointF(Target.Right, Target.Top), New PointF(Target.Left, Target.Bottom)}

        Using G As Graphics = Graphics.FromImage(TImagen)
            G.Transform = New Drawing2D.Matrix(text_rectf, target_pts)
            G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            G.FillPath(New SolidBrush(ForeColor), TextPath)
            G.DrawPath(New Pen(New SolidBrush(Color.Black)), TextPath)
            G.ResetTransform()
        End Using : TextPath.Dispose()
        Return TImagen
    End Function

    Public TempLocation As Point

    Public Sub EscalarTexto(OldSize As Size)
        Dim WPercent As Decimal = (Parent.Width * 100 / OldSize.Width) - 100
        Dim HPercent As Decimal = (Parent.Height * 100 / OldSize.Height) - 100

        Width = Math.Max(CInt(Width * (1 + WPercent / 100)), 15)
        Height = Math.Max(CInt(Height * (1 + HPercent / 100)), 15)
        Left = CInt(Left * (1 + WPercent / 100))
        Top = CInt(Top * (1 + HPercent / 100))
        Invalidate()
    End Sub

#End Region

#Region " Eventos "

#Region " Mouse Clicks "

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        isSelected = True : isResizing = False : isMoving = False : BackColor = Color.Transparent
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        For Each SLabel As SelectableLabel In Parent.Controls
            If SLabel Is Me Then isSelected = True : Invalidate() Else SLabel.isSelected = False : SLabel.Invalidate()
        Next : BringToFront()

        If e.Button <> MouseButtons.Left Then Exit Sub
        MouseDownLocation = e.Location : MouseDownSize = Size : isResizing = True
        BottomRight = New Point(Left + Width, Top + Height)
        Dim CursorLocation As Point = PointToClient(Cursor.Position)
        If CursorLocation.X < 5 And CursorLocation.Y < 5 Then
            ResizinMode = "NW"
        ElseIf CursorLocation.X > Width - 5 And CursorLocation.Y < 5 Then
            ResizinMode = "NE"
        ElseIf CursorLocation.X < 5 And CursorLocation.Y > Height - 5 Then
            ResizinMode = "SW"
        ElseIf CursorLocation.X > Width - 5 And CursorLocation.Y > Height - 5 Then
            ResizinMode = "SE"
        ElseIf CursorLocation.X < 5 Then
            ResizinMode = "W"
        ElseIf CursorLocation.X > Width - 5 Then
            ResizinMode = "E"
        ElseIf CursorLocation.Y < 5 Then
            ResizinMode = "N"
        ElseIf CursorLocation.Y > Height - 5 Then
            ResizinMode = "S"
        Else
            ResizinMode = ""
            isResizing = False
        End If
    End Sub

#End Region

#Region " Mouse Move "

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim CursorLocation As Point = PointToClient(Cursor.Position)
        If isResizing = False Then
            If CursorLocation.X < 5 And CursorLocation.Y < 5 Then
                Cursor = Cursors.SizeNWSE
            ElseIf CursorLocation.X > Width - 5 And CursorLocation.Y < 5 Then
                Cursor = Cursors.SizeNESW
            ElseIf CursorLocation.X < 5 And CursorLocation.Y > Height - 5 Then
                Cursor = Cursors.SizeNESW
            ElseIf CursorLocation.X > Width - 5 And CursorLocation.Y > Height - 5 Then
                Cursor = Cursors.SizeNWSE
            ElseIf CursorLocation.X < 5 Then
                Cursor = Cursors.SizeWE
            ElseIf CursorLocation.X > Width - 5 Then
                Cursor = Cursors.SizeWE
            ElseIf CursorLocation.Y < 5 Then
                Cursor = Cursors.SizeNS
            ElseIf CursorLocation.Y > Height - 5 Then
                Cursor = Cursors.SizeNS
            Else
                Cursor = Cursors.SizeAll
            End If
        End If

        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim MouseMoveDelta As New Point(e.X - MouseDownLocation.X, e.Y - MouseDownLocation.Y)
        If isResizing = True Then
            If ResizinMode = "NW" Then
                'Done
                If MouseMoveDelta.X < 0 Or Width > 15 Then Left = Location.X + MouseMoveDelta.X : Width = Math.Max(BottomRight.X - Location.X, 15)
                If MouseMoveDelta.Y < 0 Or Height > 15 Then Top = Location.Y + MouseMoveDelta.Y : Height = Math.Max(BottomRight.Y - Location.Y, 15)
            ElseIf ResizinMode = "NE" Then
                'Done
                If MouseMoveDelta.X > 0 Or Width > 15 Then Width = Math.Max(MouseDownSize.Width + MouseMoveDelta.X, 15)
                If MouseMoveDelta.Y < 0 Or Height > 15 Then Top = Location.Y + MouseMoveDelta.Y : Height = Math.Max(BottomRight.Y - Location.Y, 15)
            ElseIf ResizinMode = "SW" Then
                'Done
                If MouseMoveDelta.X < 0 Or Width > 15 Then Left = Location.X + MouseMoveDelta.X : Width = Math.Max(BottomRight.X - Location.X, 15)
                If MouseMoveDelta.Y > 0 Or Height > 15 Then Height = Math.Max(MouseDownSize.Height + MouseMoveDelta.Y, 15)
            ElseIf ResizinMode = "SE" Then
                If MouseMoveDelta.X > 0 Or Width > 15 Then Width = Math.Max(MouseDownSize.Width + MouseMoveDelta.X, 15)
                If MouseMoveDelta.Y > 0 Or Height > 15 Then Height = Math.Max(MouseDownSize.Height + MouseMoveDelta.Y, 15)
            ElseIf ResizinMode = "W" Then
                'Done
                If MouseMoveDelta.X < 0 Or Width > 15 Then Left = Location.X + MouseMoveDelta.X : Width = Math.Max(BottomRight.X - Location.X, 15)
            ElseIf ResizinMode = "E" Then
                'Done
                If MouseMoveDelta.X > 0 Or Width > 15 Then Width = Math.Max(MouseDownSize.Width + MouseMoveDelta.X, 15)
            ElseIf ResizinMode = "N" Then
                'Done
                If MouseMoveDelta.Y < 0 Or Height > 15 Then Top = Location.Y + MouseMoveDelta.Y : Height = Math.Max(BottomRight.Y - Location.Y, 15)
            ElseIf ResizinMode = "S" Then
                'Done
                If MouseMoveDelta.Y < 0 Or Height > 15 Then Height = Math.Max(MouseDownSize.Height + MouseMoveDelta.Y, 15)
            End If
        Else
            If isMoving = False AndAlso e.Location <> MouseDownLocation Then isMoving = True : BackColor = Color.Gray
            Location = New Point(Location.X + MouseMoveDelta.X, Location.Y + MouseMoveDelta.Y)
        End If
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim Target As New Rectangle(5, 5, Width - 10, Height - 10)
        Dim TextPath As New Drawing2D.GraphicsPath
        If Text = "" Then Text = "Dobleclick para editar"
        TextPath.AddString(Text, Font.FontFamily, Font.Style, Target.Height, New PointF(0, 0), New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center})
        Dim text_rectf As RectangleF = TextPath.GetBounds()
        Dim target_pts() As PointF = {New PointF(Target.Left, Target.Top), New PointF(Target.Right, Target.Top), New PointF(Target.Left, Target.Bottom)}

        If isSelected = True Then e.Graphics.DrawRectangle(New Pen(New SolidBrush(Color.FromArgb(205, 150, 0)), 1) With {.DashStyle = Drawing2D.DashStyle.Dash}, 0, 0, Width - 1, Height - 1)

        e.Graphics.Transform = New Drawing2D.Matrix(text_rectf, target_pts)
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        e.Graphics.FillPath(New SolidBrush(ForeColor), TextPath)
        e.Graphics.DrawPath(New Pen(New SolidBrush(Color.Black)), TextPath)
        e.Graphics.ResetTransform()
        TextPath.Dispose()
    End Sub

#End Region

#End Region

End Class

#End Region

#Region "TextBox"

<ComponentModel.DefaultEvent("TextChanged")> Class NSTextBox : Inherits Control

#Region " Variables "

    Private WithEvents Base As TextBox
    'Dim IsFocused As Boolean = False
    Public isKeyLog As Boolean = False
    Public ReducedFont As Boolean

#Region " Properties "

#Region " Colors "

    Private _BackColor As Color = Color.FromArgb(50, 50, 50)
    Overrides Property BackColor As Color
        Get
            Return _BackColor
        End Get
        Set(value As Color)
            _BackColor = value
            If Base IsNot Nothing Then Base.BackColor = value
            Invalidate()
        End Set
    End Property

    Private _BorderColor As Color = Color.FromArgb(50, 50, 50)
    Property BorderColor As Color
        Get
            Return _BorderColor
        End Get
        Set(value As Color)
            _BorderColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.FromArgb(102, 102, 102)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            If Base IsNot Nothing Then Base.ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.FromArgb(102, 102, 102)
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " Others "

    Private _TextAlign As HorizontalAlignment = HorizontalAlignment.Left
    Property TextAlign() As HorizontalAlignment
        Get
            Return _TextAlign
        End Get
        Set(value As HorizontalAlignment)
            _TextAlign = value
            If Base IsNot Nothing Then Base.TextAlign = value
        End Set
    End Property

    Private _MaxLength As Integer = 32767
    Property MaxLength() As Integer
        Get
            Return _MaxLength
        End Get
        Set(value As Integer)
            _MaxLength = value
            If Base IsNot Nothing Then Base.MaxLength = value
        End Set
    End Property

    Private _ReadOnly As Boolean
    Property [ReadOnly]() As Boolean
        Get
            Return _ReadOnly
        End Get
        Set(value As Boolean)
            _ReadOnly = value
            If Base IsNot Nothing Then Base.ReadOnly = value
        End Set
    End Property

    Private _WordWrap As Boolean
    Property [WordWrap]() As Boolean
        Get
            Return _WordWrap
        End Get
        Set(value As Boolean)
            _WordWrap = value
            If Base IsNot Nothing Then Base.WordWrap = value
        End Set
    End Property

    Private _ScrollBars As ScrollBars
    Property [ScrollBars]() As ScrollBars
        Get
            Return _ScrollBars
        End Get
        Set(value As ScrollBars)
            _ScrollBars = value
            If Base IsNot Nothing Then Base.ScrollBars = value
        End Set
    End Property

    Private _HideSelection As Boolean
    Property [HideSelection]() As Boolean
        Get
            Return _HideSelection
        End Get
        Set(value As Boolean)
            _HideSelection = value
            If Base IsNot Nothing Then Base.HideSelection = value
        End Set
    End Property

    Private _UseSystemPasswordChar As Boolean
    Property UseSystemPasswordChar() As Boolean
        Get
            Return _UseSystemPasswordChar
        End Get
        Set(value As Boolean)
            _UseSystemPasswordChar = value
            If Base IsNot Nothing Then Base.UseSystemPasswordChar = value
        End Set
    End Property

    Private _Multiline As Boolean
    Property Multiline() As Boolean
        Get
            Return _Multiline
        End Get
        Set(value As Boolean)
            _Multiline = value
            If Base IsNot Nothing Then
                Base.Multiline = value
                If value Then Base.Height = Height - 11 Else Height = Base.Height + 11
            End If
        End Set
    End Property

    Property SelectionStart() As Integer
        Get
            Return Base.SelectionStart
        End Get
        Set(value As Integer)
            If Base IsNot Nothing Then Base.SelectionStart = value
        End Set
    End Property

    Property SelectionLength() As Integer
        Get
            Return Base.SelectionLength
        End Get
        Set(value As Integer)
            If Base IsNot Nothing Then Base.SelectionLength = value
        End Set
    End Property

    Property SelectedText() As String
        Get
            Return Base.SelectedText
        End Get
        Set(value As String)
            If Base IsNot Nothing Then Base.SelectedText = value
        End Set
    End Property

    Private _ShortcutsEnabled As Boolean
    Property ShortcutsEnabled() As Boolean
        Get
            Return _ShortcutsEnabled
        End Get
        Set(value As Boolean)
            _ShortcutsEnabled = value
            If Base IsNot Nothing Then Base.ShortcutsEnabled = value
        End Set
    End Property

    Private _Rounded As Integer = 7
    Property Rounded As Integer
        Get
            Return _Rounded
        End Get
        Set(value As Integer)
            If value > 30 Then value = 30
            If value < 0 Then value = 0
            _Rounded = value
            Invalidate()
        End Set
    End Property

    Private _ControlCursor As Cursor = Cursors.Default
    Property ControlCursor() As Cursor
        Get
            Return _ControlCursor
        End Get
        Set(value As Cursor)
            _ControlCursor = value
            Cursor = value
        End Set
    End Property

    Overloads Property Enabled() As Boolean
        Get
            Return Base.Enabled
        End Get
        Set(value As Boolean)
            Base.Enabled = value
        End Set
    End Property

    Private _ContextMenuStrip As ContextMenuStrip
    Overrides Property [ContextMenuStrip]() As ContextMenuStrip
        Get
            Return _ContextMenuStrip
        End Get
        Set(value As ContextMenuStrip)
            _ContextMenuStrip = value
            If Base IsNot Nothing Then Base.ContextMenuStrip = value
        End Set
    End Property

    Overrides Property Text As String
        Get
            Return MyBase.Text
        End Get
        Set(value As String)
            MyBase.Text = value
            If Base IsNot Nothing Then Base.Text = value
        End Set
    End Property

    Overrides Property Font As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)
            MyBase.Font = value
            If Base IsNot Nothing Then
                Base.Font = value
                Base.Location = New Point(5, 5)
                Base.Width = Width - 8
                If Not _Multiline Then Height = Base.Height + 11
            End If
        End Set
    End Property

#End Region

#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True) : SetStyle(ControlStyles.Selectable, True)
        ForeColor = Color.FromArgb(102, 102, 102)
        Base = New TextBox
        Base.Font = Font
        Base.Text = Text
        Base.MaxLength = _MaxLength
        Base.Multiline = _Multiline
        Base.ReadOnly = _ReadOnly
        Base.WordWrap = _WordWrap
        Base.ScrollBars = ScrollBars
        Base.UseSystemPasswordChar = _UseSystemPasswordChar
        Base.ForeColor = Color.White
        Base.BackColor = Color.FromArgb(50, 50, 50)
        Base.BorderStyle = BorderStyle.None

        Base.Location = New Point(5, 5)
        Base.Width = Width - 10
        Base.Height = Height - 11
        'If _Multiline Then Base.Height = Height - 11 Else Height = Base.Height + 11
        AddHandler Base.TextChanged, AddressOf OnBaseTextChanged
        AddHandler Base.KeyDown, AddressOf OnBaseKeyDown
        AddHandler Base.KeyPress, AddressOf OnBaseKeyPress
        AddHandler Base.GotFocus, AddressOf TextGotFocus
        AddHandler Base.LostFocus, AddressOf TextLostFocus
        AddHandler GotFocus, AddressOf FocusBase
        AddHandler LostFocus, AddressOf UnfocusBase
    End Sub

#End Region

#Region " Events "

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        If Not Controls.Contains(Base) Then Controls.Add(Base)
        MyBase.OnHandleCreated(e)
    End Sub

    Private Sub OnBaseTextChanged(s As Object, e As EventArgs)
        Text = Base.Text
    End Sub

    Private Sub OnBaseKeyDown(s As Object, e As KeyEventArgs)
        MyBase.OnKeyDown(e)
    End Sub

    Private Sub OnBaseKeyPress(s As Object, e As KeyPressEventArgs)
        MyBase.OnKeyPress(e)
    End Sub

    Sub UnfocusBase(sender As Object, e As EventArgs)
        'IsFocused = False
        Invalidate()
    End Sub

    Sub FocusBase(sender As Object, e As EventArgs)
        Base.Focus()
    End Sub

    'Private alreadyFocused As Boolean

    Private Sub Base_MouseDown(sender As Object, e As MouseEventArgs) Handles Base.MouseDown
        If e.Button <> MouseButtons.Left Then Exit Sub
    End Sub

    Private Sub Base_MouseUp(sender As Object, e As MouseEventArgs) Handles Base.MouseUp
        If e.Button <> MouseButtons.Left Then Exit Sub
        'If SelectionLength = 0 Then If alreadyFocused = False Then SelectionStart = 0 : SelectionLength = Text.Length Else SelectionLength = 0
        'alreadyFocused = True
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e)
        'If MouseButtons = MouseButtons.None Then alreadyFocused = True : SelectionStart = 0 : SelectionLength = Text.Length
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        'If SelectionLength = 0 Then If alreadyFocused = False Then SelectionStart = 0 : SelectionLength = Text.Length Else SelectionLength = 0
        'alreadyFocused = True
    End Sub

    Sub TextGotFocus(sender As Object, e As EventArgs)
        'IsFocused = True
        Base.ForeColor = ForeColor1
        Invalidate()
        MyBase.OnGotFocus(e)
    End Sub

    Sub TextLostFocus(sender As Object, e As EventArgs)
        'alreadyFocused = False
        'IsFocused = False
        Base.ForeColor = ForeColor2
        SelectionLength = 0
        MyBase.OnLostFocus(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Base.Location = New Point(5, 5)
        Base.Width = Width - 10
        Base.Height = Height - 11
        MyBase.OnResize(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Base.Focus()
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnEnter(e As EventArgs)
        Base.Focus()
        Invalidate()
        MyBase.OnEnter(e)
    End Sub

    Protected Overrides Sub OnLeave(e As EventArgs)
        Invalidate()
        MyBase.OnLeave(e)
        'alreadyFocused = False
    End Sub

    Public Sub ScrollToCaret()
        Base.SelectionStart = SelectionStart
        Base.ScrollToCaret()
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Delete Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
        'If (keyData = Keys.Delete) Then
        '    Return True
        'Else
        Return MyBase.ProcessCmdKey(msg, keyData)
        'End If
    End Function

    Public Function Lines() As Array
        Return Base.Lines
    End Function

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        _BackColor = Color.FromArgb(50, 50, 50)
        If isKeyLog = False Then _BorderColor = Color.FromArgb(50, 50, 50) Else _BorderColor = Color.FromArgb(205, 150, 0)

        'If Name = "TBuscar" Then Base.Location = New Point(2, 2) Else Base.Location = New Point(5, 5)
        Base.Location = New Point(5, 5)
        Base.Width = Width - 10 : Base.Height = Height - 11
        Dim G As Graphics = e.Graphics : G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias : G.Clear(BackColor)
        Dim GP1 As New Drawing2D.GraphicsPath
        Dim GP2 As New Drawing2D.GraphicsPath

        If isKeyLog Then
            GP1.AddRectangle(New Rectangle(2, 2, Width - 4, Height - 5))
        Else
            GP1.AddRectangle(New Rectangle(0, 0, Width - 1, Height - 1))
        End If
        If Name = "TBuscar" Or Name = "TBuscar2" Then GP2.AddRectangle(New Rectangle(0, 1, Width - 1, Height - 2)) Else GP2.AddRectangle(New Rectangle(1, 1, Width - 3, Height - 3))
        Dim PB1 As New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(1.0F, 0.6F)
        G.FillPath(PB1, GP1)
        G.DrawPath(New Pen(BorderColor), GP1)
        If isKeyLog = True Then
        Else : G.DrawPath(New Pen(Color.FromArgb(35, 35, 35)), GP2)
        End If
    End Sub

#End Region

End Class

#End Region

#Region "CheckBox"

<ComponentModel.DefaultEvent("CheckedChanged")> Class NSCheckBox : Inherits Control

    Event CheckedChanged(sender As Object)

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        P11 = New Pen(Color.FromArgb(50, 50, 50))
        P22 = New Pen(Color.FromArgb(35, 35, 35))
        P3 = New Pen(Color.Black, 2.0F)
        P4 = New Pen(Color.FromArgb(205, 150, 0), 2.0F)
    End Sub

    Private _Checked As Boolean
    Public Property Checked() As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            _Checked = value
            RaiseEvent CheckedChanged(Me)

            Invalidate()
        End Set
    End Property

    '    Private GP1, GP2 As Drawing2D.GraphicsPath

    Private SZ1 As SizeF
    Private PT1 As PointF

    Private P11, P22, P3, P4 As Pen

    Private PB1 As Drawing2D.PathGradientBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        G.Clear(BackColor) : G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        'GP1 = CreateRound(0, 2, Height - 5, Height - 5, 5)
        'GP2 = CreateRound(1, 3, Height - 7, Height - 7, 5)
        Dim GP1 As New Drawing2D.GraphicsPath : GP1.AddRectangle(New RectangleF(0, 2, Height - 5, Height - 5))
        Dim GP2 As New Drawing2D.GraphicsPath : GP2.AddRectangle(New RectangleF(1, 3, Height - 7, Height - 7))

        P11 = New Pen(Color.FromArgb(50, 50, 50))

        PB1 = New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(0.3F, 0.3F)

        G.FillPath(PB1, GP1)
        G.DrawPath(P11, GP1)
        G.DrawPath(P22, GP2)

        If _Checked Then
            If Enabled = True Then
                P3 = New Pen(Color.Black, 2.0F)
                P4 = New Pen(Color.FromArgb(205, 150, 0), 2.0F)
            Else
                P3 = New Pen(Color.Black, 2.0F)
                P4 = New Pen(Color.FromArgb(102, 102, 102), 2.0F)
                Text = "Cambiado"
            End If
            G.DrawLine(P3, 5, Height - 9, 8, Height - 7)
            G.DrawLine(P3, 7, Height - 7, Height - 8, 7)

            G.DrawLine(P4, 4, Height - 10, 7, Height - 8)
            G.DrawLine(P4, 6, Height - 8, Height - 9, 6)
        End If

        SZ1 = G.MeasureString(Text, Font)
        PT1 = New PointF(Height - 3, Height \ 2 - SZ1.Height / 2)
        G.DrawString(Text, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
        G.DrawString(Text, Font, New SolidBrush(ForeColor), PT1)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Checked = Not Checked
    End Sub

End Class

#End Region

#Region "KBoardCheckBox"

<ComponentModel.DefaultEvent("CheckedChanged")> Class NSKBoardCheckBox : Inherits Control

    Event CheckedChanged(sender As Object)

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)
    End Sub

    Private _Checked As Boolean
    Public Property Checked() As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            _Checked = value
            RaiseEvent CheckedChanged(Me)

            Invalidate()
        End Set
    End Property

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        G.Clear(Color.FromArgb(59, 59, 59)) : G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        G.FillEllipse(New SolidBrush(Color.FromArgb(50, 50, 50)), 0, 0, Width - 1, Height - 1)
        G.DrawEllipse(New Pen(Color.FromArgb(15, 15, 15)), 0, 0, Width - 1, Height - 1)

        If _Checked Then
            If Enabled = True Then
                G.FillEllipse(New SolidBrush(Color.FromArgb(205, 150, 0)), 1, 1, Width - 3, Height - 3)
            Else
                G.FillEllipse(New SolidBrush(Color.FromArgb(102, 102, 102)), 2, 2, Width - 5, Height - 5)
            End If
        End If
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Checked = Not Checked
    End Sub

End Class

#End Region

#Region "RadioButton"

<ComponentModel.DefaultEvent("CheckedChanged")> Class NSRadioButton
    Inherits Control

    Event CheckedChanged(sender As Object)

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        P1 = New Pen(Color.FromArgb(55, 55, 55))
        P2 = New Pen(Color.FromArgb(35, 35, 35))
    End Sub

    Private _Checked As Boolean
    Public Property Checked() As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            _Checked = value

            If _Checked Then
                InvalidateParent()
            End If

            RaiseEvent CheckedChanged(Me)
            Invalidate()
        End Set
    End Property

    Private Sub InvalidateParent()
        If Parent Is Nothing Then Return

        For Each C As Control In Parent.Controls
            If Not (C Is Me) AndAlso (TypeOf C Is NSRadioButton) Then
                DirectCast(C, NSRadioButton).Checked = False
            End If
        Next
    End Sub

    Private GP1 As Drawing2D.GraphicsPath

    Private SZ1 As SizeF
    Private PT1 As PointF

    Private P1, P2 As Pen

    Private PB1 As Drawing2D.PathGradientBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = New Drawing2D.GraphicsPath
        GP1.AddEllipse(0, 2, Height - 5, Height - 5)

        PB1 = New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(0.3F, 0.3F)

        G.FillPath(PB1, GP1)

        G.DrawEllipse(P1, 0, 2, Height - 5, Height - 5)
        G.DrawEllipse(P2, 1, 3, Height - 7, Height - 7)

        If _Checked Then
            If Enabled = True Then
                G.FillEllipse(Brushes.Black, 5, 7, Height - 15, Height - 15)
                G.FillEllipse(New SolidBrush(Color.FromArgb(205, 150, 0)), 4, 6, Height - 13, Height - 13)
            Else
                G.FillEllipse(Brushes.Black, 5, 7, Height - 15, Height - 15)
                G.FillEllipse(New SolidBrush(Color.FromArgb(102, 102, 102)), 4, 6, Height - 13, Height - 13)
            End If
        End If

        SZ1 = G.MeasureString(Text, Font)
        PT1 = New PointF(Height - 3, Height \ 2 - SZ1.Height / 2)

        G.DrawString(Text, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
        G.DrawString(Text, Font, Brushes.White, PT1)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Checked = True
        MyBase.OnMouseDown(e)
    End Sub

End Class

#End Region

#Region "ComboBox"

Class NSComboBox : Inherits ComboBox

#Region " Variables "

#Region " Declarations "

    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function GetWindowRect(hWnd As IntPtr, <Runtime.InteropServices.Out> ByRef lpRect As RECT) As Boolean
    End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function GetWindowDC(hWnd As IntPtr) As IntPtr
    End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function
    <Runtime.InteropServices.DllImport("user32.dll")> Public Shared Function GetComboBoxInfo(hWnd As IntPtr, ByRef pcbi As COMBOBOXINFO) As Boolean
    End Function
    <Runtime.InteropServices.DllImport("gdi32.dll")> Public Shared Function ExcludeClipRect(hdc As IntPtr, nLeftRect As Integer, nTopRect As Integer, nRightRect As Integer, nBottomRect As Integer) As Integer
    End Function
    <Runtime.InteropServices.DllImport("gdi32.dll")> Public Shared Function CreatePen(enPenStyle As PenStyles, nWidth As Integer, crColor As Integer) As IntPtr
    End Function
    <Runtime.InteropServices.DllImport("gdi32.dll")> Public Shared Function SelectObject(hdc As IntPtr, hObject As IntPtr) As IntPtr
    End Function
    <Runtime.InteropServices.DllImport("gdi32.dll")> Public Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function
    <Runtime.InteropServices.DllImport("gdi32.dll")> Public Shared Sub Rectangle(hdc As IntPtr, X1 As Integer, Y1 As Integer, X2 As Integer, Y2 As Integer)
    End Sub

#End Region

#Region " Structures "

    <Serializable, Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)> Public Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)> Public Structure COMBOBOXINFO
        Public cbSize As Int32
        Public rcItem As RECT
        Public rcButton As RECT
        Public buttonState As ComboBoxButtonState
        Public hwndCombo As IntPtr
        Public hwndEdit As IntPtr
        Public hwndList As IntPtr
    End Structure

    Public Enum PenStyles
        PS_SOLID = 0
        PS_DASH = 1
        PS_DOT = 2
        PS_DASHDOT = 3
        PS_DASHDOTDOT = 4
    End Enum

    Public Enum ComboBoxButtonState
        STATE_SYSTEM_NONE = 0
        STATE_SYSTEM_INVISIBLE = &H8000
        STATE_SYSTEM_PRESSED = &H8
    End Enum

#End Region

#Region " Mouse States "

    Public Enum STImageMode
        Normal = 0
        Fill = 1
    End Enum

    Public Enum MouseState As Byte
        None = 0
        Hover = 1
        Down = 2
        Block = 3
    End Enum

    Public State As MouseState = MouseState.None

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        If e.Button = Windows.Forms.MouseButtons.Left Then State = MouseState.Hover : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = Windows.Forms.MouseButtons.Left Then State = MouseState.Down : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        'If e.X > Width - 23 Then State = MouseState.Hover Else State = MouseState.None : Invalidate()
        State = MouseState.Hover : Invalidate()
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        State = NSComboBox.MouseState.None : Invalidate() : MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnDropDown(e As EventArgs)
        _dropDownCheck.Start()
        MyBase.OnDropDown(e)
    End Sub

    Protected Overrides Sub OnDropDownClosed(e As EventArgs)
        MyBase.OnDropDownClosed(e)
        _dropDownCheck.Stop()
        State = MouseState.None : Invalidate()
    End Sub

    Public Event ItemHover(index As Integer)
    Private _dropDownCheck As New Timer

#End Region

#Region " Properties "

#Region " Colors "

#Region " BaseColor "

    Private _BackColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property BackColor As Color
        Get
            Return _BackColor
        End Get
        Set(value As Color)
            _BackColor = value
            Invalidate()
        End Set
    End Property

    Private _BaseColor1 As Color = Color.FromArgb(35, 35, 35)
    Property BaseColor1 As Color
        Get
            Return _BaseColor1
        End Get
        Set(value As Color)
            _BaseColor1 = value
            Invalidate()
        End Set
    End Property

    Private _BaseColor2 As Color = Color.FromArgb(50, 50, 50)
    Property BaseColor2 As Color
        Get
            Return _BaseColor2
        End Get
        Set(value As Color)
            _BaseColor2 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " ForeColor "

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.White
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _HoverColor1 As Color = Color.White
    Property HoverColor1 As Color
        Get
            Return _HoverColor1
        End Get
        Set(value As Color)
            _HoverColor1 = value
            Invalidate()
        End Set
    End Property

    Private _HoverColor2 As Color = Color.Black
    Property HoverColor2 As Color
        Get
            Return _HoverColor2
        End Get
        Set(value As Color)
            _HoverColor2 = value
            Invalidate()
        End Set
    End Property
#End Region

#Region " ItemColor "

    Private _ItemColor As Color = Color.FromArgb(102, 102, 102)
    Property ItemColor As Color
        Get
            Return _ItemColor
        End Get
        Set(value As Color)
            _ItemColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor As Color = Color.FromArgb(55, 55, 55)
    Property ItemBackColor As Color
        Get
            Return _ItemBackColor
        End Get
        Set(value As Color)
            _ItemBackColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemFont As Font = Font
    Property ItemFont As Font
        Get
            Return _ItemFont
        End Get
        Set(value As Font)
            _ItemFont = value
            Invalidate()
        End Set
    End Property

    Private _ItemHoverColor As Color = Color.FromArgb(102, 102, 102)
    Property ItemHoverColor As Color
        Get
            Return _ItemHoverColor
        End Get
        Set(value As Color)
            _ItemHoverColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemHoverBackColor As Color = Color.FromArgb(65, 65, 65)
    Property ItemHoverBackColor As Color
        Get
            Return _ItemHoverBackColor
        End Get
        Set(value As Color)
            _ItemHoverBackColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemHoverFont As Font = Font
    Property ItemHoverFont As Font
        Get
            Return _ItemHoverFont
        End Get
        Set(value As Font)
            _ItemHoverFont = value
            Invalidate()
        End Set
    End Property

    Private _ItemSelectedColor As Color = Color.FromArgb(102, 102, 102)
    Property ItemSelectedColor As Color
        Get
            Return _ItemSelectedColor
        End Get
        Set(value As Color)
            _ItemSelectedColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemSelectedBackColor As Color = Color.Blue
    Property ItemSelectedBackColor As Color
        Get
            Return _ItemSelectedBackColor
        End Get
        Set(value As Color)
            _ItemSelectedBackColor = value
            Invalidate()
        End Set
    End Property

    Private _ItemSelectedFont As Font = Font
    Property ItemSelectedFont As Font
        Get
            Return _ItemSelectedFont
        End Get
        Set(value As Font)
            _ItemSelectedFont = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " BorderColor "

    Private _BorderColor1 As Color = Color.FromArgb(35, 35, 35)
    Property BorderColor1 As Color
        Get
            Return _BorderColor1
        End Get
        Set(value As Color)
            _BorderColor1 = value
            Invalidate()
        End Set
    End Property

    Private _BorderColor2 As Color = Color.FromArgb(65, 65, 65)
    Property BorderColor2 As Color
        Get
            Return _BorderColor2
        End Get
        Set(value As Color)
            _BorderColor2 = value
            Invalidate()
        End Set
    End Property

    Private _BorderColor3 As Color = Color.Empty
    Property BorderColor3 As Color
        Get
            Return _BorderColor3
        End Get
        Set(value As Color)
            _BorderColor3 = value
            Invalidate()
        End Set
    End Property

    private  _DropdownBorderColor As Color = Color.Gray
    Property DropdownBorderColor As Color
        Get
            Return _DropdownBorderColor
        End Get
        Set(value As Color)
            _DropdownBorderColor = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " Gradient "

    Private _GradientColor1 As Color = Color.FromArgb(60, 60, 60)
    Property GradientColor1 As Color
        Get
            Return _GradientColor1
        End Get
        Set(value As Color)
            _GradientColor1 = value
            Invalidate()
        End Set
    End Property

    Private _GradientColor2 As Color = Color.FromArgb(55, 55, 55)
    Property GradientColor2 As Color
        Get
            Return _GradientColor2
        End Get
        Set(value As Color)
            _GradientColor2 = value
            Invalidate()
        End Set
    End Property

    Private _GradientAngle As Integer = 90
    Property GradientAngle As Integer
        Get
            Return _GradientAngle
        End Get
        Set(value As Integer)
            If value > 360 Then value = 360
            If value < 1 Then value = 1
            _GradientAngle = value
            Invalidate()
        End Set
    End Property

    Private _GradientTransparency As Integer = 0
    Property GradientTransparency As Integer
        Get
            Return _GradientTransparency
        End Get
        Set(value As Integer)
            If value > 255 Then value = 255
            If value < 0 Then value = 0
            _GradientTransparency = value
            Invalidate()
        End Set
    End Property

#End Region

#End Region

#Region " Images "

    Private _BackgroundImageLayout As ImageLayout
    <ComponentModel.Browsable(False)> Overrides Property BackgroundImageLayout As ImageLayout
        Get
            Return _BackgroundImageLayout
        End Get
        Set(value As ImageLayout)
            _BackgroundImageLayout = value
        End Set
    End Property

    Private _BackgroundImage As Image
    <ComponentModel.Browsable(False)> Overrides Property BackgroundImage As Image
        Get
            Return _BackgroundImage
        End Get
        Set(value As Image)
            _BackgroundImage = value
        End Set
    End Property

    Private _Image As Image = Nothing
    Property Image As Image
        Get
            Return _Image
        End Get
        Set(value As Image)
            _Image = value
            Invalidate()
        End Set
    End Property

    Private _ImageSize As New Size(19, 17)
    Property ImageSize As Size
        Get
            Return _ImageSize
        End Get
        Set(value As Size)
            _ImageSize = value
            Invalidate()
        End Set
    End Property

    Private _ImageAlign As HorizontalAlignment = HorizontalAlignment.Center
    Property ImageAlign() As HorizontalAlignment
        Get
            Return _ImageAlign
        End Get
        Set(value As HorizontalAlignment)
            _ImageAlign = value
            Invalidate()
        End Set
    End Property

    Private _ImageMode As STImageMode = STImageMode.Normal
    Property ImageMode() As STImageMode
        Get
            Return _ImageMode
        End Get
        Set(value As STImageMode)
            _ImageMode = value
            Invalidate()
        End Set
    End Property

#End Region

#Region " Others "

    Private _TextAlign As HorizontalAlignment = HorizontalAlignment.Center
    Property TextAlign() As HorizontalAlignment
        Get
            Return _TextAlign
        End Get
        Set(value As HorizontalAlignment)
            _TextAlign = value
            Invalidate()
        End Set
    End Property

    Private _ItemTextAlign As HorizontalAlignment = HorizontalAlignment.Left
    Property ItemTextAlign() As HorizontalAlignment
        Get
            Return _ItemTextAlign
        End Get
        Set(value As HorizontalAlignment)
            _ItemTextAlign = value
            Invalidate()
        End Set
    End Property

    Private _DoubleText As Boolean = False
    Property DoubleText As Boolean
        Get
            Return _DoubleText
        End Get
        Set(value As Boolean)
            _DoubleText = value
            Invalidate()
        End Set
    End Property

    Private _Rounded As Integer = 7
    Property Rounded As Integer
        Get
            Return _Rounded
        End Get
        Set(value As Integer)
            If value > 20 Then value = 20
            If value < 1 Then value = 1
            _Rounded = value
            Invalidate()
        End Set
    End Property

    Private _JustButton As Boolean = False
    Property JustButton As Boolean
        Get
            Return _JustButton
        End Get
        Set(value As Boolean)
            _JustButton = value
            Invalidate()
        End Set
    End Property

#End Region

#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.SupportsTransparentBackColor, True) : SetStyle(DirectCast(139286, ControlStyles), True) : SetStyle(ControlStyles.Selectable, False)
        DoubleBuffered = True : DrawMode = DrawMode.OwnerDrawFixed : DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler _dropDownCheck.Tick, AddressOf dropDownCheck_Tick
        Font = New Font("Segoe UI", 7)
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim G As Graphics = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit : G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias : G.Clear(Parent.BackColor)

        'Dim GP1 As Drawing2D.GraphicsPath = CreateRound(0, 0, Width - 1, Height - 1, Rounded)
        'Dim GP2 As Drawing2D.GraphicsPath = CreateRound(1, 1, Width - 3, Height - 3, Rounded)
        Dim GP1 As New Drawing2D.GraphicsPath : GP1.AddRectangle(New RectangleF(0, 0, Width - 1, Height - 1))
        Dim GP2 As New Drawing2D.GraphicsPath : GP2.AddRectangle(New RectangleF(1, 1, Width - 3, Height - 3))
        Dim GB1 As New Drawing2D.LinearGradientBrush(ClientRectangle, GradientColor1, GradientColor2, GradientAngle)
        G.SetClip(GP1) : G.FillRectangle(GB1, ClientRectangle) : G.ResetClip()

        Select Case State
            Case MouseState.None

                G.DrawPath(New Pen(BorderColor1), GP1)
                G.DrawPath(New Pen(BorderColor2), GP2)

                G.DrawLine(New Pen(BorderColor1), Width - 22, 0, Width - 22, Height)
                G.DrawLine(New Pen(BorderColor2), Width - 23, 1, Width - 23, Height - 2)
                G.DrawLine(New Pen(BorderColor2), Width - 21, 1, Width - 21, Height - 2)

                If DoubleText = True Then
                    G.DrawLine(New Pen(ForeColor2, 2.0F), Width - 15, 10, Width - 11, 13)
                    G.DrawLine(New Pen(ForeColor2, 2.0F), Width - 7, 10, Width - 11, 13)
                    G.DrawLine(New Pen(ForeColor2), Width - 11, 13, Width - 11, 14)
                    If JustButton = False Then DrawText(G, Text, Font, ForeColor2, 1, 1, , , -23)
                End If

                G.DrawLine(New Pen(ForeColor1, 2.0F), Width - 16, 9, Width - 12, 12)
                G.DrawLine(New Pen(ForeColor1, 2.0F), Width - 8, 9, Width - 12, 12)
                G.DrawLine(New Pen(ForeColor1), Width - 12, 12, Width - 12, 13)

                If JustButton = False Then DrawText(G, Text, Font, ForeColor1, , , , , -23)
            Case MouseState.Hover
                G.DrawPath(New Pen(BorderColor3), GP1)
                G.DrawPath(New Pen(BorderColor2), GP2)

                G.DrawLine(New Pen(BorderColor3), Width - 22, 0, Width - 22, Height)
                G.DrawLine(New Pen(BorderColor2), Width - 23, 1, Width - 23, Height - 2)
                G.DrawLine(New Pen(BorderColor2), Width - 21, 1, Width - 21, Height - 2)

                If DoubleText = True Then
                    G.DrawLine(New Pen(HoverColor2, 2.0F), Width - 15, 10, Width - 11, 13)
                    G.DrawLine(New Pen(HoverColor2, 2.0F), Width - 7, 10, Width - 11, 13)
                    G.DrawLine(New Pen(HoverColor2), Width - 11, 13, Width - 11, 14)
                    If JustButton = False Then DrawText(G, Text, Font, ForeColor2, 1, 1, , , -23)
                End If

                G.DrawLine(New Pen(HoverColor1, 2.0F), Width - 16, 9, Width - 12, 12)
                G.DrawLine(New Pen(HoverColor1, 2.0F), Width - 8, 9, Width - 12, 12)
                G.DrawLine(New Pen(HoverColor1), Width - 12, 12, Width - 12, 13)
                If JustButton = False Then DrawText(G, Text, Font, ForeColor1, , , , , -23)
            Case MouseState.Down
                G.DrawPath(New Pen(BorderColor3), GP1)
                G.DrawPath(New Pen(BorderColor2), GP2)

                G.DrawLine(New Pen(BorderColor3), Width - 22, 0, Width - 22, Height)
                G.DrawLine(New Pen(BorderColor2), Width - 23, 1, Width - 23, Height - 2)
                G.DrawLine(New Pen(BorderColor2), Width - 21, 1, Width - 21, Height - 2)

                If DoubleText = True Then
                    G.DrawLine(New Pen(HoverColor2, 2.0F), Width - 15, 10, Width - 11, 13)
                    G.DrawLine(New Pen(HoverColor2, 2.0F), Width - 7, 10, Width - 11, 13)
                    G.DrawLine(New Pen(HoverColor2), Width - 11, 13, Width - 11, 14)
                    If JustButton = False Then DrawText(G, Text, Font, ForeColor2, 1, 1, , , -23)
                End If

                G.DrawLine(New Pen(HoverColor1, 2.0F), Width - 16, 9, Width - 12, 12)
                G.DrawLine(New Pen(HoverColor1, 2.0F), Width - 8, 9, Width - 12, 12)
                G.DrawLine(New Pen(HoverColor1), Width - 12, 12, Width - 12, 13)
                If JustButton = False Then DrawText(G, Text, Font, ForeColor1, , , , , -23)
        End Select

    End Sub

    Private Function SetFontSize(ByRef Control As Object, Fuente As Font, MaxFontSize As Single, Optional MinFontSize As Single = 6) As Font
        Dim TextSize As Integer = MaxFontSize
        Do Until TextRenderer.MeasureText(Control, New Font(Fuente.Name, TextSize, FontStyle.Regular)).Width < Width Or TextSize <= MinFontSize : TextSize -= 1 : Loop
        If TextSize < MinFontSize Then TextSize = MinFontSize
        Return New Font(Fuente.Name, TextSize, FontStyle.Regular)
    End Function

    Private Function SetTextItem(TempText As String, Fuente As Font) As String
        Dim SZ2 = TextRenderer.MeasureText(TempText, ItemSelectedFont)
        If SZ2.Width > Width + 41 Then
Repite:     SZ2 = TextRenderer.MeasureText(TempText + "...", Fuente)
            If SZ2.Width > Width + 41 Then
                TempText = TempText.Substring(0, TempText.Length - 1)
                GoTo Repite
            End If
            Return TempText + "..."
        Else
            Return TempText
        End If
    End Function

#Region " DrawItem "

    Protected Overrides Sub OnDrawItem(e As DrawItemEventArgs)
        e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Dim NormalItemHeight As Integer = MeasureString("@", Font).Height
        If e.Index <> -1 Then
            Dim TItem As String = Items(e.Index)
            If TItem.Substring(0, 1) = "@" Then TItem = TItem.Substring(1)
            If (e.State And DrawItemState.Selected) = DrawItemState.Selected Then
                If TItem.Substring(0, 1) = "|" Then
                    'If Name <> "Lista_CComandos" Then ItemSelectedFont = SetFontSize(TItem, ItemSelectedFont, 8)
                    ItemSelectedFont = SetFontSize(TItem, ItemSelectedFont, 8)
                    TItem = SetTextItem(TItem, ItemSelectedFont)

                    Dim Header As New Drawing2D.LinearGradientBrush(New Rectangle(e.Bounds.X, e.Bounds.Y - 1, e.Bounds.Width, e.Bounds.Height), Color.FromArgb(25, 25, 25), Color.FromArgb(42, 42, 42), 270)
                    e.Graphics.FillRectangle(Header, New Rectangle(e.Bounds.X, e.Bounds.Y - 1, e.Bounds.Width, e.Bounds.Height))
                    Dim HeaderHatch As New Drawing2D.HatchBrush(Drawing2D.HatchStyle.Trellis, Color.FromArgb(35, Color.Black), Color.Transparent)
                    e.Graphics.FillRectangle(HeaderHatch, New Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height - 1))
                    e.Graphics.DrawRectangle(New Pen(Color.Black), New Rectangle(e.Bounds.X, e.Bounds.Y - 1, e.Bounds.Width, e.Bounds.Height))
                    Select Case ItemTextAlign
                        Case HorizontalAlignment.Left
                            e.Graphics.DrawString(TItem.Substring(1), ItemSelectedFont, New SolidBrush(ItemSelectedColor), e.Bounds.Width, e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemSelectedFont).Height / 2)))
                        Case HorizontalAlignment.Center
                            e.Graphics.DrawString(TItem.Substring(1), ItemSelectedFont, New SolidBrush(ItemSelectedColor), e.Bounds.Width / 2 - (MeasureString(TItem.Substring(1), ItemSelectedFont).Width / 2), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemSelectedFont).Height / 2)))
                        Case HorizontalAlignment.Right
                            e.Graphics.DrawString(TItem.Substring(1), ItemSelectedFont, New SolidBrush(ItemSelectedColor), e.Bounds.Width - (MeasureString(TItem.Substring(1), ItemSelectedFont).Width), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemSelectedFont).Height / 2)))
                    End Select
                Else
                    'If Name <> "Lista_CComandos" Then ItemHoverFont = SetFontSize(TItem, ItemHoverFont, 8,)
                    ItemHoverFont = SetFontSize(TItem, ItemHoverFont, 8)
                    TItem = SetTextItem(TItem, ItemSelectedFont)

                    e.Graphics.FillRectangle(New SolidBrush(ItemHoverBackColor), e.Bounds.X + 1, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height - 1)
                    Select Case ItemTextAlign
                        Case HorizontalAlignment.Left
                            e.Graphics.DrawString(TItem, ItemHoverFont, New SolidBrush(ItemHoverColor), 5, e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                        Case HorizontalAlignment.Center
                            e.Graphics.DrawString(TItem, ItemHoverFont, New SolidBrush(ItemHoverColor), e.Bounds.Width / 2 - (MeasureString(TItem, ItemHoverFont).Width / 2), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                        Case HorizontalAlignment.Right
                            e.Graphics.DrawString(TItem, ItemHoverFont, New SolidBrush(ItemHoverColor), e.Bounds.Width - (MeasureString(TItem, ItemHoverFont).Width), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                    End Select
                End If
            Else
                If TItem.Substring(0, 1) = "|" Then
                    'If Name <> "Lista_CComandos" Then ItemHoverFont = SetFontSize(TItem, ItemHoverFont, 8,)
                    ItemHoverFont = SetFontSize(TItem, ItemHoverFont, 8)
                    TItem = SetTextItem(TItem, ItemSelectedFont)

                    Dim Header As New Drawing2D.LinearGradientBrush(New Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height + 1), Color.FromArgb(25, 25, 25), Color.FromArgb(42, 42, 42), 270)
                    e.Graphics.FillRectangle(Header, New Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height + 1))
                    Dim HeaderHatch As New Drawing2D.HatchBrush(Drawing2D.HatchStyle.Trellis, Color.FromArgb(35, Color.Black), Color.Transparent)
                    e.Graphics.FillRectangle(HeaderHatch, New Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height - 1))
                    e.Graphics.DrawRectangle(New Pen(Color.Black), New Rectangle(e.Bounds.X, e.Bounds.Y - 1, e.Bounds.Width, e.Bounds.Height))
                    Select Case ItemTextAlign
                        Case HorizontalAlignment.Left
                            e.Graphics.DrawString(TItem.Substring(1), ItemHoverFont, New SolidBrush(ItemHoverColor), e.Bounds.Width, e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                        Case HorizontalAlignment.Center
                            e.Graphics.DrawString(TItem.Substring(1), ItemHoverFont, New SolidBrush(ItemHoverColor), e.Bounds.Width / 2 - (MeasureString(TItem.Substring(1), ItemHoverFont).Width / 2), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                        Case HorizontalAlignment.Right
                            e.Graphics.DrawString(TItem.Substring(1), ItemHoverFont, New SolidBrush(ItemHoverColor), e.Bounds.Width - (MeasureString(TItem.Substring(1), ItemHoverFont).Width), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemHoverFont).Height / 2)))
                    End Select
                Else
                    'If Name <> "Lista_CComandos" Then ItemFont = SetFontSize(TItem, ItemFont, 8,)
                    ItemFont = SetFontSize(TItem, ItemFont, 8)
                    TItem = SetTextItem(TItem, ItemSelectedFont)

                    e.Graphics.FillRectangle(New SolidBrush(ItemBackColor), e.Bounds.X + 1, e.Bounds.Y, e.Bounds.Width - 2, e.Bounds.Height - 1)
                    Select Case ItemTextAlign
                        Case HorizontalAlignment.Left
                            e.Graphics.DrawString(TItem, ItemFont, New SolidBrush(ItemColor), 5, e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemFont).Height / 2)))
                        Case HorizontalAlignment.Center
                            e.Graphics.DrawString(TItem, ItemFont, New SolidBrush(ItemColor), e.Bounds.Width / 2 - (MeasureString(TItem, ItemFont).Width / 2), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemFont).Height / 2)))
                        Case HorizontalAlignment.Right
                            e.Graphics.DrawString(TItem, ItemFont, New SolidBrush(ItemColor), e.Bounds.Width - (MeasureString(TItem, ItemFont).Width), e.Bounds.Y + ((NormalItemHeight / 2) - (MeasureString(TItem, ItemFont).Height / 2)))
                    End Select
                End If
            End If
            RaiseEvent ItemHover(e.Index)
        End If
    End Sub

#End Region

#Region " DrawText "

    Private Sub DrawText(ByRef G As Graphics, DText As String, DFont As Font, DColor As Color, Optional XOffset As Integer = 0, Optional YOffset As Integer = 0, Optional DrawImage As Boolean = True, Optional CalcImage As Boolean = False, Optional WOffset As Integer = 0, Optional HOffset As Integer = 0)
        Dim TextLocation As PointF
        Dim TextSize As New Size(G.MeasureString(DText, DFont).Width, G.MeasureString(DText, DFont).Height)
        If TextAlign = HorizontalAlignment.Left Then TextLocation = New PointF(5 + XOffset, ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)
        If TextAlign = HorizontalAlignment.Center Then TextLocation = New PointF(((((Width + WOffset) \ 2) - G.MeasureString(DText, DFont).Width / 2) + XOffset), ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)
        If TextAlign = HorizontalAlignment.Right Then TextLocation = New PointF((Width + WOffset - (G.MeasureString(DText, DFont).Width + 5)) + XOffset, ((Height + HOffset) \ 2 - G.MeasureString(DText, DFont).Height / 2) + YOffset)

        If ImageSize.Width > Width - 23 Then ImageSize = New Size(Width - 23, ImageSize.Height)
        If ImageSize.Height > Height - 4 Then ImageSize = New Size(ImageSize.Width, Height - 4)
        Dim ImageLocation As PointF
        If ImageAlign = HorizontalAlignment.Left Then ImageLocation = New PointF(3, ((Height - 1) / 2) - (ImageSize.Height / 2))
        If ImageAlign = HorizontalAlignment.Center Then ImageLocation = New PointF((((Width - WOffset) / 2) - (ImageSize.Width / 2) + WOffset), ((Height - 1) / 2) - (ImageSize.Height / 2))
        If ImageAlign = HorizontalAlignment.Right Then ImageLocation = New PointF(Width + WOffset - ImageSize.Width, ((Height - 1) / 2) - (ImageSize.Height / 2))

        If Image Is Nothing Or DrawImage = False And CalcImage = False Then
            G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
        ElseIf ImageMode = STImageMode.Fill Then
            G.DrawImage(Image, 1, 1 + HOffset, Width + WOffset, Height - 2 + HOffset)
            G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
        Else
            G.DrawImage(Image, ImageLocation.X, ImageLocation.Y, ImageSize.Width, ImageSize.Height)
            Select Case TextAlign
                Case HorizontalAlignment.Left
                    If ImageAlign = HorizontalAlignment.Left Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X + ImageSize.Width + 4 + XOffset, TextLocation.Y) Else G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case HorizontalAlignment.Center
                    G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
                Case HorizontalAlignment.Right
                    If ImageAlign = HorizontalAlignment.Right Then G.DrawString(DText, DFont, New SolidBrush(DColor), ImageLocation.X - TextSize.Width - 4 + XOffset, TextLocation.Y) Else G.DrawString(DText, DFont, New SolidBrush(DColor), TextLocation)
            End Select
        End If
    End Sub

#End Region

#End Region

#Region " DropDown Border "

    Private Sub DropDownCheck_Tick(sender As Object, e As EventArgs)
        Dim m As Message = GetControlListBoxMessage(Handle)
        WndProc(m)
    End Sub

    'Protected Overrides ReadOnly Property CreateParams() As System.Windows.Forms.CreateParams
    '    Get
    '        Dim cp As CreateParams = MyBase.CreateParams
    '        cp.Style = cp.Style And Not &H200000   ' Turn off WS_VSCROLL style
    '        Return cp
    '    End Get
    'End Property

    Protected Overrides Sub WndProc(ByRef m As Message)
        Select Case m.Msg
            Case &H134
                MyBase.WndProc(m)
                DrawNativeBorder(m.LParam)
            Case Else
                MyBase.WndProc(m)
        End Select
    End Sub

    Public Function GetControlListBoxMessage(handle As IntPtr) As Message
        Dim m As New Message()
        Dim info As New COMBOBOXINFO()
        info.cbSize = Runtime.InteropServices.Marshal.SizeOf(info)
        m.LParam = If(GetComboBoxInfo(handle, info), info.hwndList, IntPtr.Zero)
        m.WParam = IntPtr.Zero
        m.HWnd = handle
        m.Msg = &H134
        m.Result = IntPtr.Zero
        Return m
    End Function

    Public Sub DrawNativeBorder(handle As IntPtr)
        Dim controlRect As RECT
        GetWindowRect(handle, controlRect)
        controlRect.Right -= controlRect.Left
        controlRect.Bottom -= controlRect.Top
        controlRect.Top = Assign(controlRect.Left, 0)
        Dim dc As IntPtr = GetWindowDC(handle)
        Dim clientRect As RECT = controlRect
        clientRect.Left += 1
        clientRect.Top += 1
        clientRect.Right -= 1
        clientRect.Bottom -= 1
        ExcludeClipRect(dc, clientRect.Left, clientRect.Top, clientRect.Right, clientRect.Bottom)
        Dim border As IntPtr = CreatePen(PenStyles.PS_SOLID, 1, RGB(DropdownBorderColor.R, DropdownBorderColor.G, DropdownBorderColor.B))
        Dim borderPen As IntPtr = SelectObject(dc, border)
        Rectangle(dc, controlRect.Left, controlRect.Top, controlRect.Right, controlRect.Bottom)
        SelectObject(dc, borderPen)
        DeleteObject(border)
        ReleaseDC(handle, dc)
        SetFocus(handle)
    End Sub

    Function Assign(Of T)(ByRef target As T, value As T) As T
        target = value
        Return value
    End Function

#End Region

End Class

#End Region

#Region "TabControl"

Class NSTabControl
    Inherits TabControl

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        SizeMode = TabSizeMode.Fixed
        Alignment = TabAlignment.Left
        ItemSize = New Size(28, 115)

        DrawMode = TabDrawMode.OwnerDrawFixed

        P1 = New Pen(Color.FromArgb(55, 55, 55))
        P2 = New Pen(Color.FromArgb(35, 35, 35))
        P3 = New Pen(Color.FromArgb(45, 45, 45), 2)

        B1 = New SolidBrush(Color.FromArgb(50, 50, 50))
        B2 = New SolidBrush(Color.FromArgb(35, 35, 35))
        B3 = New SolidBrush(Color.FromArgb(205, 150, 0))
        B4 = New SolidBrush(Color.FromArgb(65, 65, 65))

        SF1 = New StringFormat()
        SF1.LineAlignment = StringAlignment.Center
    End Sub

    Protected Overrides Sub OnControlAdded(e As ControlEventArgs)
        If TypeOf e.Control Is TabPage Then
            e.Control.BackColor = Color.FromArgb(50, 50, 50)
        End If

        MyBase.OnControlAdded(e)
    End Sub

    Private GP1, GP2, GP3, GP4 As Drawing2D.GraphicsPath

    Private R1, R2 As Rectangle

    Private P1, P2, P3 As Pen
    Private B1, B2, B3, B4 As SolidBrush

    Private PB1 As Drawing2D.PathGradientBrush

    Private TP1 As TabPage
    Private SF1 As StringFormat

    Private Offset As Integer
    Private ItemHeight As Integer

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        G.Clear(Color.FromArgb(50, 50, 50))
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        ItemHeight = ItemSize.Height + 2

        GP1 = CreateRound(0, 0, ItemHeight + 3, Height - 1, 7)
        GP2 = CreateRound(1, 1, ItemHeight + 3, Height - 3, 7)

        PB1 = New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(0.8F, 0.95F)

        G.FillPath(PB1, GP1)

        G.DrawPath(P1, GP1)
        G.DrawPath(P2, GP2)

        For I As Integer = 0 To TabCount - 1
            R1 = GetTabRect(I)
            R1.Y += 2
            R1.Height -= 3
            R1.Width += 1
            R1.X -= 1

            TP1 = TabPages(I)
            Offset = 0

            If SelectedIndex = I Then
                G.FillRectangle(B1, R1)

                For J As Integer = 0 To 1
                    G.FillRectangle(B2, R1.X + 5 + (J * 5), R1.Y + 6, 2, R1.Height - 9)

                    G.SmoothingMode = Drawing2D.SmoothingMode.None
                    G.FillRectangle(B3, R1.X + 5 + (J * 5), R1.Y + 5, 2, R1.Height - 9)
                    G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

                    Offset += 5
                Next

                G.DrawRectangle(P3, R1.X + 1, R1.Y - 1, R1.Width, R1.Height + 2)
                G.DrawRectangle(P1, R1.X + 1, R1.Y + 1, R1.Width - 2, R1.Height - 2)
                G.DrawRectangle(P2, R1)
            Else
                For J As Integer = 0 To 1
                    G.FillRectangle(B2, R1.X + 5 + (J * 5), R1.Y + 6, 2, R1.Height - 9)

                    G.SmoothingMode = Drawing2D.SmoothingMode.None
                    G.FillRectangle(B4, R1.X + 5 + (J * 5), R1.Y + 5, 2, R1.Height - 9)
                    G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

                    Offset += 5
                Next
            End If

            R1.X += 5 + Offset

            R2 = R1
            R2.Y += 1
            R2.X += 1

            G.DrawString(TP1.Text, Font, Brushes.Black, R2, SF1)
            G.DrawString(TP1.Text, Font, Brushes.White, R1, SF1)
        Next

        GP3 = CreateRound(ItemHeight, 0, Width - ItemHeight - 1, Height - 1, 7)
        GP4 = CreateRound(ItemHeight + 1, 1, Width - ItemHeight - 3, Height - 3, 7)

        G.DrawPath(P2, GP3)
        G.DrawPath(P1, GP4)
    End Sub

End Class

#End Region

#Region "OnOffBox"

<ComponentModel.DefaultEvent("CheckedChanged")> Class NSOnOffBox : Inherits Control

    Event CheckedChanged(sender As Object)

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        P1 = New Pen(Color.FromArgb(55, 55, 55))
        P2 = New Pen(Color.FromArgb(35, 35, 35))
        P3 = New Pen(Color.FromArgb(65, 65, 65))

        B1 = New SolidBrush(Color.FromArgb(35, 35, 35))
        B2 = New SolidBrush(Color.FromArgb(85, 85, 85))
        B3 = New SolidBrush(Color.FromArgb(65, 65, 65))
        B4 = New SolidBrush(Color.FromArgb(205, 150, 0))
        B5 = New SolidBrush(Color.FromArgb(40, 40, 40))

        SF1 = New StringFormat()
        SF1.LineAlignment = StringAlignment.Center
        SF1.Alignment = StringAlignment.Near

        SF2 = New StringFormat()
        SF2.LineAlignment = StringAlignment.Center
        SF2.Alignment = StringAlignment.Far

        Size = New Size(56, 24)
        MinimumSize = Size
        MaximumSize = Size
    End Sub

    Public IgnoreChecked As Boolean
    Private _Checked As Boolean
    Public Property Checked() As Boolean
        Get
            Return _Checked
        End Get
        Set(value As Boolean)
            _Checked = value
            RaiseEvent CheckedChanged(Me)

            Invalidate()
        End Set
    End Property

    Private GP1, GP2, GP3, GP4 As Drawing2D.GraphicsPath

    Private P1, P2, P3 As Pen
    Private B1, B2, B3, B4, B5 As SolidBrush

    Private PB1 As Drawing2D.PathGradientBrush
    Private GB1 As Drawing2D.LinearGradientBrush

    Private R1, R2, R3 As Rectangle
    Private SF1, SF2 As StringFormat

    Private Offset As Integer

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = CreateRound(0, 0, Width - 1, Height - 1, 7)
        GP2 = CreateRound(1, 1, Width - 3, Height - 3, 7)

        PB1 = New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(0.3F, 0.3F)

        G.FillPath(PB1, GP1)
        G.DrawPath(P1, GP1)
        G.DrawPath(P2, GP2)

        R1 = New Rectangle(5, 0, Width - 10, Height + 2)
        R2 = New Rectangle(6, 1, Width - 10, Height + 2)

        R3 = New Rectangle(1, 1, (Width \ 2) - 1, Height - 3)
        If Enabled Then
            If _Checked Then
                G.DrawString("On", Font, Brushes.Black, R2, SF1)
                G.DrawString("On", Font, New SolidBrush(Color.FromArgb(205, 150, 0)), R1, SF1)
                R3.X += (Width \ 2) - 1
            Else
                G.DrawString("Off", Font, B1, R2, SF2)
                G.DrawString("Off", Font, B2, R1, SF2)
            End If
        Else
            If _Checked Then
                G.DrawString("On", Font, B1, R2, SF1)
                G.DrawString("On", Font, New SolidBrush(Color.FromArgb(19, 23, 24)), R1, SF1)
                R3.X += (Width \ 2) - 1
            Else
                G.DrawString("Off", Font, B1, R2, SF2)
                G.DrawString("Off", Font, New SolidBrush(Color.FromArgb(19, 23, 24)), R1, SF2)
            End If
        End If
        GP3 = CreateRound(R3, 7)
        GP4 = CreateRound(R3.X + 1, R3.Y + 1, R3.Width - 2, R3.Height - 2, 7)

        GB1 = New Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)

        G.FillPath(GB1, GP3)
        G.DrawPath(P2, GP3)
        G.DrawPath(P3, GP4)

        Offset = R3.X + (R3.Width \ 2) - 3

        For I As Integer = 0 To 1
            If Enabled Then
                If _Checked Then
                    G.FillRectangle(B1, Offset + (I * 5), 7, 2, Height - 14)
                Else
                    G.FillRectangle(B3, Offset + (I * 5), 7, 2, Height - 14)
                End If
                G.SmoothingMode = Drawing2D.SmoothingMode.None
                If _Checked Then
                    G.FillRectangle(B4, Offset + (I * 5), 7, 2, Height - 14)
                Else
                    G.FillRectangle(B5, Offset + (I * 5), 7, 2, Height - 14)
                End If
                G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Else
                G.FillRectangle(B3, Offset + (I * 5), 7, 2, Height - 14)
                G.SmoothingMode = Drawing2D.SmoothingMode.None
                G.FillRectangle(B5, Offset + (I * 5), 7, 2, Height - 14)
                G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            End If
        Next
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = MouseButtons.Left Then If IgnoreChecked = False Then Checked = Not Checked
    End Sub

End Class

#End Region

#Region "ControlButton"

Class NSControlButton : Inherits Control

#Region " Mouse States "

    Enum Button As Byte
        None = 0
        Minimize = 1
        MaximizeRestore = 2
        Close = 3
    End Enum

    Public Enum MouseState As Byte
        None = 0
        Hover = 1
        Down = 2
        Block = 3
    End Enum

    Public State As MouseState = MouseState.None
    
    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        Invalidate()
    End Sub
    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = Windows.Forms.MouseButtons.Left Then State = MouseState.Down : Invalidate()
    End Sub
    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        MyBase.OnMouseEnter(e)
        State = MouseState.Hover : Invalidate()
    End Sub
    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        State = MouseState.None : Invalidate()
    End Sub

#End Region

#Region " Properties "

    Private _ControlButton As Button = Button.Close
    Public Property ControlButton() As Button
        Get
            Return _ControlButton
        End Get
        Set(value As Button)
            _ControlButton = value
            Invalidate()
        End Set
    End Property

#Region " ForeColor "

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.FromArgb(65, 65, 65)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor3 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor3 As Color
        Get
            Return _ForeColor3
        End Get
        Set(value As Color)
            _ForeColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor4 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor4 As Color
        Get
            Return _ForeColor4
        End Get
        Set(value As Color)
            _ForeColor4 = value
            Invalidate()
        End Set
    End Property
#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Anchor = AnchorStyles.Top Or AnchorStyles.Right

        Width = 18
        Height = 20

        MinimumSize = Size
        MaximumSize = Size

        Margin = New Padding(0)
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.Clear(BackColor)

        Select Case _ControlButton
            Case Button.Minimize
                DrawMinimize(3, 10)
            Case Button.MaximizeRestore
                If FindForm().WindowState = FormWindowState.Normal Then
                    DrawMaximize(3, 5)
                Else
                    DrawRestore(3, 4)
                End If
            Case Button.Close
                DrawClose(4, 5)
        End Select
    End Sub

#Region " Minimize "

    Private Sub DrawMinimize(x As Integer, y As Integer)
        Select Case State
            Case MouseState.None
                G.FillRectangle(New SolidBrush(ForeColor1), x, y, 12, 5)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 4)
            Case MouseState.Down
                G.FillRectangle(New SolidBrush(ForeColor4), x, y, 12, 5)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 4)
            Case MouseState.Hover
                G.FillRectangle(New SolidBrush(ForeColor3), x, y, 12, 5)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 4)
        End Select
    End Sub

#End Region

#Region " Maximize "

    Private Sub DrawMaximize(x As Integer, y As Integer)
        Select Case State
            Case MouseState.None
                G.DrawRectangle(New Pen(ForeColor1, 2), x + 2, y + 2, 8, 6)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 3, 5, 3)
            Case MouseState.Down
                G.DrawRectangle(New Pen(ForeColor4, 2), x + 2, y + 2, 8, 6)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 3, 5, 3)
            Case MouseState.Hover
                G.DrawRectangle(New Pen(ForeColor3, 2), x + 2, y + 2, 8, 6)
                G.DrawRectangle(New Pen(ForeColor2), x, y, 11, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 3, 5, 3)
        End Select
    End Sub

#End Region

#Region " Restore "

    Private Sub DrawRestore(x As Integer, y As Integer)
        Select Case State
            Case MouseState.None
                G.FillRectangle(New SolidBrush(ForeColor1), x + 3, y + 1, 8, 4)
                G.FillRectangle(New SolidBrush(ForeColor1), x + 7, y + 5, 4, 4)
                G.DrawRectangle(New Pen(ForeColor2), x + 2, y + 0, 9, 9)

                G.FillRectangle(New SolidBrush(ForeColor1), x + 1, y + 3, 2, 6)
                G.FillRectangle(New SolidBrush(ForeColor1), x + 1, y + 9, 8, 2)
                G.DrawRectangle(New Pen(ForeColor2), x, y + 2, 9, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 5, 3, 3)
            Case MouseState.Down
                G.FillRectangle(New SolidBrush(ForeColor4), x + 3, y + 1, 8, 4)
                G.FillRectangle(New SolidBrush(ForeColor4), x + 7, y + 5, 4, 4)
                G.DrawRectangle(New Pen(ForeColor2), x + 2, y + 0, 9, 9)

                G.FillRectangle(New SolidBrush(ForeColor4), x + 1, y + 3, 2, 6)
                G.FillRectangle(New SolidBrush(ForeColor4), x + 1, y + 9, 8, 2)
                G.DrawRectangle(New Pen(ForeColor2), x, y + 2, 9, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 5, 3, 3)
            Case MouseState.Hover
                G.FillRectangle(New SolidBrush(ForeColor3), x + 3, y + 1, 8, 4)
                G.FillRectangle(New SolidBrush(ForeColor3), x + 7, y + 5, 4, 4)
                G.DrawRectangle(New Pen(ForeColor2), x + 2, y + 0, 9, 9)

                G.FillRectangle(New SolidBrush(ForeColor3), x + 1, y + 3, 2, 6)
                G.FillRectangle(New SolidBrush(ForeColor3), x + 1, y + 9, 8, 2)
                G.DrawRectangle(New Pen(ForeColor2), x, y + 2, 9, 9)
                G.DrawRectangle(New Pen(ForeColor2), x + 3, y + 5, 3, 3)
        End Select
    End Sub

#End Region

#Region " Close "

    Dim ClosePath As Drawing2D.GraphicsPath
    Private Sub DrawClose(x As Integer, y As Integer)
        If ClosePath Is Nothing Then
            ClosePath = New Drawing2D.GraphicsPath
            ClosePath.AddLine(x + 1, y, x + 3, y)
            ClosePath.AddLine(x + 5, y + 2, x + 7, y)
            ClosePath.AddLine(x + 9, y, x + 10, y + 1)
            ClosePath.AddLine(x + 7, y + 4, x + 7, y + 5)
            ClosePath.AddLine(x + 10, y + 8, x + 9, y + 9)
            ClosePath.AddLine(x + 7, y + 9, x + 5, y + 7)
            ClosePath.AddLine(x + 3, y + 9, x + 1, y + 9)
            ClosePath.AddLine(x + 0, y + 8, x + 3, y + 5)
            ClosePath.AddLine(x + 3, y + 4, x + 0, y + 1)
        End If
        Select Case State
            Case MouseState.None
                G.FillPath(New SolidBrush(ForeColor1), ClosePath)
                G.DrawPath(New Pen(ForeColor2), ClosePath)
            Case MouseState.Down
                G.FillPath(New SolidBrush(ForeColor4), ClosePath)
                G.DrawPath(New Pen(ForeColor2), ClosePath)
            Case MouseState.Hover
                G.FillPath(New SolidBrush(ForeColor3), ClosePath)
                G.DrawPath(New Pen(ForeColor2), ClosePath)
        End Select
    End Sub

#End Region

#End Region

#Region " Events "

    Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left Then
            Dim F As Form = FindForm()
            Select Case _ControlButton
                Case Button.Minimize
                    F.WindowState = FormWindowState.Minimized
                Case Button.MaximizeRestore
                    If F.WindowState = FormWindowState.Normal Then
                        F.WindowState = FormWindowState.Maximized
                    Else
                        F.WindowState = FormWindowState.Normal
                    End If
                Case Button.Close
                    F.Close()
            End Select
        End If
        Invalidate()
        MyBase.OnMouseClick(e)
    End Sub

#End Region

End Class

#End Region

#Region "GroupBox"

Class NSGroupBox
    Inherits ContainerControl

    Private _DrawSeperator As Boolean
    Public Property DrawSeperator() As Boolean
        Get
            Return _DrawSeperator
        End Get
        Set(value As Boolean)
            _DrawSeperator = value
            Invalidate()
        End Set
    End Property

    Private _Title As String = "GroupBox"
    Public Property Title() As String
        Get
            Return _Title
        End Get
        Set(value As String)
            _Title = value
            Invalidate()
        End Set
    End Property

    Private _SubTitle As String = "Details"
    Public Property SubTitle() As String
        Get
            Return _SubTitle
        End Get
        Set(value As String)
            _SubTitle = value
            Invalidate()
        End Set
    End Property

    Private _TitleFont As Font
    Private _SubTitleFont As Font

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        _TitleFont = New Font("Verdana", 10.0F)
        _SubTitleFont = New Font("Verdana", 6.5F)

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(55, 55, 55))

        B1 = New SolidBrush(Color.FromArgb(205, 150, 0))
    End Sub

    Private GP1, GP2 As Drawing2D.GraphicsPath

    Private PT1 As PointF
    Private SZ1, SZ2 As SizeF

    Private P1, P2 As Pen
    Private B1 As SolidBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        If Name = "GTituloAyuda" Then
            P1 = New Pen(Color.FromArgb(35, 35, 35))
            P2 = New Pen(Color.FromArgb(205, 150, 0))
        End If
        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = CreateRound(0, 0, Width - 1, Height - 1, 7)
        GP2 = CreateRound(1, 1, Width - 3, Height - 3, 7)

        G.DrawPath(P1, GP1)
        G.DrawPath(P2, GP2)

        SZ1 = G.MeasureString(_Title, _TitleFont, Width, StringFormat.GenericTypographic)
        SZ2 = G.MeasureString(_SubTitle, _SubTitleFont, Width, StringFormat.GenericTypographic)

        G.DrawString(_Title, _TitleFont, Brushes.Black, 6, 6)
        G.DrawString(_Title, _TitleFont, B1, 5, 5)

        PT1 = New PointF(6.0F, SZ1.Height + 4.0F)

        G.DrawString(_SubTitle, _SubTitleFont, Brushes.Black, PT1.X + 1, PT1.Y + 1)
        G.DrawString(_SubTitle, _SubTitleFont, Brushes.White, PT1.X, PT1.Y)

        If _DrawSeperator Then
            Dim Y As Integer = CInt(PT1.Y + SZ2.Height) + 8

            G.DrawLine(P1, 4, Y, Width - 5, Y)
            G.DrawLine(P2, 4, Y + 1, Width - 5, Y + 1)
        End If
    End Sub

End Class

#End Region

#Region " Separator "

Class NSSeperator
    Inherits Control

#Region " Variables "

    Private P1, P2 As Pen
    Public GlowingCrossMode As String = "None"

#Region " Properties "

    Private _LineColor As Color = Color.FromArgb(35, 35, 35)
    Property LineColor As Color
        Get
            Return _LineColor
        End Get
        Set(value As Color)
            _LineColor = value
            Invalidate()
        End Set
    End Property

    Private _GlowColor As Color = Color.DarkGreen
    Property GlowColor As Color
        Get
            Return _GlowColor
        End Get
        Set(value As Color)
            _GlowColor = value
            Invalidate()
        End Set
    End Property

    Private _VerticalLine As Boolean = False
    Property VerticalLine As Boolean
        Get
            Return _VerticalLine
        End Get
        Set(value As Boolean)
            _VerticalLine = value
            Invalidate()
        End Set
    End Property

    Private _Cross As Boolean = False
    Property Cross As Boolean
        Get
            Return _Cross
        End Get
        Set(value As Boolean)
            _Cross = value
            Invalidate()
        End Set
    End Property

    Private _Glowing As Boolean = False
    Property Glowing As Boolean
        Get
            Return _Glowing
        End Get
        Set(value As Boolean)
            _Glowing = value
            Invalidate()
        End Set
    End Property

#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)
        Height = 2
        P1 = New Pen(LineColor)
        P2 = New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20))
        'P1 = New Pen(Color.FromArgb(35, 35, 35))
        'P2 = New Pen(Color.FromArgb(55, 55, 55))
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.Clear(BackColor)
        If Glowing = True Then
            P1 = New Pen(LineColor)
            P2 = New Pen(GlowColor)
        Else
            P1 = New Pen(LineColor)
            P2 = New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20))
        End If
        If Cross = True Then
            If Glowing = True Then
                If GlowingCrossMode = "None" Then
                    G.DrawLine(P1, 0, 5, Width, 5)
                    G.DrawLine(P2, 0, 6, Width, 6)
                    G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "Horizontal" Then
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), 0, 6, Width, 6)
                ElseIf GlowingCrossMode = "Vertical" Then
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "Both" Then
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "AngleRight" Then
                    G.DrawLine(P1, 0, 5, Width, 5)
                    G.DrawLine(P2, 0, 6, Width, 6)
                    G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)

                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), Convert.ToSingle(Height / 2), Convert.ToSingle(Width / 2), Convert.ToSingle(Height / 2))
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), Convert.ToSingle(Height / 2) + 1, Convert.ToSingle((Width / 2) + 1), Convert.ToSingle(Height / 2) + 1)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2) + 1, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle(Width / 2) + 1, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                End If
            Else
                G.DrawLine(P1, 0, 5, Width, 5)
                G.DrawLine(P2, 0, 6, Width, 6)
                G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
            End If
        Else
            If VerticalLine = False Then
                G.DrawLine(P1, 0, 0, Width, 0)
                G.DrawLine(P2, 0, 1, Width, 1)
            Else
                G.DrawLine(P1, 0, 0, 0, Height)
                G.DrawLine(P2, 1, 0, 1, Height)
            End If
        End If
    End Sub

#End Region

End Class

#End Region

#Region " Separator Crop "

Class NSSeperatorCrop
    Inherits Control

#Region " Variables "

    Private P1, P2 As Pen
    Public GlowingCrossMode As String = "None"

#Region " Properties "

    Private _LineColor As Color = Color.FromArgb(35, 35, 35)
    Property LineColor As Color
        Get
            Return _LineColor
        End Get
        Set(value As Color)
            _LineColor = value
            Invalidate()
        End Set
    End Property

    Private _GlowColor As Color = Color.DarkGreen
    Property GlowColor As Color
        Get
            Return _GlowColor
        End Get
        Set(value As Color)
            _GlowColor = value
            Invalidate()
        End Set
    End Property

    Private _InvertedLine As Boolean = False
    Property InvertedLine As Boolean
        Get
            Return _InvertedLine
        End Get
        Set(value As Boolean)
            _InvertedLine = value
            Invalidate()
        End Set
    End Property

    Private _VerticalLine As Boolean = False
    Property VerticalLine As Boolean
        Get
            Return _VerticalLine
        End Get
        Set(value As Boolean)
            _VerticalLine = value
            Invalidate()
        End Set
    End Property

    Private _Cross As Boolean = False
    Property Cross As Boolean
        Get
            Return _Cross
        End Get
        Set(value As Boolean)
            _Cross = value
            Invalidate()
        End Set
    End Property

    Private _Glowing As Boolean = False
    Property Glowing As Boolean
        Get
            Return _Glowing
        End Get
        Set(value As Boolean)
            _Glowing = value
            Invalidate()
        End Set
    End Property

#End Region

#End Region

#Region " Constructor "

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)
        Height = 2
        P1 = New Pen(LineColor)
        P2 = New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20))
        'P1 = New Pen(Color.FromArgb(35, 35, 35))
        'P2 = New Pen(Color.FromArgb(55, 55, 55))
    End Sub

#End Region

#Region " Paint "

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.Clear(BackColor)
        If Glowing = True Then
            P1 = New Pen(LineColor)
            P2 = New Pen(GlowColor)
        Else
            P1 = New Pen(LineColor)
            P2 = New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20))
        End If
        If Cross = True Then
            If Glowing = True Then
                If GlowingCrossMode = "None" Then
                    G.DrawLine(P1, 0, 5, Width, 5)
                    G.DrawLine(P2, 0, 6, Width, 6)
                    G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "Horizontal" Then
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), 0, 6, Width, 6)
                ElseIf GlowingCrossMode = "Vertical" Then
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "Both" Then
                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                ElseIf GlowingCrossMode = "AngleRight" Then
                    G.DrawLine(P1, 0, 5, Width, 5)
                    G.DrawLine(P2, 0, 6, Width, 6)
                    G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)

                    G.DrawLine(New Pen(LineColor), 0, 5, Width, 5)
                    G.DrawLine(New Pen(Color.FromArgb(LineColor.R + 20, LineColor.G + 20, LineColor.B + 20)), 0, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), Convert.ToSingle(Height / 2), Convert.ToSingle(Width / 2), Convert.ToSingle(Height / 2))
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), Convert.ToSingle(Height / 2) + 1, Convert.ToSingle((Width / 2) + 1), Convert.ToSingle(Height / 2) + 1)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2) + 1, 5, Width, 5)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle(Width / 2) + 1, 6, Width, 6)
                    G.DrawLine(New Pen(LineColor), Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                    G.DrawLine(New Pen(GlowColor), Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
                End If
            Else
                G.DrawLine(P1, 0, 5, Width, 5)
                G.DrawLine(P2, 0, 6, Width, 6)
                G.DrawLine(P1, Convert.ToSingle(Width / 2), 0, Convert.ToSingle(Width / 2), Height)
                G.DrawLine(P2, Convert.ToSingle((Width / 2) + 1), 0, Convert.ToSingle((Width / 2) + 1), Height)
            End If
        Else
            If VerticalLine = False Then
                G.DrawLine(P1, 0, 0, Width, 0)
                G.DrawLine(P2, 0, 1, Width, 1)
            Else
                G.DrawLine(P1, 0, 0, 0, Height)
                G.DrawLine(P2, 1, 0, 1, Height)
            End If
        End If
    End Sub

#End Region

End Class

#End Region

#Region "TrackBar"

<ComponentModel.DefaultEvent("Scroll")> Class NSTrackBar
    Inherits Control

    Event Scroll(sender As Object)

    Private _Minimum As Integer
    Property Minimum() As Integer
        Get
            Return _Minimum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Minimum = value
            If value > _Value Then _Value = value
            If value > _Maximum Then _Maximum = value
            Invalidate()
        End Set
    End Property

    Private _Maximum As Integer = 10
    Property Maximum() As Integer
        Get
            Return _Maximum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Maximum = value
            If value < _Value Then _Value = value
            If value < _Minimum Then _Minimum = value
            Invalidate()
        End Set
    End Property

    Private _Value As Integer
    Property Value() As Integer
        Get
            Return _Value
        End Get
        Set(value As Integer)
            If value = _Value Then Return

            If value > _Maximum Then value = _Maximum
            If value < _Minimum Then value = _Minimum

            _Value = value
            Invalidate()
        End Set
    End Property

    Private _DrawValue As Boolean
    Property DrawValue() As Boolean
        Get
            Return _DrawValue
        End Get
        Set(DrawValue As Boolean)
            _DrawValue = DrawValue
            Invalidate()
        End Set
    End Property

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Height = 17

        P1 = New Pen(Color.FromArgb(150, 110, 0), 2)
        P2 = New Pen(Color.FromArgb(55, 55, 55))
        P3 = New Pen(Color.FromArgb(35, 35, 35))
        P4 = New Pen(Color.FromArgb(65, 65, 65))
    End Sub

    Private GP1, GP2, GP3, GP4 As Drawing2D.GraphicsPath

    Private R1, R2, R3 As Rectangle
    Private I1 As Integer

    Private P1, P2, P3, P4 As Pen

    Private GB1, GB2, GB3 As Drawing2D.LinearGradientBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = CreateRound(0, 5, Width - 1, 10, 5)
        GP2 = CreateRound(1, 6, Width - 3, 8, 5)

        R1 = New Rectangle(0, 7, Width - 1, 5)
        GB1 = New Drawing2D.LinearGradientBrush(R1, Color.FromArgb(45, 45, 45), Color.FromArgb(50, 50, 50), 90.0F)

        I1 = CInt((_Value - _Minimum) / (_Maximum - _Minimum) * (Width - 11))
        R2 = New Rectangle(I1, 0, 10, 15)

        G.SetClip(GP2)
        G.FillRectangle(GB1, R1)

        R3 = New Rectangle(1, 7, R2.X + R2.Width - 2, 8)
        If Enabled = True Then
            GB2 = New Drawing2D.LinearGradientBrush(R3, Color.FromArgb(205, 150, 0), Color.FromArgb(150, 110, 0), 90.0F)
        Else
            GB2 = New Drawing2D.LinearGradientBrush(R3, Color.Gray, Color.Black, 90.0F)
        End If

        G.SmoothingMode = Drawing2D.SmoothingMode.None
        G.FillRectangle(GB2, R3)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        For I As Integer = 0 To R3.Width - 15 Step 5
            G.DrawLine(P1, I, 0, I + 15, Height)
        Next

        G.ResetClip()

        G.DrawPath(P2, GP1)
        G.DrawPath(P3, GP2)

        GP3 = CreateRound(R2, 5)
        GP4 = CreateRound(R2.X + 1, R2.Y + 1, R2.Width - 2, R2.Height - 2, 5)
        GB3 = New Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)

        G.FillPath(GB3, GP3)
        G.DrawPath(P3, GP3)
        G.DrawPath(P4, GP4)

        'Dim PT1 As PointF = New PointF(I1, Height \ 2 - G.MeasureString((_Value + 1).ToString, Font, Width, StringFormat.GenericTypographic).Height / 2)
        'G.DrawString((_Value + 1).ToString, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
        'G.DrawString((_Value + 1).ToString, Font, New SolidBrush(Color.FromArgb(205, 150, 0)), PT1)
    End Sub

    Private TrackDown As Boolean
    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left Then
            I1 = CInt((_Value - _Minimum) / (_Maximum - _Minimum) * (Width - 11))
            R2 = New Rectangle(I1, 0, 10, 20)

            TrackDown = R2.Contains(e.Location)
            If TrackDown = False Then
                Value = _Minimum + CInt((_Maximum - _Minimum) * (e.X / Width))
                RaiseEvent Scroll(Me)
            End If
        End If

        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If TrackDown AndAlso e.X > -1 AndAlso e.X < (Width + 1) Then
            Value = _Minimum + CInt((_Maximum - _Minimum) * (e.X / Width))
            RaiseEvent Scroll(Me)
        End If

        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        TrackDown = False
        MyBase.OnMouseUp(e)
    End Sub

End Class

#End Region

#Region "RamdomPool"

<ComponentModel.DefaultEvent("ValueChanged")> Class NSRandomPool
    Inherits Control

    Event ValueChanged(sender As Object)

    Private _Value As New Text.StringBuilder
    ReadOnly Property Value() As String
        Get
            Return _Value.ToString()
        End Get
    End Property

    ReadOnly Property FullValue() As String
        Get
            Return BitConverter.ToString(Table).Replace("-", "")
        End Get
    End Property

    Private RNG As New Random()

    Private ItemSize As Integer = 9
    Private DrawSize As Integer = 8

    Private WA As Rectangle

    Private RowSize As Integer
    Private ColumnSize As Integer

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        P1 = New Pen(Color.FromArgb(55, 55, 55))
        P2 = New Pen(Color.FromArgb(35, 35, 35))

        B1 = New SolidBrush(Color.FromArgb(30, 30, 30))
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        UpdateTable()
        MyBase.OnHandleCreated(e)
    End Sub

    Private Table As Byte()
    Private Sub UpdateTable()
        WA = New Rectangle(5, 5, Width - 10, Height - 10)

        RowSize = WA.Width \ ItemSize
        ColumnSize = WA.Height \ ItemSize

        WA.Width = RowSize * ItemSize
        WA.Height = ColumnSize * ItemSize

        WA.X = (Width \ 2) - (WA.Width \ 2)
        WA.Y = (Height \ 2) - (WA.Height \ 2)

        Table = New Byte((RowSize * ColumnSize) - 1) {}

        For I As Integer = 0 To Table.Length - 1
            Table(I) = CByte(RNG.Next(100))
        Next

        Invalidate()
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        UpdateTable()
    End Sub

    Private Index1 As Integer = -1
    Private Index2 As Integer

    Private InvertColors As Boolean

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        HandleDraw(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        HandleDraw(e)
        MyBase.OnMouseDown(e)
    End Sub

    Private Sub HandleDraw(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left OrElse e.Button = Windows.Forms.MouseButtons.Right Then
            If Not WA.Contains(e.Location) Then Return

            InvertColors = (e.Button = Windows.Forms.MouseButtons.Right)

            Index1 = GetIndex(e.X, e.Y)
            If Index1 = Index2 Then Return

            Dim L As Boolean = Not (Index1 Mod RowSize = 0)
            Dim R As Boolean = Not (Index1 Mod RowSize = (RowSize - 1))

            Randomize(Index1 - RowSize)
            If L Then Randomize(Index1 - 1)
            Randomize(Index1)
            If R Then Randomize(Index1 + 1)
            Randomize(Index1 + RowSize)

            _Value.Append(Table(Index1).ToString("X"))
            If _Value.Length > 32 Then _Value.Remove(0, 2)

            RaiseEvent ValueChanged(Me)

            Index2 = Index1
            Invalidate()
        End If
    End Sub

    Private GP1, GP2 As Drawing2D.GraphicsPath

    Private P1, P2 As Pen
    Private B1, B2 As SolidBrush

    Private PB1 As Drawing2D.PathGradientBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        GP1 = CreateRound(0, 0, Width - 1, Height - 1, 7)
        GP2 = CreateRound(1, 1, Width - 3, Height - 3, 7)

        PB1 = New Drawing2D.PathGradientBrush(GP1)
        PB1.CenterColor = Color.FromArgb(50, 50, 50)
        PB1.SurroundColors = {Color.FromArgb(45, 45, 45)}
        PB1.FocusScales = New PointF(0.9F, 0.5F)

        G.FillPath(PB1, GP1)

        G.DrawPath(P1, GP1)
        G.DrawPath(P2, GP2)

        G.SmoothingMode = Drawing2D.SmoothingMode.None

        For I As Integer = 0 To Table.Length - 1
            Dim C As Integer = Math.Max(Table(I), 75)

            Dim X As Integer = ((I Mod RowSize) * ItemSize) + WA.X
            Dim Y As Integer = ((I \ RowSize) * ItemSize) + WA.Y

            B2 = New SolidBrush(Color.FromArgb(C, C, C))

            G.FillRectangle(B1, X + 1, Y + 1, DrawSize, DrawSize)
            G.FillRectangle(B2, X, Y, DrawSize, DrawSize)

            B2.Dispose()
        Next

    End Sub

    Private Function GetIndex(x As Integer, y As Integer) As Integer
        Return (((y - WA.Y) \ ItemSize) * RowSize) + ((x - WA.X) \ ItemSize)
    End Function

    Private Sub Randomize(index As Integer)
        If index > -1 AndAlso index < Table.Length Then
            If InvertColors Then
                Table(index) = CByte(RNG.Next(100))
            Else
                Table(index) = CByte(RNG.Next(100, 256))
            End If
        End If
    End Sub

End Class

#End Region

#Region "Keyboard"

Class NSKeyboard
    Inherits Control

    Private TextBitmap As Bitmap
    Private TextGraphics As Graphics

    Const LowerKeys As String = "1234567890-=qwertyuiop[]asdfghjkl\;'zxcvbnm,./`"
    Const UpperKeys As String = "!@#$%^&*()_+QWERTYUIOP{}ASDFGHJKL|:""ZXCVBNM<>?~"

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Font = New Font("Verdana", 8.25F)

        TextBitmap = New Bitmap(1, 1)
        TextGraphics = Graphics.FromImage(TextBitmap)

        MinimumSize = New Size(386, 162)
        MaximumSize = New Size(386, 162)

        Lower = LowerKeys.ToCharArray()
        Upper = UpperKeys.ToCharArray()

        PrepareCache()

        P1 = New Pen(Color.FromArgb(45, 45, 45))
        P2 = New Pen(Color.FromArgb(65, 65, 65))
        P3 = New Pen(Color.FromArgb(35, 35, 35))

        B1 = New SolidBrush(Color.FromArgb(100, 100, 100))
    End Sub

    Private _Target As Control
    Public Property Target() As Control
        Get
            Return _Target
        End Get
        Set(value As Control)
            _Target = value
        End Set
    End Property

    Private Shift As Boolean

    Private Pressed As Integer = -1
    Private Buttons As Rectangle()

    Private Lower As Char()
    Private Upper As Char()
    Private Other As String() = {"Shift", "Space", "Back"}

    Private UpperCache As PointF()
    Private LowerCache As PointF()

    Private Sub PrepareCache()
        Buttons = New Rectangle(50) {}
        UpperCache = New PointF(Upper.Length - 1) {}
        LowerCache = New PointF(Lower.Length - 1) {}

        Dim I As Integer

        Dim S As SizeF
        Dim R As Rectangle

        For Y As Integer = 0 To 3
            For X As Integer = 0 To 11
                I = (Y * 12) + X
                R = New Rectangle(X * 32, Y * 32, 32, 32)

                Buttons(I) = R

                If Not I = 47 AndAlso Not Char.IsLetter(Upper(I)) Then
                    S = TextGraphics.MeasureString(Upper(I), Font)
                    UpperCache(I) = New PointF(R.X + (R.Width \ 2 - S.Width / 2), R.Y + R.Height - S.Height - 2)

                    S = TextGraphics.MeasureString(Lower(I), Font)
                    LowerCache(I) = New PointF(R.X + (R.Width \ 2 - S.Width / 2), R.Y + R.Height - S.Height - 2)
                End If
            Next
        Next

        Buttons(48) = New Rectangle(0, 4 * 32, 2 * 32, 32)
        Buttons(49) = New Rectangle(Buttons(48).Right, 4 * 32, 8 * 32, 32)
        Buttons(50) = New Rectangle(Buttons(49).Right, 4 * 32, 2 * 32, 32)
    End Sub

    Private GP1 As Drawing2D.GraphicsPath

    Private SZ1 As SizeF
    Private PT1 As PointF

    Private P1, P2, P3 As Pen
    Private B1 As SolidBrush

    Private PB1 As Drawing2D.PathGradientBrush
    Private GB1 As Drawing2D.LinearGradientBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        G.Clear(BackColor)

        Dim R As Rectangle

        Dim Offset As Integer
        G.DrawRectangle(P1, 0, 0, (12 * 32) + 1, (5 * 32) + 1)

        For I As Integer = 0 To Buttons.Length - 1
            R = Buttons(I)

            Offset = 0
            If I = Pressed Then
                Offset = 1

                GP1 = New Drawing2D.GraphicsPath()
                GP1.AddRectangle(R)

                PB1 = New Drawing2D.PathGradientBrush(GP1)
                PB1.CenterColor = Color.FromArgb(60, 60, 60)
                PB1.SurroundColors = {Color.FromArgb(55, 55, 55)}
                PB1.FocusScales = New PointF(0.8F, 0.5F)

                G.FillPath(PB1, GP1)
            Else
                GB1 = New Drawing2D.LinearGradientBrush(R, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)
                G.FillRectangle(GB1, R)
            End If

            Select Case I
                Case 48, 49, 50
                    SZ1 = G.MeasureString(Other(I - 48), Font)
                    G.DrawString(Other(I - 48), Font, Brushes.Black, R.X + (R.Width \ 2 - SZ1.Width / 2) + Offset + 1, R.Y + (R.Height \ 2 - SZ1.Height / 2) + Offset + 1)
                    G.DrawString(Other(I - 48), Font, Brushes.White, R.X + (R.Width \ 2 - SZ1.Width / 2) + Offset, R.Y + (R.Height \ 2 - SZ1.Height / 2) + Offset)
                Case 47
                    DrawArrow(Color.Black, R.X + Offset + 1, R.Y + Offset + 1)
                    DrawArrow(Color.White, R.X + Offset, R.Y + Offset)
                Case Else
                    If Shift Then
                        G.DrawString(Upper(I), Font, Brushes.Black, R.X + 3 + Offset + 1, R.Y + 2 + Offset + 1)
                        G.DrawString(Upper(I), Font, Brushes.White, R.X + 3 + Offset, R.Y + 2 + Offset)

                        If Not Char.IsLetter(Lower(I)) Then
                            PT1 = LowerCache(I)
                            G.DrawString(Lower(I), Font, B1, PT1.X + Offset, PT1.Y + Offset)
                        End If
                    Else
                        G.DrawString(Lower(I), Font, Brushes.Black, R.X + 3 + Offset + 1, R.Y + 2 + Offset + 1)
                        G.DrawString(Lower(I), Font, Brushes.White, R.X + 3 + Offset, R.Y + 2 + Offset)

                        If Not Char.IsLetter(Upper(I)) Then
                            PT1 = UpperCache(I)
                            G.DrawString(Upper(I), Font, B1, PT1.X + Offset, PT1.Y + Offset)
                        End If
                    End If
            End Select

            G.DrawRectangle(P2, R.X + 1 + Offset, R.Y + 1 + Offset, R.Width - 2, R.Height - 2)
            G.DrawRectangle(P3, R.X + Offset, R.Y + Offset, R.Width, R.Height)

            If I = Pressed Then
                G.DrawLine(P1, R.X, R.Y, R.Right, R.Y)
                G.DrawLine(P1, R.X, R.Y, R.X, R.Bottom)
            End If
        Next
    End Sub

    Private Sub DrawArrow(color As Color, rx As Integer, ry As Integer)
        Dim R As New Rectangle(rx + 8, ry + 8, 16, 16)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        Dim P As New Pen(color, 1)
        Dim C As New Drawing2D.AdjustableArrowCap(3, 2)
        P.CustomEndCap = C

        G.DrawArc(P, R, 0.0F, 290.0F)

        P.Dispose()
        C.Dispose()
        G.SmoothingMode = Drawing2D.SmoothingMode.None
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Dim Index As Integer = ((e.Y \ 32) * 12) + (e.X \ 32)

        If Index > 47 Then
            For I As Integer = 48 To Buttons.Length - 1
                If Buttons(I).Contains(e.X, e.Y) Then
                    Pressed = I
                    Exit For
                End If
            Next
        Else
            Pressed = Index
        End If

        HandleKey()
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        Pressed = -1
        Invalidate()
    End Sub

    Private Sub HandleKey()
        If _Target Is Nothing Then Return
        If Pressed = -1 Then Return

        Select Case Pressed
            Case 47
                _Target.Text = String.Empty
            Case 48
                Shift = Not Shift
            Case 49
                _Target.Text &= " "
            Case 50
                If Not _Target.Text.Length = 0 Then
                    _Target.Text = _Target.Text.Remove(_Target.Text.Length - 1)
                End If
            Case Else
                If Shift Then
                    _Target.Text &= Upper(Pressed)
                Else
                    _Target.Text &= Lower(Pressed)
                End If
        End Select
    End Sub

End Class

#End Region

#Region "Paginator"

<ComponentModel.DefaultEvent("SelectedIndexChanged")> Class NSPaginator
    Inherits Control

    Public Event SelectedIndexChanged(sender As Object, e As EventArgs)

    Private TextBitmap As Bitmap
    Private TextGraphics As Graphics

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Size = New Size(202, 26)

        TextBitmap = New Bitmap(1, 1)
        TextGraphics = Graphics.FromImage(TextBitmap)

        InvalidateItems()

        B1 = New SolidBrush(Color.FromArgb(50, 50, 50))
        B2 = New SolidBrush(Color.FromArgb(55, 55, 55))

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(55, 55, 55))
        P3 = New Pen(Color.FromArgb(65, 65, 65))
    End Sub

    Private _SelectedIndex As Integer
    Public Property SelectedIndex() As Integer
        Get
            Return _SelectedIndex
        End Get
        Set(value As Integer)
            _SelectedIndex = Math.Max(Math.Min(value, MaximumIndex), 0)
            Invalidate()
        End Set
    End Property

    Private _NumberOfPages As Integer
    Public Property NumberOfPages() As Integer
        Get
            Return _NumberOfPages
        End Get
        Set(value As Integer)
            _NumberOfPages = value
            _SelectedIndex = Math.Max(Math.Min(_SelectedIndex, MaximumIndex), 0)
            Invalidate()
        End Set
    End Property

    Public ReadOnly Property MaximumIndex As Integer
        Get
            Return NumberOfPages - 1
        End Get
    End Property

    Private ItemWidth As Integer

    Public Overrides Property Font As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)
            MyBase.Font = value

            InvalidateItems()
            Invalidate()
        End Set
    End Property

    Private Sub InvalidateItems()
        Dim S As Size = TextGraphics.MeasureString("000 ..", Font).ToSize()
        ItemWidth = S.Width + 10
    End Sub

    Private GP1, GP2 As Drawing2D.GraphicsPath

    Private R1 As Rectangle

    Private SZ1 As Size
    Private PT1 As Point

    Private P1, P2, P3 As Pen
    Private B1, B2 As SolidBrush

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        G.Clear(BackColor)
        G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        Dim LeftEllipse, RightEllipse As Boolean

        If _SelectedIndex < 4 Then
            For I As Integer = 0 To Math.Min(MaximumIndex, 4)
                RightEllipse = (I = 4) AndAlso (MaximumIndex > 4)
                DrawBox(I * ItemWidth, I, False, RightEllipse)
            Next
        ElseIf _SelectedIndex > 3 AndAlso _SelectedIndex < (MaximumIndex - 3) Then
            For I As Integer = 0 To 4
                LeftEllipse = (I = 0)
                RightEllipse = (I = 4)
                DrawBox(I * ItemWidth, _SelectedIndex + I - 2, LeftEllipse, RightEllipse)
            Next
        Else
            For I As Integer = 0 To 4
                LeftEllipse = (I = 0) AndAlso (MaximumIndex > 4)
                DrawBox(I * ItemWidth, MaximumIndex - (4 - I), LeftEllipse, False)
            Next
        End If
    End Sub

    Private Sub DrawBox(x As Integer, index As Integer, leftEllipse As Boolean, rightEllipse As Boolean)
        R1 = New Rectangle(x, 0, ItemWidth - 4, Height - 1)

        GP1 = CreateRound(R1, 7)
        GP2 = CreateRound(R1.X + 1, R1.Y + 1, R1.Width - 2, R1.Height - 2, 7)

        Dim T As String = CStr(index + 1)

        If leftEllipse Then T = ".. " & T
        If rightEllipse Then T += " .."

        SZ1 = G.MeasureString(T, Font).ToSize()
        PT1 = New Point(R1.X + (R1.Width \ 2 - SZ1.Width \ 2), R1.Y + (R1.Height \ 2 - SZ1.Height \ 2))

        If index = _SelectedIndex Then
            G.FillPath(B1, GP1)

            Dim F As New Font(Font, FontStyle.Underline)
            G.DrawString(T, F, Brushes.Black, PT1.X + 1, PT1.Y + 1)
            G.DrawString(T, F, Brushes.White, PT1)
            F.Dispose()

            G.DrawPath(P1, GP2)
            G.DrawPath(P2, GP1)
        Else
            G.FillPath(B2, GP1)

            G.DrawString(T, Font, Brushes.Black, PT1.X + 1, PT1.Y + 1)
            G.DrawString(T, Font, Brushes.White, PT1)

            G.DrawPath(P3, GP2)
            G.DrawPath(P1, GP1)
        End If
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left Then
            Dim NewIndex As Integer
            Dim OldIndex As Integer = _SelectedIndex

            If _SelectedIndex < 4 Then
                NewIndex = (e.X \ ItemWidth)
            ElseIf _SelectedIndex > 3 AndAlso _SelectedIndex < (MaximumIndex - 3) Then
                NewIndex = (e.X \ ItemWidth)

                Select Case NewIndex
                    Case 2
                        NewIndex = OldIndex
                    Case Is < 2
                        NewIndex = OldIndex - (2 - NewIndex)
                    Case Is > 2
                        NewIndex = OldIndex + (NewIndex - 2)
                End Select
            Else
                NewIndex = MaximumIndex - (4 - (e.X \ ItemWidth))
            End If

            If (NewIndex < _NumberOfPages) AndAlso (Not NewIndex = OldIndex) Then
                SelectedIndex = NewIndex
                RaiseEvent SelectedIndexChanged(Me, Nothing)
            End If
        End If

        MyBase.OnMouseDown(e)
    End Sub

End Class

#End Region

#Region "Scroll"

<ComponentModel.DefaultEvent("Scroll")> Class NSVScrollBar
    Inherits Control

    Event Scroll(sender As Object)

    Private _Minimum As Integer
    Property Minimum() As Integer
        Get
            Return _Minimum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Minimum = value
            If value > _Value Then _Value = value
            If value > _Maximum Then _Maximum = value

            InvalidateLayout()
        End Set
    End Property

    Private _Maximum As Integer = 100
    Property Maximum() As Integer
        Get
            Return _Maximum
        End Get
        Set(value As Integer)
            If value < 1 Then value = 1

            _Maximum = value
            If value < _Value Then _Value = value
            If value < _Minimum Then _Minimum = value

            InvalidateLayout()
        End Set
    End Property

    Private _Value As Integer
    Property Value() As Integer
        Get
            If Not ShowThumb Then Return _Minimum
            Return _Value
        End Get
        Set(value As Integer)
            If value = _Value Then Return

            If value > _Maximum OrElse value < _Minimum Then
            End If

            _Value = value
            InvalidatePosition()

            RaiseEvent Scroll(Me)
        End Set
    End Property

    Public ReadOnly Property Percent As Double
        Get
            If Not ShowThumb Then Return 0
            Return GetProgress()
        End Get
    End Property

    Private _SmallChange As Integer = 1
    Public Property SmallChange() As Integer
        Get
            Return _SmallChange
        End Get
        Set(value As Integer)
            If value < 1 Then
                Throw New Exception("Property value is not valid.")
            End If

            _SmallChange = value
        End Set
    End Property

    Private _LargeChange As Integer = 10
    Public Property LargeChange() As Integer
        Get
            Return _LargeChange
        End Get
        Set(value As Integer)
            If value < 1 Then
                Throw New Exception("Property value is not valid.")
            End If

            _LargeChange = value
        End Set
    End Property

    Private ButtonSize As Integer = 16
    Private ThumbSize As Integer = 24 ' 14 minimum

    Private TSA As Rectangle
    Private BSA As Rectangle
    Private Shaft As Rectangle
    Private Thumb As Rectangle

    Private ShowThumb As Boolean
    Private ThumbDown As Boolean

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Width = 18

        B1 = New SolidBrush(Color.FromArgb(55, 55, 55))
        B2 = New SolidBrush(Color.FromArgb(35, 35, 35))

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(65, 65, 65))
        P3 = New Pen(Color.FromArgb(55, 55, 55))
        P4 = New Pen(Color.FromArgb(40, 40, 40))
    End Sub

    Private GP1, GP2, GP3, GP4 As Drawing2D.GraphicsPath

    Private P1, P2, P3, P4 As Pen
    Private B1, B2 As SolidBrush

    Dim I1 As Integer

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.Clear(BackColor)

        GP1 = DrawArrow(4, 6, False)
        GP2 = DrawArrow(5, 7, False)

        G.FillPath(B1, GP2)
        G.FillPath(B2, GP1)

        GP3 = DrawArrow(4, Height - 11, True)
        GP4 = DrawArrow(5, Height - 10, True)

        G.FillPath(B1, GP4)
        G.FillPath(B2, GP3)

        If ShowThumb Then
            G.FillRectangle(B1, Thumb)
            G.DrawRectangle(P1, Thumb)
            G.DrawRectangle(P2, Thumb.X + 1, Thumb.Y + 1, Thumb.Width - 2, Thumb.Height - 2)

            Dim Y As Integer
            Dim LY As Integer = Thumb.Y + (Thumb.Height \ 2) - 3

            For I As Integer = 0 To 2
                Y = LY + (I * 3)

                G.DrawLine(P1, Thumb.X + 5, Y, Thumb.Right - 5, Y)
                G.DrawLine(P2, Thumb.X + 5, Y + 1, Thumb.Right - 5, Y + 1)
            Next
        End If

        G.DrawRectangle(P3, 0, 0, Width - 1, Height - 1)
        G.DrawRectangle(P4, 1, 1, Width - 3, Height - 3)
    End Sub

    Private Function DrawArrow(x As Integer, y As Integer, flip As Boolean) As Drawing2D.GraphicsPath
        Dim GP As New Drawing2D.GraphicsPath()

        Dim W As Integer = 9
        Dim H As Integer = 5

        If flip Then
            GP.AddLine(x + 1, y, x + W + 1, y)
            GP.AddLine(x + W, y, x + H, y + H - 1)
        Else
            GP.AddLine(x, y + H, x + W, y + H)
            GP.AddLine(x + W, y + H, x + H, y)
        End If

        GP.CloseFigure()
        Return GP
    End Function

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        InvalidateLayout()
    End Sub

    Private Sub InvalidateLayout()
        TSA = New Rectangle(0, 0, Width, ButtonSize)
        BSA = New Rectangle(0, Height - ButtonSize, Width, ButtonSize)
        Shaft = New Rectangle(0, TSA.Bottom + 1, Width, Height - (ButtonSize * 2) - 1)

        ShowThumb = ((_Maximum - _Minimum) > Shaft.Height)

        If ShowThumb Then
            'ThumbSize = Math.Max(0, 14) 'TODO: Implement this.
            Thumb = New Rectangle(1, 0, Width - 3, ThumbSize)
        End If

        RaiseEvent Scroll(Me)
        InvalidatePosition()
    End Sub

    Private Sub InvalidatePosition()
        Thumb.Y = CInt(GetProgress() * (Shaft.Height - ThumbSize)) + TSA.Height
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left AndAlso ShowThumb Then
            If TSA.Contains(e.Location) Then
                I1 = _Value - _SmallChange
            ElseIf BSA.Contains(e.Location) Then
                I1 = _Value + _SmallChange
            Else
                If Thumb.Contains(e.Location) Then
                    ThumbDown = True
                    MyBase.OnMouseDown(e)
                    Return
                Else
                    If e.Y < Thumb.Y Then
                        I1 = _Value - _LargeChange
                    Else
                        I1 = _Value + _LargeChange
                    End If
                End If
            End If

            Value = Math.Min(Math.Max(I1, _Minimum), _Maximum)
            InvalidatePosition()
        End If

        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If ThumbDown AndAlso ShowThumb Then
            Dim ThumbPosition As Integer = e.Y - TSA.Height - (ThumbSize \ 2)
            Dim ThumbBounds As Integer = Shaft.Height - ThumbSize

            I1 = CInt((ThumbPosition / ThumbBounds) * (_Maximum - _Minimum)) + _Minimum

            Value = Math.Min(Math.Max(I1, _Minimum), _Maximum)
            InvalidatePosition()
        End If

        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        ThumbDown = False
        MyBase.OnMouseUp(e)
    End Sub

    Private Function GetProgress() As Double
        Return (_Value - _Minimum) / (_Maximum - _Minimum)
    End Function

End Class

#End Region

#Region "HScrollBar"

<ComponentModel.DefaultEvent("Scroll")> Class NSHScrollBar
    Inherits Control

    Event Scroll(sender As Object)

    Private _Minimum As Integer
    Property Minimum() As Integer
        Get
            Return _Minimum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Minimum = value
            If value > _Value Then _Value = value
            If value > _Maximum Then _Maximum = value

            InvalidateLayout()
        End Set
    End Property

    Private _Maximum As Integer = 100
    Property Maximum() As Integer
        Get
            Return _Maximum
        End Get
        Set(value As Integer)
            If value < 0 Then
                Throw New Exception("Property value is not valid.")
            End If

            _Maximum = value
            If value < _Value Then _Value = value
            If value < _Minimum Then _Minimum = value

            InvalidateLayout()
        End Set
    End Property

    Private _Value As Integer
    Property Value() As Integer
        Get
            If Not ShowThumb Then Return _Minimum
            Return _Value
        End Get
        Set(value As Integer)
            If value = _Value Then Return

            If value > _Maximum OrElse value < _Minimum Then
                Throw New Exception("Property value is not valid.")
            End If

            _Value = value
            InvalidatePosition()

            RaiseEvent Scroll(Me)
        End Set
    End Property

    Private _SmallChange As Integer = 1
    Public Property SmallChange() As Integer
        Get
            Return _SmallChange
        End Get
        Set(value As Integer)
            If value < 1 Then
                Throw New Exception("Property value is not valid.")
            End If

            _SmallChange = value
        End Set
    End Property

    Private _LargeChange As Integer = 10
    Public Property LargeChange() As Integer
        Get
            Return _LargeChange
        End Get
        Set(value As Integer)
            If value < 1 Then
                Throw New Exception("Property value is not valid.")
            End If

            _LargeChange = value
        End Set
    End Property

    Private ButtonSize As Integer = 16
    Private ThumbSize As Integer = 24 ' 14 minimum

    Private LSA As Rectangle
    Private RSA As Rectangle
    Private Shaft As Rectangle
    Private Thumb As Rectangle

    Private ShowThumb As Boolean
    Private ThumbDown As Boolean

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True)
        SetStyle(ControlStyles.Selectable, False)

        Height = 18

        B1 = New SolidBrush(Color.FromArgb(55, 55, 55))
        B2 = New SolidBrush(Color.FromArgb(35, 35, 35))

        P1 = New Pen(Color.FromArgb(35, 35, 35))
        P2 = New Pen(Color.FromArgb(65, 65, 65))
        P3 = New Pen(Color.FromArgb(55, 55, 55))
        P4 = New Pen(Color.FromArgb(40, 40, 40))
    End Sub

    Private GP1, GP2, GP3, GP4 As Drawing2D.GraphicsPath

    Private P1, P2, P3, P4 As Pen
    Private B1, B2 As SolidBrush

    Dim I1 As Integer

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics
        G.Clear(BackColor)

        GP1 = DrawArrow(6, 4, False)
        GP2 = DrawArrow(7, 5, False)

        G.FillPath(B1, GP2)
        G.FillPath(B2, GP1)

        GP3 = DrawArrow(Width - 11, 4, True)
        GP4 = DrawArrow(Width - 10, 5, True)

        G.FillPath(B1, GP4)
        G.FillPath(B2, GP3)

        If ShowThumb Then
            G.FillRectangle(B1, Thumb)
            G.DrawRectangle(P1, Thumb)
            G.DrawRectangle(P2, Thumb.X + 1, Thumb.Y + 1, Thumb.Width - 2, Thumb.Height - 2)

            Dim X As Integer
            Dim LX As Integer = Thumb.X + (Thumb.Width \ 2) - 3

            For I As Integer = 0 To 2
                X = LX + (I * 3)

                G.DrawLine(P1, X, Thumb.Y + 5, X, Thumb.Bottom - 5)
                G.DrawLine(P2, X + 1, Thumb.Y + 5, X + 1, Thumb.Bottom - 5)
            Next
        End If

        G.DrawRectangle(P3, 0, 0, Width - 1, Height - 1)
        G.DrawRectangle(P4, 1, 1, Width - 3, Height - 3)
    End Sub

    Private Function DrawArrow(x As Integer, y As Integer, flip As Boolean) As Drawing2D.GraphicsPath
        Dim GP As New Drawing2D.GraphicsPath()

        Dim W As Integer = 5
        Dim H As Integer = 9

        If flip Then
            GP.AddLine(x, y + 1, x, y + H + 1)
            GP.AddLine(x, y + H, x + W - 1, y + W)
        Else
            GP.AddLine(x + W, y, x + W, y + H)
            GP.AddLine(x + W, y + H, x + 1, y + W)
        End If

        GP.CloseFigure()
        Return GP
    End Function

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        InvalidateLayout()
    End Sub

    Private Sub InvalidateLayout()
        LSA = New Rectangle(0, 0, ButtonSize, Height)
        RSA = New Rectangle(Width - ButtonSize, 0, ButtonSize, Height)
        Shaft = New Rectangle(LSA.Right + 1, 0, Width - (ButtonSize * 2) - 1, Height)

        ShowThumb = ((_Maximum - _Minimum) > Shaft.Width)

        If ShowThumb Then
            'ThumbSize = Math.Max(0, 14) 'TODO: Implement this.
            Thumb = New Rectangle(0, 1, ThumbSize, Height - 3)
        End If

        RaiseEvent Scroll(Me)
        InvalidatePosition()
    End Sub

    Private Sub InvalidatePosition()
        Thumb.X = CInt(GetProgress() * (Shaft.Width - ThumbSize)) + LSA.Width
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left AndAlso ShowThumb Then
            If LSA.Contains(e.Location) Then
                I1 = _Value - _SmallChange
            ElseIf RSA.Contains(e.Location) Then
                I1 = _Value + _SmallChange
            Else
                If Thumb.Contains(e.Location) Then
                    ThumbDown = True
                    MyBase.OnMouseDown(e)
                    Return
                Else
                    If e.X < Thumb.X Then
                        I1 = _Value - _LargeChange
                    Else
                        I1 = _Value + _LargeChange
                    End If
                End If
            End If

            Value = Math.Min(Math.Max(I1, _Minimum), _Maximum)
            InvalidatePosition()
        End If

        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If ThumbDown AndAlso ShowThumb Then
            Dim ThumbPosition As Integer = e.X - LSA.Width - (ThumbSize \ 2)
            Dim ThumbBounds As Integer = Shaft.Width - ThumbSize

            I1 = CInt((ThumbPosition / ThumbBounds) * (_Maximum - _Minimum)) + _Minimum

            Value = Math.Min(Math.Max(I1, _Minimum), _Maximum)
            InvalidatePosition()
        End If

        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        ThumbDown = False
        MyBase.OnMouseUp(e)
    End Sub

    Private Function GetProgress() As Double
        Return (_Value - _Minimum) / (_Maximum - _Minimum)
    End Function

End Class

#End Region

#Region "ContextMenu"

Public Class NSContextMenu : Inherits ContextMenuStrip

    Private _ForeColor1 As Color = Color.FromArgb(130, 130, 130)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Public Nombre As String
    Public ID As String

    Sub New()
        Renderer = New CustomRenderer(New NSColorTable())
        ForeColor = ForeColor1
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        ForeColor = ForeColor1
        MyBase.OnPaint(e)
    End Sub

    Friend Class CustomRenderer
        Inherits ToolStripProfessionalRenderer

        Sub New()

        End Sub

        Public Sub New(professionalColorTable As ProfessionalColorTable)
            MyBase.New(professionalColorTable)
        End Sub

        Protected Overrides Sub OnRenderArrow(e As ToolStripArrowRenderEventArgs)
            e.ArrowColor = Color.FromArgb(35, 35, 35)
            MyBase.OnRenderArrow(e)
        End Sub


    End Class

End Class

#End Region

#Region "MenuStrip"

Public Class NSMenuStrip : Inherits MenuStrip

    Private _ForeColor1 As Color = Color.FromArgb(130, 130, 130)
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Sub New()
        Renderer = New ToolStripProfessionalRenderer(New NSColorTable())
        ForeColor = ForeColor1
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        ForeColor = ForeColor1
        MyBase.OnPaint(e)
    End Sub

End Class

#End Region

#Region "ColorTable"

Class NSColorTable : Inherits ProfessionalColorTable

    Private BackColor As Color = Color.FromArgb(55, 55, 55)

    Public Overrides ReadOnly Property ButtonSelectedBorder() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property CheckBackground() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property CheckPressedBackground() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property CheckSelectedBackground() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property ImageMarginGradientBegin() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property ImageMarginGradientEnd() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property ImageMarginGradientMiddle() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property MenuBorder() As Color
        Get
            Return Color.FromArgb(25, 25, 25)
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemBorder() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemSelected() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property SeparatorDark() As Color
        Get
            Return Color.FromArgb(35, 35, 35)
        End Get
    End Property

    Public Overrides ReadOnly Property ToolStripDropDownBackground() As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemSelectedGradientBegin As Color
        Get
            Return Color.FromArgb(50, 50, 50)
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemSelectedGradientEnd As Color
        Get
            Return Color.FromArgb(50, 50, 50)
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemPressedGradientBegin As Color
        Get
            Return BackColor
        End Get
    End Property

    Public Overrides ReadOnly Property MenuItemPressedGradientEnd As Color
        Get
            Return BackColor
        End Get
    End Property

End Class

#End Region

#Region "ListView"

Class NSListView : Inherits Control

    Public HoveredItem As Integer = -1
    Dim Keysearch As Integer
    Event SelectedIndexChanged(sender As Object, Indice As Integer)
    Event DobleClick(sender As Object, e As EventArgs, Indice As Integer)

#Region "Item"

    Class NSListViewItem

        Property Text As String
        Property Tag As Object
        Property Tag2 As Object
        Property Marked As Boolean
        Property MarkedColor As Color = Color.SteelBlue
        Property Area As Rectangle
        Property Image As Bitmap
        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Property SubItems As New List(Of NSListViewSubItem)
        Protected UniqueId As Guid

        Sub New()
            UniqueId = Guid.NewGuid()
        End Sub

        Public Overrides Function ToString() As String
            Return Text
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If TypeOf obj Is NSListViewItem Then Return (DirectCast(obj, NSListViewItem).UniqueId = UniqueId)
            Return False
        End Function

    End Class

#End Region

#Region "SubItem"

    Class NSListViewSubItem

        Property Text As String

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "ColumnHeader"

    Class NSListViewColumnHeader

        Property Text As String
        Property Width As Integer = 60

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "Properties"

    Private _Items As New List(Of NSListViewItem)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Items() As NSListViewItem()
        Get
            Return _Items.ToArray()
        End Get
        Set(value As NSListViewItem())
            _Items = New List(Of NSListViewItem)(value)
            InvalidateScroll()
        End Set
    End Property

    Private _SelectedItems As New List(Of NSListViewItem)
    Public ReadOnly Property SelectedItems() As NSListViewItem()
        Get
            Return _SelectedItems.ToArray()
        End Get
    End Property

    Private _SelectedIndex As New List(Of Integer)
    Public ReadOnly Property SelectedIndex() As Integer()
        Get
            Return _SelectedIndex.ToArray
        End Get
    End Property

    Private _Columns As New List(Of NSListViewColumnHeader)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Columns() As NSListViewColumnHeader()
        Get
            Return _Columns.ToArray()
        End Get
        Set(value As NSListViewColumnHeader())
            _Columns = New List(Of NSListViewColumnHeader)(value)
            InvalidateColumns()
        End Set
    End Property

    Private _MultiSelect As Boolean = True
    Public Property MultiSelect() As Boolean
        Get
            Return _MultiSelect
        End Get
        Set(value As Boolean)
            _MultiSelect = value

            If _SelectedItems.Count > 1 Then
                _SelectedItems.RemoveRange(1, _SelectedItems.Count - 1)
            End If

            Invalidate()
        End Set
    End Property

    Private _ShowColumn As Boolean = True
    Public Property ShowColumn() As Boolean
        Get
            Return _ShowColumn
        End Get
        Set(value As Boolean)
            _ShowColumn = value
            Invalidate()
        End Set
    End Property

    <ComponentModel.Browsable(False)> Public ItemHeight As Integer = 24
    Public Overrides Property Font As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)


            MyBase.Font = value
            InvalidateLayout()
        End Set
    End Property

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.White
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor3 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor3 As Color
        Get
            Return _ForeColor3
        End Get
        Set(value As Color)
            _ForeColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor1 As Color = Color.White
    Property ColumnColor1 As Color
        Get
            Return _ColumnColor1
        End Get
        Set(value As Color)
            _ColumnColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor2 As Color = Color.Black
    Property ColumnColor2 As Color
        Get
            Return _ColumnColor2
        End Get
        Set(value As Color)
            _ColumnColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnFont As Font = Font
    Property ColumnFont As Font
        Get
            Return _ColumnFont
        End Get
        Set(value As Font)
            _ColumnFont = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor1 As Color = Color.FromArgb(62, 62, 62)
    Property ItemBackColor1 As Color
        Get
            Return _ItemBackColor1
        End Get
        Set(value As Color)
            _ItemBackColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor2 As Color = Color.FromArgb(65, 65, 65)
    Property ItemBackColor2 As Color
        Get
            Return _ItemBackColor2
        End Get
        Set(value As Color)
            _ItemBackColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor3 As Color = Color.FromArgb(47, 47, 47)
    Property ItemBackColor3 As Color
        Get
            Return _ItemBackColor3
        End Get
        Set(value As Color)
            _ItemBackColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor4 As Color = Color.FromArgb(50, 50, 50)
    Property ItemBackColor4 As Color
        Get
            Return _ItemBackColor4
        End Get
        Set(value As Color)
            _ItemBackColor4 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region "Item Helper"

    Public Sub AddItem(text As String, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItem()
        Item.Text = text
        Item.SubItems = Items

        _Items.Add(Item)
        InvalidateScroll()
    End Sub

    Public Sub AddItem(text As String, ITag As String, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItem()
        Item.Text = text
        Item.SubItems = Items
        Item.Tag = ITag

        _Items.Add(Item)
        InvalidateScroll()
    End Sub

    Public Sub Clear()
        _Items.Clear()
        InvalidateScroll()
    End Sub

    Public Sub RemoveItemAt(index As Integer)
        If index <> -1 AndAlso index < _Items.Count Then _Items.RemoveAt(index)
        InvalidateScroll()
    End Sub

    Public Sub RemoveItem(item As NSListViewItem)
        _Items.Remove(item)
        InvalidateScroll()
    End Sub

    Public Sub RemoveItems(items As NSListViewItem())
        For Each I As NSListViewItem In items
            _Items.Remove(I)
        Next
        InvalidateScroll()
    End Sub

    Public Sub InsertItemAt(pos As Integer, item As NSListViewItem)
        _Items.Insert(pos, item)
        InvalidateScroll()
    End Sub

    Public Sub SelectItem(pos As Integer)
        _SelectedIndex.Clear()
        _SelectedItems.Clear()
        _SelectedIndex.Add(pos)
        If pos <> -1 And _Items.Count > 0 Then _SelectedItems.Add(_Items(pos))
        RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0))
    End Sub

    Public Sub EnsureVisibleAt(pos As Integer)
        If pos = 0 Then VS.Value = VS.Minimum : InvalidateLayout() : Exit Sub
        If pos = Items.Count - 1 Then VS.Value = VS.Maximum : InvalidateLayout() : Exit Sub
        VS.Value = ItemHeight * pos
        'If pos * 25 < VS.Value Then
        '    VS.Value = Math.Max(pos * 25, VS.Minimum)
        '    If VS.Value < 25 Then VS.Value = VS.Minimum
        'ElseIf pos * 25 > (VS.Value + Height) - (25 * 2) Then
        '    VS.Value = Math.Min(((pos * 25) - (Height) + 5), VS.Maximum)
        '    If VS.Value > VS.Maximum - 25 Then VS.Value = VS.Maximum
        'End If
        InvalidateLayout()
    End Sub

#End Region

#Region "Methods"

    Public VS As NSVScrollBar

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True) : SetStyle(ControlStyles.Selectable, True)
        P1 = New Pen(Color.FromArgb(55, 55, 55)) : P2 = New Pen(Color.FromArgb(35, 35, 35)) : P3 = New Pen(Color.FromArgb(65, 65, 65))
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 6
        VS = New NSVScrollBar : If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight
        AddHandler VS.Scroll, AddressOf HandleScroll : AddHandler VS.MouseDown, AddressOf VS_MouseDown : Controls.Add(VS)
        InvalidateLayout()
    End Sub
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If Focused = False Then Return MyBase.ProcessCmdKey(msg, keyData)
        If (keyData = Keys.Right) OrElse (keyData = Keys.Left) OrElse (keyData = Keys.Up) OrElse (keyData = Keys.Down) OrElse (keyData = Keys.Home) OrElse (keyData = Keys.End) OrElse (keyData = Keys.PageUp) OrElse (keyData = Keys.PageDown) OrElse (keyData = Keys.Tab) OrElse (keyData = Keys.Apps) OrElse (keyData = Keys.Delete) Then
            If keyData = Keys.Delete Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Tab Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Apps Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If Items.Count > 1 Then
                If keyData = Keys.Down Then
                    SelectItem(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Up Then
                    SelectItem(Math.Max(SelectedIndex(0) - 1, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 1, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Home Then
                    SelectItem(0) : EnsureVisibleAt(0) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.End Then
                    SelectItem(Items.Count - 1) : EnsureVisibleAt(Items.Count - 1) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageUp Then
                    SelectItem(Math.Max(SelectedIndex(0) - 14, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 14, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageDown Then
                    SelectItem(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
            End If : Return True
        Else
            Return MyBase.ProcessCmdKey(msg, keyData)
        End If
    End Function

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnClick(e)
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnDoubleClick(e)
        RaiseEvent DobleClick(Me, e, SelectedIndex(0))
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.F1 Or e.KeyCode = Keys.F2 Or e.KeyCode = Keys.F3 Or e.KeyCode = Keys.F4 Or e.KeyCode = Keys.F5 Or e.KeyCode = Keys.F6 Or e.KeyCode = Keys.F7 Or e.KeyCode = Keys.F8 Or e.KeyCode = Keys.F9 Or e.KeyCode = Keys.F10 Or e.KeyCode = Keys.F11 Or e.KeyCode = Keys.F12 Then Exit Sub
        Dim Repite As Boolean = True
        If SelectedIndex Is Nothing OrElse SelectedIndex.Length = 0 Then Exit Sub
        Dim OldIndex As Integer = SelectedIndex(0)
        Dim Index As Integer = SelectedIndex(0)
        If Keysearch = -1 Then Exit Sub
Repetir : For Cuenta = Keysearch To Items.Count - 1
            If Items(Cuenta).Text.ToLower.StartsWith(ChrW(e.KeyCode).ToString.ToLower) Then
                SelectItem(Cuenta) : EnsureVisibleAt(Cuenta) : Invalidate()
                Keysearch = Cuenta + 1
                If Keysearch = Items.Count Then Keysearch = 0
                Index = Cuenta
                GoTo Salta
            End If
        Next
        Keysearch = 0
        If Repite = True Then Repite = False : GoTo Repetir
Salta:
        InvalidateLayout()
        If OldIndex <> Index Then RaiseEvent SelectedIndexChanged(Me, Index)
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        InvalidateLayout()
        MyBase.OnSizeChanged(e)
    End Sub

    Public Sub Actualiza()
        Invalidate() : InvalidateColumns()
    End Sub
    Private Sub HandleScroll(sender As Object)
        Invalidate()
    End Sub

    Private Sub InvalidateScroll()
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 6
        If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight * 3
        If ShowColumn = True Then VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight) Else VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight * 2)
        Invalidate()
    End Sub

    Private Sub InvalidateLayout()
        VS.Location = New Point(Width - VS.Width - 1, 1) : VS.Size = New Size(18, Height - 2) : Invalidate()
    End Sub

    Private ColumnOffsets As Integer()
    Private Sub InvalidateColumns()
        Dim Width As Integer = 3 : ColumnOffsets = New Integer(_Columns.Count - 1) {}
        For I As Integer = 0 To _Columns.Count - 1
            ColumnOffsets(I) = Width : Width += Columns(I).Width
        Next : Invalidate()
    End Sub

    Private Sub VS_MouseDown(sender As Object, e As MouseEventArgs)
        Focus()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim pt As Point = PointToClient(Control.MousePosition)
        For i As Integer = 0 To Items.Count - 1
            If Items(i).Area.Contains(pt) Then
                HoveredItem = i
                GoTo Salta
            End If
        Next
        HoveredItem = -1
Salta:  MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Focus()
        If e.Button = Windows.Forms.MouseButtons.Left Or e.Button = Windows.Forms.MouseButtons.Right Then

            If Name = "Lista" AndAlso PointToClient(MousePosition).Y < 22 Then Exit Sub

            Dim Offset As Integer
            Dim Index As Integer
            If ShowColumn = True Then
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset - ItemHeight) \ ItemHeight)
            Else
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset) \ ItemHeight)
            End If
            If Index > _Items.Count - 1 Then Index = -1
            If Not Index = -1 Then 'TODO: Handle Shift key
                If ModifierKeys = Keys.Control AndAlso _MultiSelect Then
                    If _SelectedItems.Contains(_Items(Index)) Then _SelectedItems.Remove(_Items(Index)) Else _SelectedItems.Add(_Items(Index))
                Else
                    _SelectedItems.Clear() : _SelectedItems.Add(_Items(Index))
                End If
            Else
                _SelectedItems.Clear()
            End If
            If ModifierKeys = Keys.Control AndAlso _MultiSelect Then
                If _SelectedItems.Contains(_Items(Index)) Then _SelectedIndex.Remove(Index) Else _SelectedIndex.Add(Index)
            Else
                _SelectedIndex.Clear() : _SelectedIndex.Add(Index)
            End If
            Invalidate()
            RaiseEvent SelectedIndexChanged(Me, Index)
        End If
        MyBase.OnMouseDown(e)
    End Sub

    Private P1, P2, P3 As Pen : Private B1, B2, B3, B4 As SolidBrush : Private GB1 As Drawing2D.LinearGradientBrush
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit : G.Clear(BackColor)
        B1 = New SolidBrush(ItemBackColor1) : B2 = New SolidBrush(ItemBackColor2) : B3 = New SolidBrush(ItemBackColor3) : B4 = New SolidBrush(ItemBackColor4)
        Dim CFont As Font : Dim VSOffset As Integer : Dim X, Y As Integer : Dim H As Single
        Dim R1 As Rectangle : Dim CI As NSListViewItem
        If ShowColumn = True Then
            If (Items.Length * ItemHeight) + ItemHeight > Height Then
                VS.Visible = True : VSOffset = -VS.Width
            Else
                VS.Visible = False : VSOffset = 0
            End If
        Else
            If Items.Length * ItemHeight > Height Then
                VS.Visible = True : VSOffset = -VS.Width
            Else
                VS.Visible = False : VSOffset = 0
            End If
        End If
        G.DrawRectangle(P1, 1, 1, Width - 3, Height - 3)
        Dim Offset As Integer = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2))))
        Dim StartIndex As Integer : If Offset = 0 Then StartIndex = 0 Else StartIndex = (Offset \ ItemHeight)
        Dim EndIndex As Integer = Math.Min(StartIndex + (Height \ ItemHeight), Items.Count - 1)
        If EndIndex < Items.Count - 1 Then EndIndex += 1
        If Items.Count = 0 Then GoTo Salta
        For I As Integer = StartIndex To EndIndex
            CI = Items(I)
            If ShowColumn = True Then R1 = New Rectangle(0, ItemHeight + (I * ItemHeight) + 1 - Offset, Width, ItemHeight - 1) Else R1 = New Rectangle(0, (ItemHeight + (I * ItemHeight) + 1 - Offset) - ItemHeight, Width, ItemHeight - 1)
            Items(I).Area = R1 : H = G.MeasureString(CI.Text, Font).Height : Y = R1.Y + CInt((ItemHeight / 2) - (H / 2))
            VS.SmallChange = (R1.Height - 1) * 2 : VS.LargeChange = R1.Height * 5
            If SelectedItems.Contains(CI) Then
                If I Mod 2 = 0 Then
                    G.FillRectangle(B1, R1)
                Else
                    'G.FillRectangle(B1, R1)
                    G.FillRectangle(B2, R1)
                End If
            Else
                If I Mod 2 = 0 Then
                    G.FillRectangle(B3, R1)
                Else
                    'G.FillRectangle(B3, R1)
                    G.FillRectangle(B4, R1)
                End If
            End If
            G.DrawLine(P2, 0, R1.Bottom, Width, R1.Bottom)
            If Columns.Length > 0 Then R1.Width = Columns(0).Width : G.SetClip(R1)
            CFont = Font
Repite1:    Dim SZ1 = G.MeasureString(CI.Text, CFont)
            If SZ1.Width > (Columns(0).Width + VSOffset) And CFont.Size > 7 Then
                CFont = New Font(CFont.FontFamily, CFont.Size - 1)
                GoTo Repite1
            End If
            If Name = "Lista" AndAlso (ShowColumn = False Or I > 0) Then
                CFont = Font
Repite2:        Dim SZ2 = G.MeasureString(CI.Text, CFont)
                If SZ2.Width > (Columns(0).Width + VSOffset - 41) And CFont.Size > 6 Then
                    CFont = New Font(CFont.FontFamily, CFont.Size - 1) : GoTo Repite2
                End If
            End If
            If Name = "Lista" AndAlso (ShowColumn = False Or I > 0) Then
                Dim TempText As String = CI.Text
                Dim SZ2 = G.MeasureString(TempText, CFont)
                If SZ2.Width > (Columns(0).Width + VSOffset - 41) Then
Repite3:            SZ2 = G.MeasureString(TempText + "...", CFont)
                    If SZ2.Width > (Columns(0).Width + VSOffset - 41) Then
                        TempText = TempText.Substring(0, TempText.Length - 1)
                        GoTo Repite3
                    End If
                    CI.Text = TempText + "..."
                End If
            End If
            Dim Sumar As Integer = 1
            If SelectedItems.Contains(CI) Then 'Or I = HoveredItem Then
                If CI.Text = "_______________________" Then
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y - 5)
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y - 6)
                Else
                    If Name = "Lista" Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 40 + 1, Y + 1 + Sumar + (((ItemHeight / 2) - G.MeasureString(CI.Text, CFont).Height) / 2))
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 40, Y + Sumar + (((ItemHeight / 2) - G.MeasureString(CI.Text, CFont).Height) / 2))
                    Else
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y)
                    End If
                End If
            Else
                If CI.Text = "_______________________" Then
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y - 5)
                    If CI.Marked = True Then G.DrawString(CI.Text, CFont, New SolidBrush(CI.MarkedColor), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y - 6) Else G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y)
                Else
                    If Name = "Lista" Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 40 + 1, Y + 1 + Sumar + (((ItemHeight / 2) - G.MeasureString(CI.Text, CFont).Height) / 2))
                        If CI.Marked = True Then G.DrawString(CI.Text, CFont, New SolidBrush(CI.MarkedColor), 40, Y + Sumar + (((ItemHeight / 2) - G.MeasureString(CI.Text, CFont).Height) / 2)) Else G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 40, Y + Sumar + (((ItemHeight / 2) - G.MeasureString(CI.Text, CFont).Height) / 2))
                    Else
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y + 1)
                        If CI.Marked = True Then G.DrawString(CI.Text, CFont, New SolidBrush(CI.MarkedColor), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y) Else G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y)
                    End If
                End If
            End If
            If CI.Image IsNot Nothing Then e.Graphics.DrawImage(CI.Image, New Rectangle(10, Y - 2, 18, 18))
            If CI.SubItems IsNot Nothing Then
                For I2 As Integer = 0 To Math.Min(CI.SubItems.Count, Columns.Count) - 1
                    X = ColumnOffsets(I2 + 1) + 4
                    R1.X = X
                    R1.Width = Columns(I2 + 1).Width
                    G.SetClip(R1)
                    CFont = Font
                    SZ1 = G.MeasureString(CI.SubItems(I2).Text, CFont)
Repite4:            If SZ1.Width > R1.Width And CFont.Size > 7 Then
                        CFont = New Font(Font.FontFamily, CFont.Size - 1)
                        GoTo Repite4
                    ElseIf SZ1.Width > R1.Width Then
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), X, Y)
                        End If
                    Else
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        End If
                    End If
                Next
            End If
            G.ResetClip()
        Next
Salta:        'Marca-----------------------------------------------
        If ShowColumn = True Then
            R1 = New Rectangle(0, 0, Width, ItemHeight) : GB1 = New Drawing2D.LinearGradientBrush(R1, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)
            G.FillRectangle(GB1, R1) : G.DrawRectangle(P3, 1, 1, Width + VSOffset, ItemHeight - 2)
        End If
        'Marca-----------------------------------------------
        Dim LH As Integer = Math.Min(VS.Maximum + (ItemHeight * 2) + ItemHeight - Offset, Height)
        'Marca-----------------------------------------------
        If ShowColumn = True Then
            Dim CC As NSListViewColumnHeader
            For I As Integer = 0 To _Columns.Count - 1
                CC = Columns(I)
                H = G.MeasureString(CC.Text, ColumnFont).Height
                Y = CInt((ItemHeight / 2) - (H / 2))
                X = ColumnOffsets(I)
                CFont = ColumnFont
Repite5:        Dim SZ1 = G.MeasureString(CC.Text, CFont)
                If SZ1.Width > (Columns(0).Width + VSOffset - 40) And CFont.Size > 4 Then
                    CFont = New Font(CFont.FontFamily, CFont.Size - 1)
                    GoTo Repite5
                End If
                Dim Sumar As Integer = 2 : If CFont.Size < 7 Then Sumar += 1
                SZ1 = G.MeasureString(CC.Text, CFont)
                G.DrawString(CC.Text, CFont, New SolidBrush(ColumnColor2), ((Columns(I).Width \ 2 - SZ1.Width / 2) + (X + 1)) + (VSOffset / 2), Y + 1 + Sumar + (((ItemHeight / 2) - G.MeasureString(CC.Text, CFont).Height) / 2))
                G.DrawString(CC.Text, CFont, New SolidBrush(ColumnColor1), ((Columns(I).Width \ 2 - SZ1.Width / 2) + X) + (VSOffset / 2), Y + Sumar + (((ItemHeight / 2) - G.MeasureString(CC.Text, CFont).Height) / 2))
                G.DrawLine(P2, X - 3, 0, X - 3, LH)
                G.DrawLine(P3, X - 2, 0, X - 2, ItemHeight)
            Next
            If Name = "Lista" Then
                If VSOffset < 0 Then FMenu.Lista_POculto.Left = 169 Else FMenu.Lista_POculto.Left = 188
            End If
        End If
        'Marca-----------------------------------------------
        G.DrawRectangle(P2, 0, 0, Width - 1, Height - 1)
        'Marca-----------------------------------------------
        If ShowColumn = True Then G.DrawLine(P2, 0, ItemHeight, Width, ItemHeight)
        'Marca-----------------------------------------------
        If VS.Visible = True Then G.DrawLine(P2, VS.Location.X - 1, 0, VS.Location.X - 1, Height)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        InvalidateScroll()
        Dim Move As Integer = -((e.Delta * SystemInformation.MouseWheelScrollLines \ 120) * (ItemHeight \ 2))
        Dim Value As Integer = Math.Max(Math.Min(VS.Value + Move, VS.Maximum), VS.Minimum) : VS.Value = Value
        MyBase.OnMouseWheel(e)
    End Sub

#End Region

End Class

#End Region

#Region "ListViewSK"

Class NSListViewSK : Inherits Control

    Public HoveredItem As Integer = -1
    Dim Keysearch As Integer
    Event SelectedIndexChanged(sender As Object, Indice As Integer)
    Event DobleClick(sender As Object, e As EventArgs, Indice As Integer)

#Region "Item"

    Class NSListViewItemSK

        Property Tipo As TipoEN
        Property Tecla As String
        Property Text As String
        Property Tag As Object
        Property Tag2 As Object
        Property Marked As Boolean
        Property MarkedColor As Color = Color.SteelBlue
        Property Area As Rectangle
        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Property SubItems As New List(Of NSListViewSubItem)
        Protected UniqueId As Guid

        Enum TipoEN
            EsperarEjecucion
            KeyDown
            KeyUp
            MouseDown
            MouseUp
            MouseWheelDown
            MouseWheelUp
            MouseHWheelDown
            MouseHWheelUp
            MouseMove
            MouseMoveR
        End Enum

        Sub New()
            UniqueId = Guid.NewGuid()
        End Sub

        Public Overrides Function ToString() As String
            Return Text
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If TypeOf obj Is NSListViewItemSK Then Return (DirectCast(obj, NSListViewItemSK).UniqueId = UniqueId)
            Return False
        End Function

    End Class

#End Region

#Region "SubItem"

    Class NSListViewSubItem

        Property Text As String

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "ColumnHeader"

    Class NSListViewColumnHeader

        Property Text As String
        Property Width As Integer = 60

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "Properties"

    Private _Items As New List(Of NSListViewItemSK)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Items() As NSListViewItemSK()
        Get
            Return _Items.ToArray()
        End Get
        Set(value As NSListViewItemSK())
            _Items = New List(Of NSListViewItemSK)(value)
            InvalidateScroll()
        End Set
    End Property

    Private _SelectedItems As New List(Of NSListViewItemSK)
    Public ReadOnly Property SelectedItems() As NSListViewItemSK()
        Get
            Return _SelectedItems.ToArray()
        End Get
    End Property

    Private _SelectedIndex As New List(Of Integer)
    Public ReadOnly Property SelectedIndex() As Integer()
        Get
            Return _SelectedIndex.ToArray
        End Get
    End Property

    Private _Columns As New List(Of NSListViewColumnHeader)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Columns() As NSListViewColumnHeader()
        Get
            Return _Columns.ToArray()
        End Get
        Set(value As NSListViewColumnHeader())
            _Columns = New List(Of NSListViewColumnHeader)(value)
            InvalidateColumns()
        End Set
    End Property

    Private _MultiSelect As Boolean = True
    Public Property MultiSelect() As Boolean
        Get
            Return _MultiSelect
        End Get
        Set(value As Boolean)
            _MultiSelect = value
            If value = False Then If _SelectedItems.Count > 1 Then _SelectedItems.RemoveRange(1, _SelectedItems.Count - 1)
            Invalidate()
        End Set
    End Property

    Private _ShowColumn As Boolean = True
    Public Property ShowColumn() As Boolean
        Get
            Return _ShowColumn
        End Get
        Set(value As Boolean)
            _ShowColumn = value
            Invalidate()
        End Set
    End Property

    <ComponentModel.Browsable(False)> Public ItemHeight As Integer = 24
    Public Overrides Property Font As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)
            MyBase.Font = value
            InvalidateLayout()
        End Set
    End Property

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.White
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor3 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor3 As Color
        Get
            Return _ForeColor3
        End Get
        Set(value As Color)
            _ForeColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor1 As Color = Color.White
    Property ColumnColor1 As Color
        Get
            Return _ColumnColor1
        End Get
        Set(value As Color)
            _ColumnColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor2 As Color = Color.Black
    Property ColumnColor2 As Color
        Get
            Return _ColumnColor2
        End Get
        Set(value As Color)
            _ColumnColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnFont As Font = Font
    Property ColumnFont As Font
        Get
            Return _ColumnFont
        End Get
        Set(value As Font)
            _ColumnFont = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor1 As Color = Color.FromArgb(62, 62, 62)
    Property ItemBackColor1 As Color
        Get
            Return _ItemBackColor1
        End Get
        Set(value As Color)
            _ItemBackColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor2 As Color = Color.FromArgb(65, 65, 65)
    Property ItemBackColor2 As Color
        Get
            Return _ItemBackColor2
        End Get
        Set(value As Color)
            _ItemBackColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor3 As Color = Color.FromArgb(47, 47, 47)
    Property ItemBackColor3 As Color
        Get
            Return _ItemBackColor3
        End Get
        Set(value As Color)
            _ItemBackColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor4 As Color = Color.FromArgb(50, 50, 50)
    Property ItemBackColor4 As Color
        Get
            Return _ItemBackColor4
        End Get
        Set(value As Color)
            _ItemBackColor4 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region "Item Helper"

    Public Sub AddItem(cItems() As NSListViewItemSK)
        For Each Item As NSListViewItemSK In cItems
            AddItem(Item.Text, Item.Tag) : _Items.Item(_Items.Count - 1).Tipo = Item.Tipo
        Next
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub AddItem(Text As String, Tecla As String, Tipo As NSListViewItemSK.TipoEN, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItemSK()
        Item.Text = Text
        Item.SubItems = Items
        Item.Tag = Tecla
        Item.Tipo = Tipo

        _Items.Add(Item)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub AddItem(text As String, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItemSK()
        Item.Text = text
        Item.SubItems = Items

        _Items.Add(Item)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub AddItem(text As String, ITag As Object, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItemSK()
        Item.Text = text
        Item.SubItems = Items
        Item.Tag = ITag

        _Items.Add(Item)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub Clear()
        _Items.Clear()
        Selecting = False : SelectingReset = True : SelectingIndex = -1
        InvalidateScroll()
    End Sub

    Public Sub ClearSelected()
        _SelectedIndex.Clear()
        _SelectedItems.Clear()
        Selecting = False : SelectingReset = True : SelectingIndex = -1
        InvalidateScroll()
    End Sub

    Public Sub ClearSelectedIndex()
        _SelectedIndex.Clear()
        Selecting = False : SelectingReset = True : SelectingIndex = -1
        InvalidateScroll()
    End Sub

    Public Sub ClearSelectedItems()
        _SelectedItems.Clear()
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub


    Public Sub RemoveItemAt(index As Integer)
        If index = -1 Then Exit Sub
        _Items.RemoveAt(index)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub RemoveItem(item As NSListViewItemSK)
        _Items.Remove(item)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub RemoveItems(items As NSListViewItemSK())
        For Each I As NSListViewItemSK In items
            _Items.Remove(I)
        Next
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub InsertItemAt(pos As Integer, item As NSListViewItemSK)
        _Items.Insert(pos, item)
        Selecting = False : SelectingReset = True
        InvalidateScroll()
    End Sub

    Public Sub SelectItem(pos As Integer)
        _SelectedIndex.Clear()
        _SelectedItems.Clear()
        _SelectedIndex.Add(pos)
        If pos <> -1 Then _SelectedItems.Add(_Items(pos))
        Selecting = False : SelectingReset = True
        RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0))
    End Sub

    Public Sub EnsureVisibleAt(pos As Integer)
        'ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 2
        ''Dim Valor As Integer = 16
        If pos = 0 Then VS.Value = VS.Minimum : InvalidateLayout() : Exit Sub
        If pos = Items.Count - 1 Then VS.Value = VS.Maximum : InvalidateLayout() : Exit Sub
        VS.Value = ItemHeight * pos
        'If pos < CInt(Height / ItemHeight) Then VS.Value = 0 : InvalidateLayout() : Exit Sub
        'If pos > CInt(_Items.Count - CInt(Height / ItemHeight)) Then VS.Value = VS.Maximum : InvalidateLayout() : Exit Sub
        'VS.Value = CInt((pos * VS.Maximum) / _Items.Count)
        ''If pos * Valor < VS.Value Then
        ''    VS.Value = Math.Max(pos * Valor, VS.Minimum)
        ''    If VS.Value < Valor Then VS.Value = VS.Minimum
        ''ElseIf pos * Valor > (VS.Value + Height) - (Valor * 2) Then
        ''    VS.Value = Math.Min(((pos * Valor) - (Height) + 5), VS.Maximum)
        ''    If VS.Value > VS.Maximum - Valor Then VS.Value = VS.Maximum
        ''End If
        InvalidateLayout()
    End Sub

#End Region

#Region "Methods"

    Private VS As NSVScrollBar

    Sub New()
        SetStyle(139286, True) : SetStyle(ControlStyles.Selectable, True)
        P1 = New Pen(Color.FromArgb(55, 55, 55)) : P2 = New Pen(Color.FromArgb(35, 35, 35)) : P3 = New Pen(Color.FromArgb(65, 65, 65))
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 2
        VS = New NSVScrollBar : If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight
        AddHandler VS.Scroll, AddressOf HandleScroll : AddHandler VS.MouseDown, AddressOf VS_MouseDown : Controls.Add(VS)
        InvalidateLayout()
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If Focused = False Then Return MyBase.ProcessCmdKey(msg, keyData)
        If (keyData = Keys.Right) OrElse (keyData = Keys.Left) OrElse (keyData = Keys.Up) OrElse (keyData = Keys.Down) OrElse (keyData = Keys.Home) OrElse (keyData = Keys.End) OrElse (keyData = Keys.PageUp) OrElse (keyData = Keys.PageDown) OrElse (keyData = Keys.Tab) OrElse (keyData = Keys.Apps) OrElse (keyData = Keys.Delete) Then
            If keyData = Keys.Delete Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Tab Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Apps Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If Items.Count > 1 Then
                If keyData = Keys.Down Then
                    SelectItem(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Up Then
                    SelectItem(Math.Max(SelectedIndex(0) - 1, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 1, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Home Then
                    SelectItem(0) : EnsureVisibleAt(0) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.End Then
                    SelectItem(Items.Count - 1) : EnsureVisibleAt(Items.Count - 1) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageUp Then
                    SelectItem(Math.Max(SelectedIndex(0) - 14, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 14, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageDown Then
                    SelectItem(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
            End If : Return True
        Else
            Return MyBase.ProcessCmdKey(msg, keyData)
        End If
    End Function

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnClick(e)
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnDoubleClick(e)
        RaiseEvent DobleClick(Me, e, SelectedIndex(0))
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.F1 Or e.KeyCode = Keys.F2 Or e.KeyCode = Keys.F3 Or e.KeyCode = Keys.F4 Or e.KeyCode = Keys.F5 Or e.KeyCode = Keys.F6 Or e.KeyCode = Keys.F7 Or e.KeyCode = Keys.F8 Or e.KeyCode = Keys.F9 Or e.KeyCode = Keys.F10 Or e.KeyCode = Keys.F11 Or e.KeyCode = Keys.F12 Then Exit Sub
        Dim Repite As Boolean = True
        Dim OldIndex As Integer = SelectedIndex(0)
        Dim Index As Integer = SelectedIndex(0)
        If Keysearch = -1 Then Exit Sub
Repetir: For Cuenta = Keysearch To Items.Count - 1
            If Items(Cuenta).Text.ToLower.StartsWith(ChrW(e.KeyCode).ToString.ToLower) Then
                SelectItem(Cuenta) : EnsureVisibleAt(Cuenta) : Invalidate()
                Keysearch = Cuenta + 1
                If Keysearch = Items.Count Then Keysearch = 0
                Index = Cuenta
                GoTo Salta
            End If
        Next
        Keysearch = 0
        If Repite = True Then Repite = False : GoTo Repetir
Salta:
        InvalidateLayout()
        If OldIndex <> Index Then RaiseEvent SelectedIndexChanged(Me, Index)
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        InvalidateLayout()
        MyBase.OnSizeChanged(e)
    End Sub

    Public Sub Actualiza()
        Invalidate() : InvalidateColumns()
    End Sub
    Private Sub HandleScroll(sender As Object)
        Invalidate()
    End Sub

    Private Sub InvalidateScroll()
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 2
        If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight * 3
        If ShowColumn = True Then VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight) Else VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight * 2)
        Invalidate()
    End Sub

    Private Sub InvalidateLayout()
        VS.Location = New Point(Width - VS.Width - 1, 1) : VS.Size = New Size(18, Height - 2) : Invalidate()
    End Sub

    Private ColumnOffsets As Integer()
    Private Sub InvalidateColumns()
        Dim Width As Integer = 3 : ColumnOffsets = New Integer(_Columns.Count - 1) {}
        For I As Integer = 0 To _Columns.Count - 1
            ColumnOffsets(I) = Width : Width += Columns(I).Width
        Next : Invalidate()
    End Sub

    Private Sub VS_MouseDown(sender As Object, e As MouseEventArgs)
        Focus()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim pt As Point = PointToClient(Control.MousePosition)
        For i As Integer = 0 To Items.Count - 1
            If Items(i).Area.Contains(pt) Then
                HoveredItem = i
                GoTo Salta
            End If
        Next
        HoveredItem = -1
Salta:  MyBase.OnMouseMove(e)
    End Sub

    Private Selecting, SelectingReset As Boolean
    Private SelectingIndex As Integer

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Focus() : If e.Button = MouseButtons.Left Or e.Button = MouseButtons.Right Then
            Dim Offset As Integer
            Dim Index As Integer
            If ShowColumn = True Then
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset - ItemHeight) \ ItemHeight)
            Else
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset) \ ItemHeight)
            End If
            If Index > _Items.Count - 1 Then Index = -1
            If Not Index = -1 Then
                If ModifierKeys = Keys.Shift AndAlso _MultiSelect AndAlso e.Button = MouseButtons.Left Then
                    If Selecting = True And SelectingReset = True Then Selecting = True : SelectingReset = False : SelectingIndex = -1 : _SelectedIndex.Clear() : _SelectedIndex.Add(Index) : _SelectedItems.Clear() : _SelectedItems.Add(_Items(Index)) : GoTo Salta
                    Selecting = True : SelectingReset = False
                    If Not SelectingIndex = -1 Then
                        _SelectedItems.Clear() : _SelectedIndex.Clear()
                        If SelectingIndex < Index Then
                            For Cuenta = Index To SelectingIndex Step -1
                                _SelectedItems.Add(_Items(Cuenta))
                                _SelectedIndex.Add(Cuenta)
                            Next
                        Else
                            For Cuenta = Index To SelectingIndex
                                _SelectedItems.Add(_Items(Cuenta))
                                _SelectedIndex.Add(Cuenta)
                            Next
                        End If
                    End If
                    GoTo Salta
                ElseIf ModifierKeys = Keys.Control AndAlso _MultiSelect AndAlso e.Button = MouseButtons.Left Then
                    If _SelectedItems.Contains(_Items(Index)) Then
                        _SelectedItems.Remove(_Items(Index))
                        _SelectedIndex.Remove(Index)
                    Else
                        _SelectedItems.Add(_Items(Index))
                        _SelectedIndex.Add(Index)
                    End If
                ElseIf _SelectedItems.Contains(_Items(Index)) AndAlso _MultiSelect AndAlso e.Button = MouseButtons.Right Then
                    Invalidate() : Exit Sub
                Else
                    _SelectedItems.Clear() : _SelectedIndex.Clear() : _SelectedItems.Add(_Items(Index)) : _SelectedIndex.Add(Index) : SelectingIndex = Index
                End If
            ElseIf ModifierKeys = Keys.Shift AndAlso _MultiSelect Then
                Invalidate() : Exit Sub
            ElseIf ModifierKeys = Keys.Control AndAlso _MultiSelect Then
                Invalidate() : Exit Sub
            Else
                SelectingIndex = Index : _SelectedItems.Clear()
            End If
            Selecting = False : SelectingReset = True : SelectingIndex = Index : _SelectedIndex.Clear() : _SelectedIndex.Add(Index)
Salta:      Invalidate() : RaiseEvent SelectedIndexChanged(Me, Index)
        End If : MyBase.OnMouseDown(e)
    End Sub

    Private P1, P2, P3 As Pen : Private B1, B2, B3, B4 As SolidBrush : Private GB1 As Drawing2D.LinearGradientBrush
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim ItemLeft As Integer = 23 : Dim ItemLeftS As Integer = ItemLeft + 1
        G = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit : G.Clear(BackColor)
        B1 = New SolidBrush(ItemBackColor1) : B2 = New SolidBrush(ItemBackColor2) : B3 = New SolidBrush(ItemBackColor3) : B4 = New SolidBrush(ItemBackColor4)
        Dim CFont As Font : Dim VSOffset As Integer : Dim X, Y As Integer : Dim H As Single
        Dim R1 As Rectangle : Dim CI As NSListViewItemSK
        If ShowColumn = True Then
            If (Items.Length * ItemHeight) + ItemHeight > Height Then VS.Visible = True : VSOffset = -VS.Width Else VS.Visible = False : VSOffset = 0
        Else
            If Items.Length * ItemHeight > Height Then VS.Visible = True : VSOffset = -VS.Width Else VS.Visible = False : VSOffset = 0
        End If
        G.DrawRectangle(P1, 1, 1, Width - 3, Height - 3)
        VS.BackColor = Color.FromArgb(50, 50, 50)
        Dim Offset As Integer = VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))
        Dim StartIndex As Integer : If Offset = 0 Then StartIndex = 0 Else StartIndex = (Offset \ ItemHeight)
        Dim EndIndex As Integer = Math.Min(StartIndex + (Height \ ItemHeight), Items.Count - 1)
        If EndIndex < Items.Count - 1 Then EndIndex += 1
        If Items.Count = 0 Then GoTo Salta
        For I As Integer = StartIndex To EndIndex
            CI = Items(I)
            If ShowColumn = True Then R1 = New Rectangle(0, ItemHeight + (I * ItemHeight) + 1 - Offset, Width, ItemHeight - 1) Else R1 = New Rectangle(0, (ItemHeight + (I * ItemHeight) + 1 - Offset) - ItemHeight, Width, ItemHeight - 1)
            Items(I).Area = R1 : H = G.MeasureString(CI.Text, Font).Height : Y = R1.Y + CInt((ItemHeight / 2) - (H / 2))
            VS.SmallChange = (R1.Height - 1) * 2 : VS.LargeChange = R1.Height * 5
            If SelectedItems.Contains(CI) Then
                If I Mod 2 = 0 Then
                    G.FillRectangle(B1, R1)
                Else
                    'G.FillRectangle(B1, R1)
                    G.FillRectangle(B2, R1)
                End If
            Else
                If I Mod 2 = 0 Then
                    G.FillRectangle(B3, R1)
                Else
                    'G.FillRectangle(B3, R1)
                    G.FillRectangle(B4, R1)
                End If
            End If
            'G.DrawLine(P2, 0, R1.Bottom, Width, R1.Bottom)
            If Columns.Length > 0 Then R1.Width = Columns(0).Width : G.SetClip(R1)
            CFont = Font
            If SelectedItems.Contains(CI) Then
                Select Case CI.Tipo
                    Case NSListViewItemSK.TipoEN.KeyDown
                        e.Graphics.DrawImage(My.Resources.Macro_Down, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.KeyUp
                        e.Graphics.DrawImage(My.Resources.Macro_Up, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.EsperarEjecucion
                        e.Graphics.DrawImage(My.Resources.Macro_Espera, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseDown
                        e.Graphics.DrawImage(My.Resources.Macro_LClick, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseUp
                        e.Graphics.DrawImage(My.Resources.Macro_RClick, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseWheelDown, NSListViewItemSK.TipoEN.MouseWheelUp
                        e.Graphics.DrawImage(My.Resources.Macro_VWheel, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseHWheelDown, NSListViewItemSK.TipoEN.MouseHWheelUp
                        e.Graphics.DrawImage(My.Resources.Macro_HWheel, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseMove
                        e.Graphics.DrawImage(My.Resources.Macro_Move, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseMoveR
                        e.Graphics.DrawImage(My.Resources.Macro_Mover, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                End Select
            Else
                Select Case CI.Tipo
                    Case NSListViewItemSK.TipoEN.KeyDown
                        e.Graphics.DrawImage(My.Resources.Macro_Down_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.KeyUp
                        e.Graphics.DrawImage(My.Resources.Macro_Up_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.EsperarEjecucion
                        e.Graphics.DrawImage(My.Resources.Macro_Espera_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseDown
                        e.Graphics.DrawImage(My.Resources.Macro_LClick_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseUp
                        e.Graphics.DrawImage(My.Resources.Macro_RClick_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseWheelDown, NSListViewItemSK.TipoEN.MouseWheelUp
                        e.Graphics.DrawImage(My.Resources.Macro_VWheel_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseHWheelDown, NSListViewItemSK.TipoEN.MouseHWheelUp
                        e.Graphics.DrawImage(My.Resources.Macro_HWheel_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseMove
                        e.Graphics.DrawImage(My.Resources.Macro_Move_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                    Case NSListViewItemSK.TipoEN.MouseMoveR
                        e.Graphics.DrawImage(My.Resources.Macro_MoveR_Off, New Rectangle(7, CInt(Y + (ItemHeight / 2)) - 6, 10, 10))
                End Select
            End If
Repite1:    Dim SZ1 = G.MeasureString(CI.Text, CFont)
            If SZ1.Width > (Columns(0).Width + VSOffset) And CFont.Size > 7 Then
                CFont = New Font(CFont.FontFamily, CFont.Size - 1)
                GoTo Repite1
            ElseIf SZ1.Width > (Columns(0).Width + VSOffset) Then
                If SelectedItems.Contains(CI) Then 'Or I = HoveredItem Then
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ItemLeftS, Y + 1)
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), ItemLeft, Y)
                Else
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ItemLeftS, Y + 1)
                    If CI.Marked = True Then G.DrawString(CI.Text, CFont, New SolidBrush(CI.MarkedColor), ItemLeft, Y) Else G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), ItemLeft, Y)
                End If
            Else
                If SelectedItems.Contains(CI) Then 'Or I = HoveredItem Then
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ItemLeftS, Y + 1)
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), ItemLeft, Y)
                Else
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ItemLeftS, Y + 1)
                    If CI.Marked = True Then G.DrawString(CI.Text, CFont, New SolidBrush(CI.MarkedColor), ItemLeft, Y) Else G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), ItemLeft, Y)
                End If
            End If
            If CI.SubItems IsNot Nothing Then
                For I2 As Integer = 0 To Math.Min(CI.SubItems.Count, Columns.Count) - 1
                    X = ColumnOffsets(I2 + 1) + 4
                    R1.X = X
                    R1.Width = Columns(I2 + 1).Width
                    G.SetClip(R1)
                    CFont = Font
                    SZ1 = G.MeasureString(CI.SubItems(I2).Text, CFont)
Repite2:            If SZ1.Width > R1.Width And CFont.Size > 7 Then
                        CFont = New Font(Font.FontFamily, CFont.Size - 1)
                        GoTo Repite2
                    ElseIf SZ1.Width > R1.Width Then
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), X, Y)
                        End If
                    Else
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        End If
                    End If
                Next
            End If
            G.ResetClip()
        Next
Salta:        'Marca-----------------------------------------------
        If ShowColumn = True Then
            R1 = New Rectangle(0, 0, Width, ItemHeight) : GB1 = New Drawing2D.LinearGradientBrush(R1, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)
            G.FillRectangle(GB1, R1) : G.DrawRectangle(P3, 1, 1, Width + VSOffset, ItemHeight - 2)
        End If
        'Marca-----------------------------------------------
        Dim LH As Integer = Math.Min(VS.Maximum + (ItemHeight * 2) + ItemHeight - Offset, Height)
        'Marca-----------------------------------------------
        If ShowColumn = True Then
            Dim CC As NSListViewColumnHeader
            For I As Integer = 0 To _Columns.Count - 1
                CC = Columns(I)
                H = G.MeasureString(CC.Text, ColumnFont).Height
                Y = CInt((ItemHeight / 2) - (H / 2))
                X = ColumnOffsets(I)
                Dim SZ1 = G.MeasureString(CC.Text, ColumnFont)
                G.DrawString(CC.Text, ColumnFont, New SolidBrush(ColumnColor2), (X + 1), Y + 1)
                G.DrawString(CC.Text, ColumnFont, New SolidBrush(ColumnColor1), X, Y)
                G.DrawLine(P2, X - 3, 0, X - 3, LH)
                G.DrawLine(P3, X - 2, 0, X - 2, ItemHeight)
            Next
        End If
        'Marca-----------------------------------------------
        G.DrawRectangle(P2, 0, 0, Width - 1, Height - 1)
        'Marca-----------------------------------------------
        If ShowColumn = True Then G.DrawLine(P2, 0, ItemHeight, Width, ItemHeight)
        'Marca-----------------------------------------------
        If VS.Visible = True Then G.DrawLine(P2, VS.Location.X - 1, 0, VS.Location.X - 1, Height)
        G.DrawRectangle(P1, 1, 1, Width - 3, Height - 3)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        InvalidateScroll()
        Dim Move As Integer = -((e.Delta * SystemInformation.MouseWheelScrollLines \ 120) * (ItemHeight \ 2))
        Dim Value As Integer = Math.Max(Math.Min(VS.Value + Move, VS.Maximum), VS.Minimum) : VS.Value = Value
        MyBase.OnMouseWheel(e)
    End Sub

#End Region

End Class

#End Region

#Region "ListViewHLP"

Class NSListViewHLP : Inherits Control

    Public HoveredItem As Integer = -1
    Dim Keysearch As Integer
    Event SelectedIndexChanged(sender As Object, Indice As Integer)
    Event DobleClick(sender As Object, e As EventArgs, Indice As Integer)

#Region "Item"

    Class NSListViewItem

        Property Text As String
        Property Tag As Object
        Property Tag2 As Object
        Property Principal As Boolean
        Property Secundario As Boolean
        Property SecundarioChild As Boolean
        Property IgnorarIcono As Boolean
        Property Opened As Boolean
        Property Marked As Boolean
        Property MarkedColor As Color = Color.SteelBlue
        Property Area As Rectangle
        Property Imagen As Image
        Property ImagenPNG As Boolean
        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Property SubItems As New List(Of NSListViewSubItem)
        Protected UniqueId As Guid

        Sub New()
            UniqueId = Guid.NewGuid()
        End Sub

        Public Overrides Function ToString() As String
            Return Text
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If TypeOf obj Is NSListViewItem Then Return (DirectCast(obj, NSListViewItem).UniqueId = UniqueId)
            Return False
        End Function

    End Class

#End Region

#Region "SubItem"

    Class NSListViewSubItem

        Property Text As String

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "ColumnHeader"

    Class NSListViewColumnHeader

        Property Text As String
        Property Width As Integer = 60

        Public Overrides Function ToString() As String
            Return Text
        End Function

    End Class

#End Region

#Region "Properties"

    Private _Items As New List(Of NSListViewItem)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Items() As NSListViewItem()
        Get
            Return _Items.ToArray()
        End Get
        Set(value As NSListViewItem())
            _Items = New List(Of NSListViewItem)(value)
            InvalidateScroll()
        End Set
    End Property

    Private _SelectedItems As New List(Of NSListViewItem)
    Public ReadOnly Property SelectedItems() As NSListViewItem()
        Get
            Return _SelectedItems.ToArray()
        End Get
    End Property

    Private _SelectedIndex As New List(Of Integer)
    Public ReadOnly Property SelectedIndex() As Integer()
        Get
            Return _SelectedIndex.ToArray
        End Get
    End Property

    Private _Columns As New List(Of NSListViewColumnHeader)
    <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Content)> Public Property Columns() As NSListViewColumnHeader()
        Get
            Return _Columns.ToArray()
        End Get
        Set(value As NSListViewColumnHeader())
            _Columns = New List(Of NSListViewColumnHeader)(value)
            InvalidateColumns()
        End Set
    End Property

    Private _MultiSelect As Boolean = True
    Public Property MultiSelect() As Boolean
        Get
            Return _MultiSelect
        End Get
        Set(value As Boolean)
            _MultiSelect = value

            If _SelectedItems.Count > 1 Then
                _SelectedItems.RemoveRange(1, _SelectedItems.Count - 1)
            End If

            Invalidate()
        End Set
    End Property

    Private _ShowColumn As Boolean = True
    Public Property ShowColumn() As Boolean
        Get
            Return _ShowColumn
        End Get
        Set(value As Boolean)
            _ShowColumn = value
            Invalidate()
        End Set
    End Property

    <ComponentModel.Browsable(False)> Public ItemHeight As Integer = 24
    Public Overrides Property Font As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)


            MyBase.Font = value
            InvalidateLayout()
        End Set
    End Property

    Private _ForeColor As Color = Color.Empty
    <ComponentModel.Browsable(False)> Overrides Property ForeColor As Color
        Get
            Return _ForeColor
        End Get
        Set(value As Color)
            _ForeColor = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor1 As Color = Color.White
    Property ForeColor1 As Color
        Get
            Return _ForeColor1
        End Get
        Set(value As Color)
            _ForeColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor2 As Color = Color.Black
    Property ForeColor2 As Color
        Get
            Return _ForeColor2
        End Get
        Set(value As Color)
            _ForeColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ForeColor3 As Color = Color.FromArgb(205, 150, 0)
    Property ForeColor3 As Color
        Get
            Return _ForeColor3
        End Get
        Set(value As Color)
            _ForeColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor1 As Color = Color.White
    Property ColumnColor1 As Color
        Get
            Return _ColumnColor1
        End Get
        Set(value As Color)
            _ColumnColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnColor2 As Color = Color.Black
    Property ColumnColor2 As Color
        Get
            Return _ColumnColor2
        End Get
        Set(value As Color)
            _ColumnColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ColumnFont As Font = Font
    Property ColumnFont As Font
        Get
            Return _ColumnFont
        End Get
        Set(value As Font)
            _ColumnFont = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor1 As Color = Color.FromArgb(62, 62, 62)
    Property ItemBackColor1 As Color
        Get
            Return _ItemBackColor1
        End Get
        Set(value As Color)
            _ItemBackColor1 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor2 As Color = Color.FromArgb(65, 65, 65)
    Property ItemBackColor2 As Color
        Get
            Return _ItemBackColor2
        End Get
        Set(value As Color)
            _ItemBackColor2 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor3 As Color = Color.FromArgb(47, 47, 47)
    Property ItemBackColor3 As Color
        Get
            Return _ItemBackColor3
        End Get
        Set(value As Color)
            _ItemBackColor3 = value
            Invalidate()
        End Set
    End Property

    Private _ItemBackColor4 As Color = Color.FromArgb(50, 50, 50)
    Property ItemBackColor4 As Color
        Get
            Return _ItemBackColor4
        End Get
        Set(value As Color)
            _ItemBackColor4 = value
            Invalidate()
        End Set
    End Property

#End Region

#Region "Item Helper"

    Public Sub AddItem(text As String, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItem()
        Item.Text = text
        Item.SubItems = Items

        _Items.Add(Item)
        InvalidateScroll()
    End Sub

    Public Sub AddItem(text As String, ITag As String, ParamArray subItems As String())
        Dim Items As New List(Of NSListViewSubItem)
        For Each I As String In subItems
            Dim SubItem As New NSListViewSubItem()
            SubItem.Text = I
            Items.Add(SubItem)
        Next

        Dim Item As New NSListViewItem()
        Item.Text = text
        Item.SubItems = Items
        Item.Tag = ITag

        _Items.Add(Item)
        InvalidateScroll()
    End Sub

    Public Sub Clear()
        _Items.Clear()
        InvalidateScroll()
    End Sub

    Public Sub RemoveItemAt(index As Integer)
        If index <> -1 Then _Items.RemoveAt(index)
        InvalidateScroll()
    End Sub

    Public Sub RemoveItem(item As NSListViewItem)
        _Items.Remove(item)
        InvalidateScroll()
    End Sub

    Public Sub RemoveItems(items As NSListViewItem())
        For Each I As NSListViewItem In items
            _Items.Remove(I)
        Next
        InvalidateScroll()
    End Sub

    Public Sub InsertItemAt(pos As Integer, item As NSListViewItem)
        _Items.Insert(pos, item)
        InvalidateScroll()
    End Sub

    Public Sub SelectItem(pos As Integer)
        _SelectedIndex.Clear()
        _SelectedItems.Clear()
        _SelectedIndex.Add(pos)
        If pos <> -1 Then _SelectedItems.Add(_Items(pos))
        RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0))
    End Sub

    Public Sub EnsureVisibleAt(pos As Integer)
        If pos = 0 Then VS.Value = VS.Minimum : InvalidateLayout() : Exit Sub
        If pos = Items.Count - 1 Then VS.Value = VS.Maximum : InvalidateLayout() : Exit Sub
        VS.Value = ItemHeight * pos
        'If pos * 25 < VS.Value Then
        '    VS.Value = Math.Max(pos * 25, VS.Minimum)
        '    If VS.Value < 25 Then VS.Value = VS.Minimum
        'ElseIf pos * 25 > (VS.Value + Height) - (25 * 2) Then
        '    VS.Value = Math.Min(((pos * 25) - (Height) + 5), VS.Maximum)
        '    If VS.Value > VS.Maximum - 25 Then VS.Value = VS.Maximum
        'End If
        InvalidateLayout()
    End Sub

#End Region

#Region "Methods"

    Public VS As NSVScrollBar

    Sub New()
        SetStyle(DirectCast(139286, ControlStyles), True) : SetStyle(ControlStyles.Selectable, True)
        P1 = New Pen(Color.FromArgb(55, 55, 55)) : P2 = New Pen(Color.FromArgb(35, 35, 35)) : P3 = New Pen(Color.FromArgb(65, 65, 65))
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 6
        VS = New NSVScrollBar : If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight
        AddHandler VS.Scroll, AddressOf HandleScroll : AddHandler VS.MouseDown, AddressOf VS_MouseDown : Controls.Add(VS)
        InvalidateLayout()
    End Sub

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If Focused = False Then Return MyBase.ProcessCmdKey(msg, keyData)
        If (keyData = Keys.Right) OrElse (keyData = Keys.Left) OrElse (keyData = Keys.Up) OrElse (keyData = Keys.Down) OrElse (keyData = Keys.Home) OrElse (keyData = Keys.End) OrElse (keyData = Keys.PageUp) OrElse (keyData = Keys.PageDown) OrElse (keyData = Keys.Tab) OrElse (keyData = Keys.Apps) OrElse (keyData = Keys.Delete) Then
            If keyData = Keys.Delete Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Tab Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If keyData = Keys.Apps Then MyBase.OnKeyDown(New KeyEventArgs(keyData))
            If Items.Count > 1 Then
                If keyData = Keys.Down Then
                    SelectItem(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 1, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Up Then
                    SelectItem(Math.Max(SelectedIndex(0) - 1, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 1, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    If msg.Msg = &H101 Then MyBase.OnKeyDown(New KeyEventArgs(keyData)) Else MyBase.OnKeyDown(New KeyEventArgs(keyData))
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.Home Then
                    SelectItem(0) : EnsureVisibleAt(0) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.End Then
                    SelectItem(Items.Count - 1) : EnsureVisibleAt(Items.Count - 1) : Keysearch = 0
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageUp Then
                    SelectItem(Math.Max(SelectedIndex(0) - 14, 0))
                    EnsureVisibleAt(Math.Max(SelectedIndex(0) - 14, 0))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
                If keyData = Keys.PageDown Then
                    SelectItem(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    EnsureVisibleAt(Math.Min(SelectedIndex(0) + 14, Items.Count - 1))
                    Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
                    RaiseEvent SelectedIndexChanged(Me, SelectedIndex(0)) : InvalidateLayout()
                End If
            End If : Return True
        Else
            Return MyBase.ProcessCmdKey(msg, keyData)
        End If
    End Function

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnGotFocus(e)
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnClick(e)
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        If SelectedIndex.Count > 0 Then Keysearch = Math.Min(SelectedIndex(0) + 1, Items.Count - 1)
        InvalidateLayout()
        MyBase.OnDoubleClick(e)
        RaiseEvent DobleClick(Me, e, SelectedIndex(0))
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.F1 Or e.KeyCode = Keys.F2 Or e.KeyCode = Keys.F3 Or e.KeyCode = Keys.F4 Or e.KeyCode = Keys.F5 Or e.KeyCode = Keys.F6 Or e.KeyCode = Keys.F7 Or e.KeyCode = Keys.F8 Or e.KeyCode = Keys.F9 Or e.KeyCode = Keys.F10 Or e.KeyCode = Keys.F11 Or e.KeyCode = Keys.F12 Then Exit Sub
        Dim Repite As Boolean = True
        Dim OldIndex As Integer = SelectedIndex(0)
        Dim Index As Integer = SelectedIndex(0)
        If Keysearch = -1 Then Exit Sub
Repetir: For Cuenta = Keysearch To Items.Count - 1
            If Items(Cuenta).Text.ToLower.StartsWith(ChrW(e.KeyCode).ToString.ToLower) Then
                SelectItem(Cuenta)
                EnsureVisibleAt(Cuenta)
                Invalidate()
                Keysearch = Cuenta + 1
                If Keysearch = Items.Count Then Keysearch = 0
                Index = Cuenta
                GoTo Salta
            End If
        Next
        Keysearch = 0
        If Repite = True Then Repite = False : GoTo Repetir
Salta:
        InvalidateLayout()
        If OldIndex <> Index Then RaiseEvent SelectedIndexChanged(Me, Index)
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        InvalidateLayout()
        MyBase.OnSizeChanged(e)
    End Sub

    Public Sub Actualiza()
        Invalidate() : InvalidateColumns()
    End Sub
    Private Sub HandleScroll(sender As Object)
        Invalidate()
    End Sub

    Private Sub InvalidateScroll()
        ItemHeight = CInt(Graphics.FromHwnd(Handle).MeasureString("@", Font).Height) + 6
        If VS IsNot Nothing Then VS.SmallChange = ItemHeight : VS.LargeChange = ItemHeight * 3
        If ShowColumn = True Then VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight) Else VS.Maximum = (Items.Count * ItemHeight) - (ItemHeight * 2)
        Invalidate()
    End Sub

    Private Sub InvalidateLayout()
        VS.Location = New Point(Width - VS.Width - 1, 1) : VS.Size = New Size(18, Height - 2) : Invalidate()
    End Sub

    Private ColumnOffsets As Integer()
    Private Sub InvalidateColumns()
        Dim Width As Integer = 3 : ColumnOffsets = New Integer(_Columns.Count - 1) {}
        For I As Integer = 0 To _Columns.Count - 1
            ColumnOffsets(I) = Width : Width += Columns(I).Width
        Next : Invalidate()
    End Sub

    Private Sub VS_MouseDown(sender As Object, e As MouseEventArgs)
        Focus()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        Dim pt As Point = PointToClient(Control.MousePosition)
        For i As Integer = 0 To Items.Count - 1
            If Items(i).Area.Contains(pt) Then
                HoveredItem = i
                GoTo Salta
            End If
        Next
        HoveredItem = -1
Salta:  MyBase.OnMouseMove(e)
        'Invalidate()
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Focus()
        If e.Button = Windows.Forms.MouseButtons.Left Or e.Button = Windows.Forms.MouseButtons.Right Then

            If Name = "Lista" AndAlso PointToClient(MousePosition).Y < 22 Then Exit Sub

            Dim Offset As Integer
            Dim Index As Integer
            If ShowColumn = True Then
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset - ItemHeight) \ ItemHeight)
            Else
                Offset = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2)))) : Index = ((e.Y + Offset) \ ItemHeight)
            End If
            If Index > _Items.Count - 1 Then Index = -1
            If Not Index = -1 Then 'TODO: Handle Shift key
                If ModifierKeys = Keys.Control AndAlso _MultiSelect Then
                    If _SelectedItems.Contains(_Items(Index)) Then _SelectedItems.Remove(_Items(Index)) Else _SelectedItems.Add(_Items(Index))
                Else
                    _SelectedItems.Clear() : _SelectedItems.Add(_Items(Index))
                End If
            Else
                _SelectedItems.Clear()
            End If
            If ModifierKeys = Keys.Control AndAlso _MultiSelect Then
                If _SelectedItems.Contains(_Items(Index)) Then _SelectedIndex.Remove(Index) Else _SelectedIndex.Add(Index)
            Else
                _SelectedIndex.Clear() : _SelectedIndex.Add(Index)
            End If
            Invalidate()
            RaiseEvent SelectedIndexChanged(Me, Index)
        End If
        MyBase.OnMouseDown(e)
    End Sub

    Private P1, P2, P3 As Pen : Private B1, B2, B3, B4 As SolidBrush : Private GB1 As Drawing2D.LinearGradientBrush
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        G = e.Graphics : G.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit : G.Clear(BackColor)
        B1 = New SolidBrush(ItemBackColor1) : B2 = New SolidBrush(ItemBackColor2) : B3 = New SolidBrush(ItemBackColor3) : B4 = New SolidBrush(ItemBackColor4)
        Dim CFont As Font : Dim VSOffset As Integer : Dim X, Y As Integer : Dim H As Single
        Dim R1 As Rectangle : Dim CI As NSListViewItem
        If ShowColumn = True Then
            If (Items.Length * ItemHeight) + ItemHeight > Height Then
                VS.Visible = True : VSOffset = -VS.Width
            Else
                VS.Visible = False : VSOffset = 0
            End If
        Else
            If Items.Length * ItemHeight > Height Then
                VS.Visible = True : VSOffset = -VS.Width
            Else
                VS.Visible = False : VSOffset = 0
            End If
        End If
        G.DrawRectangle(P1, 1, 1, Width - 3, Height - 3)
        Dim Offset As Integer = CInt(VS.Percent * (VS.Maximum - (Height - (ItemHeight * 2))))
        Dim StartIndex As Integer : If Offset = 0 Then StartIndex = 0 Else StartIndex = (Offset \ ItemHeight)
        Dim EndIndex As Integer = Math.Min(StartIndex + (Height \ ItemHeight), Items.Count - 1)
        If EndIndex < Items.Count - 1 Then EndIndex += 1
        If Items.Count = 0 Then GoTo Salta
        For I As Integer = StartIndex To EndIndex
            CI = Items(I)
            If ShowColumn = True Then R1 = New Rectangle(0, ItemHeight + (I * ItemHeight) + 1 - Offset, Width, ItemHeight - 1) Else R1 = New Rectangle(0, (ItemHeight + (I * ItemHeight) + 1 - Offset) - ItemHeight, Width, ItemHeight - 1)
            Items(I).Area = R1 : H = G.MeasureString(CI.Text, Font).Height : Y = R1.Y + CInt((ItemHeight / 2) - (H / 2))
            VS.SmallChange = (R1.Height - 1) * 2 : VS.LargeChange = R1.Height * 5
            If SelectedItems.Contains(CI) Then
                If I Mod 2 = 0 Then G.FillRectangle(B1, R1) Else G.FillRectangle(B2, R1)
            Else
                If I Mod 2 = 0 Then G.FillRectangle(B3, R1) Else G.FillRectangle(B4, R1)
            End If
            G.DrawLine(P2, 0, R1.Bottom, Width, R1.Bottom)
            If Columns.Length > 0 Then R1.Width = Columns(0).Width : G.SetClip(R1)
            CFont = Font
Repite1:    Dim SZ1 = G.MeasureString(CI.Text, CFont)
            If SZ1.Width > (Columns(0).Width + VSOffset) And CFont.Size > 7 Then
                CFont = New Font(CFont.FontFamily, CFont.Size - 1) : GoTo Repite1
            ElseIf SZ1.Width > (Columns(0).Width + VSOffset) Then
                If SelectedItems.Contains(CI) Then 'Or I = HoveredItem Then
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 1, Y + 1)
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 0, Y)
                Else
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 1, Y + 1)
                    G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 0, Y)
                End If
            Else
                If SelectedItems.Contains(CI) Then 'Or I = HoveredItem Then
                    If CI.Principal = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 25 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 25, Y)
                        If CI.Opened = False Then
                            e.Graphics.DrawImage(My.Resources.Ayuda_Mas, New Rectangle(5, Y, 14, 14))
                        Else
                            e.Graphics.DrawImage(My.Resources.Ayuda_Menos, New Rectangle(5, Y, 14, 14))
                        End If
                    ElseIf CI.Secundario = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 35 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 35, Y)
                        If CI.Opened = False Then
                            If CI.IgnorarIcono = False Then
                                e.Graphics.DrawImage(My.Resources.Ayuda_Mas, New Rectangle(10, Y, 14, 14))
                            Else
                                If CI.Imagen IsNot Nothing Then
                                    If CI.ImagenPNG = False Then e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 2, 18, 18)) Else e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 1, 14, 14))
                                End If
                            End If
                        Else
                            If CI.IgnorarIcono = False Then
                                e.Graphics.DrawImage(My.Resources.Ayuda_Menos, New Rectangle(10, Y, 14, 14))
                            Else
                                If CI.Imagen IsNot Nothing Then
                                    If CI.ImagenPNG = False Then e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 2, 18, 18)) Else e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 1, 14, 14))
                                End If
                            End If
                        End If
                    ElseIf CI.SecundarioChild = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 45 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 45, Y)
                    Else
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 35 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), 35, Y)
                    End If
                    'G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y + 1)
                    'G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor3), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y)
                Else
                    If CI.Principal = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 25 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 25, Y)
                        If CI.Opened = False Then
                            e.Graphics.DrawImage(My.Resources.Ayuda_Mas_Off, New Rectangle(5, Y, 14, 14))
                        Else
                            e.Graphics.DrawImage(My.Resources.Ayuda_Menos_Off, New Rectangle(5, Y, 14, 14))
                        End If
                    ElseIf CI.Secundario = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 35 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 35, Y)
                        If CI.Opened = False Then
                            If CI.IgnorarIcono = False Then
                                e.Graphics.DrawImage(My.Resources.Ayuda_Mas_Off, New Rectangle(10, Y, 14, 14))
                            Else
                                If CI.Imagen IsNot Nothing Then
                                    If CI.ImagenPNG = False Then e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 2, 18, 18)) Else e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 1, 14, 14))
                                End If
                            End If
                        Else
                            If CI.IgnorarIcono = False Then
                                e.Graphics.DrawImage(My.Resources.Ayuda_Menos_Off, New Rectangle(10, Y, 14, 14))
                            Else
                                If CI.Imagen IsNot Nothing Then
                                    If CI.ImagenPNG = False Then e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 2, 18, 18)) Else e.Graphics.DrawImage(CI.Imagen, New Rectangle(9, Y - 1, 14, 14))
                                End If
                            End If
                        End If
                    ElseIf CI.SecundarioChild = True Then
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 45 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 45, Y)
                    Else
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), 35 + 1, Y + 1)
                        G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), 35, Y)
                    End If
                    'G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor2), ((Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2) + 1, Y + 1)
                    'G.DrawString(CI.Text, CFont, New SolidBrush(ForeColor1), (Columns(0).Width + VSOffset) \ 2 - SZ1.Width / 2, Y)
                End If
            End If
            If CI.SubItems IsNot Nothing Then
                For I2 As Integer = 0 To Math.Min(CI.SubItems.Count, Columns.Count) - 1
                    X = ColumnOffsets(I2 + 1) + 4
                    R1.X = X
                    R1.Width = Columns(I2 + 1).Width
                    G.SetClip(R1)
                    CFont = Font
                    SZ1 = G.MeasureString(CI.SubItems(I2).Text, CFont)
Repite2:            If SZ1.Width > R1.Width And CFont.Size > 7 Then
                        CFont = New Font(Font.FontFamily, CFont.Size - 1)
                        GoTo Repite2
                    ElseIf SZ1.Width > R1.Width Then
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), X + 1, Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), X, Y)
                        End If
                    Else
                        If SelectedItems.Contains(CI) Then
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor3), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        Else
                            G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor2), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                            If CI.Marked = True Then G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(CI.MarkedColor), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y) Else G.DrawString(CI.SubItems(I2).Text, CFont, New SolidBrush(ForeColor1), (Columns(I2 + 1).Width \ 2 - SZ1.Width / 2) + X, Y)
                        End If
                    End If
                Next
            End If
            G.ResetClip()
        Next
Salta:        'Marca-----------------------------------------------
        If ShowColumn = True Then
            R1 = New Rectangle(0, 0, Width, ItemHeight) : GB1 = New Drawing2D.LinearGradientBrush(R1, Color.FromArgb(60, 60, 60), Color.FromArgb(55, 55, 55), 90.0F)
            G.FillRectangle(GB1, R1) : G.DrawRectangle(P3, 1, 1, Width + VSOffset, ItemHeight - 2)
        End If
        'Marca-----------------------------------------------
        Dim LH As Integer = Math.Min(VS.Maximum + (ItemHeight * 2) + ItemHeight - Offset, Height)
        'Marca-----------------------------------------------
        If ShowColumn = True Then
            Dim CC As NSListViewColumnHeader
            For I As Integer = 0 To _Columns.Count - 1
                CC = Columns(I)
                H = G.MeasureString(CC.Text, ColumnFont).Height
                Y = CInt((ItemHeight / 2) - (H / 2))
                X = ColumnOffsets(I)
                Dim SZ1 = G.MeasureString(CC.Text, ColumnFont)
                G.DrawString(CC.Text, ColumnFont, New SolidBrush(ColumnColor2), (Columns(I).Width \ 2 - SZ1.Width / 2) + (X + 1), Y + 1)
                G.DrawString(CC.Text, ColumnFont, New SolidBrush(ColumnColor1), (Columns(I).Width \ 2 - SZ1.Width / 2) + X, Y)
                G.DrawLine(P2, X - 3, 0, X - 3, LH)
                G.DrawLine(P3, X - 2, 0, X - 2, ItemHeight)
            Next
        End If
        'Marca-----------------------------------------------
        G.DrawRectangle(P2, 0, 0, Width - 1, Height - 1)
        'Marca-----------------------------------------------
        If ShowColumn = True Then G.DrawLine(P2, 0, ItemHeight, Width, ItemHeight)
        'Marca-----------------------------------------------
        If VS.Visible = True Then G.DrawLine(P2, VS.Location.X - 1, 0, VS.Location.X - 1, Height)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        InvalidateScroll()
        Dim Move As Integer = -((e.Delta * SystemInformation.MouseWheelScrollLines \ 120) * (ItemHeight \ 2))
        Dim Value As Integer = Math.Max(Math.Min(VS.Value + Move, VS.Maximum), VS.Minimum) : VS.Value = Value
        MyBase.OnMouseWheel(e)
    End Sub

#End Region

End Class

#End Region

#Region "MessageBox"

Class NSMessageBox

    Public Shared Function ShowYesNo(text As String, caption As String) As DialogResult
        Dim F As Form = CreateDialog(text, caption)

        Dim B1 As New NSButton
        Dim B2 As New NSButton

        B1.ForeColor1 = Color.FromArgb(102, 102, 102)
        B1.ForeColor2 = Color.Black
        B1.ForeColor3 = Color.FromArgb(205, 150, 0)
        B1.DoubleText = True
        B1.MarkedHovered = False
        B2.ForeColor1 = Color.FromArgb(102, 102, 102)
        B2.ForeColor2 = Color.Black
        B2.ForeColor3 = Color.FromArgb(205, 150, 0)
        B2.DoubleText = True
        B2.MarkedHovered = False

        Dim S As New Size(90, 25)
        B1.Size = S
        B2.Size = S

        B1.Location = New Point(35, 90)
        B2.Location = New Point(155, 90)

        B1.Text = "Aceptar"
        B2.Text = "Cancelar"

        AddHandler B1.Click, Sub() F.DialogResult = DialogResult.Yes
        AddHandler B2.Click, Sub() F.DialogResult = DialogResult.No

        F.Controls(0).Controls.Add(B1)
        F.Controls(0).Controls.Add(B2)

        Return F.ShowDialog()
    End Function

    Public Shared Function ShowOk(text As String, caption As String) As DialogResult
        Dim F As Form = CreateDialog(text, caption)

        Dim B1 As New NSButton

        B1.ForeColor1 = Color.FromArgb(102, 102, 102)
        B1.ForeColor2 = Color.Black
        B1.ForeColor3 = Color.FromArgb(205, 150, 0)
        B1.DoubleText = True
        B1.MarkedHovered = False

        Dim S As New Size(90, 25)
        B1.Size = S

        B1.Location = New Point(95, 90)

        B1.Text = "Aceptar"

        AddHandler B1.Click, Sub() F.DialogResult = DialogResult.OK

        F.Controls(0).Controls.Add(B1)

        Return F.ShowDialog()
    End Function

    Private Shared Function CreateDialog(text As String, caption As String) As Form
        Dim F As New Form

        Dim MTheme1 As New NSTheme()
        Dim MControlButton1 As New NSControlButton()
        Dim Label1 As New NSLabel

        F.Icon = FMenu.Icon
        MTheme1.Controls.Add(MControlButton1)
        MTheme1.Controls.Add(Label1)
        MTheme1.Sizable = False
        MTheme1.Size = New Size(280, 127)
        MTheme1.ForeColor1 = Color.FromArgb(205, 150, 0)
        MTheme1.Text = caption

        MControlButton1.ControlButton = NSControlButton.Button.Close
        MControlButton1.ForeColor3 = Color.FromArgb(205, 150, 0)
        MControlButton1.Location = New Point(254, 4)

        Label1.ForeColor1 = Color.Gray
        Label1.Font = New Font("Segoe UI", 8, FontStyle.Regular)
        Label1.Location = New Point(12, 35)
        Label1.Size = New Size(256, 30)
        'Label1.Text = text
        'Label1.Value1 = text.Substring(0, 1)
        'Label1.Value2 = text.Substring(1)
        Label1.Value1 = ""
        Label1.Value2 = text

        F.TopMost = True
        F.StartPosition = FormStartPosition.CenterScreen
        F.Size = New Size(280, 156)
        F.ClientSize = New Size(280, 127)
        F.Controls.Add(MTheme1)
        F.FormBorderStyle = FormBorderStyle.None
        F.Text = caption

        Return F
    End Function

End Class

#End Region

#Region " DateTime "

Public Class NSDateTime : Inherits DateTimePicker

    Public Sub New()
        MyBase.New() : SetStyle(ControlStyles.UserPaint, True)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics : g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
        Width = g.MeasureString(Text, Font).Width + 25 : g.Clear(Color.FromArgb(35, 35, 35))
        Dim TextColor As Color = Color.FromArgb(205, 150, 0) : If Enabled = False Then TextColor = Color.Black
        g.FillRectangle(New SolidBrush(Color.FromArgb(50, 50, 50)), 1, 1, Width - 2, Height - 2)
        'g.DrawString(Text, Font, New SolidBrush(Color.Black), 6, 4)
        g.DrawString(Text, Font, New SolidBrush(Color.FromArgb(102, 102, 102)), 5, 3)
        g.DrawImage(FMenu.Icon.ToBitmap, New Rectangle(ClientRectangle.Width - 17, 3, 14, 14))
        g.Dispose()
    End Sub

End Class

#End Region

#Region " ThemeBase 1.5.4 "

#Region " Theme Container "

MustInherit Class ThemeContainer154
    Inherits ContainerControl

#Region " Initialization "

    Protected G As Graphics, B As Bitmap

    Sub New()
        SetStyle(DirectCast(139270, ControlStyles), True)
        _ImageSize = Size.Empty
        Font = New Font("Verdana", 8S)
        MeasureBitmap = New Bitmap(1, 1)
        MeasureGraphics = Graphics.FromImage(MeasureBitmap)
        DrawRadialPath = New Drawing2D.GraphicsPath
        InvalidateCustimization()
    End Sub

    Protected NotOverridable Overrides Sub OnHandleCreated(e As EventArgs)
        If DoneCreation Then InitializeMessages()
        InvalidateCustimization()
        ColorHook()
        If Not _LockWidth = 0 Then Width = _LockWidth
        If Not _LockHeight = 0 Then Height = _LockHeight
        If Not _ControlMode Then MyBase.Dock = DockStyle.Fill
        Transparent = _Transparent
        If _Transparent AndAlso _BackColor Then BackColor = Color.Transparent
        MyBase.OnHandleCreated(e)
    End Sub

    Private DoneCreation As Boolean
    Protected NotOverridable Overrides Sub OnParentChanged(e As EventArgs)
        MyBase.OnParentChanged(e)
        If Parent Is Nothing Then Return
        _IsParentForm = TypeOf Parent Is Form
        If Not _ControlMode Then
            InitializeMessages()
            If _IsParentForm Then
                ParentForm.FormBorderStyle = _BorderStyle
                ParentForm.TransparencyKey = _TransparencyKey

                If Not DesignMode Then
                    AddHandler ParentForm.Shown, AddressOf FormShown
                End If
            End If
            Parent.BackColor = BackColor
        End If
        OnCreation()
        DoneCreation = True
        InvalidateTimer()
    End Sub

    Private Sub DoAnimation(i As Boolean)
        OnAnimation()
        If i Then Invalidate()
    End Sub

    Protected NotOverridable Overrides Sub OnPaint(e As PaintEventArgs)
        If Width = 0 OrElse Height = 0 Then Return
        If _Transparent AndAlso _ControlMode Then
            PaintHook()
            e.Graphics.DrawImage(B, 0, 0)
        Else
            G = e.Graphics
            PaintHook()
        End If
    End Sub

    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        RemoveAnimationCallback(AddressOf DoAnimation)
        MyBase.OnHandleDestroyed(e)
    End Sub

    Private HasShown As Boolean
    Private Sub FormShown(sender As Object, e As EventArgs)
        If _ControlMode OrElse HasShown Then Return

        If _StartPosition = FormStartPosition.CenterParent OrElse _StartPosition = FormStartPosition.CenterScreen Then
            Dim SB As Rectangle = Screen.PrimaryScreen.Bounds
            Dim CB As Rectangle = ParentForm.Bounds
            ParentForm.Location = New Point(SB.Width \ 2 - CB.Width \ 2, SB.Height \ 2 - CB.Width \ 2)
        End If
        HasShown = True
    End Sub

#End Region

#Region " Size Handling "

    Private Frame As Rectangle
    Protected NotOverridable Overrides Sub OnSizeChanged(e As EventArgs)
        If _Movable AndAlso Not _ControlMode Then
            Frame = New Rectangle(7, 7, Width - 14, _Header - 7)
        End If

        InvalidateBitmap()
        Invalidate()

        MyBase.OnSizeChanged(e)
    End Sub

    Protected Overrides Sub SetBoundsCore(x As Integer, y As Integer, width As Integer, height As Integer, specified As BoundsSpecified)
        If Not _LockWidth = 0 Then width = _LockWidth
        If Not _LockHeight = 0 Then height = _LockHeight
        MyBase.SetBoundsCore(x, y, width, height, specified)
    End Sub

#End Region

#Region " State Handling "

    Protected State As MouseState
    Private Sub SetState(current As MouseState)
        State = current
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If Not (_IsParentForm AndAlso ParentForm.WindowState = FormWindowState.Maximized) Then
            If _Sizable AndAlso Not _ControlMode Then InvalidateMouse()
        End If

        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        If Enabled Then SetState(MouseState.None) Else SetState(MouseState.Block)
        MyBase.OnEnabledChanged(e)
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        SetState(MouseState.Over)
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        SetState(MouseState.Over)
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        SetState(MouseState.None)

        If GetChildAtPoint(PointToClient(MousePosition)) IsNot Nothing Then
            If _Sizable AndAlso Not _ControlMode Then
                Cursor = Cursors.Default
                Previous = 0
            End If
        End If

        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left Then SetState(MouseState.Down)

        If Not (_IsParentForm AndAlso ParentForm.WindowState = FormWindowState.Maximized OrElse _ControlMode) Then
            If _Movable AndAlso Frame.Contains(e.Location) Then
                Capture = False
                WM_LMBUTTONDOWN = True
                DefWndProc(Messages(0))
            ElseIf _Sizable AndAlso Not Previous = 0 Then
                Capture = False
                WM_LMBUTTONDOWN = True
                DefWndProc(Messages(Previous))
            End If
        End If

        MyBase.OnMouseDown(e)
    End Sub

    Private WM_LMBUTTONDOWN As Boolean
    Protected Overrides Sub WndProc(ByRef m As Message)
        MyBase.WndProc(m)

        If WM_LMBUTTONDOWN AndAlso m.Msg = 513 Then
            WM_LMBUTTONDOWN = False

            SetState(MouseState.Over)
            If Not _SmartBounds Then Return

            If IsParentMdi Then
                CorrectBounds(New Rectangle(Point.Empty, Parent.Parent.Size))
            Else
                CorrectBounds(Screen.FromControl(Parent).WorkingArea)
            End If
        End If
    End Sub

    Private GetIndexPoint As Point
    Private B1, B2, B3, B4 As Boolean
    Private Function GetIndex() As Integer
        GetIndexPoint = PointToClient(MousePosition)
        B1 = GetIndexPoint.X < 7
        B2 = GetIndexPoint.X > Width - 7
        B3 = GetIndexPoint.Y < 7
        B4 = GetIndexPoint.Y > Height - 7

        If B1 AndAlso B3 Then Return 4
        If B1 AndAlso B4 Then Return 7
        If B2 AndAlso B3 Then Return 5
        If B2 AndAlso B4 Then Return 8
        If B1 Then Return 1
        If B2 Then Return 2
        If B3 Then Return 3
        If B4 Then Return 6
        Return 0
    End Function

    Private Current, Previous As Integer
    Private Sub InvalidateMouse()
        Current = GetIndex()
        If Current = Previous Then Return

        Previous = Current
        Select Case Previous
            Case 0
                Cursor = Cursors.Default
            Case 1, 2
                Cursor = Cursors.SizeWE
            Case 3, 6
                Cursor = Cursors.SizeNS
            Case 4, 8
                Cursor = Cursors.SizeNWSE
            Case 5, 7
                Cursor = Cursors.SizeNESW
        End Select
    End Sub

    Private Messages(8) As Message
    Private Sub InitializeMessages()
        Messages(0) = Message.Create(Parent.Handle, 161, New IntPtr(2), IntPtr.Zero)
        For I As Integer = 1 To 8
            Messages(I) = Message.Create(Parent.Handle, 161, New IntPtr(I + 9), IntPtr.Zero)
        Next
    End Sub

    Private Sub CorrectBounds(bounds As Rectangle)
        If Parent.Width > bounds.Width Then Parent.Width = bounds.Width
        If Parent.Height > bounds.Height Then Parent.Height = bounds.Height

        Dim X As Integer = Parent.Location.X
        Dim Y As Integer = Parent.Location.Y

        If X < bounds.X Then X = bounds.X
        If Y < bounds.Y Then Y = bounds.Y

        Dim Width As Integer = bounds.X + bounds.Width
        Dim Height As Integer = bounds.Y + bounds.Height

        If X + Parent.Width > Width Then X = Width - Parent.Width
        If Y + Parent.Height > Height Then Y = Height - Parent.Height

        Parent.Location = New Point(X, Y)
    End Sub

#End Region

#Region " Base Properties "

    Overrides Property Dock As DockStyle
        Get
            Return MyBase.Dock
        End Get
        Set(value As DockStyle)
            If Not _ControlMode Then Return
            MyBase.Dock = value
        End Set
    End Property

    Private _BackColor As Boolean
    <ComponentModel.Category("Misc")> Overrides Property BackColor() As Color
        Get
            Return MyBase.BackColor
        End Get
        Set(value As Color)
            If value = MyBase.BackColor Then Return

            If Not IsHandleCreated AndAlso _ControlMode AndAlso value = Color.Transparent Then
                _BackColor = True
                Return
            End If

            MyBase.BackColor = value
            If Parent IsNot Nothing Then
                If Not _ControlMode Then Parent.BackColor = value
                ColorHook()
            End If
        End Set
    End Property

    Overrides Property MinimumSize As Size
        Get
            Return MyBase.MinimumSize
        End Get
        Set(value As Size)
            MyBase.MinimumSize = value
            If Parent IsNot Nothing Then Parent.MinimumSize = value
        End Set
    End Property

    Overrides Property MaximumSize As Size
        Get
            Return MyBase.MaximumSize
        End Get
        Set(value As Size)
            MyBase.MaximumSize = value
            If Parent IsNot Nothing Then Parent.MaximumSize = value
        End Set
    End Property

    Overrides Property Text() As String
        Get
            Return MyBase.Text
        End Get
        Set(value As String)
            MyBase.Text = value
            Invalidate()
        End Set
    End Property

    Overrides Property Font() As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)
            MyBase.Font = value
            Invalidate()
        End Set
    End Property

    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property ForeColor() As Color
        Get
            Return Color.Empty
        End Get
        Set(value As Color)
        End Set
    End Property
    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property BackgroundImage() As Image
        Get
            Return Nothing
        End Get
        Set(value As Image)
        End Set
    End Property
    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property BackgroundImageLayout() As ImageLayout
        Get
            Return ImageLayout.None
        End Get
        Set(value As ImageLayout)
        End Set
    End Property

#End Region

#Region " Public Properties "

    Private _SmartBounds As Boolean = True
    Property SmartBounds() As Boolean
        Get
            Return _SmartBounds
        End Get
        Set(value As Boolean)
            _SmartBounds = value
        End Set
    End Property

    Private _Movable As Boolean = True
    Property Movable() As Boolean
        Get
            Return _Movable
        End Get
        Set(value As Boolean)
            _Movable = value
        End Set
    End Property

    Private _Sizable As Boolean = True
    Property Sizable() As Boolean
        Get
            Return _Sizable
        End Get
        Set(value As Boolean)
            _Sizable = value
        End Set
    End Property

    Private _TransparencyKey As Color
    Property TransparencyKey() As Color
        Get
            If _IsParentForm AndAlso Not _ControlMode Then Return ParentForm.TransparencyKey Else Return _TransparencyKey
        End Get
        Set(value As Color)
            If value = _TransparencyKey Then Return
            _TransparencyKey = value

            If _IsParentForm AndAlso Not _ControlMode Then
                ParentForm.TransparencyKey = value
                ColorHook()
            End If
        End Set
    End Property

    Private _BorderStyle As FormBorderStyle
    Property BorderStyle() As FormBorderStyle
        Get
            If _IsParentForm AndAlso Not _ControlMode Then Return ParentForm.FormBorderStyle Else Return _BorderStyle
        End Get
        Set(value As FormBorderStyle)
            _BorderStyle = value

            If _IsParentForm AndAlso Not _ControlMode Then
                ParentForm.FormBorderStyle = value

                If Not value = FormBorderStyle.None Then
                    Movable = False
                    Sizable = False
                End If
            End If
        End Set
    End Property

    Private _StartPosition As FormStartPosition
    Property StartPosition As FormStartPosition
        Get
            If _IsParentForm AndAlso Not _ControlMode Then Return ParentForm.StartPosition Else Return _StartPosition
        End Get
        Set(value As FormStartPosition)
            _StartPosition = value

            If _IsParentForm AndAlso Not _ControlMode Then
                ParentForm.StartPosition = value
            End If
        End Set
    End Property

    Private _NoRounding As Boolean
    Property NoRounding() As Boolean
        Get
            Return _NoRounding
        End Get
        Set(value As Boolean)
            _NoRounding = value
            Invalidate()
        End Set
    End Property

    Private _Image As Image
    Property Image() As Image
        Get
            Return _Image
        End Get
        Set( value As Image)
            If value Is Nothing Then _ImageSize = Size.Empty Else _ImageSize = value.Size

            _Image = value
            Invalidate()
        End Set
    End Property

    Private Items As New Dictionary(Of String, Color)
    Property Colors() As Bloom()
        Get
            Dim T As New List(Of Bloom)
            Dim E As Dictionary(Of String, Color).Enumerator = Items.GetEnumerator

            While E.MoveNext
                T.Add(New Bloom(E.Current.Key, E.Current.Value))
            End While

            Return T.ToArray
        End Get
        Set(value As Bloom())
            For Each B As Bloom In value
                If Items.ContainsKey(B.Name) Then Items(B.Name) = B.Value
            Next

            InvalidateCustimization()
            ColorHook()
            Invalidate()
        End Set
    End Property

    Private _Customization As String
    Property Customization() As String
        Get
            Return _Customization
        End Get
        Set(value As String)
            If value = _Customization Then Return

            Dim Data As Byte()
            Dim Items As Bloom() = Colors

            Try
                Data = Convert.FromBase64String(value)
                For I As Integer = 0 To Items.Length - 1
                    Items(I).Value = Color.FromArgb(BitConverter.ToInt32(Data, I * 4))
                Next
            Catch
                Return
            End Try

            _Customization = value

            Colors = Items
            ColorHook()
            Invalidate()
        End Set
    End Property

    Private _Transparent As Boolean
    Property Transparent() As Boolean
        Get
            Return _Transparent
        End Get
        Set(value As Boolean)
            _Transparent = value
            If Not (IsHandleCreated OrElse _ControlMode) Then Return

            If Not value AndAlso Not BackColor.A = 255 Then
                Throw New Exception("Unable to change value to false while a transparent BackColor is in use.")
            End If

            SetStyle(ControlStyles.Opaque, Not value)
            SetStyle(ControlStyles.SupportsTransparentBackColor, value)

            InvalidateBitmap()
            Invalidate()
        End Set
    End Property

#End Region

#Region " Private Properties "

    Private _ImageSize As Size
    Protected ReadOnly Property ImageSize() As Size
        Get
            Return _ImageSize
        End Get
    End Property

    Private _IsParentForm As Boolean
    Protected ReadOnly Property IsParentForm As Boolean
        Get
            Return _IsParentForm
        End Get
    End Property

    Protected ReadOnly Property IsParentMdi As Boolean
        Get
            If Parent Is Nothing Then Return False
            Return Parent.Parent IsNot Nothing
        End Get
    End Property

    Private _LockWidth As Integer
    Protected Property LockWidth() As Integer
        Get
            Return _LockWidth
        End Get
        Set(value As Integer)
            _LockWidth = value
            If Not LockWidth = 0 AndAlso IsHandleCreated Then Width = LockWidth
        End Set
    End Property

    Private _LockHeight As Integer
    Protected Property LockHeight() As Integer
        Get
            Return _LockHeight
        End Get
        Set(value As Integer)
            _LockHeight = value
            If Not LockHeight = 0 AndAlso IsHandleCreated Then Height = LockHeight
        End Set
    End Property

    Private _Header As Integer = 24
    Protected Property Header() As Integer
        Get
            Return _Header
        End Get
        Set(value As Integer)
            _Header = value

            If Not _ControlMode Then
                Frame = New Rectangle(7, 7, Width - 14, value - 7)
                Invalidate()
            End If
        End Set
    End Property

    Private _ControlMode As Boolean
    Protected Property ControlMode() As Boolean
        Get
            Return _ControlMode
        End Get
        Set(value As Boolean)
            _ControlMode = value

            Transparent = _Transparent
            If _Transparent AndAlso _BackColor Then BackColor = Color.Transparent

            InvalidateBitmap()
            Invalidate()
        End Set
    End Property

    Private _IsAnimated As Boolean
    Protected Property IsAnimated() As Boolean
        Get
            Return _IsAnimated
        End Get
        Set(value As Boolean)
            _IsAnimated = value
            InvalidateTimer()
        End Set
    End Property

#End Region

#Region " Property Helpers "

    Protected Function GetPen(name As String) As Pen
        Return New Pen(Items(name))
    End Function
    Protected Function GetPen(name As String, width As Single) As Pen
        Return New Pen(Items(name), width)
    End Function

    Protected Function GetBrush(name As String) As SolidBrush
        Return New SolidBrush(Items(name))
    End Function

    Protected Function GetColor(name As String) As Color
        Return Items(name)
    End Function

    Protected Sub SetColor(name As String, value As Color)
        If Items.ContainsKey(name) Then Items(name) = value Else Items.Add(name, value)
    End Sub
    Protected Sub SetColor(name As String, r As Byte, g As Byte, b As Byte)
        SetColor(name, Color.FromArgb(r, g, b))
    End Sub
    Protected Sub SetColor(name As String, a As Byte, r As Byte, g As Byte, b As Byte)
        SetColor(name, Color.FromArgb(a, r, g, b))
    End Sub
    Protected Sub SetColor(name As String, a As Byte, value As Color)
        SetColor(name, Color.FromArgb(a, value))
    End Sub

    Private Sub InvalidateBitmap()
        If _Transparent AndAlso _ControlMode Then
            If Width = 0 OrElse Height = 0 Then Return
            B = New Bitmap(Width, Height, Imaging.PixelFormat.Format32bppPArgb)
            G = Graphics.FromImage(B)
        Else
            G = Nothing
            B = Nothing
        End If
    End Sub

    Private Sub InvalidateCustimization()
        Dim M As New IO.MemoryStream(Items.Count * 4)

        For Each B As Bloom In Colors
            M.Write(BitConverter.GetBytes(B.Value.ToArgb), 0, 4)
        Next

        M.Close()
        _Customization = Convert.ToBase64String(M.ToArray)
    End Sub

    Private Sub InvalidateTimer()
        If DesignMode OrElse Not DoneCreation Then Return

        If _IsAnimated Then
            AddAnimationCallback(AddressOf DoAnimation)
        Else
            RemoveAnimationCallback(AddressOf DoAnimation)
        End If
    End Sub

#End Region

#Region " User Hooks "

    Protected MustOverride Sub ColorHook()
    Protected MustOverride Sub PaintHook()

    Protected Overridable Sub OnCreation()
    End Sub

    Protected Overridable Sub OnAnimation()
    End Sub

#End Region

#Region " Offset "

    Private OffsetReturnRectangle As Rectangle
    Protected Function Offset(r As Rectangle, amount As Integer) As Rectangle
        OffsetReturnRectangle = New Rectangle(r.X + amount, r.Y + amount, r.Width - (amount * 2), r.Height - (amount * 2))
        Return OffsetReturnRectangle
    End Function

    Private OffsetReturnSize As Size
    Protected Function Offset(s As Size, amount As Integer) As Size
        OffsetReturnSize = New Size(s.Width + amount, s.Height + amount)
        Return OffsetReturnSize
    End Function

    Private OffsetReturnPoint As Point
    Protected Function Offset(p As Point, amount As Integer) As Point
        OffsetReturnPoint = New Point(p.X + amount, p.Y + amount)
        Return OffsetReturnPoint
    End Function

#End Region

#Region " Center "

    Private CenterReturn As Point

    Protected Function Center(p As Rectangle, c As Rectangle) As Point
        CenterReturn = New Point((p.Width \ 2 - c.Width \ 2) + p.X + c.X, (p.Height \ 2 - c.Height \ 2) + p.Y + c.Y)
        Return CenterReturn
    End Function
    Protected Function Center(p As Rectangle, c As Size) As Point
        CenterReturn = New Point((p.Width \ 2 - c.Width \ 2) + p.X, (p.Height \ 2 - c.Height \ 2) + p.Y)
        Return CenterReturn
    End Function

    Protected Function Center(child As Rectangle) As Point
        Return Center(Width, Height, child.Width, child.Height)
    End Function
    Protected Function Center(child As Size) As Point
        Return Center(Width, Height, child.Width, child.Height)
    End Function
    Protected Function Center(childWidth As Integer, childHeight As Integer) As Point
        Return Center(Width, Height, childWidth, childHeight)
    End Function

    Protected Function Center(p As Size, c As Size) As Point
        Return Center(p.Width, p.Height, c.Width, c.Height)
    End Function

    Protected Function Center(pWidth As Integer, pHeight As Integer, cWidth As Integer, cHeight As Integer) As Point
        CenterReturn = New Point(pWidth \ 2 - cWidth \ 2, pHeight \ 2 - cHeight \ 2)
        Return CenterReturn
    End Function

#End Region

#Region " Measure "

    Private MeasureBitmap As Bitmap
    Private MeasureGraphics As Graphics

    Protected Function Measure() As Size
        SyncLock MeasureGraphics
            Return MeasureGraphics.MeasureString(Text, Font, Width).ToSize
        End SyncLock
    End Function
    Protected Function Measure(text As String) As Size
        SyncLock MeasureGraphics
            Return MeasureGraphics.MeasureString(text, Font, Width).ToSize
        End SyncLock
    End Function

#End Region

#Region " DrawPixel "

    Private DrawPixelBrush As SolidBrush

    Protected Sub DrawPixel(c1 As Color, x As Integer, y As Integer)
        If _Transparent Then
            B.SetPixel(x, y, c1)
        Else
            DrawPixelBrush = New SolidBrush(c1)
            G.FillRectangle(DrawPixelBrush, x, y, 1, 1)
        End If
    End Sub

#End Region

#Region " DrawCorners "

    Private DrawCornersBrush As SolidBrush

    Protected Sub DrawCorners(c1 As Color, offset As Integer)
        DrawCorners(c1, 0, 0, Width, Height, offset)
    End Sub
    Protected Sub DrawCorners(c1 As Color, r1 As Rectangle, offset As Integer)
        DrawCorners(c1, r1.X, r1.Y, r1.Width, r1.Height, offset)
    End Sub
    Protected Sub DrawCorners(c1 As Color, x As Integer, y As Integer, width As Integer, height As Integer, offset As Integer)
        DrawCorners(c1, x + offset, y + offset, width - (offset * 2), height - (offset * 2))
    End Sub

    Protected Sub DrawCorners(c1 As Color)
        DrawCorners(c1, 0, 0, Width, Height)
    End Sub
    Protected Sub DrawCorners(c1 As Color, r1 As Rectangle)
        DrawCorners(c1, r1.X, r1.Y, r1.Width, r1.Height)
    End Sub
    Protected Sub DrawCorners(c1 As Color, x As Integer, y As Integer, width As Integer, height As Integer)
        If _NoRounding Then Return

        If _Transparent Then
            B.SetPixel(x, y, c1)
            B.SetPixel(x + (width - 1), y, c1)
            B.SetPixel(x, y + (height - 1), c1)
            B.SetPixel(x + (width - 1), y + (height - 1), c1)
        Else
            DrawCornersBrush = New SolidBrush(c1)
            G.FillRectangle(DrawCornersBrush, x, y, 1, 1)
            G.FillRectangle(DrawCornersBrush, x + (width - 1), y, 1, 1)
            G.FillRectangle(DrawCornersBrush, x, y + (height - 1), 1, 1)
            G.FillRectangle(DrawCornersBrush, x + (width - 1), y + (height - 1), 1, 1)
        End If
    End Sub

#End Region

#Region " DrawBorders "

    Protected Sub DrawBorders(p1 As Pen, offset As Integer)
        DrawBorders(p1, 0, 0, Width, Height, offset)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, r As Rectangle, offset As Integer)
        DrawBorders(p1, r.X, r.Y, r.Width, r.Height, offset)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, x As Integer, y As Integer, width As Integer, height As Integer, offset As Integer)
        DrawBorders(p1, x + offset, y + offset, width - (offset * 2), height - (offset * 2))
    End Sub

    Protected Sub DrawBorders(p1 As Pen)
        DrawBorders(p1, 0, 0, Width, Height)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, r As Rectangle)
        DrawBorders(p1, r.X, r.Y, r.Width, r.Height)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, x As Integer, y As Integer, width As Integer, height As Integer)
        G.DrawRectangle(p1, x, y, width - 1, height - 1)
    End Sub

#End Region

#Region " DrawText "

    Private DrawTextPoint As Point
    Private DrawTextSize As Size

    Protected Sub DrawText(b1 As Brush, a As HorizontalAlignment, x As Integer, y As Integer)
        DrawText(b1, Text, a, x, y)
    End Sub
    Protected Sub DrawText(b1 As Brush, text As String, a As HorizontalAlignment, x As Integer, y As Integer)
        If text.Length = 0 Then Return

        DrawTextSize = Measure(text)
        DrawTextPoint = New Point(Width \ 2 - DrawTextSize.Width \ 2, Header \ 2 - DrawTextSize.Height \ 2)
        Select Case a
            Case HorizontalAlignment.Left
                G.DrawString(text, Font, b1, x, DrawTextPoint.Y + y)
            Case HorizontalAlignment.Center
                G.DrawString(text, Font, b1, DrawTextPoint.X + x, DrawTextPoint.Y + y)
            Case HorizontalAlignment.Right
                G.DrawString(text, Font, b1, Width - DrawTextSize.Width - x, DrawTextPoint.Y + y)
        End Select
    End Sub

    Protected Sub DrawText(b1 As Brush, p1 As Point)
        If Text.Length = 0 Then Return
        G.DrawString(Text, Font, b1, p1)
    End Sub
    Protected Sub DrawText(b1 As Brush, x As Integer, y As Integer)
        If Text.Length = 0 Then Return
        G.DrawString(Text, Font, b1, x, y)
    End Sub

#End Region

#Region " DrawImage "

    Private DrawImagePoint As Point

    Protected Sub DrawImage(a As HorizontalAlignment, x As Integer, y As Integer)
        DrawImage(_Image, a, x, y)
    End Sub
    Protected Sub DrawImage(image As Image, a As HorizontalAlignment, x As Integer, y As Integer)
        If image Is Nothing Then Return
        DrawImagePoint = New Point(Width \ 2 - image.Width \ 2, Header \ 2 - image.Height \ 2)

        Select Case a
            Case HorizontalAlignment.Left
                G.DrawImage(image, x, DrawImagePoint.Y + y, image.Width, image.Height)
            Case HorizontalAlignment.Center
                G.DrawImage(image, DrawImagePoint.X + x, DrawImagePoint.Y + y, image.Width, image.Height)
            Case HorizontalAlignment.Right
                G.DrawImage(image, Width - image.Width - x, DrawImagePoint.Y + y, image.Width, image.Height)
        End Select
    End Sub

    Protected Sub DrawImage(p1 As Point)
        DrawImage(_Image, p1.X, p1.Y)
    End Sub
    Protected Sub DrawImage(x As Integer, y As Integer)
        DrawImage(_Image, x, y)
    End Sub

    Protected Sub DrawImage(image As Image, p1 As Point)
        DrawImage(image, p1.X, p1.Y)
    End Sub
    Protected Sub DrawImage(image As Image, x As Integer, y As Integer)
        If image Is Nothing Then Return
        G.DrawImage(image, x, y, image.Width, image.Height)
    End Sub

#End Region

#Region " DrawGradient "

    Private DrawGradientBrush As Drawing2D.LinearGradientBrush
    Private DrawGradientRectangle As Rectangle

    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(blend, DrawGradientRectangle)
    End Sub
    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, angle As Single)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(blend, DrawGradientRectangle, angle)
    End Sub

    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, r As Rectangle)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, Color.Empty, Color.Empty, 90.0F)
        DrawGradientBrush.InterpolationColors = blend
        G.FillRectangle(DrawGradientBrush, r)
    End Sub
    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, r As Rectangle, angle As Single)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, Color.Empty, Color.Empty, angle)
        DrawGradientBrush.InterpolationColors = blend
        G.FillRectangle(DrawGradientBrush, r)
    End Sub


    Protected Sub DrawGradient(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(c1, c2, DrawGradientRectangle)
    End Sub
    Protected Sub DrawGradient(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer, angle As Single)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(c1, c2, DrawGradientRectangle, angle)
    End Sub

    Protected Sub DrawGradient(c1 As Color, c2 As Color, r As Rectangle)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, c1, c2, 90.0F)
        G.FillRectangle(DrawGradientBrush, r)
    End Sub
    Protected Sub DrawGradient(c1 As Color, c2 As Color, r As Rectangle, angle As Single)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, c1, c2, angle)
        G.FillRectangle(DrawGradientBrush, r)
    End Sub

#End Region

#Region " DrawRadial "

    Private DrawRadialPath As Drawing2D.GraphicsPath
    Private DrawRadialBrush1 As Drawing2D.PathGradientBrush
    'Private DrawRadialBrush2 As Drawing2D.LinearGradientBrush
    Private DrawRadialRectangle As Rectangle

    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, width \ 2, height \ 2)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, center As Point)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, center.X, center.Y)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, cx As Integer, cy As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, cx, cy)
    End Sub

    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle)
        DrawRadial(blend, r, r.Width \ 2, r.Height \ 2)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle, center As Point)
        DrawRadial(blend, r, center.X, center.Y)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle, cx As Integer, cy As Integer)
        DrawRadialPath.Reset()
        DrawRadialPath.AddEllipse(r.X, r.Y, r.Width - 1, r.Height - 1)

        DrawRadialBrush1 = New Drawing2D.PathGradientBrush(DrawRadialPath)
        DrawRadialBrush1.CenterPoint = New Point(r.X + cx, r.Y + cy)
        DrawRadialBrush1.InterpolationColors = blend

        If G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias Then
            G.FillEllipse(DrawRadialBrush1, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3)
        Else
            G.FillEllipse(DrawRadialBrush1, r)
        End If
    End Sub


    Protected Sub DrawRadial(x As Integer, y As Integer, width As Integer, height As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(DrawGradientRectangle)
    End Sub

    Protected Sub DrawRadial(r As Rectangle)
        'DrawRadialBrush2 = New Drawing2D.LinearGradientBrush(r, c1, c2, 90.0F)
        G.FillRectangle(DrawGradientBrush, r)
    End Sub

#End Region

#Region " CreateRound "

    Private CreateRoundPath As Drawing2D.GraphicsPath
    Private CreateRoundRectangle As Rectangle

    Function CreateRound(x As Integer, y As Integer, width As Integer, height As Integer, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundRectangle = New Rectangle(x, y, width, height)
        Return CreateRound(CreateRoundRectangle, slope)
    End Function

    Function CreateRound(r As Rectangle, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundPath = New Drawing2D.GraphicsPath(Drawing2D.FillMode.Winding)
        CreateRoundPath.AddArc(r.X, r.Y, slope, slope, 180.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Y, slope, slope, 270.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Bottom - slope, slope, slope, 0.0F, 90.0F)
        CreateRoundPath.AddArc(r.X, r.Bottom - slope, slope, slope, 90.0F, 90.0F)
        CreateRoundPath.CloseFigure()
        Return CreateRoundPath
    End Function

#End Region

End Class

#End Region

#Region " Theme Controls "

MustInherit Class ThemeControl154
    Inherits Control

#Region " Initialization "

    Protected G As Graphics, B As Bitmap

    Sub New()
        SetStyle(DirectCast(139270, ControlStyles), True)
        _ImageSize = Size.Empty
        Font = New Font("Verdana", 8S)
        MeasureBitmap = New Bitmap(1, 1)
        MeasureGraphics = Graphics.FromImage(MeasureBitmap)
        DrawRadialPath = New Drawing2D.GraphicsPath
        InvalidateCustimization() 'Remove?
    End Sub

    Protected NotOverridable Overrides Sub OnHandleCreated(e As EventArgs)
        InvalidateCustimization()
        ColorHook()
        If Not _LockWidth = 0 Then Width = _LockWidth
        If Not _LockHeight = 0 Then Height = _LockHeight
        Transparent = _Transparent
        If _Transparent AndAlso _BackColor Then BackColor = Color.Transparent
        MyBase.OnHandleCreated(e)
    End Sub

    Private DoneCreation As Boolean
    Protected NotOverridable Overrides Sub OnParentChanged(e As EventArgs)
        If Parent IsNot Nothing Then
            OnCreation()
            DoneCreation = True
            InvalidateTimer()
        End If
        MyBase.OnParentChanged(e)
    End Sub

    Private Sub DoAnimation(i As Boolean)
        OnAnimation()
        If i Then Invalidate()
    End Sub

    Protected NotOverridable Overrides Sub OnPaint(e As PaintEventArgs)
        If Width = 0 OrElse Height = 0 Then Return
        If _Transparent Then
            PaintHook()
            e.Graphics.DrawImage(B, 0, 0)
        Else
            G = e.Graphics
            PaintHook()
        End If
    End Sub

    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        RemoveAnimationCallback(AddressOf DoAnimation)
        MyBase.OnHandleDestroyed(e)
    End Sub

#End Region

#Region " Size Handling "

    Protected NotOverridable Overrides Sub OnSizeChanged(e As EventArgs)
        If _Transparent Then
            InvalidateBitmap()
        End If

        Invalidate()
        MyBase.OnSizeChanged(e)
    End Sub

    Protected Overrides Sub SetBoundsCore(x As Integer, y As Integer, width As Integer, height As Integer, specified As BoundsSpecified)
        If Not _LockWidth = 0 Then width = _LockWidth
        If Not _LockHeight = 0 Then height = _LockHeight
        MyBase.SetBoundsCore(x, y, width, height, specified)
    End Sub

#End Region

#Region " State Handling "

    Private InPosition As Boolean
    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        InPosition = True
        SetState(MouseState.Over)
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        If InPosition Then SetState(MouseState.Over)
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = Windows.Forms.MouseButtons.Left Then SetState(MouseState.Down)
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        InPosition = False
        SetState(MouseState.None)
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        If Enabled Then SetState(MouseState.None) Else SetState(MouseState.Block)
        MyBase.OnEnabledChanged(e)
    End Sub

    Protected State As MouseState
    Private Sub SetState(current As MouseState)
        State = current
        Invalidate()
    End Sub

#End Region

#Region " Base Properties "

    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property ForeColor() As Color
        Get
            Return Color.Empty
        End Get
        Set(value As Color)
        End Set
    End Property
    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property BackgroundImage() As Image
        Get
            Return Nothing
        End Get
        Set(value As Image)
        End Set
    End Property
    <ComponentModel.Browsable(False), ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never), ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
    Overrides Property BackgroundImageLayout() As ImageLayout
        Get
            Return ImageLayout.None
        End Get
        Set(value As ImageLayout)
        End Set
    End Property

    Overrides Property Text() As String
        Get
            Return MyBase.Text
        End Get
        Set(value As String)
            MyBase.Text = value
            Invalidate()
        End Set
    End Property
    Overrides Property Font() As Font
        Get
            Return MyBase.Font
        End Get
        Set(value As Font)
            MyBase.Font = value
            Invalidate()
        End Set
    End Property

    Private _BackColor As Boolean
    <ComponentModel.Category("Misc")> Overrides Property BackColor() As Color
        Get
            Return MyBase.BackColor
        End Get
        Set(value As Color)
            If Not IsHandleCreated AndAlso value = Color.Transparent Then
                _BackColor = True
                Return
            End If

            MyBase.BackColor = value
            If Parent IsNot Nothing Then ColorHook()
        End Set
    End Property

#End Region

#Region " Public Properties "

    Private _NoRounding As Boolean
    Property NoRounding() As Boolean
        Get
            Return _NoRounding
        End Get
        Set(value As Boolean)
            _NoRounding = value
            Invalidate()
        End Set
    End Property

    Private _Image As Image
    Property Image() As Image
        Get
            Return _Image
        End Get
        Set(value As Image)
            If value Is Nothing Then
                _ImageSize = Size.Empty
            Else
                _ImageSize = value.Size
            End If

            _Image = value
            Invalidate()
        End Set
    End Property

    Private _Transparent As Boolean
    Property Transparent() As Boolean
        Get
            Return _Transparent
        End Get
        Set(value As Boolean)
            _Transparent = value
            If Not IsHandleCreated Then Return

            If Not value AndAlso Not BackColor.A = 255 Then
                Throw New Exception("Unable to change value to false while a transparent BackColor is in use.")
            End If

            SetStyle(ControlStyles.Opaque, Not value)
            SetStyle(ControlStyles.SupportsTransparentBackColor, value)

            If value Then InvalidateBitmap() Else B = Nothing
            Invalidate()
        End Set
    End Property

    Private Items As New Dictionary(Of String, Color)
    Property Colors() As Bloom()
        Get
            Dim T As New List(Of Bloom)
            Dim E As Dictionary(Of String, Color).Enumerator = Items.GetEnumerator

            While E.MoveNext
                T.Add(New Bloom(E.Current.Key, E.Current.Value))
            End While

            Return T.ToArray
        End Get
        Set(value As Bloom())
            For Each B As Bloom In value
                If Items.ContainsKey(B.Name) Then Items(B.Name) = B.Value
            Next

            InvalidateCustimization()
            ColorHook()
            Invalidate()
        End Set
    End Property

    Private _Customization As String
    Property Customization() As String
        Get
            Return _Customization
        End Get
        Set(value As String)
            If value = _Customization Then Return

            Dim Data As Byte()
            Dim Items As Bloom() = Colors

            Try
                Data = Convert.FromBase64String(value)
                For I As Integer = 0 To Items.Length - 1
                    Items(I).Value = Color.FromArgb(BitConverter.ToInt32(Data, I * 4))
                Next
            Catch
                Return
            End Try

            _Customization = value

            Colors = Items
            ColorHook()
            Invalidate()
        End Set
    End Property

#End Region

#Region " Private Properties "

    Private _ImageSize As Size
    Protected ReadOnly Property ImageSize() As Size
        Get
            Return _ImageSize
        End Get
    End Property

    Private _LockWidth As Integer
    Protected Property LockWidth() As Integer
        Get
            Return _LockWidth
        End Get
        Set(value As Integer)
            _LockWidth = value
            If Not LockWidth = 0 AndAlso IsHandleCreated Then Width = LockWidth
        End Set
    End Property

    Private _LockHeight As Integer
    Protected Property LockHeight() As Integer
        Get
            Return _LockHeight
        End Get
        Set(value As Integer)
            _LockHeight = value
            If Not LockHeight = 0 AndAlso IsHandleCreated Then Height = LockHeight
        End Set
    End Property

    Private _IsAnimated As Boolean
    Protected Property IsAnimated() As Boolean
        Get
            Return _IsAnimated
        End Get
        Set(value As Boolean)
            _IsAnimated = value
            InvalidateTimer()
        End Set
    End Property

#End Region

#Region " Property Helpers "

    Protected Function GetPen(name As String) As Pen
        Return New Pen(Items(name))
    End Function
    Protected Function GetPen(name As String, width As Single) As Pen
        Return New Pen(Items(name), width)
    End Function

    Protected Function GetBrush(name As String) As SolidBrush
        Return New SolidBrush(Items(name))
    End Function

    Protected Function GetColor(name As String) As Color
        Return Items(name)
    End Function

    Protected Sub SetColor(name As String, value As Color)
        If Items.ContainsKey(name) Then Items(name) = value Else Items.Add(name, value)
    End Sub
    Protected Sub SetColor(name As String, r As Byte, g As Byte, b As Byte)
        SetColor(name, Color.FromArgb(r, g, b))
    End Sub
    Protected Sub SetColor(name As String, a As Byte, r As Byte, g As Byte, b As Byte)
        SetColor(name, Color.FromArgb(a, r, g, b))
    End Sub
    Protected Sub SetColor(name As String, a As Byte, value As Color)
        SetColor(name, Color.FromArgb(a, value))
    End Sub

    Private Sub InvalidateBitmap()
        If Width = 0 OrElse Height = 0 Then Return
        B = New Bitmap(Width, Height, Imaging.PixelFormat.Format32bppPArgb)
        G = Graphics.FromImage(B)
    End Sub

    Private Sub InvalidateCustimization()
        Dim M As New IO.MemoryStream(Items.Count * 4)

        For Each B As Bloom In Colors
            M.Write(BitConverter.GetBytes(B.Value.ToArgb), 0, 4)
        Next

        M.Close()
        _Customization = Convert.ToBase64String(M.ToArray)
    End Sub

    Private Sub InvalidateTimer()
        If DesignMode OrElse Not DoneCreation Then Return

        If _IsAnimated Then
            AddAnimationCallback(AddressOf DoAnimation)
        Else
            RemoveAnimationCallback(AddressOf DoAnimation)
        End If
    End Sub
#End Region

#Region " User Hooks "

    Protected MustOverride Sub ColorHook()
    Protected MustOverride Sub PaintHook()

    Protected Overridable Sub OnCreation()
    End Sub

    Protected Overridable Sub OnAnimation()
    End Sub

#End Region

#Region " Offset "

    Private OffsetReturnRectangle As Rectangle
    Protected Function Offset(r As Rectangle, amount As Integer) As Rectangle
        OffsetReturnRectangle = New Rectangle(r.X + amount, r.Y + amount, r.Width - (amount * 2), r.Height - (amount * 2))
        Return OffsetReturnRectangle
    End Function

    Private OffsetReturnSize As Size
    Protected Function Offset(s As Size, amount As Integer) As Size
        OffsetReturnSize = New Size(s.Width + amount, s.Height + amount)
        Return OffsetReturnSize
    End Function

    Private OffsetReturnPoint As Point
    Protected Function Offset(p As Point, amount As Integer) As Point
        OffsetReturnPoint = New Point(p.X + amount, p.Y + amount)
        Return OffsetReturnPoint
    End Function

#End Region

#Region " Center "

    Private CenterReturn As Point

    Protected Function Center(p As Rectangle, c As Rectangle) As Point
        CenterReturn = New Point((p.Width \ 2 - c.Width \ 2) + p.X + c.X, (p.Height \ 2 - c.Height \ 2) + p.Y + c.Y)
        Return CenterReturn
    End Function
    Protected Function Center(p As Rectangle, c As Size) As Point
        CenterReturn = New Point((p.Width \ 2 - c.Width \ 2) + p.X, (p.Height \ 2 - c.Height \ 2) + p.Y)
        Return CenterReturn
    End Function

    Protected Function Center(child As Rectangle) As Point
        Return Center(Width, Height, child.Width, child.Height)
    End Function
    Protected Function Center(child As Size) As Point
        Return Center(Width, Height, child.Width, child.Height)
    End Function
    Protected Function Center(childWidth As Integer, childHeight As Integer) As Point
        Return Center(Width, Height, childWidth, childHeight)
    End Function

    Protected Function Center(p As Size, c As Size) As Point
        Return Center(p.Width, p.Height, c.Width, c.Height)
    End Function

    Protected Function Center(pWidth As Integer, pHeight As Integer, cWidth As Integer, cHeight As Integer) As Point
        CenterReturn = New Point(pWidth \ 2 - cWidth \ 2, pHeight \ 2 - cHeight \ 2)
        Return CenterReturn
    End Function

#End Region

#Region " Measure "

    Private MeasureBitmap As Bitmap
    Private MeasureGraphics As Graphics 'TODO: Potential issues during multi-threading.

    Protected Function Measure() As Size
        Return MeasureGraphics.MeasureString(Text, Font, Width).ToSize
    End Function
    Protected Function Measure(text As String) As Size
        Return MeasureGraphics.MeasureString(text, Font, Width).ToSize
    End Function

#End Region

#Region " DrawPixel "

    Private DrawPixelBrush As SolidBrush

    Protected Sub DrawPixel(c1 As Color, x As Integer, y As Integer)
        If _Transparent Then
            B.SetPixel(x, y, c1)
        Else
            DrawPixelBrush = New SolidBrush(c1)
            G.FillRectangle(DrawPixelBrush, x, y, 1, 1)
        End If
    End Sub

#End Region

#Region " DrawCorners "

    Private DrawCornersBrush As SolidBrush

    Protected Sub DrawCorners(c1 As Color, offset As Integer)
        DrawCorners(c1, 0, 0, Width, Height, offset)
    End Sub
    Protected Sub DrawCorners(c1 As Color, r1 As Rectangle, offset As Integer)
        DrawCorners(c1, r1.X, r1.Y, r1.Width, r1.Height, offset)
    End Sub
    Protected Sub DrawCorners(c1 As Color, x As Integer, y As Integer, width As Integer, height As Integer, offset As Integer)
        DrawCorners(c1, x + offset, y + offset, width - (offset * 2), height - (offset * 2))
    End Sub

    Protected Sub DrawCorners(c1 As Color)
        DrawCorners(c1, 0, 0, Width, Height)
    End Sub
    Protected Sub DrawCorners(c1 As Color, r1 As Rectangle)
        DrawCorners(c1, r1.X, r1.Y, r1.Width, r1.Height)
    End Sub
    Protected Sub DrawCorners(c1 As Color, x As Integer, y As Integer, width As Integer, height As Integer)
        If _NoRounding Then Return

        If _Transparent Then
            B.SetPixel(x, y, c1)
            B.SetPixel(x + (width - 1), y, c1)
            B.SetPixel(x, y + (height - 1), c1)
            B.SetPixel(x + (width - 1), y + (height - 1), c1)
        Else
            DrawCornersBrush = New SolidBrush(c1)
            G.FillRectangle(DrawCornersBrush, x, y, 1, 1)
            G.FillRectangle(DrawCornersBrush, x + (width - 1), y, 1, 1)
            G.FillRectangle(DrawCornersBrush, x, y + (height - 1), 1, 1)
            G.FillRectangle(DrawCornersBrush, x + (width - 1), y + (height - 1), 1, 1)
        End If
    End Sub

#End Region

#Region " DrawBorders "

    Protected Sub DrawBorders(p1 As Pen, offset As Integer)
        DrawBorders(p1, 0, 0, Width, Height, offset)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, r As Rectangle, offset As Integer)
        DrawBorders(p1, r.X, r.Y, r.Width, r.Height, offset)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, x As Integer, y As Integer, width As Integer, height As Integer, offset As Integer)
        DrawBorders(p1, x + offset, y + offset, width - (offset * 2), height - (offset * 2))
    End Sub

    Protected Sub DrawBorders(p1 As Pen)
        DrawBorders(p1, 0, 0, Width, Height)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, r As Rectangle)
        DrawBorders(p1, r.X, r.Y, r.Width, r.Height)
    End Sub
    Protected Sub DrawBorders(p1 As Pen, x As Integer, y As Integer, width As Integer, height As Integer)
        G.DrawRectangle(p1, x, y, width - 1, height - 1)
    End Sub

#End Region

#Region " DrawText "

    Private DrawTextPoint As Point
    Private DrawTextSize As Size

    Protected Sub DrawText(b1 As Brush, a As HorizontalAlignment, x As Integer, y As Integer)
        DrawText(b1, Text, a, x, y)
    End Sub
    Protected Sub DrawText(b1 As Brush, text As String, a As HorizontalAlignment, x As Integer, y As Integer)
        If text.Length = 0 Then Return

        DrawTextSize = Measure(text)
        DrawTextPoint = Center(DrawTextSize)

        Select Case a
            Case HorizontalAlignment.Left
                G.DrawString(text, Font, b1, x, DrawTextPoint.Y + y)
            Case HorizontalAlignment.Center
                G.DrawString(text, Font, b1, DrawTextPoint.X + x, DrawTextPoint.Y + y)
            Case HorizontalAlignment.Right
                G.DrawString(text, Font, b1, Width - DrawTextSize.Width - x, DrawTextPoint.Y + y)
        End Select
    End Sub

    Protected Sub DrawText(b1 As Brush, p1 As Point)
        If Text.Length = 0 Then Return
        G.DrawString(Text, Font, b1, p1)
    End Sub
    Protected Sub DrawText(b1 As Brush, x As Integer, y As Integer)
        If Text.Length = 0 Then Return
        G.DrawString(Text, Font, b1, x, y)
    End Sub

#End Region

#Region " DrawImage "

    Private DrawImagePoint As Point

    Protected Sub DrawImage(a As HorizontalAlignment, x As Integer, y As Integer)
        DrawImage(_Image, a, x, y)
    End Sub
    Protected Sub DrawImage(image As Image, a As HorizontalAlignment, x As Integer, y As Integer)
        If image Is Nothing Then Return
        DrawImagePoint = Center(image.Size)

        Select Case a
            Case HorizontalAlignment.Left
                G.DrawImage(image, x, DrawImagePoint.Y + y, image.Width, image.Height)
            Case HorizontalAlignment.Center
                G.DrawImage(image, DrawImagePoint.X + x, DrawImagePoint.Y + y, image.Width, image.Height)
            Case HorizontalAlignment.Right
                G.DrawImage(image, Width - image.Width - x, DrawImagePoint.Y + y, image.Width, image.Height)
        End Select
    End Sub

    Protected Sub DrawImage(p1 As Point)
        DrawImage(_Image, p1.X, p1.Y)
    End Sub
    Protected Sub DrawImage(x As Integer, y As Integer)
        DrawImage(_Image, x, y)
    End Sub

    Protected Sub DrawImage(image As Image, p1 As Point)
        DrawImage(image, p1.X, p1.Y)
    End Sub
    Protected Sub DrawImage(image As Image, x As Integer, y As Integer)
        If image Is Nothing Then Return
        G.DrawImage(image, x, y, image.Width, image.Height)
    End Sub

#End Region

#Region " DrawGradient "

    Private DrawGradientBrush As Drawing2D.LinearGradientBrush
    Private DrawGradientRectangle As Rectangle

    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(blend, DrawGradientRectangle)
    End Sub
    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, angle As Single)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(blend, DrawGradientRectangle, angle)
    End Sub

    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, r As Rectangle)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, Color.Empty, Color.Empty, 90.0F)
        DrawGradientBrush.InterpolationColors = blend
        G.FillRectangle(DrawGradientBrush, r)
    End Sub
    Protected Sub DrawGradient(blend As Drawing2D.ColorBlend, r As Rectangle, angle As Single)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, Color.Empty, Color.Empty, angle)
        DrawGradientBrush.InterpolationColors = blend
        G.FillRectangle(DrawGradientBrush, r)
    End Sub


    Protected Sub DrawGradient(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(c1, c2, DrawGradientRectangle)
    End Sub
    Protected Sub DrawGradient(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer, angle As Single)
        DrawGradientRectangle = New Rectangle(x, y, width, height)
        DrawGradient(c1, c2, DrawGradientRectangle, angle)
    End Sub

    Protected Sub DrawGradient(c1 As Color, c2 As Color, r As Rectangle)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, c1, c2, 90.0F)
        G.FillRectangle(DrawGradientBrush, r)
    End Sub
    Protected Sub DrawGradient(c1 As Color, c2 As Color, r As Rectangle, angle As Single)
        DrawGradientBrush = New Drawing2D.LinearGradientBrush(r, c1, c2, angle)
        G.FillRectangle(DrawGradientBrush, r)
    End Sub

#End Region

#Region " DrawRadial "

    Private DrawRadialPath As Drawing2D.GraphicsPath
    Private DrawRadialBrush1 As Drawing2D.PathGradientBrush
    Private DrawRadialBrush2 As Drawing2D.LinearGradientBrush
    Private DrawRadialRectangle As Rectangle

    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, width \ 2, height \ 2)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, center As Point)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, center.X, center.Y)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, x As Integer, y As Integer, width As Integer, height As Integer, cx As Integer, cy As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(blend, DrawRadialRectangle, cx, cy)
    End Sub

    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle)
        DrawRadial(blend, r, r.Width \ 2, r.Height \ 2)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle, center As Point)
        DrawRadial(blend, r, center.X, center.Y)
    End Sub
    Sub DrawRadial(blend As Drawing2D.ColorBlend, r As Rectangle, cx As Integer, cy As Integer)
        DrawRadialPath.Reset()
        DrawRadialPath.AddEllipse(r.X, r.Y, r.Width - 1, r.Height - 1)

        DrawRadialBrush1 = New Drawing2D.PathGradientBrush(DrawRadialPath)
        DrawRadialBrush1.CenterPoint = New Point(r.X + cx, r.Y + cy)
        DrawRadialBrush1.InterpolationColors = blend

        If G.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias Then
            G.FillEllipse(DrawRadialBrush1, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3)
        Else
            G.FillEllipse(DrawRadialBrush1, r)
        End If
    End Sub


    Protected Sub DrawRadial(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(c1, c2, DrawRadialRectangle)
    End Sub
    Protected Sub DrawRadial(c1 As Color, c2 As Color, x As Integer, y As Integer, width As Integer, height As Integer, angle As Single)
        DrawRadialRectangle = New Rectangle(x, y, width, height)
        DrawRadial(c1, c2, DrawRadialRectangle, angle)
    End Sub

    Protected Sub DrawRadial(c1 As Color, c2 As Color, r As Rectangle)
        DrawRadialBrush2 = New Drawing2D.LinearGradientBrush(r, c1, c2, 90.0F)
        G.FillEllipse(DrawRadialBrush2, r)
    End Sub
    Protected Sub DrawRadial(c1 As Color, c2 As Color, r As Rectangle, angle As Single)
        DrawRadialBrush2 = New Drawing2D.LinearGradientBrush(r, c1, c2, angle)
        G.FillEllipse(DrawRadialBrush2, r)
    End Sub

#End Region

#Region " CreateRound "

    Private CreateRoundPath As Drawing2D.GraphicsPath
    Private CreateRoundRectangle As Rectangle

    Function CreateRound(x As Integer, y As Integer, width As Integer, height As Integer, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundRectangle = New Rectangle(x, y, width, height)
        Return CreateRound(CreateRoundRectangle, slope)
    End Function

    Function CreateRound(r As Rectangle, slope As Integer) As Drawing2D.GraphicsPath
        CreateRoundPath = New Drawing2D.GraphicsPath(Drawing2D.FillMode.Winding)
        CreateRoundPath.AddArc(r.X, r.Y, slope, slope, 180.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Y, slope, slope, 270.0F, 90.0F)
        CreateRoundPath.AddArc(r.Right - slope, r.Bottom - slope, slope, slope, 0.0F, 90.0F)
        CreateRoundPath.AddArc(r.X, r.Bottom - slope, slope, slope, 90.0F, 90.0F)
        CreateRoundPath.CloseFigure()
        Return CreateRoundPath
    End Function

#End Region

End Class

#End Region

#Region " ThemeShare "

Module ThemeShare

    Private Frames As Integer
    Private Invalidate As Boolean
    Public ThemeTimer As New PrecisionTimer

    Private Const FPS As Integer = 50 '1000 / 50 = 20 FPS
    Private Const Rate As Integer = 10

    Public Delegate Sub AnimationDelegate(invalidate As Boolean)

    Private Callbacks As New List(Of AnimationDelegate)

    Private Sub HandleCallbacks(state As IntPtr, reserve As Boolean)
        Invalidate = (Frames >= FPS)
        If Invalidate Then Frames = 0

        SyncLock Callbacks
            For I As Integer = 0 To Callbacks.Count - 1
                Callbacks(I).Invoke(Invalidate)
            Next
        End SyncLock

        Frames += Rate
    End Sub

    Private Sub InvalidateThemeTimer()
        If Callbacks.Count = 0 Then
            ThemeTimer.Delete()
        Else
            ThemeTimer.Create(0, Rate, AddressOf HandleCallbacks)
        End If
    End Sub

    Sub AddAnimationCallback(callback As AnimationDelegate)
        SyncLock Callbacks
            If Callbacks.Contains(callback) Then Return

            Callbacks.Add(callback)
            InvalidateThemeTimer()
        End SyncLock
    End Sub

    Sub RemoveAnimationCallback(callback As AnimationDelegate)
        SyncLock Callbacks
            If Not Callbacks.Contains(callback) Then Return

            Callbacks.Remove(callback)
            InvalidateThemeTimer()
        End SyncLock
    End Sub

End Module

#End Region

#Region " Properties "

Enum MouseState As Byte
    None = 0
    Over = 1
    Down = 2
    Block = 3
End Enum

Structure Bloom

    Public _Name As String
    ReadOnly Property Name() As String
        Get
            Return _Name
        End Get
    End Property

    Private _Value As Color
    Property Value() As Color
        Get
            Return _Value
        End Get
        Set(value As Color)
            _Value = value
        End Set
    End Property

    Property ValueHex() As String
        Get
            Return String.Concat("#", _Value.R.ToString("X2", Nothing), _Value.G.ToString("X2", Nothing), _Value.B.ToString("X2", Nothing))
        End Get
        Set(value As String)
            Try
                _Value = ColorTranslator.FromHtml(value)
            Catch
                Return
            End Try
        End Set
    End Property


    Sub New(name As String, value As Color)
        _Name = name
        _Value = value
    End Sub
End Structure

#End Region

#Region " PrecisionTimer "

Class PrecisionTimer
    Implements IDisposable

    Private _Enabled As Boolean
    ReadOnly Property Enabled() As Boolean
        Get
            Return _Enabled
        End Get
    End Property

    Private Handle As IntPtr
    Private TimerCallback As TimerDelegate

    <Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint:="CreateTimerQueueTimer")> Private Shared Function CreateTimerQueueTimer(ByRef handle As IntPtr, queue As IntPtr, callback As TimerDelegate, state As IntPtr, dueTime As UInteger, period As UInteger, flags As UInteger) As Boolean
    End Function

    <Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint:="DeleteTimerQueueTimer")> Private Shared Function DeleteTimerQueueTimer(queue As IntPtr, handle As IntPtr, callback As IntPtr) As Boolean
    End Function

    Delegate Sub TimerDelegate(r1 As IntPtr, r2 As Boolean)

    Sub Create(dueTime As UInteger, period As UInteger, callback As TimerDelegate)
        If _Enabled Then Return

        TimerCallback = callback
        Dim Success As Boolean = CreateTimerQueueTimer(Handle, IntPtr.Zero, TimerCallback, IntPtr.Zero, dueTime, period, 0)

        If Not Success Then ThrowNewException("CreateTimerQueueTimer")
        _Enabled = Success
    End Sub

    Sub Delete()
        If Not _Enabled Then Return
        Dim Success As Boolean = DeleteTimerQueueTimer(IntPtr.Zero, Handle, IntPtr.Zero)

        If Not Success AndAlso Not Runtime.InteropServices.Marshal.GetLastWin32Error = 997 Then
            ThrowNewException("DeleteTimerQueueTimer")
        End If

        _Enabled = Not Success
    End Sub

    Private Sub ThrowNewException(name As String)
        Throw New Exception(String.Format("{0} failed. Win32Error: {1}", name, Runtime.InteropServices.Marshal.GetLastWin32Error))
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Delete()
    End Sub

End Class

#End Region

#End Region

#Region " UnsafeNativeMethods "

Module UnsafeNativeMethods

    <Runtime.InteropServices.DllImport("user32.dll", CharSet:=Runtime.InteropServices.CharSet.Auto, ExactSpelling:=True)> Public Function SetForegroundWindow(hWnd As Runtime.InteropServices.HandleRef) As Boolean : End Function
    'File Type
    <Runtime.InteropServices.DllImport("shell32.dll")> Public Function SHGetFileInfo(pszPath As String, dwFileAttributes As UInteger, ByRef psfi As SHFILEINFO, cbSizeFileInfo As UInteger, uFlags As UInteger) As IntPtr : End Function
    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)> Public Structure SHFILEINFO : Public hIcon As IntPtr : Public iIcon As Integer : Public dwAttributes As UInteger : <Runtime.InteropServices.MarshalAs(Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst:=260)> Public szDisplayName As String : <Runtime.InteropServices.MarshalAs(Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst:=80)> Public szTypeName As String : End Structure
    Public NotInheritable Class FILE_ATTRIBUTE : Public Const FILE_ATTRIBUTE_NORMAL As UInteger = &H80 : End Class
    Public NotInheritable Class SHGFI : Public Const SHGFI_TYPENAME As UInteger = &H400 : Public Const SHGFI_USEFILEATTRIBUTES As UInteger = &H10 : End Class

    Public Function FileType(Ruta As String) As String
        Try : Dim info As New SHFILEINFO()
            SHGetFileInfo(Ruta, FILE_ATTRIBUTE.FILE_ATTRIBUTE_NORMAL, info, Runtime.InteropServices.Marshal.SizeOf(info), SHGFI.SHGFI_TYPENAME Or SHGFI.SHGFI_USEFILEATTRIBUTES)
            If info.szTypeName = "Archivo 0" Then Return "Archivo" Else Return info.szTypeName
        Catch : End Try : Return "Desconocido"
    End Function

End Module

#End Region

#End Region
