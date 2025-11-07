<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FSerialGenerator
    Inherits System.Windows.Forms.Form

    'Form reemplaza a Dispose para limpiar la lista de componentes.
    <System.Diagnostics.DebuggerNonUserCode()>
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
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FSerialGenerator))
        Me.FTheme = New KDesktop.NSTheme()
        Me.TFecha = New KDesktop.NSDateTime()
        Me.BCerrarc = New KDesktop.NSButton()
        Me.BAplicar = New KDesktop.NSButton()
        Me.BGenerar = New KDesktop.NSButton()
        Me.LSerial = New KDesktop.NSLabel()
        Me.LValidez = New KDesktop.NSLabel()
        Me.LEmail = New KDesktop.NSLabel()
        Me.TSerial = New KDesktop.NSTextBox()
        Me.TEmail = New KDesktop.NSTextBox()
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
        Me.FTheme.Controls.Add(Me.TFecha)
        Me.FTheme.Controls.Add(Me.BCerrarc)
        Me.FTheme.Controls.Add(Me.BAplicar)
        Me.FTheme.Controls.Add(Me.BGenerar)
        Me.FTheme.Controls.Add(Me.LSerial)
        Me.FTheme.Controls.Add(Me.LValidez)
        Me.FTheme.Controls.Add(Me.LEmail)
        Me.FTheme.Controls.Add(Me.TSerial)
        Me.FTheme.Controls.Add(Me.TEmail)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Customization = ""
        Me.FTheme.Dock = System.Windows.Forms.DockStyle.Fill
        Me.FTheme.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.FTheme.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.FTheme.ForeColor2 = System.Drawing.Color.Black
        Me.FTheme.Image = CType(resources.GetObject("FTheme.Image"), System.Drawing.Image)
        Me.FTheme.Location = New System.Drawing.Point(0, 0)
        Me.FTheme.Movable = True
        Me.FTheme.Name = "FTheme"
        Me.FTheme.NoRounding = False
        Me.FTheme.Sizable = False
        Me.FTheme.Size = New System.Drawing.Size(248, 230)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Key Generator"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'TFecha
        '
        Me.TFecha.Format = System.Windows.Forms.DateTimePickerFormat.[Short]
        Me.TFecha.Location = New System.Drawing.Point(63, 71)
        Me.TFecha.Name = "TFecha"
        Me.TFecha.Size = New System.Drawing.Size(94, 20)
        Me.TFecha.TabIndex = 161
        '
        'BCerrarc
        '
        Me.BCerrarc.ALT = False
        Me.BCerrarc.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BCerrarc.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCerrarc.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCerrarc.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCerrarc.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrarc.BorderColor2 = System.Drawing.Color.Red
        Me.BCerrarc.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BCerrarc.CTRL = False
        Me.BCerrarc.DoubleText = True
        Me.BCerrarc.FirstLetterColor = System.Drawing.Color.White
        Me.BCerrarc.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BCerrarc.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BCerrarc.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrarc.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrarc.GradientAngle = 90
        Me.BCerrarc.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BCerrarc.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BCerrarc.GradientTransparency = 0
        Me.BCerrarc.Image = Nothing
        Me.BCerrarc.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BCerrarc.ImageCode = ""
        Me.BCerrarc.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BCerrarc.ImageSize = New System.Drawing.Size(19, 19)
        Me.BCerrarc.Location = New System.Drawing.Point(167, 191)
        Me.BCerrarc.Marked = False
        Me.BCerrarc.Marked_Set = False
        Me.BCerrarc.MarkedHovered = False
        Me.BCerrarc.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrarc.Name = "BCerrarc"
        Me.BCerrarc.Rounded = 7
        Me.BCerrarc.SHIFT = False
        Me.BCerrarc.Size = New System.Drawing.Size(60, 26)
        Me.BCerrarc.TabIndex = 159
        Me.BCerrarc.Text = "Cerrar"
        Me.BCerrarc.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BCerrarc.WIN = False
        '
        'BAplicar
        '
        Me.BAplicar.ALT = False
        Me.BAplicar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BAplicar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAplicar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAplicar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BAplicar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BAplicar.BorderColor2 = System.Drawing.Color.Red
        Me.BAplicar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BAplicar.CTRL = False
        Me.BAplicar.DoubleText = True
        Me.BAplicar.FirstLetterColor = System.Drawing.Color.White
        Me.BAplicar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BAplicar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BAplicar.ForeColor2 = System.Drawing.Color.Black
        Me.BAplicar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAplicar.GradientAngle = 90
        Me.BAplicar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BAplicar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BAplicar.GradientTransparency = 0
        Me.BAplicar.Image = Nothing
        Me.BAplicar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BAplicar.ImageCode = ""
        Me.BAplicar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BAplicar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BAplicar.Location = New System.Drawing.Point(21, 191)
        Me.BAplicar.Marked = False
        Me.BAplicar.Marked_Set = False
        Me.BAplicar.MarkedHovered = False
        Me.BAplicar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAplicar.Name = "BAplicar"
        Me.BAplicar.Rounded = 7
        Me.BAplicar.SHIFT = False
        Me.BAplicar.Size = New System.Drawing.Size(60, 26)
        Me.BAplicar.TabIndex = 159
        Me.BAplicar.Text = "Aplicar"
        Me.BAplicar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BAplicar.WIN = False
        '
        'BGenerar
        '
        Me.BGenerar.ALT = False
        Me.BGenerar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BGenerar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BGenerar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BGenerar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BGenerar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BGenerar.BorderColor2 = System.Drawing.Color.Red
        Me.BGenerar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BGenerar.CTRL = False
        Me.BGenerar.DoubleText = True
        Me.BGenerar.FirstLetterColor = System.Drawing.Color.White
        Me.BGenerar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BGenerar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BGenerar.ForeColor2 = System.Drawing.Color.Black
        Me.BGenerar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BGenerar.GradientAngle = 90
        Me.BGenerar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BGenerar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BGenerar.GradientTransparency = 0
        Me.BGenerar.Image = Nothing
        Me.BGenerar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BGenerar.ImageCode = ""
        Me.BGenerar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BGenerar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BGenerar.Location = New System.Drawing.Point(94, 191)
        Me.BGenerar.Marked = False
        Me.BGenerar.Marked_Set = False
        Me.BGenerar.MarkedHovered = False
        Me.BGenerar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BGenerar.Name = "BGenerar"
        Me.BGenerar.Rounded = 7
        Me.BGenerar.SHIFT = False
        Me.BGenerar.Size = New System.Drawing.Size(60, 26)
        Me.BGenerar.TabIndex = 159
        Me.BGenerar.Text = "Generar"
        Me.BGenerar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BGenerar.WIN = False
        '
        'LSerial
        '
        Me.LSerial.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LSerial.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LSerial.Location = New System.Drawing.Point(15, 97)
        Me.LSerial.Name = "LSerial"
        Me.LSerial.Size = New System.Drawing.Size(56, 23)
        Me.LSerial.TabIndex = 157
        Me.LSerial.TabStop = False
        Me.LSerial.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LSerial.Value1 = "L"
        Me.LSerial.Value2 = "icencia:"
        '
        'LValidez
        '
        Me.LValidez.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LValidez.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LValidez.Location = New System.Drawing.Point(15, 71)
        Me.LValidez.Name = "LValidez"
        Me.LValidez.Size = New System.Drawing.Size(56, 23)
        Me.LValidez.TabIndex = 157
        Me.LValidez.TabStop = False
        Me.LValidez.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LValidez.Value1 = "V"
        Me.LValidez.Value2 = "álido:"
        '
        'LEmail
        '
        Me.LEmail.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LEmail.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LEmail.Location = New System.Drawing.Point(18, 42)
        Me.LEmail.Name = "LEmail"
        Me.LEmail.Size = New System.Drawing.Size(41, 23)
        Me.LEmail.TabIndex = 157
        Me.LEmail.TabStop = False
        Me.LEmail.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LEmail.Value1 = "E"
        Me.LEmail.Value2 = "mail:"
        '
        'TSerial
        '
        Me.TSerial.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TSerial.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TSerial.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TSerial.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TSerial.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSerial.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSerial.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSerial.HideSelection = False
        Me.TSerial.Location = New System.Drawing.Point(15, 119)
        Me.TSerial.MaxLength = 255
        Me.TSerial.Multiline = True
        Me.TSerial.Name = "TSerial"
        Me.TSerial.ReadOnly = True
        Me.TSerial.Rounded = 1
        Me.TSerial.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TSerial.SelectedText = ""
        Me.TSerial.SelectionLength = 0
        Me.TSerial.SelectionStart = 0
        Me.TSerial.ShortcutsEnabled = True
        Me.TSerial.Size = New System.Drawing.Size(218, 61)
        Me.TSerial.TabIndex = 156
        Me.TSerial.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TSerial.UseSystemPasswordChar = False
        Me.TSerial.WordWrap = False
        '
        'TEmail
        '
        Me.TEmail.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TEmail.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TEmail.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TEmail.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TEmail.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TEmail.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TEmail.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TEmail.HideSelection = False
        Me.TEmail.Location = New System.Drawing.Point(62, 42)
        Me.TEmail.MaxLength = 255
        Me.TEmail.Multiline = False
        Me.TEmail.Name = "TEmail"
        Me.TEmail.ReadOnly = False
        Me.TEmail.Rounded = 1
        Me.TEmail.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TEmail.SelectedText = ""
        Me.TEmail.SelectionLength = 0
        Me.TEmail.SelectionStart = 0
        Me.TEmail.ShortcutsEnabled = True
        Me.TEmail.Size = New System.Drawing.Size(171, 23)
        Me.TEmail.TabIndex = 156
        Me.TEmail.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TEmail.UseSystemPasswordChar = False
        Me.TEmail.WordWrap = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(222, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 154
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
        Me.HTexto.Location = New System.Drawing.Point(119, 128)
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
        Me.HTexto.TabIndex = 160
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'FSerialGenerator
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(248, 230)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FSerialGenerator"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "FSerialGenerator"
        Me.FTheme.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents LSerial As NSLabel
    Friend WithEvents LValidez As NSLabel
    Friend WithEvents LEmail As NSLabel
    Friend WithEvents TSerial As NSTextBox
    Friend WithEvents TEmail As NSTextBox
    Friend WithEvents BAplicar As NSButton
    Friend WithEvents BGenerar As NSButton
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents BCerrarc As NSButton
    Friend WithEvents TFecha As NSDateTime
End Class
