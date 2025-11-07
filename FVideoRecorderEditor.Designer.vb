<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FVideoRecorderEditor
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FVideoRecorderEditor))
        Me.FTheme = New KDesktop.NSTheme()
        Me.PAceptar = New System.Windows.Forms.PictureBox()
        Me.PCerrar = New System.Windows.Forms.PictureBox()
        Me.GrabacionPantallaEditor_LHelp = New KDesktop.NSLabel()
        Me.GPlayer1 = New KDesktop.NSGroupBox()
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.EditingPanel = New System.Windows.Forms.Panel()
        Me.EditingLabel = New KDesktop.NSLabel()
        Me.SCropTop = New KDesktop.NSSeperatorCrop()
        Me.SCropBottom = New KDesktop.NSSeperatorCrop()
        Me.SCropLeft = New KDesktop.NSSeperatorCrop()
        Me.SCropRight = New KDesktop.NSSeperatorCrop()
        Me.PCropTop = New System.Windows.Forms.Panel()
        Me.PCropBottom = New System.Windows.Forms.Panel()
        Me.PCropLeft = New System.Windows.Forms.Panel()
        Me.PCropRight = New System.Windows.Forms.Panel()
        Me.Player1 = New AxWMPLib.AxWindowsMediaPlayer()
        Me.Progreso = New KDesktop.NSProgressBar()
        Me.BPlay = New System.Windows.Forms.Label()
        Me.BClose = New KDesktop.NSControlButton()
        Me.Trim_Right = New KDesktop.NSSeperatorCrop()
        Me.Trim_Left = New KDesktop.NSSeperatorCrop()
        Me.FTheme.SuspendLayout()
        CType(Me.PAceptar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.GPlayer1.SuspendLayout()
        Me.Panel1.SuspendLayout()
        Me.EditingPanel.SuspendLayout()
        CType(Me.Player1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.PAceptar)
        Me.FTheme.Controls.Add(Me.PCerrar)
        Me.FTheme.Controls.Add(Me.GrabacionPantallaEditor_LHelp)
        Me.FTheme.Controls.Add(Me.GPlayer1)
        Me.FTheme.Controls.Add(Me.Progreso)
        Me.FTheme.Controls.Add(Me.BPlay)
        Me.FTheme.Controls.Add(Me.BClose)
        Me.FTheme.Controls.Add(Me.Trim_Right)
        Me.FTheme.Controls.Add(Me.Trim_Left)
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
        Me.FTheme.Size = New System.Drawing.Size(999, 636)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Editar Grabación"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'PAceptar
        '
        Me.PAceptar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PAceptar.Image = Global.KDesktop.My.Resources.Resources.Aceptar_Off
        Me.PAceptar.Location = New System.Drawing.Point(928, 605)
        Me.PAceptar.Name = "PAceptar"
        Me.PAceptar.Size = New System.Drawing.Size(23, 23)
        Me.PAceptar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PAceptar.TabIndex = 135
        Me.PAceptar.TabStop = False
        '
        'PCerrar
        '
        Me.PCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCerrar.Image = Global.KDesktop.My.Resources.Resources.Cerrar_Off
        Me.PCerrar.Location = New System.Drawing.Point(957, 605)
        Me.PCerrar.Name = "PCerrar"
        Me.PCerrar.Size = New System.Drawing.Size(23, 23)
        Me.PCerrar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCerrar.TabIndex = 135
        Me.PCerrar.TabStop = False
        '
        'GrabacionPantallaEditor_LHelp
        '
        Me.GrabacionPantallaEditor_LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GrabacionPantallaEditor_LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.GrabacionPantallaEditor_LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.GrabacionPantallaEditor_LHelp.Location = New System.Drawing.Point(956, 5)
        Me.GrabacionPantallaEditor_LHelp.Name = "GrabacionPantallaEditor_LHelp"
        Me.GrabacionPantallaEditor_LHelp.Size = New System.Drawing.Size(14, 17)
        Me.GrabacionPantallaEditor_LHelp.TabIndex = 130
        Me.GrabacionPantallaEditor_LHelp.Text = "?"
        Me.GrabacionPantallaEditor_LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.GrabacionPantallaEditor_LHelp.Value1 = ""
        Me.GrabacionPantallaEditor_LHelp.Value2 = ""
        '
        'GPlayer1
        '
        Me.GPlayer1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GPlayer1.Controls.Add(Me.Panel1)
        Me.GPlayer1.DrawSeperator = False
        Me.GPlayer1.Location = New System.Drawing.Point(11, 40)
        Me.GPlayer1.Name = "GPlayer1"
        Me.GPlayer1.Size = New System.Drawing.Size(975, 559)
        Me.GPlayer1.SubTitle = ""
        Me.GPlayer1.TabIndex = 53
        Me.GPlayer1.Title = ""
        '
        'Panel1
        '
        Me.Panel1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Panel1.Controls.Add(Me.EditingPanel)
        Me.Panel1.Controls.Add(Me.SCropTop)
        Me.Panel1.Controls.Add(Me.SCropBottom)
        Me.Panel1.Controls.Add(Me.SCropLeft)
        Me.Panel1.Controls.Add(Me.SCropRight)
        Me.Panel1.Controls.Add(Me.PCropTop)
        Me.Panel1.Controls.Add(Me.PCropBottom)
        Me.Panel1.Controls.Add(Me.PCropLeft)
        Me.Panel1.Controls.Add(Me.PCropRight)
        Me.Panel1.Controls.Add(Me.Player1)
        Me.Panel1.Location = New System.Drawing.Point(3, 3)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(969, 553)
        Me.Panel1.TabIndex = 53
        '
        'EditingPanel
        '
        Me.EditingPanel.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.EditingPanel.BackColor = System.Drawing.Color.Black
        Me.EditingPanel.Controls.Add(Me.EditingLabel)
        Me.EditingPanel.Location = New System.Drawing.Point(4, 6)
        Me.EditingPanel.Name = "EditingPanel"
        Me.EditingPanel.Size = New System.Drawing.Size(960, 542)
        Me.EditingPanel.TabIndex = 70
        Me.EditingPanel.Visible = False
        '
        'EditingLabel
        '
        Me.EditingLabel.Anchor = System.Windows.Forms.AnchorStyles.None
        Me.EditingLabel.Font = New System.Drawing.Font("Segoe UI", 20.0!, System.Drawing.FontStyle.Bold)
        Me.EditingLabel.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.EditingLabel.Location = New System.Drawing.Point(369, 210)
        Me.EditingLabel.Name = "EditingLabel"
        Me.EditingLabel.Size = New System.Drawing.Size(192, 93)
        Me.EditingLabel.TabIndex = 0
        Me.EditingLabel.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.EditingLabel.Value1 = "P"
        Me.EditingLabel.Value2 = "rocesando..."
        Me.EditingLabel.Visible = False
        '
        'SCropTop
        '
        Me.SCropTop.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SCropTop.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropTop.Cross = False
        Me.SCropTop.Cursor = System.Windows.Forms.Cursors.SizeNS
        Me.SCropTop.GlowColor = System.Drawing.Color.DarkGreen
        Me.SCropTop.Glowing = False
        Me.SCropTop.InvertedLine = False
        Me.SCropTop.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropTop.Location = New System.Drawing.Point(0, 1)
        Me.SCropTop.Name = "SCropTop"
        Me.SCropTop.Size = New System.Drawing.Size(969, 3)
        Me.SCropTop.TabIndex = 64
        Me.SCropTop.VerticalLine = False
        '
        'SCropBottom
        '
        Me.SCropBottom.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SCropBottom.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropBottom.Cross = False
        Me.SCropBottom.Cursor = System.Windows.Forms.Cursors.SizeNS
        Me.SCropBottom.GlowColor = System.Drawing.Color.DarkGreen
        Me.SCropBottom.Glowing = False
        Me.SCropBottom.InvertedLine = False
        Me.SCropBottom.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropBottom.Location = New System.Drawing.Point(0, 550)
        Me.SCropBottom.Name = "SCropBottom"
        Me.SCropBottom.Size = New System.Drawing.Size(969, 3)
        Me.SCropBottom.TabIndex = 63
        Me.SCropBottom.Text = "NsSeperatorCrop1"
        Me.SCropBottom.VerticalLine = False
        '
        'SCropLeft
        '
        Me.SCropLeft.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.SCropLeft.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropLeft.Cross = False
        Me.SCropLeft.Cursor = System.Windows.Forms.Cursors.SizeWE
        Me.SCropLeft.GlowColor = System.Drawing.Color.DarkGreen
        Me.SCropLeft.Glowing = False
        Me.SCropLeft.InvertedLine = False
        Me.SCropLeft.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropLeft.Location = New System.Drawing.Point(0, 4)
        Me.SCropLeft.Name = "SCropLeft"
        Me.SCropLeft.Size = New System.Drawing.Size(3, 549)
        Me.SCropLeft.TabIndex = 67
        Me.SCropLeft.VerticalLine = True
        '
        'SCropRight
        '
        Me.SCropRight.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SCropRight.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropRight.Cross = False
        Me.SCropRight.Cursor = System.Windows.Forms.Cursors.SizeWE
        Me.SCropRight.GlowColor = System.Drawing.Color.DarkGreen
        Me.SCropRight.Glowing = False
        Me.SCropRight.InvertedLine = False
        Me.SCropRight.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.SCropRight.Location = New System.Drawing.Point(966, 3)
        Me.SCropRight.Name = "SCropRight"
        Me.SCropRight.Size = New System.Drawing.Size(3, 549)
        Me.SCropRight.TabIndex = 68
        Me.SCropRight.VerticalLine = True
        '
        'PCropTop
        '
        Me.PCropTop.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCropTop.BackColor = System.Drawing.Color.Black
        Me.PCropTop.Location = New System.Drawing.Point(0, -100)
        Me.PCropTop.Name = "PCropTop"
        Me.PCropTop.Size = New System.Drawing.Size(969, 100)
        Me.PCropTop.TabIndex = 65
        '
        'PCropBottom
        '
        Me.PCropBottom.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCropBottom.BackColor = System.Drawing.Color.Black
        Me.PCropBottom.Location = New System.Drawing.Point(0, 553)
        Me.PCropBottom.Name = "PCropBottom"
        Me.PCropBottom.Size = New System.Drawing.Size(969, 100)
        Me.PCropBottom.TabIndex = 62
        '
        'PCropLeft
        '
        Me.PCropLeft.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.PCropLeft.BackColor = System.Drawing.Color.Black
        Me.PCropLeft.Location = New System.Drawing.Point(-100, 0)
        Me.PCropLeft.Name = "PCropLeft"
        Me.PCropLeft.Size = New System.Drawing.Size(100, 553)
        Me.PCropLeft.TabIndex = 66
        '
        'PCropRight
        '
        Me.PCropRight.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCropRight.BackColor = System.Drawing.Color.Black
        Me.PCropRight.Location = New System.Drawing.Point(969, 0)
        Me.PCropRight.Name = "PCropRight"
        Me.PCropRight.Size = New System.Drawing.Size(100, 553)
        Me.PCropRight.TabIndex = 69
        '
        'Player1
        '
        Me.Player1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Player1.Enabled = True
        Me.Player1.Location = New System.Drawing.Point(0, 5)
        Me.Player1.Name = "Player1"
        Me.Player1.OcxState = CType(resources.GetObject("Player1.OcxState"), System.Windows.Forms.AxHost.State)
        Me.Player1.Size = New System.Drawing.Size(968, 608)
        Me.Player1.TabIndex = 135
        '
        'Progreso
        '
        Me.Progreso.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Progreso.Location = New System.Drawing.Point(26, 604)
        Me.Progreso.Maximum = 100
        Me.Progreso.Minimum = 0
        Me.Progreso.Name = "Progreso"
        Me.Progreso.Size = New System.Drawing.Size(894, 15)
        Me.Progreso.TabIndex = 57
        Me.Progreso.Value = 0
        '
        'BPlay
        '
        Me.BPlay.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.BPlay.Font = New System.Drawing.Font("Webdings", 10.0!)
        Me.BPlay.ForeColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BPlay.Location = New System.Drawing.Point(8, 601)
        Me.BPlay.Name = "BPlay"
        Me.BPlay.Size = New System.Drawing.Size(20, 15)
        Me.BPlay.TabIndex = 56
        Me.BPlay.Text = "4"
        Me.BPlay.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'BClose
        '
        Me.BClose.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BClose.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BClose.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BClose.ForeColor2 = System.Drawing.Color.Black
        Me.BClose.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.Location = New System.Drawing.Point(973, 3)
        Me.BClose.Margin = New System.Windows.Forms.Padding(0)
        Me.BClose.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BClose.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BClose.Name = "BClose"
        Me.BClose.Size = New System.Drawing.Size(18, 20)
        Me.BClose.TabIndex = 54
        Me.BClose.Text = "NsControlButton1"
        '
        'Trim_Right
        '
        Me.Trim_Right.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Trim_Right.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Right.Cross = False
        Me.Trim_Right.Cursor = System.Windows.Forms.Cursors.VSplit
        Me.Trim_Right.GlowColor = System.Drawing.Color.DarkGreen
        Me.Trim_Right.Glowing = False
        Me.Trim_Right.InvertedLine = False
        Me.Trim_Right.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Right.Location = New System.Drawing.Point(916, 619)
        Me.Trim_Right.Name = "Trim_Right"
        Me.Trim_Right.Size = New System.Drawing.Size(3, 10)
        Me.Trim_Right.TabIndex = 134
        Me.Trim_Right.VerticalLine = True
        '
        'Trim_Left
        '
        Me.Trim_Left.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.Trim_Left.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Left.Cross = False
        Me.Trim_Left.Cursor = System.Windows.Forms.Cursors.VSplit
        Me.Trim_Left.GlowColor = System.Drawing.Color.DarkGreen
        Me.Trim_Left.Glowing = False
        Me.Trim_Left.InvertedLine = False
        Me.Trim_Left.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Left.Location = New System.Drawing.Point(26, 619)
        Me.Trim_Left.Name = "Trim_Left"
        Me.Trim_Left.Size = New System.Drawing.Size(3, 10)
        Me.Trim_Left.TabIndex = 133
        Me.Trim_Left.VerticalLine = True
        '
        'FVideoRecorderEditor
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(999, 636)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "FVideoRecorderEditor"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Editar Grabación"
        Me.FTheme.ResumeLayout(False)
        CType(Me.PAceptar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).EndInit()
        Me.GPlayer1.ResumeLayout(False)
        Me.Panel1.ResumeLayout(False)
        Me.EditingPanel.ResumeLayout(False)
        CType(Me.Player1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents GPlayer1 As NSGroupBox
    Friend WithEvents Panel1 As Panel
    Friend WithEvents Progreso As NSProgressBar
    Friend WithEvents BPlay As Label
    Friend WithEvents BClose As NSControlButton
    Friend WithEvents PCropBottom As Panel
    Friend WithEvents SCropBottom As NSSeperatorCrop
    Friend WithEvents PCropTop As Panel
    Friend WithEvents SCropTop As NSSeperatorCrop
    Friend WithEvents SCropLeft As NSSeperatorCrop
    Friend WithEvents PCropLeft As Panel
    Friend WithEvents SCropRight As NSSeperatorCrop
    Friend WithEvents PCropRight As Panel
    Friend WithEvents GrabacionPantallaEditor_LHelp As NSLabel
    Friend WithEvents Trim_Left As NSSeperatorCrop
    Friend WithEvents Trim_Right As NSSeperatorCrop
    Friend WithEvents EditingPanel As Panel
    Friend WithEvents EditingLabel As NSLabel
    Friend WithEvents Player1 As AxWMPLib.AxWindowsMediaPlayer
    Friend WithEvents PAceptar As PictureBox
    Friend WithEvents PCerrar As PictureBox
End Class
