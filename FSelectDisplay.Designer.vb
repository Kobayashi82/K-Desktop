<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FSelectDisplay
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
        Me.FTheme = New KDesktop.NSTheme()
        Me.PanelFondo = New System.Windows.Forms.Panel()
        Me.BPanel = New System.Windows.Forms.Panel()
        Me.BSubPanel = New System.Windows.Forms.Panel()
        Me.CMicrofono = New KDesktop.NSOnOffBox()
        Me.LMicrofono = New KDesktop.NSLabel()
        Me.CSonido = New KDesktop.NSOnOffBox()
        Me.LSonido = New KDesktop.NSLabel()
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.SubPanel1 = New System.Windows.Forms.Panel()
        Me.LPantalla1 = New System.Windows.Forms.Label()
        Me.Panel2 = New System.Windows.Forms.Panel()
        Me.SubPanel2 = New System.Windows.Forms.Panel()
        Me.LPantalla2 = New System.Windows.Forms.Label()
        Me.Panel3 = New System.Windows.Forms.Panel()
        Me.SubPanel3 = New System.Windows.Forms.Panel()
        Me.LPantalla3 = New System.Windows.Forms.Label()
        Me.Panel4 = New System.Windows.Forms.Panel()
        Me.SubPanel4 = New System.Windows.Forms.Panel()
        Me.LPantalla4 = New System.Windows.Forms.Label()
        Me.PanelIcono = New System.Windows.Forms.Panel()
        Me.LDesktopDuplication = New KDesktop.NSLabel()
        Me.CDesktopDuplication = New KDesktop.NSOnOffBox()
        Me.LMarcarClicks = New KDesktop.NSLabel()
        Me.CMarcarClicks = New KDesktop.NSOnOffBox()
        Me.FTheme.SuspendLayout()
        Me.PanelFondo.SuspendLayout()
        Me.BPanel.SuspendLayout()
        Me.BSubPanel.SuspendLayout()
        Me.Panel1.SuspendLayout()
        Me.SubPanel1.SuspendLayout()
        Me.Panel2.SuspendLayout()
        Me.SubPanel2.SuspendLayout()
        Me.Panel3.SuspendLayout()
        Me.SubPanel3.SuspendLayout()
        Me.Panel4.SuspendLayout()
        Me.SubPanel4.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.PanelFondo)
        Me.FTheme.Controls.Add(Me.PanelIcono)
        Me.FTheme.Customization = ""
        Me.FTheme.Dock = System.Windows.Forms.DockStyle.Fill
        Me.FTheme.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.FTheme.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.FTheme.ForeColor2 = System.Drawing.Color.Black
        Me.FTheme.Image = Nothing
        Me.FTheme.Location = New System.Drawing.Point(0, 0)
        Me.FTheme.Movable = False
        Me.FTheme.Name = "FTheme"
        Me.FTheme.NoRounding = False
        Me.FTheme.Sizable = False
        Me.FTheme.Size = New System.Drawing.Size(623, 177)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Seleccionar Pantalla"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'PanelFondo
        '
        Me.PanelFondo.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PanelFondo.Controls.Add(Me.BPanel)
        Me.PanelFondo.Controls.Add(Me.Panel1)
        Me.PanelFondo.Controls.Add(Me.Panel2)
        Me.PanelFondo.Controls.Add(Me.Panel3)
        Me.PanelFondo.Controls.Add(Me.Panel4)
        Me.PanelFondo.Location = New System.Drawing.Point(1, 5)
        Me.PanelFondo.Name = "PanelFondo"
        Me.PanelFondo.Size = New System.Drawing.Size(621, 171)
        Me.PanelFondo.TabIndex = 14
        '
        'BPanel
        '
        Me.BPanel.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BPanel.BackColor = System.Drawing.Color.FromArgb(CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer))
        Me.BPanel.Controls.Add(Me.BSubPanel)
        Me.BPanel.Location = New System.Drawing.Point(3, 103)
        Me.BPanel.Name = "BPanel"
        Me.BPanel.Size = New System.Drawing.Size(615, 65)
        Me.BPanel.TabIndex = 1
        '
        'BSubPanel
        '
        Me.BSubPanel.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BSubPanel.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.BSubPanel.Controls.Add(Me.CMarcarClicks)
        Me.BSubPanel.Controls.Add(Me.CDesktopDuplication)
        Me.BSubPanel.Controls.Add(Me.LMarcarClicks)
        Me.BSubPanel.Controls.Add(Me.CMicrofono)
        Me.BSubPanel.Controls.Add(Me.LDesktopDuplication)
        Me.BSubPanel.Controls.Add(Me.LMicrofono)
        Me.BSubPanel.Controls.Add(Me.CSonido)
        Me.BSubPanel.Controls.Add(Me.LSonido)
        Me.BSubPanel.Location = New System.Drawing.Point(4, 4)
        Me.BSubPanel.Name = "BSubPanel"
        Me.BSubPanel.Size = New System.Drawing.Size(608, 58)
        Me.BSubPanel.TabIndex = 0
        '
        'CMicrofono
        '
        Me.CMicrofono.Checked = False
        Me.CMicrofono.Location = New System.Drawing.Point(5, 30)
        Me.CMicrofono.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CMicrofono.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CMicrofono.Name = "CMicrofono"
        Me.CMicrofono.Size = New System.Drawing.Size(56, 24)
        Me.CMicrofono.TabIndex = 12
        '
        'LMicrofono
        '
        Me.LMicrofono.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LMicrofono.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LMicrofono.Location = New System.Drawing.Point(64, 34)
        Me.LMicrofono.Name = "LMicrofono"
        Me.LMicrofono.Size = New System.Drawing.Size(66, 15)
        Me.LMicrofono.TabIndex = 13
        Me.LMicrofono.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LMicrofono.Value1 = "M"
        Me.LMicrofono.Value2 = "icrófono"
        '
        'CSonido
        '
        Me.CSonido.Checked = False
        Me.CSonido.Location = New System.Drawing.Point(5, 3)
        Me.CSonido.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CSonido.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CSonido.Name = "CSonido"
        Me.CSonido.Size = New System.Drawing.Size(56, 24)
        Me.CSonido.TabIndex = 12
        '
        'LSonido
        '
        Me.LSonido.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LSonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LSonido.Location = New System.Drawing.Point(64, 7)
        Me.LSonido.Name = "LSonido"
        Me.LSonido.Size = New System.Drawing.Size(43, 15)
        Me.LSonido.TabIndex = 13
        Me.LSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LSonido.Value1 = "S"
        Me.LSonido.Value2 = "onido"
        '
        'Panel1
        '
        Me.Panel1.BackColor = System.Drawing.Color.FromArgb(CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer))
        Me.Panel1.Controls.Add(Me.SubPanel1)
        Me.Panel1.Location = New System.Drawing.Point(3, 0)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(158, 107)
        Me.Panel1.TabIndex = 0
        '
        'SubPanel1
        '
        Me.SubPanel1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SubPanel1.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.SubPanel1.Controls.Add(Me.LPantalla1)
        Me.SubPanel1.Location = New System.Drawing.Point(4, 4)
        Me.SubPanel1.Name = "SubPanel1"
        Me.SubPanel1.Size = New System.Drawing.Size(150, 99)
        Me.SubPanel1.TabIndex = 0
        '
        'LPantalla1
        '
        Me.LPantalla1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LPantalla1.Font = New System.Drawing.Font("Verdana", 48.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LPantalla1.ForeColor = System.Drawing.Color.White
        Me.LPantalla1.Location = New System.Drawing.Point(0, 0)
        Me.LPantalla1.Name = "LPantalla1"
        Me.LPantalla1.Size = New System.Drawing.Size(150, 99)
        Me.LPantalla1.TabIndex = 0
        Me.LPantalla1.Text = "1"
        Me.LPantalla1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'Panel2
        '
        Me.Panel2.BackColor = System.Drawing.Color.FromArgb(CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer))
        Me.Panel2.Controls.Add(Me.SubPanel2)
        Me.Panel2.Location = New System.Drawing.Point(156, 0)
        Me.Panel2.Name = "Panel2"
        Me.Panel2.Size = New System.Drawing.Size(158, 107)
        Me.Panel2.TabIndex = 0
        '
        'SubPanel2
        '
        Me.SubPanel2.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SubPanel2.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.SubPanel2.Controls.Add(Me.LPantalla2)
        Me.SubPanel2.Location = New System.Drawing.Point(4, 4)
        Me.SubPanel2.Name = "SubPanel2"
        Me.SubPanel2.Size = New System.Drawing.Size(150, 99)
        Me.SubPanel2.TabIndex = 0
        '
        'LPantalla2
        '
        Me.LPantalla2.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LPantalla2.Font = New System.Drawing.Font("Verdana", 48.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LPantalla2.ForeColor = System.Drawing.Color.White
        Me.LPantalla2.Location = New System.Drawing.Point(0, 0)
        Me.LPantalla2.Name = "LPantalla2"
        Me.LPantalla2.Size = New System.Drawing.Size(150, 99)
        Me.LPantalla2.TabIndex = 0
        Me.LPantalla2.Text = "2"
        Me.LPantalla2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'Panel3
        '
        Me.Panel3.BackColor = System.Drawing.Color.FromArgb(CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer))
        Me.Panel3.Controls.Add(Me.SubPanel3)
        Me.Panel3.Location = New System.Drawing.Point(309, 0)
        Me.Panel3.Name = "Panel3"
        Me.Panel3.Size = New System.Drawing.Size(158, 107)
        Me.Panel3.TabIndex = 0
        '
        'SubPanel3
        '
        Me.SubPanel3.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SubPanel3.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.SubPanel3.Controls.Add(Me.LPantalla3)
        Me.SubPanel3.Location = New System.Drawing.Point(4, 4)
        Me.SubPanel3.Name = "SubPanel3"
        Me.SubPanel3.Size = New System.Drawing.Size(150, 99)
        Me.SubPanel3.TabIndex = 0
        '
        'LPantalla3
        '
        Me.LPantalla3.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LPantalla3.Font = New System.Drawing.Font("Verdana", 48.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LPantalla3.ForeColor = System.Drawing.Color.White
        Me.LPantalla3.Location = New System.Drawing.Point(0, 0)
        Me.LPantalla3.Name = "LPantalla3"
        Me.LPantalla3.Size = New System.Drawing.Size(150, 99)
        Me.LPantalla3.TabIndex = 0
        Me.LPantalla3.Text = "3"
        Me.LPantalla3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'Panel4
        '
        Me.Panel4.BackColor = System.Drawing.Color.FromArgb(CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer), CType(CType(224, Byte), Integer))
        Me.Panel4.Controls.Add(Me.SubPanel4)
        Me.Panel4.Location = New System.Drawing.Point(462, 0)
        Me.Panel4.Name = "Panel4"
        Me.Panel4.Size = New System.Drawing.Size(158, 107)
        Me.Panel4.TabIndex = 0
        '
        'SubPanel4
        '
        Me.SubPanel4.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.SubPanel4.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.SubPanel4.Controls.Add(Me.LPantalla4)
        Me.SubPanel4.Location = New System.Drawing.Point(4, 4)
        Me.SubPanel4.Name = "SubPanel4"
        Me.SubPanel4.Size = New System.Drawing.Size(150, 99)
        Me.SubPanel4.TabIndex = 0
        '
        'LPantalla4
        '
        Me.LPantalla4.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LPantalla4.Font = New System.Drawing.Font("Verdana", 48.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LPantalla4.ForeColor = System.Drawing.Color.White
        Me.LPantalla4.Location = New System.Drawing.Point(0, 0)
        Me.LPantalla4.Name = "LPantalla4"
        Me.LPantalla4.Size = New System.Drawing.Size(150, 99)
        Me.LPantalla4.TabIndex = 0
        Me.LPantalla4.Text = "4"
        Me.LPantalla4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'PanelIcono
        '
        Me.PanelIcono.Location = New System.Drawing.Point(1, 2)
        Me.PanelIcono.Name = "PanelIcono"
        Me.PanelIcono.Size = New System.Drawing.Size(74, 19)
        Me.PanelIcono.TabIndex = 14
        '
        'LDesktopDuplication
        '
        Me.LDesktopDuplication.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LDesktopDuplication.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LDesktopDuplication.Location = New System.Drawing.Point(186, 7)
        Me.LDesktopDuplication.Name = "LDesktopDuplication"
        Me.LDesktopDuplication.Size = New System.Drawing.Size(112, 15)
        Me.LDesktopDuplication.TabIndex = 13
        Me.LDesktopDuplication.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LDesktopDuplication.Value1 = "D"
        Me.LDesktopDuplication.Value2 = "esktop Duplication"
        '
        'CDesktopDuplication
        '
        Me.CDesktopDuplication.Checked = False
        Me.CDesktopDuplication.Location = New System.Drawing.Point(127, 3)
        Me.CDesktopDuplication.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CDesktopDuplication.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CDesktopDuplication.Name = "CDesktopDuplication"
        Me.CDesktopDuplication.Size = New System.Drawing.Size(56, 24)
        Me.CDesktopDuplication.TabIndex = 12
        '
        'LMarcarClicks
        '
        Me.LMarcarClicks.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LMarcarClicks.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LMarcarClicks.Location = New System.Drawing.Point(186, 34)
        Me.LMarcarClicks.Name = "LMarcarClicks"
        Me.LMarcarClicks.Size = New System.Drawing.Size(75, 15)
        Me.LMarcarClicks.TabIndex = 13
        Me.LMarcarClicks.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LMarcarClicks.Value1 = "M"
        Me.LMarcarClicks.Value2 = "arcar Clicks"
        '
        'CMarcarClicks
        '
        Me.CMarcarClicks.Checked = False
        Me.CMarcarClicks.Location = New System.Drawing.Point(127, 30)
        Me.CMarcarClicks.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CMarcarClicks.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CMarcarClicks.Name = "CMarcarClicks"
        Me.CMarcarClicks.Size = New System.Drawing.Size(56, 24)
        Me.CMarcarClicks.TabIndex = 12
        '
        'FSelectDisplay
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(623, 177)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.KeyPreview = True
        Me.Name = "FSelectDisplay"
        Me.ShowIcon = False
        Me.ShowInTaskbar = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Seleccionar Pantalla"
        Me.TopMost = True
        Me.FTheme.ResumeLayout(False)
        Me.PanelFondo.ResumeLayout(False)
        Me.BPanel.ResumeLayout(False)
        Me.BSubPanel.ResumeLayout(False)
        Me.Panel1.ResumeLayout(False)
        Me.SubPanel1.ResumeLayout(False)
        Me.Panel2.ResumeLayout(False)
        Me.SubPanel2.ResumeLayout(False)
        Me.Panel3.ResumeLayout(False)
        Me.SubPanel3.ResumeLayout(False)
        Me.Panel4.ResumeLayout(False)
        Me.SubPanel4.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents PanelFondo As Panel
    Friend WithEvents Panel4 As Panel
    Friend WithEvents SubPanel4 As Panel
    Friend WithEvents LPantalla4 As Label
    Friend WithEvents Panel3 As Panel
    Friend WithEvents SubPanel3 As Panel
    Friend WithEvents LPantalla3 As Label
    Friend WithEvents Panel1 As Panel
    Friend WithEvents SubPanel1 As Panel
    Friend WithEvents LPantalla1 As Label
    Friend WithEvents Panel2 As Panel
    Friend WithEvents SubPanel2 As Panel
    Friend WithEvents LPantalla2 As Label
    Friend WithEvents BPanel As Panel
    Friend WithEvents BSubPanel As Panel
    Friend WithEvents CMicrofono As NSOnOffBox
    Friend WithEvents LMicrofono As NSLabel
    Friend WithEvents CSonido As NSOnOffBox
    Friend WithEvents LSonido As NSLabel
    Friend WithEvents PanelIcono As Panel
    Friend WithEvents CMarcarClicks As NSOnOffBox
    Friend WithEvents CDesktopDuplication As NSOnOffBox
    Friend WithEvents LMarcarClicks As NSLabel
    Friend WithEvents LDesktopDuplication As NSLabel
End Class
