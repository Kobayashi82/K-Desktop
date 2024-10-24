<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FFirstRun
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FFirstRun))
        Me.FTheme = New KDesktop.NSTheme()
        Me.Separador1 = New KDesktop.NSSeperator()
        Me.LLabel3 = New System.Windows.Forms.Label()
        Me.LLabel2 = New System.Windows.Forms.Label()
        Me.LRegistrar2 = New KDesktop.NSLabel()
        Me.LTrial2 = New KDesktop.NSLabel()
        Me.LRegistrar1 = New KDesktop.NSLabel()
        Me.LTrial1 = New KDesktop.NSLabel()
        Me.LLabel1 = New KDesktop.NSLabel()
        Me.PLogo = New System.Windows.Forms.PictureBox()
        Me.BRegistrar = New KDesktop.NSButton()
        Me.BTrial = New KDesktop.NSButton()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.FTheme.SuspendLayout()
        CType(Me.PLogo, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.Separador1)
        Me.FTheme.Controls.Add(Me.LLabel3)
        Me.FTheme.Controls.Add(Me.LLabel2)
        Me.FTheme.Controls.Add(Me.LRegistrar2)
        Me.FTheme.Controls.Add(Me.LTrial2)
        Me.FTheme.Controls.Add(Me.LRegistrar1)
        Me.FTheme.Controls.Add(Me.LTrial1)
        Me.FTheme.Controls.Add(Me.LLabel1)
        Me.FTheme.Controls.Add(Me.PLogo)
        Me.FTheme.Controls.Add(Me.BRegistrar)
        Me.FTheme.Controls.Add(Me.BTrial)
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
        Me.FTheme.Size = New System.Drawing.Size(296, 460)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "KDesktop 4.0"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'Separador1
        '
        Me.Separador1.Cross = False
        Me.Separador1.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador1.Glowing = False
        Me.Separador1.LineColor = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.Separador1.Location = New System.Drawing.Point(29, 359)
        Me.Separador1.Name = "Separador1"
        Me.Separador1.Size = New System.Drawing.Size(237, 10)
        Me.Separador1.TabIndex = 172
        Me.Separador1.Text = "NsSeperator1"
        Me.Separador1.VerticalLine = False
        '
        'LLabel3
        '
        Me.LLabel3.Font = New System.Drawing.Font("Verdana", 6.5!)
        Me.LLabel3.ForeColor = System.Drawing.Color.DarkGray
        Me.LLabel3.Location = New System.Drawing.Point(16, 235)
        Me.LLabel3.Name = "LLabel3"
        Me.LLabel3.Size = New System.Drawing.Size(270, 116)
        Me.LLabel3.TabIndex = 171
        Me.LLabel3.Text = resources.GetString("LLabel3.Text")
        '
        'LLabel2
        '
        Me.LLabel2.ForeColor = System.Drawing.Color.DarkGray
        Me.LLabel2.Location = New System.Drawing.Point(15, 193)
        Me.LLabel2.Name = "LLabel2"
        Me.LLabel2.Size = New System.Drawing.Size(270, 29)
        Me.LLabel2.TabIndex = 171
        Me.LLabel2.Text = "La nueva versión de KDesktop incluye muchas mejoras..."
        '
        'LRegistrar2
        '
        Me.LRegistrar2.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LRegistrar2.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LRegistrar2.Location = New System.Drawing.Point(151, 393)
        Me.LRegistrar2.Name = "LRegistrar2"
        Me.LRegistrar2.Size = New System.Drawing.Size(135, 18)
        Me.LRegistrar2.TabIndex = 170
        Me.LRegistrar2.Text = "con un número de serie"
        Me.LRegistrar2.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LRegistrar2.Value1 = "NET"
        Me.LRegistrar2.Value2 = "SEAL"
        '
        'LTrial2
        '
        Me.LTrial2.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LTrial2.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LTrial2.Location = New System.Drawing.Point(26, 393)
        Me.LTrial2.Name = "LTrial2"
        Me.LTrial2.Size = New System.Drawing.Size(93, 18)
        Me.LTrial2.TabIndex = 170
        Me.LTrial2.Text = "durante 15 días"
        Me.LTrial2.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LTrial2.Value1 = "NET"
        Me.LTrial2.Value2 = "SEAL"
        '
        'LRegistrar1
        '
        Me.LRegistrar1.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LRegistrar1.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LRegistrar1.Location = New System.Drawing.Point(154, 375)
        Me.LRegistrar1.Name = "LRegistrar1"
        Me.LRegistrar1.Size = New System.Drawing.Size(126, 18)
        Me.LRegistrar1.TabIndex = 170
        Me.LRegistrar1.Text = "Registra KDesktop 4.0"
        Me.LRegistrar1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LRegistrar1.Value1 = "NET"
        Me.LRegistrar1.Value2 = "SEAL"
        '
        'LTrial1
        '
        Me.LTrial1.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LTrial1.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LTrial1.Location = New System.Drawing.Point(12, 375)
        Me.LTrial1.Name = "LTrial1"
        Me.LTrial1.Size = New System.Drawing.Size(126, 18)
        Me.LTrial1.TabIndex = 170
        Me.LTrial1.Text = "Prueba KDesktop 4.0"
        Me.LTrial1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LTrial1.Value1 = "NET"
        Me.LTrial1.Value2 = "SEAL"
        '
        'LLabel1
        '
        Me.LLabel1.Font = New System.Drawing.Font("Segoe UI", 11.25!, System.Drawing.FontStyle.Bold)
        Me.LLabel1.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LLabel1.Location = New System.Drawing.Point(47, 160)
        Me.LLabel1.Name = "LLabel1"
        Me.LLabel1.Size = New System.Drawing.Size(197, 23)
        Me.LLabel1.TabIndex = 170
        Me.LLabel1.Text = "Bienvenido a KDesktop 4.0"
        Me.LLabel1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LLabel1.Value1 = "NET"
        Me.LLabel1.Value2 = "SEAL"
        '
        'PLogo
        '
        Me.PLogo.Image = CType(resources.GetObject("PLogo.Image"), System.Drawing.Image)
        Me.PLogo.Location = New System.Drawing.Point(11, 36)
        Me.PLogo.Name = "PLogo"
        Me.PLogo.Size = New System.Drawing.Size(275, 117)
        Me.PLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PLogo.TabIndex = 169
        Me.PLogo.TabStop = False
        '
        'BRegistrar
        '
        Me.BRegistrar.ALT = False
        Me.BRegistrar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BRegistrar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BRegistrar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BRegistrar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BRegistrar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BRegistrar.BorderColor2 = System.Drawing.Color.Red
        Me.BRegistrar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BRegistrar.CTRL = False
        Me.BRegistrar.DoubleText = True
        Me.BRegistrar.FirstLetterColor = System.Drawing.Color.White
        Me.BRegistrar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BRegistrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BRegistrar.ForeColor2 = System.Drawing.Color.Black
        Me.BRegistrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BRegistrar.GradientAngle = 90
        Me.BRegistrar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BRegistrar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BRegistrar.GradientTransparency = 0
        Me.BRegistrar.Image = Nothing
        Me.BRegistrar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BRegistrar.ImageCode = ""
        Me.BRegistrar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BRegistrar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BRegistrar.Location = New System.Drawing.Point(154, 420)
        Me.BRegistrar.Marked = False
        Me.BRegistrar.Marked_Set = False
        Me.BRegistrar.MarkedHovered = False
        Me.BRegistrar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BRegistrar.Name = "BRegistrar"
        Me.BRegistrar.Rounded = 7
        Me.BRegistrar.SHIFT = False
        Me.BRegistrar.Size = New System.Drawing.Size(126, 26)
        Me.BRegistrar.TabIndex = 6
        Me.BRegistrar.TabStop = False
        Me.BRegistrar.Text = "Registrar KDesktop"
        Me.BRegistrar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BTrial
        '
        Me.BTrial.ALT = False
        Me.BTrial.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BTrial.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BTrial.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BTrial.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BTrial.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BTrial.BorderColor2 = System.Drawing.Color.Red
        Me.BTrial.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BTrial.CTRL = False
        Me.BTrial.DoubleText = True
        Me.BTrial.FirstLetterColor = System.Drawing.Color.White
        Me.BTrial.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BTrial.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BTrial.ForeColor2 = System.Drawing.Color.Black
        Me.BTrial.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BTrial.GradientAngle = 90
        Me.BTrial.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BTrial.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BTrial.GradientTransparency = 0
        Me.BTrial.Image = Nothing
        Me.BTrial.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BTrial.ImageCode = ""
        Me.BTrial.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BTrial.ImageSize = New System.Drawing.Size(19, 19)
        Me.BTrial.Location = New System.Drawing.Point(12, 420)
        Me.BTrial.Marked = False
        Me.BTrial.Marked_Set = False
        Me.BTrial.MarkedHovered = False
        Me.BTrial.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BTrial.Name = "BTrial"
        Me.BTrial.Rounded = 7
        Me.BTrial.SHIFT = False
        Me.BTrial.Size = New System.Drawing.Size(126, 26)
        Me.BTrial.TabIndex = 5
        Me.BTrial.TabStop = False
        Me.BTrial.Text = "Periodo de Prueba"
        Me.BTrial.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(272, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 4
        Me.BCerrar.Text = "NsControlButton1"
        '
        'FFirstRun
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(296, 460)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FFirstRun"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "KDesktop 4.0"
        Me.FTheme.ResumeLayout(False)
        CType(Me.PLogo, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents BRegistrar As NSButton
    Friend WithEvents BTrial As NSButton
    Friend WithEvents PLogo As PictureBox
    Friend WithEvents LLabel3 As Label
    Friend WithEvents LLabel2 As Label
    Friend WithEvents LLabel1 As NSLabel
    Friend WithEvents Separador1 As NSSeperator
    Friend WithEvents LRegistrar2 As NSLabel
    Friend WithEvents LTrial2 As NSLabel
    Friend WithEvents LRegistrar1 As NSLabel
    Friend WithEvents LTrial1 As NSLabel
End Class
