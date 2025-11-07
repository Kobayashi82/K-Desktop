<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FNombreIcono
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FNombreIcono))
        Me.FTheme = New KDesktop.NSTheme()
        Me.BAceptar = New KDesktop.NSButton()
        Me.BCancelar = New KDesktop.NSButton()
        Me.TNombre = New KDesktop.NSTextBox()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.PIcono = New System.Windows.Forms.PictureBox()
        Me.NombreIcono_Menu = New KDesktop.NSContextMenu()
        Me.NombreIcono_Menu_Seleccionar = New System.Windows.Forms.ToolStripMenuItem()
        Me.NombreIcono_Menu_Separador0 = New System.Windows.Forms.ToolStripSeparator()
        Me.NombreIcono_Menu_Examinar = New System.Windows.Forms.ToolStripMenuItem()
        Me.NombreIcono_Menu_Separador1 = New System.Windows.Forms.ToolStripSeparator()
        Me.NombreIcono_Menu_Default = New System.Windows.Forms.ToolStripMenuItem()
        Me.NombreIcono_Menu_Separador2 = New System.Windows.Forms.ToolStripSeparator()
        Me.NombreIcono_Menu_Eliminar = New System.Windows.Forms.ToolStripMenuItem()
        Me.LNombre = New KDesktop.NSLabel()
        Me.FTheme.SuspendLayout()
        CType(Me.PIcono, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.NombreIcono_Menu.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.BAceptar)
        Me.FTheme.Controls.Add(Me.BCancelar)
        Me.FTheme.Controls.Add(Me.TNombre)
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.PIcono)
        Me.FTheme.Controls.Add(Me.LNombre)
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
        Me.FTheme.Size = New System.Drawing.Size(232, 117)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 100
        Me.FTheme.TabStop = False
        Me.FTheme.Text = "Nombre e Icono"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
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
        Me.BAceptar.Location = New System.Drawing.Point(84, 72)
        Me.BAceptar.Marked = False
        Me.BAceptar.Marked_Set = False
        Me.BAceptar.MarkedHovered = False
        Me.BAceptar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.Name = "BAceptar"
        Me.BAceptar.Rounded = 7
        Me.BAceptar.SHIFT = False
        Me.BAceptar.Size = New System.Drawing.Size(61, 29)
        Me.BAceptar.TabIndex = 130
        Me.BAceptar.TabStop = False
        Me.BAceptar.Text = "Aceptar"
        Me.BAceptar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BAceptar.WIN = False
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
        Me.BCancelar.Location = New System.Drawing.Point(153, 72)
        Me.BCancelar.Marked = False
        Me.BCancelar.Marked_Set = False
        Me.BCancelar.MarkedHovered = False
        Me.BCancelar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.Name = "BCancelar"
        Me.BCancelar.Rounded = 7
        Me.BCancelar.SHIFT = False
        Me.BCancelar.Size = New System.Drawing.Size(61, 29)
        Me.BCancelar.TabIndex = 131
        Me.BCancelar.TabStop = False
        Me.BCancelar.Text = "Cancelar"
        Me.BCancelar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BCancelar.WIN = False
        '
        'TNombre
        '
        Me.TNombre.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TNombre.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TNombre.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TNombre.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TNombre.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.HideSelection = False
        Me.TNombre.Location = New System.Drawing.Point(69, 38)
        Me.TNombre.MaxLength = 255
        Me.TNombre.Multiline = False
        Me.TNombre.Name = "TNombre"
        Me.TNombre.ReadOnly = False
        Me.TNombre.Rounded = 1
        Me.TNombre.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TNombre.SelectedText = ""
        Me.TNombre.SelectionLength = 0
        Me.TNombre.SelectionStart = 0
        Me.TNombre.ShortcutsEnabled = True
        Me.TNombre.Size = New System.Drawing.Size(151, 23)
        Me.TNombre.TabIndex = 110
        Me.TNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TNombre.UseSystemPasswordChar = False
        Me.TNombre.WordWrap = False
        '
        'HTexto
        '
        Me.HTexto.BorderColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.HTexto.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.HTexto.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.HTexto.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.HideSelection = False
        Me.HTexto.Location = New System.Drawing.Point(-500, -55)
        Me.HTexto.MaxLength = 32767
        Me.HTexto.Multiline = False
        Me.HTexto.Name = "HTexto"
        Me.HTexto.ReadOnly = True
        Me.HTexto.Rounded = 7
        Me.HTexto.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.HTexto.SelectedText = ""
        Me.HTexto.SelectionLength = 0
        Me.HTexto.SelectionStart = 0
        Me.HTexto.ShortcutsEnabled = False
        Me.HTexto.Size = New System.Drawing.Size(28, 16)
        Me.HTexto.TabIndex = 99
        Me.HTexto.TabStop = False
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(209, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 99
        Me.BCerrar.TabStop = False
        Me.BCerrar.Text = "NsControlButton1"
        '
        'PIcono
        '
        Me.PIcono.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        Me.PIcono.ContextMenuStrip = Me.NombreIcono_Menu
        Me.PIcono.Cursor = System.Windows.Forms.Cursors.Hand
        Me.PIcono.Location = New System.Drawing.Point(22, 70)
        Me.PIcono.Name = "PIcono"
        Me.PIcono.Size = New System.Drawing.Size(32, 32)
        Me.PIcono.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PIcono.TabIndex = 0
        Me.PIcono.TabStop = False
        '
        'NombreIcono_Menu
        '
        Me.NombreIcono_Menu.AutoSize = False
        Me.NombreIcono_Menu.ForeColor = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.NombreIcono_Menu.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.NombreIcono_Menu.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.NombreIcono_Menu_Seleccionar, Me.NombreIcono_Menu_Separador0, Me.NombreIcono_Menu_Examinar, Me.NombreIcono_Menu_Separador1, Me.NombreIcono_Menu_Default, Me.NombreIcono_Menu_Separador2, Me.NombreIcono_Menu_Eliminar})
        Me.NombreIcono_Menu.Name = "NombreIcono_Menu"
        Me.NombreIcono_Menu.ShowImageMargin = False
        Me.NombreIcono_Menu.Size = New System.Drawing.Size(106, 111)
        '
        'NombreIcono_Menu_Seleccionar
        '
        Me.NombreIcono_Menu_Seleccionar.AutoSize = False
        Me.NombreIcono_Menu_Seleccionar.Name = "NombreIcono_Menu_Seleccionar"
        Me.NombreIcono_Menu_Seleccionar.Size = New System.Drawing.Size(105, 22)
        Me.NombreIcono_Menu_Seleccionar.Text = "Seleccionar"
        '
        'NombreIcono_Menu_Separador0
        '
        Me.NombreIcono_Menu_Separador0.Name = "NombreIcono_Menu_Separador0"
        Me.NombreIcono_Menu_Separador0.Size = New System.Drawing.Size(102, 6)
        '
        'NombreIcono_Menu_Examinar
        '
        Me.NombreIcono_Menu_Examinar.AutoSize = False
        Me.NombreIcono_Menu_Examinar.Name = "NombreIcono_Menu_Examinar"
        Me.NombreIcono_Menu_Examinar.Size = New System.Drawing.Size(105, 22)
        Me.NombreIcono_Menu_Examinar.Text = "Examinar..."
        '
        'NombreIcono_Menu_Separador1
        '
        Me.NombreIcono_Menu_Separador1.Name = "NombreIcono_Menu_Separador1"
        Me.NombreIcono_Menu_Separador1.Size = New System.Drawing.Size(102, 6)
        '
        'NombreIcono_Menu_Default
        '
        Me.NombreIcono_Menu_Default.AutoSize = False
        Me.NombreIcono_Menu_Default.Name = "NombreIcono_Menu_Default"
        Me.NombreIcono_Menu_Default.Size = New System.Drawing.Size(105, 22)
        Me.NombreIcono_Menu_Default.Text = "Por Defecto"
        '
        'NombreIcono_Menu_Separador2
        '
        Me.NombreIcono_Menu_Separador2.Name = "NombreIcono_Menu_Separador2"
        Me.NombreIcono_Menu_Separador2.Size = New System.Drawing.Size(102, 6)
        '
        'NombreIcono_Menu_Eliminar
        '
        Me.NombreIcono_Menu_Eliminar.AutoSize = False
        Me.NombreIcono_Menu_Eliminar.Name = "NombreIcono_Menu_Eliminar"
        Me.NombreIcono_Menu_Eliminar.Size = New System.Drawing.Size(105, 22)
        Me.NombreIcono_Menu_Eliminar.Text = "Eliminar"
        '
        'LNombre
        '
        Me.LNombre.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LNombre.Location = New System.Drawing.Point(8, 41)
        Me.LNombre.Name = "LNombre"
        Me.LNombre.Size = New System.Drawing.Size(73, 17)
        Me.LNombre.TabIndex = 114
        Me.LNombre.TabStop = False
        Me.LNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LNombre.Value1 = "N"
        Me.LNombre.Value2 = "ombre:"
        '
        'FNombreIcono
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(232, 117)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FNombreIcono"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Nombre e Icono"
        Me.FTheme.ResumeLayout(False)
        CType(Me.PIcono, System.ComponentModel.ISupportInitialize).EndInit()
        Me.NombreIcono_Menu.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents PIcono As PictureBox
    Friend WithEvents LNombre As NSLabel
    Friend WithEvents BAceptar As NSButton
    Friend WithEvents BCancelar As NSButton
    Friend WithEvents NombreIcono_Menu As NSContextMenu
    Friend WithEvents NombreIcono_Menu_Seleccionar As ToolStripMenuItem
    Friend WithEvents NombreIcono_Menu_Separador0 As ToolStripSeparator
    Friend WithEvents NombreIcono_Menu_Examinar As ToolStripMenuItem
    Friend WithEvents NombreIcono_Menu_Separador1 As ToolStripSeparator
    Friend WithEvents NombreIcono_Menu_Eliminar As ToolStripMenuItem
    Friend WithEvents NombreIcono_Menu_Separador2 As ToolStripSeparator
    Friend WithEvents NombreIcono_Menu_Default As ToolStripMenuItem
End Class
