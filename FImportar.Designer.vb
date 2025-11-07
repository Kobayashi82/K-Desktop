<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FImportar
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FImportar))
        Me.FTheme = New KDesktop.NSTheme()
        Me.LReemplazar2 = New KDesktop.NSLabel()
        Me.LCombinar2 = New KDesktop.NSLabel()
        Me.LReemplazar1 = New KDesktop.NSLabel()
        Me.LCombinar1 = New KDesktop.NSLabel()
        Me.BReemplazar = New KDesktop.NSButton()
        Me.BCombinar = New KDesktop.NSButton()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.LCombinar3 = New KDesktop.NSLabel()
        Me.FTheme.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.LReemplazar2)
        Me.FTheme.Controls.Add(Me.LCombinar2)
        Me.FTheme.Controls.Add(Me.LReemplazar1)
        Me.FTheme.Controls.Add(Me.LCombinar1)
        Me.FTheme.Controls.Add(Me.BReemplazar)
        Me.FTheme.Controls.Add(Me.BCombinar)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.LCombinar3)
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
        Me.FTheme.Size = New System.Drawing.Size(386, 138)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Importar"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'LReemplazar2
        '
        Me.LReemplazar2.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LReemplazar2.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LReemplazar2.Location = New System.Drawing.Point(215, 58)
        Me.LReemplazar2.Name = "LReemplazar2"
        Me.LReemplazar2.Size = New System.Drawing.Size(142, 18)
        Me.LReemplazar2.TabIndex = 173
        Me.LReemplazar2.Text = "por los datos importados"
        Me.LReemplazar2.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LReemplazar2.Value1 = "NET"
        Me.LReemplazar2.Value2 = "SEAL"
        '
        'LCombinar2
        '
        Me.LCombinar2.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LCombinar2.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCombinar2.Location = New System.Drawing.Point(40, 58)
        Me.LCombinar2.Name = "LCombinar2"
        Me.LCombinar2.Size = New System.Drawing.Size(143, 18)
        Me.LCombinar2.TabIndex = 174
        Me.LCombinar2.Text = "con los datos importados"
        Me.LCombinar2.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LCombinar2.Value1 = "NET"
        Me.LCombinar2.Value2 = "SEAL"
        '
        'LReemplazar1
        '
        Me.LReemplazar1.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LReemplazar1.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LReemplazar1.Location = New System.Drawing.Point(213, 43)
        Me.LReemplazar1.Name = "LReemplazar1"
        Me.LReemplazar1.Size = New System.Drawing.Size(152, 18)
        Me.LReemplazar1.TabIndex = 175
        Me.LReemplazar1.Text = "Reemplaza todos los datos"
        Me.LReemplazar1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LReemplazar1.Value1 = "NET"
        Me.LReemplazar1.Value2 = "SEAL"
        '
        'LCombinar1
        '
        Me.LCombinar1.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Bold)
        Me.LCombinar1.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCombinar1.Location = New System.Drawing.Point(27, 43)
        Me.LCombinar1.Name = "LCombinar1"
        Me.LCombinar1.Size = New System.Drawing.Size(170, 18)
        Me.LCombinar1.TabIndex = 176
        Me.LCombinar1.Text = "Combina los datos existentes"
        Me.LCombinar1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LCombinar1.Value1 = "NET"
        Me.LCombinar1.Value2 = "SEAL"
        '
        'BReemplazar
        '
        Me.BReemplazar.ALT = False
        Me.BReemplazar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BReemplazar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BReemplazar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BReemplazar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BReemplazar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BReemplazar.BorderColor2 = System.Drawing.Color.Red
        Me.BReemplazar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BReemplazar.CTRL = False
        Me.BReemplazar.DoubleText = True
        Me.BReemplazar.FirstLetterColor = System.Drawing.Color.White
        Me.BReemplazar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BReemplazar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BReemplazar.ForeColor2 = System.Drawing.Color.Black
        Me.BReemplazar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BReemplazar.GradientAngle = 90
        Me.BReemplazar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BReemplazar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BReemplazar.GradientTransparency = 0
        Me.BReemplazar.Image = Nothing
        Me.BReemplazar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BReemplazar.ImageCode = ""
        Me.BReemplazar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BReemplazar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BReemplazar.Location = New System.Drawing.Point(223, 87)
        Me.BReemplazar.Marked = False
        Me.BReemplazar.Marked_Set = False
        Me.BReemplazar.MarkedHovered = False
        Me.BReemplazar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BReemplazar.Name = "BReemplazar"
        Me.BReemplazar.Rounded = 7
        Me.BReemplazar.SHIFT = False
        Me.BReemplazar.Size = New System.Drawing.Size(126, 26)
        Me.BReemplazar.TabIndex = 172
        Me.BReemplazar.TabStop = False
        Me.BReemplazar.Text = "Reemplazar"
        Me.BReemplazar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BReemplazar.WIN = False
        '
        'BCombinar
        '
        Me.BCombinar.ALT = False
        Me.BCombinar.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BCombinar.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCombinar.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCombinar.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BCombinar.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCombinar.BorderColor2 = System.Drawing.Color.Red
        Me.BCombinar.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BCombinar.CTRL = False
        Me.BCombinar.DoubleText = True
        Me.BCombinar.FirstLetterColor = System.Drawing.Color.White
        Me.BCombinar.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.BCombinar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BCombinar.ForeColor2 = System.Drawing.Color.Black
        Me.BCombinar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCombinar.GradientAngle = 90
        Me.BCombinar.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BCombinar.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BCombinar.GradientTransparency = 0
        Me.BCombinar.Image = Nothing
        Me.BCombinar.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BCombinar.ImageCode = ""
        Me.BCombinar.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BCombinar.ImageSize = New System.Drawing.Size(19, 19)
        Me.BCombinar.Location = New System.Drawing.Point(41, 87)
        Me.BCombinar.Marked = False
        Me.BCombinar.Marked_Set = False
        Me.BCombinar.MarkedHovered = False
        Me.BCombinar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCombinar.Name = "BCombinar"
        Me.BCombinar.Rounded = 7
        Me.BCombinar.SHIFT = False
        Me.BCombinar.Size = New System.Drawing.Size(126, 26)
        Me.BCombinar.TabIndex = 171
        Me.BCombinar.TabStop = False
        Me.BCombinar.Text = "Combinar"
        Me.BCombinar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BCombinar.WIN = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(357, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 13
        Me.BCerrar.Text = "NsControlButton1"
        '
        'LCombinar3
        '
        Me.LCombinar3.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LCombinar3.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCombinar3.Location = New System.Drawing.Point(27, 111)
        Me.LCombinar3.Name = "LCombinar3"
        Me.LCombinar3.Size = New System.Drawing.Size(160, 18)
        Me.LCombinar3.TabIndex = 174
        Me.LCombinar3.Text = "(Prevalecen los datos importados)"
        Me.LCombinar3.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LCombinar3.Value1 = "NET"
        Me.LCombinar3.Value2 = "SEAL"
        '
        'FImportar
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(386, 138)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FImportar"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "FImportar"
        Me.FTheme.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents LReemplazar2 As NSLabel
    Friend WithEvents LCombinar2 As NSLabel
    Friend WithEvents LReemplazar1 As NSLabel
    Friend WithEvents LCombinar1 As NSLabel
    Friend WithEvents BReemplazar As NSButton
    Friend WithEvents BCombinar As NSButton
    Friend WithEvents LCombinar3 As NSLabel
End Class
