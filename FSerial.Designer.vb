<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FSerial
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FSerial))
        Me.FTheme = New KDesktop.NSTheme()
        Me.LHelp = New KDesktop.NSLabel()
        Me.LSerialEspecial = New System.Windows.Forms.Label()
        Me.Serial_LHelp = New KDesktop.NSLabel()
        Me.BCancelar = New KDesktop.NSButton()
        Me.BAceptar = New KDesktop.NSButton()
        Me.TNombre = New KDesktop.NSTextBox()
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
        Me.FTheme.Controls.Add(Me.LSerialEspecial)
        Me.FTheme.Controls.Add(Me.Serial_LHelp)
        Me.FTheme.Controls.Add(Me.BCancelar)
        Me.FTheme.Controls.Add(Me.BAceptar)
        Me.FTheme.Controls.Add(Me.TNombre)
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
        Me.FTheme.Size = New System.Drawing.Size(248, 155)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 1
        Me.FTheme.Text = "Número de Serie"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'LHelp
        '
        Me.LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.LHelp.Location = New System.Drawing.Point(206, 5)
        Me.LHelp.Name = "LHelp"
        Me.LHelp.Size = New System.Drawing.Size(14, 17)
        Me.LHelp.TabIndex = 156
        Me.LHelp.Text = "?"
        Me.LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LHelp.Value1 = ""
        Me.LHelp.Value2 = ""
        '
        'LSerialEspecial
        '
        Me.LSerialEspecial.Location = New System.Drawing.Point(138, 19)
        Me.LSerialEspecial.Name = "LSerialEspecial"
        Me.LSerialEspecial.Size = New System.Drawing.Size(25, 6)
        Me.LSerialEspecial.TabIndex = 162
        '
        'Serial_LHelp
        '
        Me.Serial_LHelp.Font = New System.Drawing.Font("Segoe UI", 11.25!, System.Drawing.FontStyle.Bold)
        Me.Serial_LHelp.ForeColor1 = System.Drawing.Color.Gray
        Me.Serial_LHelp.Location = New System.Drawing.Point(348, 3)
        Me.Serial_LHelp.Name = "Serial_LHelp"
        Me.Serial_LHelp.Size = New System.Drawing.Size(14, 17)
        Me.Serial_LHelp.TabIndex = 154
        Me.Serial_LHelp.Text = "?"
        Me.Serial_LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.Serial_LHelp.Value1 = ""
        Me.Serial_LHelp.Value2 = ""
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
        Me.BCancelar.Location = New System.Drawing.Point(155, 117)
        Me.BCancelar.Marked = False
        Me.BCancelar.Marked_Set = False
        Me.BCancelar.MarkedHovered = False
        Me.BCancelar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.Name = "BCancelar"
        Me.BCancelar.Rounded = 7
        Me.BCancelar.SHIFT = False
        Me.BCancelar.Size = New System.Drawing.Size(60, 26)
        Me.BCancelar.TabIndex = 2
        Me.BCancelar.TabStop = False
        Me.BCancelar.Text = "Cerrar"
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
        Me.BAceptar.Location = New System.Drawing.Point(40, 117)
        Me.BAceptar.Marked = False
        Me.BAceptar.Marked_Set = False
        Me.BAceptar.MarkedHovered = False
        Me.BAceptar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.Name = "BAceptar"
        Me.BAceptar.Rounded = 7
        Me.BAceptar.SHIFT = False
        Me.BAceptar.Size = New System.Drawing.Size(60, 26)
        Me.BAceptar.TabIndex = 1
        Me.BAceptar.TabStop = False
        Me.BAceptar.Text = "Aceptar"
        Me.BAceptar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BAceptar.WIN = False
        '
        'TNombre
        '
        Me.TNombre.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TNombre.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TNombre.Cursor = System.Windows.Forms.Cursors.Default
        Me.TNombre.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.HideSelection = False
        Me.TNombre.Location = New System.Drawing.Point(15, 44)
        Me.TNombre.MaxLength = 1000
        Me.TNombre.Multiline = True
        Me.TNombre.Name = "TNombre"
        Me.TNombre.ReadOnly = False
        Me.TNombre.Rounded = 7
        Me.TNombre.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TNombre.SelectedText = ""
        Me.TNombre.SelectionLength = 0
        Me.TNombre.SelectionStart = 0
        Me.TNombre.ShortcutsEnabled = True
        Me.TNombre.Size = New System.Drawing.Size(218, 61)
        Me.TNombre.TabIndex = 0
        Me.TNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TNombre.UseSystemPasswordChar = False
        Me.TNombre.WordWrap = True
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
        Me.HTexto.Location = New System.Drawing.Point(119, 72)
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
        Me.HTexto.TabIndex = 161
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'FSerial
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(248, 155)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "FSerial"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Número de Serie"
        Me.FTheme.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCancelar As NSButton
    Friend WithEvents BAceptar As NSButton
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents Serial_LHelp As NSLabel
    Friend WithEvents LHelp As NSLabel
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents LSerialEspecial As Label
End Class
