<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FIconDialog
    Inherits System.Windows.Forms.Form

    'Form reemplaza a Dispose para limpiar la lista de componentes.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Requerido por el Diseñador de Windows Forms
    Private components As System.ComponentModel.IContainer

    'NOTA: el Diseñador de Windows Forms necesita el siguiente procedimiento
    'Se puede modificar usando el Diseñador de Windows Forms.  
    'No lo modifique con el editor de código.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FIconDialog))
        Me.FTheme = New KDesktop.NSTheme()
        Me.LHelp = New KDesktop.NSLabel()
        Me.PCargando = New System.Windows.Forms.Panel()
        Me.LCargando = New KDesktop.NSLabel()
        Me.BCancelar = New KDesktop.NSButton()
        Me.BAceptar = New KDesktop.NSButton()
        Me.BExaminar = New KDesktop.NSButton()
        Me.BIconosKDesktop = New KDesktop.NSButton()
        Me.BIconosSistema = New KDesktop.NSButton()
        Me.BIconosWindows = New KDesktop.NSButton()
        Me.Lista = New System.Windows.Forms.ListView()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.FTheme.SuspendLayout()
        Me.PCargando.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.LHelp)
        Me.FTheme.Controls.Add(Me.PCargando)
        Me.FTheme.Controls.Add(Me.BCancelar)
        Me.FTheme.Controls.Add(Me.BAceptar)
        Me.FTheme.Controls.Add(Me.BExaminar)
        Me.FTheme.Controls.Add(Me.BIconosKDesktop)
        Me.FTheme.Controls.Add(Me.BIconosSistema)
        Me.FTheme.Controls.Add(Me.BIconosWindows)
        Me.FTheme.Controls.Add(Me.Lista)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Customization = ""
        Me.FTheme.Dock = System.Windows.Forms.DockStyle.Fill
        Me.FTheme.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.FTheme.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.FTheme.ForeColor2 = System.Drawing.Color.Black
        Me.FTheme.Image = Nothing
        Me.FTheme.Location = New System.Drawing.Point(0, 0)
        Me.FTheme.Movable = True
        Me.FTheme.Name = "FTheme"
        Me.FTheme.NoRounding = False
        Me.FTheme.Sizable = False
        Me.FTheme.Size = New System.Drawing.Size(578, 293)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Seleccionar Icono"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'LHelp
        '
        Me.LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.LHelp.Location = New System.Drawing.Point(535, 5)
        Me.LHelp.Name = "LHelp"
        Me.LHelp.Size = New System.Drawing.Size(14, 17)
        Me.LHelp.TabIndex = 130
        Me.LHelp.Text = "?"
        Me.LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LHelp.Value1 = ""
        Me.LHelp.Value2 = ""
        '
        'PCargando
        '
        Me.PCargando.Controls.Add(Me.LCargando)
        Me.PCargando.Location = New System.Drawing.Point(13, 36)
        Me.PCargando.Name = "PCargando"
        Me.PCargando.Size = New System.Drawing.Size(553, 244)
        Me.PCargando.TabIndex = 27
        '
        'LCargando
        '
        Me.LCargando.Font = New System.Drawing.Font("Segoe UI", 11.25!, System.Drawing.FontStyle.Bold)
        Me.LCargando.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LCargando.Location = New System.Drawing.Point(216, 104)
        Me.LCargando.Name = "LCargando"
        Me.LCargando.Size = New System.Drawing.Size(96, 25)
        Me.LCargando.TabIndex = 26
        Me.LCargando.Text = "Cargando..."
        Me.LCargando.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LCargando.Value1 = "NET"
        Me.LCargando.Value2 = "SEAL"
        '
        'BCancelar
        '
        Me.BCancelar.ALT = False
        Me.BCancelar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BCancelar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCancelar.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BCancelar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BCancelar.CTRL = False
        Me.BCancelar.DoubleText = True
        Me.BCancelar.FirstLetterColor = System.Drawing.Color.White
        Me.BCancelar.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BCancelar.ForeColor1 = System.Drawing.Color.DimGray
        Me.BCancelar.ForeColor2 = System.Drawing.Color.Black
        Me.BCancelar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.GradientAngle = 90
        Me.BCancelar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BCancelar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BCancelar.GradientTransparency = 0
        Me.BCancelar.Image = Nothing
        Me.BCancelar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BCancelar.ImageCode = ""
        Me.BCancelar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BCancelar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BCancelar.Location = New System.Drawing.Point(111, 239)
        Me.BCancelar.Marked = False
        Me.BCancelar.Marked_Set = False
        Me.BCancelar.MarkedHovered = False
        Me.BCancelar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.Name = "BCancelar"
        Me.BCancelar.Rounded = 1
        Me.BCancelar.SHIFT = False
        Me.BCancelar.Size = New System.Drawing.Size(93, 41)
        Me.BCancelar.TabIndex = 25
        Me.BCancelar.Text = "Cancelar"
        Me.BCancelar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BAceptar
        '
        Me.BAceptar.ALT = False
        Me.BAceptar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BAceptar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BAceptar.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BAceptar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BAceptar.CTRL = False
        Me.BAceptar.DoubleText = True
        Me.BAceptar.FirstLetterColor = System.Drawing.Color.White
        Me.BAceptar.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BAceptar.ForeColor1 = System.Drawing.Color.DimGray
        Me.BAceptar.ForeColor2 = System.Drawing.Color.Black
        Me.BAceptar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.GradientAngle = 90
        Me.BAceptar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BAceptar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BAceptar.GradientTransparency = 0
        Me.BAceptar.Image = Nothing
        Me.BAceptar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BAceptar.ImageCode = ""
        Me.BAceptar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BAceptar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BAceptar.Location = New System.Drawing.Point(13, 239)
        Me.BAceptar.Marked = False
        Me.BAceptar.Marked_Set = False
        Me.BAceptar.MarkedHovered = False
        Me.BAceptar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.Name = "BAceptar"
        Me.BAceptar.Rounded = 1
        Me.BAceptar.SHIFT = False
        Me.BAceptar.Size = New System.Drawing.Size(93, 41)
        Me.BAceptar.TabIndex = 25
        Me.BAceptar.Text = "Aceptar"
        Me.BAceptar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BExaminar
        '
        Me.BExaminar.ALT = False
        Me.BExaminar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BExaminar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BExaminar.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BExaminar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BExaminar.CTRL = False
        Me.BExaminar.DoubleText = True
        Me.BExaminar.FirstLetterColor = System.Drawing.Color.White
        Me.BExaminar.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BExaminar.ForeColor1 = System.Drawing.Color.DimGray
        Me.BExaminar.ForeColor2 = System.Drawing.Color.Black
        Me.BExaminar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar.GradientAngle = 90
        Me.BExaminar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BExaminar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BExaminar.GradientTransparency = 0
        Me.BExaminar.Image = Nothing
        Me.BExaminar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BExaminar.ImageCode = ""
        Me.BExaminar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BExaminar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BExaminar.Location = New System.Drawing.Point(13, 177)
        Me.BExaminar.Marked = False
        Me.BExaminar.Marked_Set = False
        Me.BExaminar.MarkedHovered = False
        Me.BExaminar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar.Name = "BExaminar"
        Me.BExaminar.Rounded = 1
        Me.BExaminar.SHIFT = False
        Me.BExaminar.Size = New System.Drawing.Size(191, 41)
        Me.BExaminar.TabIndex = 25
        Me.BExaminar.Text = "Examinar..."
        Me.BExaminar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BIconosKDesktop
        '
        Me.BIconosKDesktop.ALT = False
        Me.BIconosKDesktop.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BIconosKDesktop.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosKDesktop.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosKDesktop.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosKDesktop.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BIconosKDesktop.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BIconosKDesktop.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BIconosKDesktop.CTRL = False
        Me.BIconosKDesktop.DoubleText = True
        Me.BIconosKDesktop.FirstLetterColor = System.Drawing.Color.White
        Me.BIconosKDesktop.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BIconosKDesktop.ForeColor1 = System.Drawing.Color.DimGray
        Me.BIconosKDesktop.ForeColor2 = System.Drawing.Color.Black
        Me.BIconosKDesktop.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosKDesktop.GradientAngle = 90
        Me.BIconosKDesktop.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BIconosKDesktop.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BIconosKDesktop.GradientTransparency = 0
        Me.BIconosKDesktop.Image = Nothing
        Me.BIconosKDesktop.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BIconosKDesktop.ImageCode = ""
        Me.BIconosKDesktop.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BIconosKDesktop.ImageSize = New System.Drawing.Size(19, 19)
        Me.BIconosKDesktop.Location = New System.Drawing.Point(13, 130)
        Me.BIconosKDesktop.Marked = False
        Me.BIconosKDesktop.Marked_Set = False
        Me.BIconosKDesktop.MarkedHovered = False
        Me.BIconosKDesktop.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosKDesktop.Name = "BIconosKDesktop"
        Me.BIconosKDesktop.Rounded = 1
        Me.BIconosKDesktop.SHIFT = False
        Me.BIconosKDesktop.Size = New System.Drawing.Size(191, 41)
        Me.BIconosKDesktop.TabIndex = 25
        Me.BIconosKDesktop.Text = "Iconos de KDesktop"
        Me.BIconosKDesktop.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BIconosSistema
        '
        Me.BIconosSistema.ALT = False
        Me.BIconosSistema.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BIconosSistema.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosSistema.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosSistema.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosSistema.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BIconosSistema.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BIconosSistema.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BIconosSistema.CTRL = False
        Me.BIconosSistema.DoubleText = True
        Me.BIconosSistema.FirstLetterColor = System.Drawing.Color.White
        Me.BIconosSistema.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BIconosSistema.ForeColor1 = System.Drawing.Color.DimGray
        Me.BIconosSistema.ForeColor2 = System.Drawing.Color.Black
        Me.BIconosSistema.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosSistema.GradientAngle = 90
        Me.BIconosSistema.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BIconosSistema.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BIconosSistema.GradientTransparency = 0
        Me.BIconosSistema.Image = Nothing
        Me.BIconosSistema.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BIconosSistema.ImageCode = ""
        Me.BIconosSistema.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BIconosSistema.ImageSize = New System.Drawing.Size(19, 19)
        Me.BIconosSistema.Location = New System.Drawing.Point(13, 83)
        Me.BIconosSistema.Marked = False
        Me.BIconosSistema.Marked_Set = False
        Me.BIconosSistema.MarkedHovered = False
        Me.BIconosSistema.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosSistema.Name = "BIconosSistema"
        Me.BIconosSistema.Rounded = 1
        Me.BIconosSistema.SHIFT = False
        Me.BIconosSistema.Size = New System.Drawing.Size(191, 41)
        Me.BIconosSistema.TabIndex = 25
        Me.BIconosSistema.Text = "Iconos del Sistema"
        Me.BIconosSistema.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BIconosWindows
        '
        Me.BIconosWindows.ALT = False
        Me.BIconosWindows.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BIconosWindows.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosWindows.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosWindows.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BIconosWindows.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BIconosWindows.BorderColor2 = System.Drawing.Color.DarkGreen
        Me.BIconosWindows.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BIconosWindows.CTRL = False
        Me.BIconosWindows.DoubleText = True
        Me.BIconosWindows.FirstLetterColor = System.Drawing.Color.White
        Me.BIconosWindows.Font = New System.Drawing.Font("Verdana", 7.0!)
        Me.BIconosWindows.ForeColor1 = System.Drawing.Color.DimGray
        Me.BIconosWindows.ForeColor2 = System.Drawing.Color.Black
        Me.BIconosWindows.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosWindows.GradientAngle = 90
        Me.BIconosWindows.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BIconosWindows.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BIconosWindows.GradientTransparency = 0
        Me.BIconosWindows.Image = Nothing
        Me.BIconosWindows.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BIconosWindows.ImageCode = ""
        Me.BIconosWindows.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BIconosWindows.ImageSize = New System.Drawing.Size(19, 19)
        Me.BIconosWindows.Location = New System.Drawing.Point(13, 36)
        Me.BIconosWindows.Marked = False
        Me.BIconosWindows.Marked_Set = False
        Me.BIconosWindows.MarkedHovered = False
        Me.BIconosWindows.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BIconosWindows.Name = "BIconosWindows"
        Me.BIconosWindows.Rounded = 1
        Me.BIconosWindows.SHIFT = False
        Me.BIconosWindows.Size = New System.Drawing.Size(191, 41)
        Me.BIconosWindows.TabIndex = 25
        Me.BIconosWindows.Text = "Iconos de Windows"
        Me.BIconosWindows.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'Lista
        '
        Me.Lista.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.Lista.ForeColor = System.Drawing.Color.DimGray
        Me.Lista.HideSelection = False
        Me.Lista.Location = New System.Drawing.Point(216, 36)
        Me.Lista.Name = "Lista"
        Me.Lista.Size = New System.Drawing.Size(350, 244)
        Me.Lista.TabIndex = 13
        Me.Lista.UseCompatibleStateImageBehavior = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(552, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 12
        Me.BCerrar.Text = "NsControlButton1"
        '
        'FIconDialog
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(578, 293)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "FIconDialog"
        Me.ShowIcon = False
        Me.ShowInTaskbar = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Seleccionar Icono"
        Me.FTheme.ResumeLayout(False)
        Me.PCargando.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents Lista As ListView
    Friend WithEvents BCancelar As NSButton
    Friend WithEvents BAceptar As NSButton
    Friend WithEvents BExaminar As NSButton
    Friend WithEvents BIconosKDesktop As NSButton
    Friend WithEvents BIconosSistema As NSButton
    Friend WithEvents BIconosWindows As NSButton
    Friend WithEvents PCargando As Panel
    Friend WithEvents LCargando As NSLabel
    Friend WithEvents LHelp As NSLabel
End Class
