<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FComandoRapido
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FComandoRapido))
        Me.FTheme = New KDesktop.NSTheme()
        Me.PModo = New System.Windows.Forms.PictureBox()
        Me.PPrioridad = New System.Windows.Forms.PictureBox()
        Me.TNombre = New KDesktop.NSTextBox()
        Me.LSonido = New KDesktop.NSLabel()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.BPlay = New KDesktop.NSButton()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.PIcono = New System.Windows.Forms.PictureBox()
        Me.ComandoRapido_Menu = New KDesktop.NSContextMenu()
        Me.ComandoRapido_Menu_Seleccionar = New System.Windows.Forms.ToolStripMenuItem()
        Me.ComandoRapido_Menu_Separador0 = New System.Windows.Forms.ToolStripSeparator()
        Me.ComandoRapido_Menu_Examinar = New System.Windows.Forms.ToolStripMenuItem()
        Me.ComandoRapido_Menu_Separador1 = New System.Windows.Forms.ToolStripSeparator()
        Me.ComandoRapido_Menu_Default = New System.Windows.Forms.ToolStripMenuItem()
        Me.ComandoRapido_Menu_Separador2 = New System.Windows.Forms.ToolStripSeparator()
        Me.ComandoRapido_Menu_Eliminar = New System.Windows.Forms.ToolStripMenuItem()
        Me.LHelp = New KDesktop.NSLabel()
        Me.BAceptar = New KDesktop.NSButton()
        Me.BCancelar = New KDesktop.NSButton()
        Me.BExaminar_Sonido = New KDesktop.NSButton()
        Me.BExaminar_Ruta = New KDesktop.NSButton()
        Me.LCSonido = New System.Windows.Forms.Label()
        Me.GInformacion = New KDesktop.NSGroupBox()
        Me.GInformacion_LoadingCircle = New KDesktop.NSLoadingCircle()
        Me.GInformacion_LTipo = New System.Windows.Forms.Label()
        Me.GInformacion_SinInformacion = New KDesktop.NSLabel()
        Me.GInformacion_LNombre = New KDesktop.NSLabel()
        Me.GInformacion_PIcono = New System.Windows.Forms.PictureBox()
        Me.GInformacion_LCompany = New KDesktop.NSLabel()
        Me.LModo = New KDesktop.NSLabel()
        Me.TSonido = New KDesktop.NSTextBox()
        Me.CSonido = New KDesktop.NSComboBox()
        Me.LAdministrador = New KDesktop.NSLabel()
        Me.Separador1 = New KDesktop.NSSeperator()
        Me.CAdministrador = New KDesktop.NSOnOffBox()
        Me.Separador0 = New KDesktop.NSSeperator()
        Me.TRuta = New KDesktop.NSTextBox()
        Me.LCPrioridad = New System.Windows.Forms.Label()
        Me.LRuta = New KDesktop.NSLabel()
        Me.LCModo = New System.Windows.Forms.Label()
        Me.TParametros = New KDesktop.NSTextBox()
        Me.LParametros = New KDesktop.NSLabel()
        Me.LNombre = New KDesktop.NSLabel()
        Me.TModo = New KDesktop.NSTextBox()
        Me.TPrioridad = New KDesktop.NSTextBox()
        Me.CModo = New KDesktop.NSComboBox()
        Me.CPrioridad = New KDesktop.NSComboBox()
        Me.LPrioridad = New KDesktop.NSLabel()
        Me.FTheme.SuspendLayout()
        CType(Me.PModo, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PPrioridad, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PIcono, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.ComandoRapido_Menu.SuspendLayout()
        Me.GInformacion.SuspendLayout()
        CType(Me.GInformacion_PIcono, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.PModo)
        Me.FTheme.Controls.Add(Me.PPrioridad)
        Me.FTheme.Controls.Add(Me.TNombre)
        Me.FTheme.Controls.Add(Me.LSonido)
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Controls.Add(Me.BPlay)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.PIcono)
        Me.FTheme.Controls.Add(Me.LHelp)
        Me.FTheme.Controls.Add(Me.BAceptar)
        Me.FTheme.Controls.Add(Me.BCancelar)
        Me.FTheme.Controls.Add(Me.BExaminar_Sonido)
        Me.FTheme.Controls.Add(Me.BExaminar_Ruta)
        Me.FTheme.Controls.Add(Me.LCSonido)
        Me.FTheme.Controls.Add(Me.GInformacion)
        Me.FTheme.Controls.Add(Me.LModo)
        Me.FTheme.Controls.Add(Me.TSonido)
        Me.FTheme.Controls.Add(Me.CSonido)
        Me.FTheme.Controls.Add(Me.LAdministrador)
        Me.FTheme.Controls.Add(Me.Separador1)
        Me.FTheme.Controls.Add(Me.CAdministrador)
        Me.FTheme.Controls.Add(Me.Separador0)
        Me.FTheme.Controls.Add(Me.TRuta)
        Me.FTheme.Controls.Add(Me.LCPrioridad)
        Me.FTheme.Controls.Add(Me.LRuta)
        Me.FTheme.Controls.Add(Me.LCModo)
        Me.FTheme.Controls.Add(Me.TParametros)
        Me.FTheme.Controls.Add(Me.LParametros)
        Me.FTheme.Controls.Add(Me.LNombre)
        Me.FTheme.Controls.Add(Me.TModo)
        Me.FTheme.Controls.Add(Me.TPrioridad)
        Me.FTheme.Controls.Add(Me.CModo)
        Me.FTheme.Controls.Add(Me.CPrioridad)
        Me.FTheme.Controls.Add(Me.LPrioridad)
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
        Me.FTheme.Size = New System.Drawing.Size(452, 233)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.FTheme.TabIndex = 99
        Me.FTheme.TabStop = False
        Me.FTheme.Text = "Comando Rápido"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'PModo
        '
        Me.PModo.Image = Global.KDesktop.My.Resources.Resources.CComandos_Off
        Me.PModo.Location = New System.Drawing.Point(182, 126)
        Me.PModo.Name = "PModo"
        Me.PModo.Size = New System.Drawing.Size(22, 21)
        Me.PModo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize
        Me.PModo.TabIndex = 188
        Me.PModo.TabStop = False
        '
        'PPrioridad
        '
        Me.PPrioridad.Image = Global.KDesktop.My.Resources.Resources.CComandos_Off
        Me.PPrioridad.Location = New System.Drawing.Point(182, 162)
        Me.PPrioridad.Name = "PPrioridad"
        Me.PPrioridad.Size = New System.Drawing.Size(22, 21)
        Me.PPrioridad.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize
        Me.PPrioridad.TabIndex = 188
        Me.PPrioridad.TabStop = False
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
        Me.TNombre.Location = New System.Drawing.Point(111, 38)
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
        'LSonido
        '
        Me.LSonido.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LSonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LSonido.Location = New System.Drawing.Point(18, 200)
        Me.LSonido.Name = "LSonido"
        Me.LSonido.Size = New System.Drawing.Size(50, 17)
        Me.LSonido.TabIndex = 130
        Me.LSonido.TabStop = False
        Me.LSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LSonido.Value1 = "S"
        Me.LSonido.Value2 = "onido:"
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
        'BPlay
        '
        Me.BPlay.ALT = False
        Me.BPlay.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BPlay.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BPlay.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BPlay.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BPlay.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BPlay.BorderColor2 = System.Drawing.Color.Red
        Me.BPlay.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BPlay.CTRL = False
        Me.BPlay.DoubleText = True
        Me.BPlay.FirstLetterColor = System.Drawing.Color.White
        Me.BPlay.Font = New System.Drawing.Font("Verdana", 12.0!)
        Me.BPlay.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BPlay.ForeColor2 = System.Drawing.Color.Black
        Me.BPlay.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BPlay.GradientAngle = 90
        Me.BPlay.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BPlay.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BPlay.GradientTransparency = 0
        Me.BPlay.Image = Nothing
        Me.BPlay.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BPlay.ImageCode = ""
        Me.BPlay.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BPlay.ImageSize = New System.Drawing.Size(19, 19)
        Me.BPlay.Location = New System.Drawing.Point(245, 198)
        Me.BPlay.Marked = False
        Me.BPlay.Marked_Set = False
        Me.BPlay.MarkedHovered = False
        Me.BPlay.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BPlay.Name = "BPlay"
        Me.BPlay.Rounded = 1
        Me.BPlay.SHIFT = False
        Me.BPlay.Size = New System.Drawing.Size(20, 21)
        Me.BPlay.TabIndex = 131
        Me.BPlay.TabStop = False
        Me.BPlay.Text = "♪"
        Me.BPlay.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BPlay.WIN = False
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(429, 3)
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
        Me.PIcono.ContextMenuStrip = Me.ComandoRapido_Menu
        Me.PIcono.Location = New System.Drawing.Point(11, 35)
        Me.PIcono.Name = "PIcono"
        Me.PIcono.Size = New System.Drawing.Size(32, 32)
        Me.PIcono.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PIcono.TabIndex = 0
        Me.PIcono.TabStop = False
        '
        'ComandoRapido_Menu
        '
        Me.ComandoRapido_Menu.AutoSize = False
        Me.ComandoRapido_Menu.ForeColor = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.ComandoRapido_Menu.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.ComandoRapido_Menu.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ComandoRapido_Menu_Seleccionar, Me.ComandoRapido_Menu_Separador0, Me.ComandoRapido_Menu_Examinar, Me.ComandoRapido_Menu_Separador1, Me.ComandoRapido_Menu_Default, Me.ComandoRapido_Menu_Separador2, Me.ComandoRapido_Menu_Eliminar})
        Me.ComandoRapido_Menu.Name = "NombreIcono_Menu"
        Me.ComandoRapido_Menu.ShowImageMargin = False
        Me.ComandoRapido_Menu.Size = New System.Drawing.Size(106, 111)
        '
        'ComandoRapido_Menu_Seleccionar
        '
        Me.ComandoRapido_Menu_Seleccionar.AutoSize = False
        Me.ComandoRapido_Menu_Seleccionar.Name = "ComandoRapido_Menu_Seleccionar"
        Me.ComandoRapido_Menu_Seleccionar.Size = New System.Drawing.Size(105, 22)
        Me.ComandoRapido_Menu_Seleccionar.Text = "Seleccionar"
        '
        'ComandoRapido_Menu_Separador0
        '
        Me.ComandoRapido_Menu_Separador0.Name = "ComandoRapido_Menu_Separador0"
        Me.ComandoRapido_Menu_Separador0.Size = New System.Drawing.Size(102, 6)
        '
        'ComandoRapido_Menu_Examinar
        '
        Me.ComandoRapido_Menu_Examinar.AutoSize = False
        Me.ComandoRapido_Menu_Examinar.Name = "ComandoRapido_Menu_Examinar"
        Me.ComandoRapido_Menu_Examinar.Size = New System.Drawing.Size(105, 22)
        Me.ComandoRapido_Menu_Examinar.Text = "Examinar..."
        '
        'ComandoRapido_Menu_Separador1
        '
        Me.ComandoRapido_Menu_Separador1.Name = "ComandoRapido_Menu_Separador1"
        Me.ComandoRapido_Menu_Separador1.Size = New System.Drawing.Size(102, 6)
        '
        'ComandoRapido_Menu_Default
        '
        Me.ComandoRapido_Menu_Default.AutoSize = False
        Me.ComandoRapido_Menu_Default.Name = "ComandoRapido_Menu_Default"
        Me.ComandoRapido_Menu_Default.Size = New System.Drawing.Size(105, 22)
        Me.ComandoRapido_Menu_Default.Text = "Por Defecto"
        '
        'ComandoRapido_Menu_Separador2
        '
        Me.ComandoRapido_Menu_Separador2.Name = "ComandoRapido_Menu_Separador2"
        Me.ComandoRapido_Menu_Separador2.Size = New System.Drawing.Size(102, 6)
        '
        'ComandoRapido_Menu_Eliminar
        '
        Me.ComandoRapido_Menu_Eliminar.AutoSize = False
        Me.ComandoRapido_Menu_Eliminar.Name = "ComandoRapido_Menu_Eliminar"
        Me.ComandoRapido_Menu_Eliminar.Size = New System.Drawing.Size(105, 22)
        Me.ComandoRapido_Menu_Eliminar.Text = "Eliminar"
        '
        'LHelp
        '
        Me.LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.LHelp.Location = New System.Drawing.Point(413, 5)
        Me.LHelp.Name = "LHelp"
        Me.LHelp.Size = New System.Drawing.Size(14, 17)
        Me.LHelp.TabIndex = 129
        Me.LHelp.Text = "?"
        Me.LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.LHelp.Value1 = ""
        Me.LHelp.Value2 = ""
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
        Me.BAceptar.Location = New System.Drawing.Point(310, 194)
        Me.BAceptar.Marked = False
        Me.BAceptar.Marked_Set = False
        Me.BAceptar.MarkedHovered = False
        Me.BAceptar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BAceptar.Name = "BAceptar"
        Me.BAceptar.Rounded = 7
        Me.BAceptar.SHIFT = False
        Me.BAceptar.Size = New System.Drawing.Size(61, 29)
        Me.BAceptar.TabIndex = 99
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
        Me.BCancelar.Location = New System.Drawing.Point(379, 194)
        Me.BCancelar.Marked = False
        Me.BCancelar.Marked_Set = False
        Me.BCancelar.MarkedHovered = False
        Me.BCancelar.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCancelar.Name = "BCancelar"
        Me.BCancelar.Rounded = 7
        Me.BCancelar.SHIFT = False
        Me.BCancelar.Size = New System.Drawing.Size(61, 29)
        Me.BCancelar.TabIndex = 99
        Me.BCancelar.TabStop = False
        Me.BCancelar.Text = "Cancelar"
        Me.BCancelar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BCancelar.WIN = False
        '
        'BExaminar_Sonido
        '
        Me.BExaminar_Sonido.ALT = False
        Me.BExaminar_Sonido.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BExaminar_Sonido.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Sonido.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Sonido.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Sonido.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BExaminar_Sonido.BorderColor2 = System.Drawing.Color.Red
        Me.BExaminar_Sonido.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BExaminar_Sonido.CTRL = False
        Me.BExaminar_Sonido.DoubleText = True
        Me.BExaminar_Sonido.FirstLetterColor = System.Drawing.Color.White
        Me.BExaminar_Sonido.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.BExaminar_Sonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BExaminar_Sonido.ForeColor2 = System.Drawing.Color.Black
        Me.BExaminar_Sonido.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Sonido.GradientAngle = 90
        Me.BExaminar_Sonido.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BExaminar_Sonido.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BExaminar_Sonido.GradientTransparency = 0
        Me.BExaminar_Sonido.Image = Nothing
        Me.BExaminar_Sonido.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BExaminar_Sonido.ImageCode = ""
        Me.BExaminar_Sonido.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BExaminar_Sonido.ImageSize = New System.Drawing.Size(19, 19)
        Me.BExaminar_Sonido.Location = New System.Drawing.Point(264, 198)
        Me.BExaminar_Sonido.Marked = False
        Me.BExaminar_Sonido.Marked_Set = False
        Me.BExaminar_Sonido.MarkedHovered = False
        Me.BExaminar_Sonido.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Sonido.Name = "BExaminar_Sonido"
        Me.BExaminar_Sonido.Rounded = 1
        Me.BExaminar_Sonido.SHIFT = False
        Me.BExaminar_Sonido.Size = New System.Drawing.Size(20, 21)
        Me.BExaminar_Sonido.TabIndex = 132
        Me.BExaminar_Sonido.TabStop = False
        Me.BExaminar_Sonido.Text = "..."
        Me.BExaminar_Sonido.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BExaminar_Sonido.WIN = False
        '
        'BExaminar_Ruta
        '
        Me.BExaminar_Ruta.ALT = False
        Me.BExaminar_Ruta.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.BExaminar_Ruta.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Ruta.BaseColor2 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Ruta.BaseColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.BExaminar_Ruta.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BExaminar_Ruta.BorderColor2 = System.Drawing.Color.Red
        Me.BExaminar_Ruta.BorderColor3 = System.Drawing.Color.DarkGreen
        Me.BExaminar_Ruta.CTRL = False
        Me.BExaminar_Ruta.DoubleText = True
        Me.BExaminar_Ruta.FirstLetterColor = System.Drawing.Color.White
        Me.BExaminar_Ruta.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.BExaminar_Ruta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.BExaminar_Ruta.ForeColor2 = System.Drawing.Color.Black
        Me.BExaminar_Ruta.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Ruta.GradientAngle = 90
        Me.BExaminar_Ruta.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.BExaminar_Ruta.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.BExaminar_Ruta.GradientTransparency = 0
        Me.BExaminar_Ruta.Image = Nothing
        Me.BExaminar_Ruta.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft
        Me.BExaminar_Ruta.ImageCode = ""
        Me.BExaminar_Ruta.ImageMode = KDesktop.NSButton.STImageMode.Normal
        Me.BExaminar_Ruta.ImageSize = New System.Drawing.Size(19, 19)
        Me.BExaminar_Ruta.Location = New System.Drawing.Point(211, 90)
        Me.BExaminar_Ruta.Marked = False
        Me.BExaminar_Ruta.Marked_Set = False
        Me.BExaminar_Ruta.MarkedHovered = False
        Me.BExaminar_Ruta.MarkedSetColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BExaminar_Ruta.Name = "BExaminar_Ruta"
        Me.BExaminar_Ruta.Rounded = 1
        Me.BExaminar_Ruta.SHIFT = False
        Me.BExaminar_Ruta.Size = New System.Drawing.Size(20, 21)
        Me.BExaminar_Ruta.TabIndex = 127
        Me.BExaminar_Ruta.TabStop = False
        Me.BExaminar_Ruta.Text = "..."
        Me.BExaminar_Ruta.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        Me.BExaminar_Ruta.WIN = False
        '
        'LCSonido
        '
        Me.LCSonido.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.LCSonido.Cursor = System.Windows.Forms.Cursors.Hand
        Me.LCSonido.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LCSonido.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCSonido.Location = New System.Drawing.Point(74, 202)
        Me.LCSonido.Name = "LCSonido"
        Me.LCSonido.Size = New System.Drawing.Size(168, 13)
        Me.LCSonido.TabIndex = 133
        Me.LCSonido.Text = "Sin Sonido"
        Me.LCSonido.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'GInformacion
        '
        Me.GInformacion.Controls.Add(Me.GInformacion_LoadingCircle)
        Me.GInformacion.Controls.Add(Me.GInformacion_LTipo)
        Me.GInformacion.Controls.Add(Me.GInformacion_SinInformacion)
        Me.GInformacion.Controls.Add(Me.GInformacion_LNombre)
        Me.GInformacion.Controls.Add(Me.GInformacion_PIcono)
        Me.GInformacion.Controls.Add(Me.GInformacion_LCompany)
        Me.GInformacion.DrawSeperator = False
        Me.GInformacion.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.GInformacion.Location = New System.Drawing.Point(210, 123)
        Me.GInformacion.Name = "GInformacion"
        Me.GInformacion.Size = New System.Drawing.Size(230, 64)
        Me.GInformacion.SubTitle = ""
        Me.GInformacion.TabIndex = 126
        Me.GInformacion.Title = ""
        '
        'GInformacion_LoadingCircle
        '
        Me.GInformacion_LoadingCircle.Active = False
        Me.GInformacion_LoadingCircle.Color = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.GInformacion_LoadingCircle.InnerCircleRadius = 5
        Me.GInformacion_LoadingCircle.Location = New System.Drawing.Point(91, 11)
        Me.GInformacion_LoadingCircle.Name = "GInformacion_LoadingCircle"
        Me.GInformacion_LoadingCircle.NumberSpoke = 12
        Me.GInformacion_LoadingCircle.OuterCircleRadius = 11
        Me.GInformacion_LoadingCircle.RotationSpeed = 50
        Me.GInformacion_LoadingCircle.Size = New System.Drawing.Size(47, 42)
        Me.GInformacion_LoadingCircle.SpokeThickness = 2
        Me.GInformacion_LoadingCircle.StylePreset = KDesktop.NSLoadingCircle.StylePresets.MacOSX
        Me.GInformacion_LoadingCircle.TabIndex = 116
        Me.GInformacion_LoadingCircle.Visible = False
        '
        'GInformacion_LTipo
        '
        Me.GInformacion_LTipo.Font = New System.Drawing.Font("Verdana", 6.5!)
        Me.GInformacion_LTipo.ForeColor = System.Drawing.Color.FromArgb(CType(CType(144, Byte), Integer), CType(CType(144, Byte), Integer), CType(CType(144, Byte), Integer))
        Me.GInformacion_LTipo.Location = New System.Drawing.Point(52, 39)
        Me.GInformacion_LTipo.Name = "GInformacion_LTipo"
        Me.GInformacion_LTipo.Size = New System.Drawing.Size(171, 17)
        Me.GInformacion_LTipo.TabIndex = 119
        Me.GInformacion_LTipo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'GInformacion_SinInformacion
        '
        Me.GInformacion_SinInformacion.Font = New System.Drawing.Font("Verdana", 9.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.GInformacion_SinInformacion.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.GInformacion_SinInformacion.Location = New System.Drawing.Point(57, 19)
        Me.GInformacion_SinInformacion.Name = "GInformacion_SinInformacion"
        Me.GInformacion_SinInformacion.Size = New System.Drawing.Size(116, 25)
        Me.GInformacion_SinInformacion.TabIndex = 117
        Me.GInformacion_SinInformacion.TabStop = False
        Me.GInformacion_SinInformacion.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.GInformacion_SinInformacion.Value1 = "S"
        Me.GInformacion_SinInformacion.Value2 = "in informacion"
        Me.GInformacion_SinInformacion.Visible = False
        '
        'GInformacion_LNombre
        '
        Me.GInformacion_LNombre.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.GInformacion_LNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.GInformacion_LNombre.Location = New System.Drawing.Point(49, 6)
        Me.GInformacion_LNombre.Name = "GInformacion_LNombre"
        Me.GInformacion_LNombre.Size = New System.Drawing.Size(177, 17)
        Me.GInformacion_LNombre.TabIndex = 115
        Me.GInformacion_LNombre.TabStop = False
        Me.GInformacion_LNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.GInformacion_LNombre.Value1 = ""
        Me.GInformacion_LNombre.Value2 = ""
        '
        'GInformacion_PIcono
        '
        Me.GInformacion_PIcono.Location = New System.Drawing.Point(10, 15)
        Me.GInformacion_PIcono.Name = "GInformacion_PIcono"
        Me.GInformacion_PIcono.Size = New System.Drawing.Size(32, 32)
        Me.GInformacion_PIcono.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.GInformacion_PIcono.TabIndex = 0
        Me.GInformacion_PIcono.TabStop = False
        '
        'GInformacion_LCompany
        '
        Me.GInformacion_LCompany.Font = New System.Drawing.Font("Verdana", 7.25!)
        Me.GInformacion_LCompany.ForeColor1 = System.Drawing.Color.Gray
        Me.GInformacion_LCompany.Location = New System.Drawing.Point(50, 22)
        Me.GInformacion_LCompany.Name = "GInformacion_LCompany"
        Me.GInformacion_LCompany.Size = New System.Drawing.Size(176, 17)
        Me.GInformacion_LCompany.TabIndex = 115
        Me.GInformacion_LCompany.TabStop = False
        Me.GInformacion_LCompany.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.GInformacion_LCompany.Value1 = ""
        Me.GInformacion_LCompany.Value2 = ""
        '
        'LModo
        '
        Me.LModo.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LModo.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LModo.Location = New System.Drawing.Point(25, 127)
        Me.LModo.Name = "LModo"
        Me.LModo.Size = New System.Drawing.Size(42, 19)
        Me.LModo.TabIndex = 118
        Me.LModo.TabStop = False
        Me.LModo.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LModo.Value1 = "M"
        Me.LModo.Value2 = "odo:"
        '
        'TSonido
        '
        Me.TSonido.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TSonido.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TSonido.Cursor = System.Windows.Forms.Cursors.Hand
        Me.TSonido.Enabled = False
        Me.TSonido.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSonido.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TSonido.HideSelection = False
        Me.TSonido.Location = New System.Drawing.Point(69, 197)
        Me.TSonido.MaxLength = 200
        Me.TSonido.Multiline = False
        Me.TSonido.Name = "TSonido"
        Me.TSonido.ReadOnly = True
        Me.TSonido.Rounded = 1
        Me.TSonido.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TSonido.SelectedText = ""
        Me.TSonido.SelectionLength = 0
        Me.TSonido.SelectionStart = 0
        Me.TSonido.ShortcutsEnabled = True
        Me.TSonido.Size = New System.Drawing.Size(178, 23)
        Me.TSonido.TabIndex = 134
        Me.TSonido.TabStop = False
        Me.TSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TSonido.UseSystemPasswordChar = False
        Me.TSonido.WordWrap = False
        '
        'CSonido
        '
        Me.CSonido.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.CSonido.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CSonido.BaseColor2 = System.Drawing.Color.Empty
        Me.CSonido.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CSonido.BorderColor2 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CSonido.BorderColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CSonido.DoubleText = True
        Me.CSonido.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed
        Me.CSonido.DropdownBorderColor = System.Drawing.Color.Gray
        Me.CSonido.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.CSonido.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.CSonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CSonido.ForeColor2 = System.Drawing.Color.Black
        Me.CSonido.FormattingEnabled = True
        Me.CSonido.GradientAngle = 90
        Me.CSonido.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.CSonido.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CSonido.GradientTransparency = 0
        Me.CSonido.HoverColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CSonido.HoverColor2 = System.Drawing.Color.Black
        Me.CSonido.Image = Nothing
        Me.CSonido.ImageAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CSonido.ImageMode = KDesktop.NSComboBox.STImageMode.Normal
        Me.CSonido.ImageSize = New System.Drawing.Size(19, 15)
        Me.CSonido.ItemBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CSonido.ItemColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CSonido.ItemFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CSonido.ItemHoverBackColor = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CSonido.ItemHoverColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CSonido.ItemHoverFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CSonido.Items.AddRange(New Object() {"Sin Sonido"})
        Me.CSonido.ItemSelectedBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CSonido.ItemSelectedColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CSonido.ItemSelectedFont = New System.Drawing.Font("Verdana", 8.25!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.CSonido.ItemTextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CSonido.JustButton = False
        Me.CSonido.Location = New System.Drawing.Point(70, 199)
        Me.CSonido.Name = "CSonido"
        Me.CSonido.Rounded = 1
        Me.CSonido.Size = New System.Drawing.Size(176, 21)
        Me.CSonido.TabIndex = 135
        Me.CSonido.TabStop = False
        Me.CSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        '
        'LAdministrador
        '
        Me.LAdministrador.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LAdministrador.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LAdministrador.Location = New System.Drawing.Point(345, 38)
        Me.LAdministrador.Name = "LAdministrador"
        Me.LAdministrador.Size = New System.Drawing.Size(94, 19)
        Me.LAdministrador.TabIndex = 117
        Me.LAdministrador.TabStop = False
        Me.LAdministrador.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LAdministrador.Value1 = "A"
        Me.LAdministrador.Value2 = "dministrador"
        '
        'Separador1
        '
        Me.Separador1.Cross = False
        Me.Separador1.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador1.Glowing = False
        Me.Separador1.LineColor = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.Separador1.Location = New System.Drawing.Point(267, 49)
        Me.Separador1.Name = "Separador1"
        Me.Separador1.Size = New System.Drawing.Size(12, 12)
        Me.Separador1.TabIndex = 124
        Me.Separador1.Text = "NsSeperator1"
        Me.Separador1.VerticalLine = False
        '
        'CAdministrador
        '
        Me.CAdministrador.Checked = False
        Me.CAdministrador.Location = New System.Drawing.Point(283, 36)
        Me.CAdministrador.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CAdministrador.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CAdministrador.Name = "CAdministrador"
        Me.CAdministrador.Size = New System.Drawing.Size(56, 24)
        Me.CAdministrador.TabIndex = 116
        Me.CAdministrador.TabStop = False
        '
        'Separador0
        '
        Me.Separador0.Cross = False
        Me.Separador0.GlowColor = System.Drawing.Color.DarkGreen
        Me.Separador0.Glowing = False
        Me.Separador0.LineColor = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.Separador0.Location = New System.Drawing.Point(235, 100)
        Me.Separador0.Name = "Separador0"
        Me.Separador0.Size = New System.Drawing.Size(12, 12)
        Me.Separador0.TabIndex = 124
        Me.Separador0.Text = "NsSeperator1"
        Me.Separador0.VerticalLine = False
        '
        'TRuta
        '
        Me.TRuta.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TRuta.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TRuta.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TRuta.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TRuta.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TRuta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TRuta.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TRuta.HideSelection = False
        Me.TRuta.Location = New System.Drawing.Point(7, 89)
        Me.TRuta.MaxLength = 255
        Me.TRuta.Multiline = False
        Me.TRuta.Name = "TRuta"
        Me.TRuta.ReadOnly = False
        Me.TRuta.Rounded = 1
        Me.TRuta.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TRuta.SelectedText = ""
        Me.TRuta.SelectionLength = 0
        Me.TRuta.SelectionStart = 0
        Me.TRuta.ShortcutsEnabled = True
        Me.TRuta.Size = New System.Drawing.Size(206, 23)
        Me.TRuta.TabIndex = 111
        Me.TRuta.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TRuta.UseSystemPasswordChar = False
        Me.TRuta.WordWrap = False
        '
        'LCPrioridad
        '
        Me.LCPrioridad.Cursor = System.Windows.Forms.Cursors.Hand
        Me.LCPrioridad.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LCPrioridad.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCPrioridad.Location = New System.Drawing.Point(74, 166)
        Me.LCPrioridad.Name = "LCPrioridad"
        Me.LCPrioridad.Size = New System.Drawing.Size(103, 13)
        Me.LCPrioridad.TabIndex = 108
        Me.LCPrioridad.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'LRuta
        '
        Me.LRuta.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LRuta.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LRuta.Location = New System.Drawing.Point(12, 70)
        Me.LRuta.Name = "LRuta"
        Me.LRuta.Size = New System.Drawing.Size(42, 17)
        Me.LRuta.TabIndex = 113
        Me.LRuta.TabStop = False
        Me.LRuta.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LRuta.Value1 = "R"
        Me.LRuta.Value2 = "uta:"
        '
        'LCModo
        '
        Me.LCModo.Cursor = System.Windows.Forms.Cursors.Hand
        Me.LCModo.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LCModo.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LCModo.Location = New System.Drawing.Point(74, 130)
        Me.LCModo.Name = "LCModo"
        Me.LCModo.Size = New System.Drawing.Size(103, 13)
        Me.LCModo.TabIndex = 109
        Me.LCModo.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'TParametros
        '
        Me.TParametros.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TParametros.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TParametros.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TParametros.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TParametros.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TParametros.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TParametros.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TParametros.HideSelection = False
        Me.TParametros.Location = New System.Drawing.Point(250, 89)
        Me.TParametros.MaxLength = 255
        Me.TParametros.Multiline = False
        Me.TParametros.Name = "TParametros"
        Me.TParametros.ReadOnly = False
        Me.TParametros.Rounded = 1
        Me.TParametros.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TParametros.SelectedText = ""
        Me.TParametros.SelectionLength = 0
        Me.TParametros.SelectionStart = 0
        Me.TParametros.ShortcutsEnabled = True
        Me.TParametros.Size = New System.Drawing.Size(188, 23)
        Me.TParametros.TabIndex = 112
        Me.TParametros.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TParametros.UseSystemPasswordChar = False
        Me.TParametros.WordWrap = False
        '
        'LParametros
        '
        Me.LParametros.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LParametros.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LParametros.Location = New System.Drawing.Point(255, 70)
        Me.LParametros.Name = "LParametros"
        Me.LParametros.Size = New System.Drawing.Size(73, 17)
        Me.LParametros.TabIndex = 114
        Me.LParametros.TabStop = False
        Me.LParametros.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LParametros.Value1 = "P"
        Me.LParametros.Value2 = "arametros:"
        '
        'LNombre
        '
        Me.LNombre.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LNombre.Location = New System.Drawing.Point(50, 41)
        Me.LNombre.Name = "LNombre"
        Me.LNombre.Size = New System.Drawing.Size(73, 17)
        Me.LNombre.TabIndex = 114
        Me.LNombre.TabStop = False
        Me.LNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LNombre.Value1 = "N"
        Me.LNombre.Value2 = "ombre:"
        '
        'TModo
        '
        Me.TModo.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TModo.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TModo.Cursor = System.Windows.Forms.Cursors.Hand
        Me.TModo.Enabled = False
        Me.TModo.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TModo.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TModo.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TModo.HideSelection = False
        Me.TModo.Location = New System.Drawing.Point(69, 125)
        Me.TModo.MaxLength = 200
        Me.TModo.Multiline = False
        Me.TModo.Name = "TModo"
        Me.TModo.ReadOnly = True
        Me.TModo.Rounded = 1
        Me.TModo.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TModo.SelectedText = ""
        Me.TModo.SelectionLength = 0
        Me.TModo.SelectionStart = 0
        Me.TModo.ShortcutsEnabled = False
        Me.TModo.Size = New System.Drawing.Size(113, 23)
        Me.TModo.TabIndex = 121
        Me.TModo.TabStop = False
        Me.TModo.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TModo.UseSystemPasswordChar = False
        Me.TModo.WordWrap = False
        '
        'TPrioridad
        '
        Me.TPrioridad.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TPrioridad.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TPrioridad.Cursor = System.Windows.Forms.Cursors.Hand
        Me.TPrioridad.Enabled = False
        Me.TPrioridad.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TPrioridad.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TPrioridad.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TPrioridad.HideSelection = False
        Me.TPrioridad.Location = New System.Drawing.Point(69, 161)
        Me.TPrioridad.MaxLength = 200
        Me.TPrioridad.Multiline = False
        Me.TPrioridad.Name = "TPrioridad"
        Me.TPrioridad.ReadOnly = True
        Me.TPrioridad.Rounded = 1
        Me.TPrioridad.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TPrioridad.SelectedText = ""
        Me.TPrioridad.SelectionLength = 0
        Me.TPrioridad.SelectionStart = 0
        Me.TPrioridad.ShortcutsEnabled = False
        Me.TPrioridad.Size = New System.Drawing.Size(113, 23)
        Me.TPrioridad.TabIndex = 120
        Me.TPrioridad.TabStop = False
        Me.TPrioridad.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TPrioridad.UseSystemPasswordChar = False
        Me.TPrioridad.WordWrap = False
        '
        'CModo
        '
        Me.CModo.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.CModo.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CModo.BaseColor2 = System.Drawing.Color.Empty
        Me.CModo.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CModo.BorderColor2 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CModo.BorderColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CModo.DoubleText = True
        Me.CModo.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed
        Me.CModo.DropdownBorderColor = System.Drawing.Color.Gray
        Me.CModo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.CModo.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.CModo.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CModo.ForeColor2 = System.Drawing.Color.Black
        Me.CModo.FormattingEnabled = True
        Me.CModo.GradientAngle = 90
        Me.CModo.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.CModo.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CModo.GradientTransparency = 0
        Me.CModo.HoverColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CModo.HoverColor2 = System.Drawing.Color.Black
        Me.CModo.Image = Nothing
        Me.CModo.ImageAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CModo.ImageMode = KDesktop.NSComboBox.STImageMode.Normal
        Me.CModo.ImageSize = New System.Drawing.Size(19, 15)
        Me.CModo.ItemBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CModo.ItemColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CModo.ItemFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CModo.ItemHoverBackColor = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CModo.ItemHoverColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CModo.ItemHoverFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CModo.Items.AddRange(New Object() {"Normal", "Minimizado", "Maximizado", "Oculto"})
        Me.CModo.ItemSelectedBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CModo.ItemSelectedColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CModo.ItemSelectedFont = New System.Drawing.Font("Verdana", 8.25!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.CModo.ItemTextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CModo.JustButton = False
        Me.CModo.Location = New System.Drawing.Point(70, 127)
        Me.CModo.Name = "CModo"
        Me.CModo.Rounded = 5
        Me.CModo.Size = New System.Drawing.Size(111, 21)
        Me.CModo.TabIndex = 123
        Me.CModo.TabStop = False
        Me.CModo.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        '
        'CPrioridad
        '
        Me.CPrioridad.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None
        Me.CPrioridad.BaseColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CPrioridad.BaseColor2 = System.Drawing.Color.Empty
        Me.CPrioridad.BorderColor1 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CPrioridad.BorderColor2 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CPrioridad.BorderColor3 = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.CPrioridad.DoubleText = True
        Me.CPrioridad.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed
        Me.CPrioridad.DropdownBorderColor = System.Drawing.Color.Gray
        Me.CPrioridad.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.CPrioridad.Font = New System.Drawing.Font("Verdana", 8.0!)
        Me.CPrioridad.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CPrioridad.ForeColor2 = System.Drawing.Color.Black
        Me.CPrioridad.FormattingEnabled = True
        Me.CPrioridad.GradientAngle = 90
        Me.CPrioridad.GradientColor1 = System.Drawing.Color.FromArgb(CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer), CType(CType(60, Byte), Integer))
        Me.CPrioridad.GradientColor2 = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CPrioridad.GradientTransparency = 0
        Me.CPrioridad.HoverColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CPrioridad.HoverColor2 = System.Drawing.Color.Black
        Me.CPrioridad.Image = Nothing
        Me.CPrioridad.ImageAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CPrioridad.ImageMode = KDesktop.NSComboBox.STImageMode.Normal
        Me.CPrioridad.ImageSize = New System.Drawing.Size(19, 15)
        Me.CPrioridad.ItemBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CPrioridad.ItemColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.CPrioridad.ItemFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CPrioridad.ItemHoverBackColor = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.CPrioridad.ItemHoverColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CPrioridad.ItemHoverFont = New System.Drawing.Font("Verdana", 8.25!)
        Me.CPrioridad.Items.AddRange(New Object() {"Low", "Below Normal", "Normal", "Above Normal", "High", "Real Time"})
        Me.CPrioridad.ItemSelectedBackColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.CPrioridad.ItemSelectedColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.CPrioridad.ItemSelectedFont = New System.Drawing.Font("Verdana", 8.25!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.CPrioridad.ItemTextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CPrioridad.JustButton = False
        Me.CPrioridad.Location = New System.Drawing.Point(70, 163)
        Me.CPrioridad.Name = "CPrioridad"
        Me.CPrioridad.Rounded = 5
        Me.CPrioridad.Size = New System.Drawing.Size(111, 21)
        Me.CPrioridad.TabIndex = 122
        Me.CPrioridad.TabStop = False
        Me.CPrioridad.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        '
        'LPrioridad
        '
        Me.LPrioridad.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LPrioridad.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LPrioridad.Location = New System.Drawing.Point(6, 163)
        Me.LPrioridad.Name = "LPrioridad"
        Me.LPrioridad.Size = New System.Drawing.Size(65, 19)
        Me.LPrioridad.TabIndex = 119
        Me.LPrioridad.TabStop = False
        Me.LPrioridad.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LPrioridad.Value1 = "P"
        Me.LPrioridad.Value2 = "rioridad:"
        '
        'FComandoRapido
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(452, 233)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.Name = "FComandoRapido"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Comando Rápido"
        Me.FTheme.ResumeLayout(False)
        Me.FTheme.PerformLayout()
        CType(Me.PModo, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PPrioridad, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PIcono, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ComandoRapido_Menu.ResumeLayout(False)
        Me.GInformacion.ResumeLayout(False)
        CType(Me.GInformacion_PIcono, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents FTheme As KDesktop.NSTheme
    Friend WithEvents BCancelar As KDesktop.NSButton
    Friend WithEvents BAceptar As KDesktop.NSButton
    Friend WithEvents BCerrar As KDesktop.NSControlButton
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents BExaminar_Ruta As NSButton
    Friend WithEvents GInformacion As NSGroupBox
    Friend WithEvents GInformacion_SinInformacion As NSLabel
    Friend WithEvents GInformacion_LoadingCircle As NSLoadingCircle
    Friend WithEvents GInformacion_LNombre As NSLabel
    Friend WithEvents GInformacion_PIcono As PictureBox
    Friend WithEvents GInformacion_LCompany As NSLabel
    Friend WithEvents Separador0 As NSSeperator
    Friend WithEvents LCPrioridad As Label
    Friend WithEvents LCModo As Label
    Friend WithEvents LParametros As NSLabel
    Friend WithEvents TParametros As NSTextBox
    Friend WithEvents LRuta As NSLabel
    Friend WithEvents TRuta As NSTextBox
    Friend WithEvents CAdministrador As NSOnOffBox
    Friend WithEvents LAdministrador As NSLabel
    Friend WithEvents TPrioridad As NSTextBox
    Friend WithEvents LModo As NSLabel
    Friend WithEvents CPrioridad As NSComboBox
    Friend WithEvents TModo As NSTextBox
    Friend WithEvents CModo As NSComboBox
    Friend WithEvents LPrioridad As NSLabel
    Friend WithEvents LHelp As NSLabel
    Friend WithEvents LSonido As NSLabel
    Friend WithEvents BPlay As NSButton
    Friend WithEvents BExaminar_Sonido As NSButton
    Friend WithEvents LCSonido As Label
    Friend WithEvents TSonido As NSTextBox
    Friend WithEvents CSonido As NSComboBox
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents PIcono As PictureBox
    Friend WithEvents Separador1 As NSSeperator
    Friend WithEvents LNombre As NSLabel
    Friend WithEvents ComandoRapido_Menu As NSContextMenu
    Friend WithEvents ComandoRapido_Menu_Seleccionar As ToolStripMenuItem
    Friend WithEvents ComandoRapido_Menu_Separador0 As ToolStripSeparator
    Friend WithEvents ComandoRapido_Menu_Examinar As ToolStripMenuItem
    Friend WithEvents ComandoRapido_Menu_Separador1 As ToolStripSeparator
    Friend WithEvents ComandoRapido_Menu_Default As ToolStripMenuItem
    Friend WithEvents ComandoRapido_Menu_Separador2 As ToolStripSeparator
    Friend WithEvents ComandoRapido_Menu_Eliminar As ToolStripMenuItem
    Friend WithEvents GInformacion_LTipo As Label
    Friend WithEvents PModo As PictureBox
    Friend WithEvents PPrioridad As PictureBox
End Class
