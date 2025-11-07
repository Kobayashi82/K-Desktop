<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FKLink
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FKLink))
        Me.FTheme = New KDesktop.NSTheme()
        Me.LHelp = New KDesktop.NSLabel()
        Me.BExaminar_Guardar = New KDesktop.NSButton()
        Me.BExaminar_Carpeta = New KDesktop.NSButton()
        Me.LGuardar = New KDesktop.NSLabel()
        Me.LArchivo = New KDesktop.NSLabel()
        Me.LCarpeta = New KDesktop.NSLabel()
        Me.TGuardar = New KDesktop.NSTextBox()
        Me.TCarpeta = New KDesktop.NSTextBox()
        Me.BCancelar = New KDesktop.NSButton()
        Me.BAceptar = New KDesktop.NSButton()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.FTheme.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.LHelp)
        Me.FTheme.Controls.Add(Me.BExaminar_Guardar)
        Me.FTheme.Controls.Add(Me.BExaminar_Carpeta)
        Me.FTheme.Controls.Add(Me.LGuardar)
        Me.FTheme.Controls.Add(Me.LArchivo)
        Me.FTheme.Controls.Add(Me.LCarpeta)
        Me.FTheme.Controls.Add(Me.TGuardar)
        Me.FTheme.Controls.Add(Me.TCarpeta)
        Me.FTheme.Controls.Add(Me.BCancelar)
        Me.FTheme.Controls.Add(Me.BAceptar)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.HTexto)
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
        Me.FTheme.Size = New System.Drawing.Size(373, 198)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "K-Link"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'LHelp
        '
        Me.LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.LHelp.Location = New System.Drawing.Point(331, 5)
        Me.LHelp.Name = "LHelp"
        Me.LHelp.Size = New System.Drawing.Size(14, 17)
        Me.LHelp.TabIndex = 153
        Me.LHelp.Text = "?"
        Me.LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LHelp.Value1 = ""
        Me.LHelp.Value2 = ""
        '
        'BExaminar_Guardar
        '
        Me.BExaminar_Guardar.ALT = False
        Me.BExaminar_Guardar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BExaminar_Guardar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Guardar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Guardar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Guardar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BExaminar_Guardar.BorderColor2 = System.Drawing.Color.Red
        Me.BExaminar_Guardar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BExaminar_Guardar.CTRL = False
        Me.BExaminar_Guardar.DoubleText = True
        Me.BExaminar_Guardar.FirstLetterColor = System.Drawing.Color.White
        Me.BExaminar_Guardar.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.BExaminar_Guardar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BExaminar_Guardar.ForeColor2 = System.Drawing.Color.Black
        Me.BExaminar_Guardar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Guardar.GradientAngle = 90
        Me.BExaminar_Guardar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BExaminar_Guardar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BExaminar_Guardar.GradientTransparency = 0
        Me.BExaminar_Guardar.Image = Nothing
        Me.BExaminar_Guardar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BExaminar_Guardar.ImageCode = ""
        Me.BExaminar_Guardar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BExaminar_Guardar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BExaminar_Guardar.Location = New System.Drawing.Point(326, 114)
        Me.BExaminar_Guardar.Marked = False
        Me.BExaminar_Guardar.Marked_Set = False
        Me.BExaminar_Guardar.MarkedHovered = False
        Me.BExaminar_Guardar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Guardar.Name = "BExaminar_Guardar"
        Me.BExaminar_Guardar.Rounded = 1
        Me.BExaminar_Guardar.SHIFT = False
        Me.BExaminar_Guardar.Size = New System.Drawing.Size(20, 21)
        Me.BExaminar_Guardar.TabIndex = 151
        Me.BExaminar_Guardar.TabStop = False
        Me.BExaminar_Guardar.Text = "..."
        Me.BExaminar_Guardar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BExaminar_Guardar.WIN = False
        '
        'BExaminar_Carpeta
        '
        Me.BExaminar_Carpeta.ALT = False
        Me.BExaminar_Carpeta.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BExaminar_Carpeta.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Carpeta.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Carpeta.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Carpeta.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BExaminar_Carpeta.BorderColor2 = System.Drawing.Color.Red
        Me.BExaminar_Carpeta.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BExaminar_Carpeta.CTRL = False
        Me.BExaminar_Carpeta.DoubleText = True
        Me.BExaminar_Carpeta.FirstLetterColor = System.Drawing.Color.White
        Me.BExaminar_Carpeta.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.BExaminar_Carpeta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BExaminar_Carpeta.ForeColor2 = System.Drawing.Color.Black
        Me.BExaminar_Carpeta.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Carpeta.GradientAngle = 90
        Me.BExaminar_Carpeta.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BExaminar_Carpeta.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BExaminar_Carpeta.GradientTransparency = 0
        Me.BExaminar_Carpeta.Image = Nothing
        Me.BExaminar_Carpeta.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BExaminar_Carpeta.ImageCode = ""
        Me.BExaminar_Carpeta.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BExaminar_Carpeta.ImageSize = New System.Drawing.Size(19, 19)
        Me.BExaminar_Carpeta.Location = New System.Drawing.Point(326, 63)
        Me.BExaminar_Carpeta.Marked = False
        Me.BExaminar_Carpeta.Marked_Set = False
        Me.BExaminar_Carpeta.MarkedHovered = False
        Me.BExaminar_Carpeta.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Carpeta.Name = "BExaminar_Carpeta"
        Me.BExaminar_Carpeta.Rounded = 1
        Me.BExaminar_Carpeta.SHIFT = False
        Me.BExaminar_Carpeta.Size = New System.Drawing.Size(20, 21)
        Me.BExaminar_Carpeta.TabIndex = 151
        Me.BExaminar_Carpeta.TabStop = False
        Me.BExaminar_Carpeta.Text = "..."
        Me.BExaminar_Carpeta.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BExaminar_Carpeta.WIN = False
        '
        'LGuardar
        '
        Me.LGuardar.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LGuardar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LGuardar.Location = New System.Drawing.Point(34, 94)
        Me.LGuardar.Name = "LGuardar"
        Me.LGuardar.Size = New System.Drawing.Size(74, 17)
        Me.LGuardar.TabIndex = 118
        Me.LGuardar.TabStop = False
        Me.LGuardar.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LGuardar.Value1 = "G"
        Me.LGuardar.Value2 = "uardar en"
        '
        'LArchivo
        '
        Me.LArchivo.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LArchivo.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LArchivo.Location = New System.Drawing.Point(97, 43)
        Me.LArchivo.Name = "LArchivo"
        Me.LArchivo.Size = New System.Drawing.Size(66, 17)
        Me.LArchivo.TabIndex = 118
        Me.LArchivo.TabStop = False
        Me.LArchivo.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LArchivo.Value1 = "A"
        Me.LArchivo.Value2 = "rchivo"
        '
        'LCarpeta
        '
        Me.LCarpeta.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LCarpeta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LCarpeta.Location = New System.Drawing.Point(34, 43)
        Me.LCarpeta.Name = "LCarpeta"
        Me.LCarpeta.Size = New System.Drawing.Size(66, 17)
        Me.LCarpeta.TabIndex = 118
        Me.LCarpeta.TabStop = False
        Me.LCarpeta.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LCarpeta.Value1 = "C"
        Me.LCarpeta.Value2 = "arpeta o"
        '
        'TGuardar
        '
        Me.TGuardar.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TGuardar.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TGuardar.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TGuardar.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TGuardar.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TGuardar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TGuardar.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TGuardar.HideSelection = False
        Me.TGuardar.Location = New System.Drawing.Point(31, 113)
        Me.TGuardar.MaxLength = 255
        Me.TGuardar.Multiline = False
        Me.TGuardar.Name = "TGuardar"
        Me.TGuardar.ReadOnly = False
        Me.TGuardar.Rounded = 1
        Me.TGuardar.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TGuardar.SelectedText = ""
        Me.TGuardar.SelectionLength = 0
        Me.TGuardar.SelectionStart = 0
        Me.TGuardar.ShortcutsEnabled = True
        Me.TGuardar.Size = New System.Drawing.Size(297, 23)
        Me.TGuardar.TabIndex = 116
        Me.TGuardar.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TGuardar.UseSystemPasswordChar = False
        Me.TGuardar.WordWrap = False
        '
        'TCarpeta
        '
        Me.TCarpeta.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TCarpeta.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TCarpeta.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TCarpeta.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TCarpeta.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TCarpeta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TCarpeta.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TCarpeta.HideSelection = False
        Me.TCarpeta.Location = New System.Drawing.Point(31, 62)
        Me.TCarpeta.MaxLength = 255
        Me.TCarpeta.Multiline = False
        Me.TCarpeta.Name = "TCarpeta"
        Me.TCarpeta.ReadOnly = False
        Me.TCarpeta.Rounded = 1
        Me.TCarpeta.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TCarpeta.SelectedText = ""
        Me.TCarpeta.SelectionLength = 0
        Me.TCarpeta.SelectionStart = 0
        Me.TCarpeta.ShortcutsEnabled = True
        Me.TCarpeta.Size = New System.Drawing.Size(297, 23)
        Me.TCarpeta.TabIndex = 116
        Me.TCarpeta.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TCarpeta.UseSystemPasswordChar = False
        Me.TCarpeta.WordWrap = False
        '
        'BCancelar
        '
        Me.BCancelar.ALT = False
        Me.BCancelar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BCancelar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCancelar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCancelar.BorderColor2 = System.Drawing.Color.Red
        Me.BCancelar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BCancelar.CTRL = False
        Me.BCancelar.DoubleText = True
        Me.BCancelar.FirstLetterColor = System.Drawing.Color.White
        Me.BCancelar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BCancelar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
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
        Me.BCancelar.Location = New System.Drawing.Point(215, 153)
        Me.BCancelar.Marked = False
        Me.BCancelar.Marked_Set = False
        Me.BCancelar.MarkedHovered = False
        Me.BCancelar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.Name = "BCancelar"
        Me.BCancelar.Rounded = 7
        Me.BCancelar.SHIFT = False
        Me.BCancelar.Size = New System.Drawing.Size(97, 27)
        Me.BCancelar.TabIndex = 18
        Me.BCancelar.Text = "Cancelar"
        Me.BCancelar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BCancelar.WIN = False
        '
        'BAceptar
        '
        Me.BAceptar.ALT = False
        Me.BAceptar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BAceptar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAceptar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BAceptar.BorderColor2 = System.Drawing.Color.Red
        Me.BAceptar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BAceptar.CTRL = False
        Me.BAceptar.DoubleText = True
        Me.BAceptar.FirstLetterColor = System.Drawing.Color.White
        Me.BAceptar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BAceptar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
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
        Me.BAceptar.Location = New System.Drawing.Point(61, 153)
        Me.BAceptar.Marked = False
        Me.BAceptar.Marked_Set = False
        Me.BAceptar.MarkedHovered = False
        Me.BAceptar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.Name = "BAceptar"
        Me.BAceptar.Rounded = 7
        Me.BAceptar.SHIFT = False
        Me.BAceptar.Size = New System.Drawing.Size(97, 27)
        Me.BAceptar.TabIndex = 17
        Me.BAceptar.Text = "Aceptar"
        Me.BAceptar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BAceptar.WIN = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(347, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 3
        Me.BCerrar.Text = "NsControlButton1"
        '
        'HTexto
        '
        Me.HTexto.BorderColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.HTexto.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.HTexto.Cursor = System.Windows.Forms.Cursors.Default
        Me.HTexto.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.HideSelection = False
        Me.HTexto.Location = New System.Drawing.Point(193, 68)
        Me.HTexto.MaxLength = 32767
        Me.HTexto.Multiline = False
        Me.HTexto.Name = "HTexto"
        Me.HTexto.ReadOnly = False
        Me.HTexto.Rounded = 7
        Me.HTexto.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.HTexto.SelectedText = ""
        Me.HTexto.SelectionLength = 0
        Me.HTexto.SelectionStart = 0
        Me.HTexto.ShortcutsEnabled = False
        Me.HTexto.Size = New System.Drawing.Size(10, 10)
        Me.HTexto.TabIndex = 152
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'FKLink
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(373, 198)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "FKLink"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "K-Link"
        Me.FTheme.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents BCancelar As NSButton
    Friend WithEvents BAceptar As NSButton
    Friend WithEvents LGuardar As NSLabel
    Friend WithEvents LCarpeta As NSLabel
    Friend WithEvents TGuardar As NSTextBox
    Friend WithEvents TCarpeta As NSTextBox
    Friend WithEvents BExaminar_Guardar As NSButton
    Friend WithEvents BExaminar_Carpeta As NSButton
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents LHelp As NSLabel
    Friend WithEvents LArchivo As NSLabel
End Class
