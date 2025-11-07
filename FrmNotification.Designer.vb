<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FrmNotification
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
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

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FrmNotification))
        Me.FTheme = New KDesktop.NSThemeLogros()
        Me.Separador4 = New KDesktop.NSSeperator()
        Me.Separador3 = New KDesktop.NSSeperator()
        Me.Separador2 = New KDesktop.NSSeperator()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.LNombre = New KDesktop.NSLabel()
        Me.Separador1 = New KDesktop.NSSeperator()
        Me.PLogro = New System.Windows.Forms.PictureBox()
        Me.TLogro = New System.Windows.Forms.Label()
        Me.FTheme.SuspendLayout()
        CType(Me.PLogro, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.Separador4)
        Me.FTheme.Controls.Add(Me.Separador3)
        Me.FTheme.Controls.Add(Me.Separador2)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.LNombre)
        Me.FTheme.Controls.Add(Me.Separador1)
        Me.FTheme.Controls.Add(Me.PLogro)
        Me.FTheme.Controls.Add(Me.TLogro)
        Me.FTheme.Customization = ""
        Me.FTheme.Dock = System.Windows.Forms.DockStyle.Fill
        Me.FTheme.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.FTheme.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.FTheme.ForeColor2 = System.Drawing.Color.Black
        Me.FTheme.Image = Nothing
        Me.FTheme.Location = New System.Drawing.Point(0, 0)
        Me.FTheme.Movable = False
        Me.FTheme.Name = "FTheme"
        Me.FTheme.NoRounding = False
        Me.FTheme.Sizable = False
        Me.FTheme.Size = New System.Drawing.Size(286, 113)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.WindowsDefaultLocation
        Me.FTheme.TabIndex = 7
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'Separador4
        '
        Me.Separador4.Cross = False
        Me.Separador4.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador4.Glowing = False
        Me.Separador4.LineColor = System.Drawing.Color.Gray
        Me.Separador4.Location = New System.Drawing.Point(95, 39)
        Me.Separador4.Name = "Separador4"
        Me.Separador4.Size = New System.Drawing.Size(1, 62)
        Me.Separador4.TabIndex = 10
        Me.Separador4.Text = "NsSeperator4"
        Me.Separador4.VerticalLine = True
        '
        'Separador3
        '
        Me.Separador3.Cross = False
        Me.Separador3.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador3.Glowing = False
        Me.Separador3.LineColor = System.Drawing.Color.Gray
        Me.Separador3.Location = New System.Drawing.Point(11, 39)
        Me.Separador3.Name = "Separador3"
        Me.Separador3.Size = New System.Drawing.Size(1, 62)
        Me.Separador3.TabIndex = 9
        Me.Separador3.Text = "NsSeperator3"
        Me.Separador3.VerticalLine = True
        '
        'Separador2
        '
        Me.Separador2.Cross = False
        Me.Separador2.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador2.Glowing = False
        Me.Separador2.LineColor = System.Drawing.Color.Gray
        Me.Separador2.Location = New System.Drawing.Point(13, 100)
        Me.Separador2.Name = "Separador2"
        Me.Separador2.Size = New System.Drawing.Size(83, 1)
        Me.Separador2.TabIndex = 8
        Me.Separador2.Text = "NsSeperator2"
        Me.Separador2.VerticalLine = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(265, 2)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 2
        Me.BCerrar.Text = "NsControlButton1"
        '
        'LNombre
        '
        Me.LNombre.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LNombre.Location = New System.Drawing.Point(4, 5)
        Me.LNombre.Name = "LNombre"
        Me.LNombre.Size = New System.Drawing.Size(278, 17)
        Me.LNombre.TabIndex = 5
        Me.LNombre.Text = "Actualizador"
        Me.LNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LNombre.Value1 = ""
        Me.LNombre.Value2 = ""
        '
        'Separador1
        '
        Me.Separador1.Cross = False
        Me.Separador1.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador1.Glowing = False
        Me.Separador1.LineColor = System.Drawing.Color.Gray
        Me.Separador1.Location = New System.Drawing.Point(12, 39)
        Me.Separador1.Name = "Separador1"
        Me.Separador1.Size = New System.Drawing.Size(83, 1)
        Me.Separador1.TabIndex = 7
        Me.Separador1.Text = "NsSeperator1"
        Me.Separador1.VerticalLine = False
        '
        'PLogro
        '
        Me.PLogro.Image = CType(resources.GetObject("PLogro.Image"), System.Drawing.Image)
        Me.PLogro.Location = New System.Drawing.Point(12, 40)
        Me.PLogro.Name = "PLogro"
        Me.PLogro.Size = New System.Drawing.Size(83, 60)
        Me.PLogro.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PLogro.TabIndex = 4
        Me.PLogro.TabStop = False
        '
        'TLogro
        '
        Me.TLogro.Font = New System.Drawing.Font("Verdana", 8.25!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.TLogro.ForeColor = System.Drawing.Color.FromArgb(CType(CType(144, Byte), Integer), CType(CType(144, Byte), Integer), CType(CType(144, Byte), Integer))
        Me.TLogro.Location = New System.Drawing.Point(97, 41)
        Me.TLogro.Name = "TLogro"
        Me.TLogro.Size = New System.Drawing.Size(175, 58)
        Me.TLogro.TabIndex = 6
        Me.TLogro.Text = "Actualiza KDesktop por primera vez"
        Me.TLogro.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'FrmNotification
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(286, 113)
        Me.ControlBox = False
        Me.Controls.Add(Me.FTheme)
        Me.DoubleBuffered = True
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Name = "FrmNotification"
        Me.ShowInTaskbar = False
        Me.TopMost = True
        Me.FTheme.ResumeLayout(False)
        CType(Me.PLogro, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents TLogro As Label
    Friend WithEvents LNombre As NSLabel
    Friend WithEvents PLogro As PictureBox
    Friend WithEvents FTheme As NSThemeLogros
    Friend WithEvents Separador2 As NSSeperator
    Friend WithEvents Separador1 As NSSeperator
    Friend WithEvents Separador4 As NSSeperator
    Friend WithEvents Separador3 As NSSeperator
End Class
