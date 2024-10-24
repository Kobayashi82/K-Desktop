<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FVideoRecorder
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FVideoRecorder))
        Me.FTheme = New KDesktop.NSTheme()
        Me.PCopiar = New System.Windows.Forms.PictureBox()
        Me.TNombre = New KDesktop.NSTextBox()
        Me.GrabacionPantalla_LHelp = New KDesktop.NSLabel()
        Me.PCerrar = New System.Windows.Forms.PictureBox()
        Me.PTelegram = New System.Windows.Forms.PictureBox()
        Me.PGuardar = New System.Windows.Forms.PictureBox()
        Me.PEditar = New System.Windows.Forms.PictureBox()
        Me.GGif = New KDesktop.NSGroupBox()
        Me.CGif = New KDesktop.NSOnOffBox()
        Me.LGif = New KDesktop.NSLabel()
        Me.GSonido = New KDesktop.NSGroupBox()
        Me.CSonido = New KDesktop.NSOnOffBox()
        Me.LSonido = New KDesktop.NSLabel()
        Me.LTamaño = New KDesktop.NSLabel()
        Me.Tamaño = New KDesktop.NSLabel()
        Me.LDuracion = New KDesktop.NSLabel()
        Me.Duracion = New KDesktop.NSLabel()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.BMaximizar = New KDesktop.NSControlButton()
        Me.BClose = New KDesktop.NSControlButton()
        Me.GPlayer = New KDesktop.NSGroupBox()
        Me.Panel1 = New System.Windows.Forms.Panel()
        Me.NoVideoImage = New System.Windows.Forms.PictureBox()
        Me.Progreso = New KDesktop.NSProgressBar()
        Me.BPlay = New System.Windows.Forms.Label()
        Me.LProgreso = New System.Windows.Forms.Label()
        Me.Player1 = New AxWMPLib.AxWindowsMediaPlayer()
        Me.EditingPanel = New System.Windows.Forms.Panel()
        Me.EditingLabel = New KDesktop.NSLabel()
        Me.FTheme.SuspendLayout()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PEditar, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.GGif.SuspendLayout()
        Me.GSonido.SuspendLayout()
        Me.GPlayer.SuspendLayout()
        Me.Panel1.SuspendLayout()
        CType(Me.NoVideoImage, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.Player1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.EditingPanel.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.PCopiar)
        Me.FTheme.Controls.Add(Me.TNombre)
        Me.FTheme.Controls.Add(Me.GrabacionPantalla_LHelp)
        Me.FTheme.Controls.Add(Me.PCerrar)
        Me.FTheme.Controls.Add(Me.PTelegram)
        Me.FTheme.Controls.Add(Me.PGuardar)
        Me.FTheme.Controls.Add(Me.PEditar)
        Me.FTheme.Controls.Add(Me.GGif)
        Me.FTheme.Controls.Add(Me.GSonido)
        Me.FTheme.Controls.Add(Me.LTamaño)
        Me.FTheme.Controls.Add(Me.Tamaño)
        Me.FTheme.Controls.Add(Me.LDuracion)
        Me.FTheme.Controls.Add(Me.Duracion)
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Controls.Add(Me.BMaximizar)
        Me.FTheme.Controls.Add(Me.BClose)
        Me.FTheme.Controls.Add(Me.GPlayer)
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
        Me.FTheme.Sizable = True
        Me.FTheme.Size = New System.Drawing.Size(1058, 681)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Grabar Pantalla"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'PCopiar
        '
        Me.PCopiar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCopiar.Image = Global.KDesktop.My.Resources.Resources.Copiar_Imagen_Off
        Me.PCopiar.Location = New System.Drawing.Point(961, 650)
        Me.PCopiar.Name = "PCopiar"
        Me.PCopiar.Size = New System.Drawing.Size(23, 23)
        Me.PCopiar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCopiar.TabIndex = 170
        Me.PCopiar.TabStop = False
        '
        'TNombre
        '
        Me.TNombre.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TNombre.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TNombre.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TNombre.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TNombre.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TNombre.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer))
        Me.TNombre.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.HideSelection = False
        Me.TNombre.Location = New System.Drawing.Point(789, 650)
        Me.TNombre.MaxLength = 30
        Me.TNombre.Multiline = False
        Me.TNombre.Name = "TNombre"
        Me.TNombre.ReadOnly = False
        Me.TNombre.Rounded = 7
        Me.TNombre.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TNombre.SelectedText = ""
        Me.TNombre.SelectionLength = 0
        Me.TNombre.SelectionStart = 0
        Me.TNombre.ShortcutsEnabled = True
        Me.TNombre.Size = New System.Drawing.Size(108, 23)
        Me.TNombre.TabIndex = 35
        Me.TNombre.Text = "Escriba un nombre"
        Me.TNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TNombre.UseSystemPasswordChar = False
        Me.TNombre.WordWrap = False
        '
        'GrabacionPantalla_LHelp
        '
        Me.GrabacionPantalla_LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GrabacionPantalla_LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.GrabacionPantalla_LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.GrabacionPantalla_LHelp.Location = New System.Drawing.Point(984, 5)
        Me.GrabacionPantalla_LHelp.Name = "GrabacionPantalla_LHelp"
        Me.GrabacionPantalla_LHelp.Size = New System.Drawing.Size(14, 17)
        Me.GrabacionPantalla_LHelp.TabIndex = 130
        Me.GrabacionPantalla_LHelp.Text = "?"
        Me.GrabacionPantalla_LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.GrabacionPantalla_LHelp.Value1 = ""
        Me.GrabacionPantalla_LHelp.Value2 = ""
        '
        'PCerrar
        '
        Me.PCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCerrar.Image = Global.KDesktop.My.Resources.Resources.Cerrar_Off
        Me.PCerrar.Location = New System.Drawing.Point(1019, 650)
        Me.PCerrar.Name = "PCerrar"
        Me.PCerrar.Size = New System.Drawing.Size(23, 23)
        Me.PCerrar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCerrar.TabIndex = 41
        Me.PCerrar.TabStop = False
        '
        'PTelegram
        '
        Me.PTelegram.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PTelegram.Image = Global.KDesktop.My.Resources.Resources.Telegram_Off
        Me.PTelegram.Location = New System.Drawing.Point(903, 650)
        Me.PTelegram.Name = "PTelegram"
        Me.PTelegram.Size = New System.Drawing.Size(23, 23)
        Me.PTelegram.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PTelegram.TabIndex = 41
        Me.PTelegram.TabStop = False
        '
        'PGuardar
        '
        Me.PGuardar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PGuardar.Image = Global.KDesktop.My.Resources.Resources.Save_Off
        Me.PGuardar.Location = New System.Drawing.Point(990, 650)
        Me.PGuardar.Name = "PGuardar"
        Me.PGuardar.Size = New System.Drawing.Size(23, 23)
        Me.PGuardar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PGuardar.TabIndex = 41
        Me.PGuardar.TabStop = False
        '
        'PEditar
        '
        Me.PEditar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PEditar.Image = Global.KDesktop.My.Resources.Resources.GrabacionVideo_Editar_Off
        Me.PEditar.Location = New System.Drawing.Point(932, 650)
        Me.PEditar.Name = "PEditar"
        Me.PEditar.Size = New System.Drawing.Size(23, 23)
        Me.PEditar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PEditar.TabIndex = 41
        Me.PEditar.TabStop = False
        '
        'GGif
        '
        Me.GGif.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GGif.Controls.Add(Me.CGif)
        Me.GGif.Controls.Add(Me.LGif)
        Me.GGif.DrawSeperator = False
        Me.GGif.Location = New System.Drawing.Point(488, 647)
        Me.GGif.Name = "GGif"
        Me.GGif.Size = New System.Drawing.Size(89, 30)
        Me.GGif.SubTitle = ""
        Me.GGif.TabIndex = 39
        Me.GGif.Title = ""
        '
        'CGif
        '
        Me.CGif.Checked = False
        Me.CGif.Location = New System.Drawing.Point(3, 3)
        Me.CGif.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CGif.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CGif.Name = "CGif"
        Me.CGif.Size = New System.Drawing.Size(56, 24)
        Me.CGif.TabIndex = 3
        '
        'LGif
        '
        Me.LGif.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LGif.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LGif.Location = New System.Drawing.Point(62, 6)
        Me.LGif.Name = "LGif"
        Me.LGif.Size = New System.Drawing.Size(25, 17)
        Me.LGif.TabIndex = 11
        Me.LGif.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LGif.Value1 = "G"
        Me.LGif.Value2 = "IF"
        '
        'GSonido
        '
        Me.GSonido.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GSonido.Controls.Add(Me.CSonido)
        Me.GSonido.Controls.Add(Me.LSonido)
        Me.GSonido.DrawSeperator = False
        Me.GSonido.Location = New System.Drawing.Point(580, 647)
        Me.GSonido.Name = "GSonido"
        Me.GSonido.Size = New System.Drawing.Size(110, 30)
        Me.GSonido.SubTitle = ""
        Me.GSonido.TabIndex = 39
        Me.GSonido.Title = ""
        '
        'CSonido
        '
        Me.CSonido.Checked = True
        Me.CSonido.Location = New System.Drawing.Point(3, 3)
        Me.CSonido.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CSonido.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CSonido.Name = "CSonido"
        Me.CSonido.Size = New System.Drawing.Size(56, 24)
        Me.CSonido.TabIndex = 3
        '
        'LSonido
        '
        Me.LSonido.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LSonido.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LSonido.Location = New System.Drawing.Point(62, 6)
        Me.LSonido.Name = "LSonido"
        Me.LSonido.Size = New System.Drawing.Size(47, 17)
        Me.LSonido.TabIndex = 11
        Me.LSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LSonido.Value1 = "S"
        Me.LSonido.Value2 = "onido"
        '
        'LTamaño
        '
        Me.LTamaño.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LTamaño.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LTamaño.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LTamaño.Location = New System.Drawing.Point(701, 660)
        Me.LTamaño.Name = "LTamaño"
        Me.LTamaño.Size = New System.Drawing.Size(40, 17)
        Me.LTamaño.TabIndex = 38
        Me.LTamaño.Text = "Tamaño:"
        Me.LTamaño.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LTamaño.Value1 = ""
        Me.LTamaño.Value2 = ""
        '
        'Tamaño
        '
        Me.Tamaño.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Tamaño.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Tamaño.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Tamaño.Location = New System.Drawing.Point(743, 661)
        Me.Tamaño.Name = "Tamaño"
        Me.Tamaño.Size = New System.Drawing.Size(42, 17)
        Me.Tamaño.TabIndex = 38
        Me.Tamaño.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Tamaño.Value1 = "0 "
        Me.Tamaño.Value2 = " MB"
        '
        'LDuracion
        '
        Me.LDuracion.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LDuracion.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LDuracion.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LDuracion.Location = New System.Drawing.Point(696, 645)
        Me.LDuracion.Name = "LDuracion"
        Me.LDuracion.Size = New System.Drawing.Size(46, 17)
        Me.LDuracion.TabIndex = 38
        Me.LDuracion.Text = "Duración:"
        Me.LDuracion.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LDuracion.Value1 = ""
        Me.LDuracion.Value2 = ""
        '
        'Duracion
        '
        Me.Duracion.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Duracion.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Duracion.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Duracion.Location = New System.Drawing.Point(743, 646)
        Me.Duracion.Name = "Duracion"
        Me.Duracion.Size = New System.Drawing.Size(35, 17)
        Me.Duracion.TabIndex = 38
        Me.Duracion.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Duracion.Value1 = "00 :"
        Me.Duracion.Value2 = "00"
        '
        'HTexto
        '
        Me.HTexto.BorderColor = System.Drawing.Color.FromArgb(CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer), CType(CType(55, Byte), Integer))
        Me.HTexto.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.HTexto.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.HTexto.Enabled = False
        Me.HTexto.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.HideSelection = False
        Me.HTexto.Location = New System.Drawing.Point(-116, 87)
        Me.HTexto.MaxLength = 32767
        Me.HTexto.Multiline = True
        Me.HTexto.Name = "HTexto"
        Me.HTexto.ReadOnly = False
        Me.HTexto.Rounded = 7
        Me.HTexto.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.HTexto.SelectedText = ""
        Me.HTexto.SelectionLength = 0
        Me.HTexto.SelectionStart = 0
        Me.HTexto.ShortcutsEnabled = False
        Me.HTexto.Size = New System.Drawing.Size(78, 19)
        Me.HTexto.TabIndex = 37
        Me.HTexto.TabStop = False
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'BMaximizar
        '
        Me.BMaximizar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BMaximizar.ControlButton = KDesktop.NSControlButton.Button.MaximizeRestore
        Me.BMaximizar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BMaximizar.ForeColor2 = System.Drawing.Color.Black
        Me.BMaximizar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BMaximizar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BMaximizar.Location = New System.Drawing.Point(1006, 3)
        Me.BMaximizar.Margin = New System.Windows.Forms.Padding(0)
        Me.BMaximizar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BMaximizar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BMaximizar.Name = "BMaximizar"
        Me.BMaximizar.Size = New System.Drawing.Size(18, 20)
        Me.BMaximizar.TabIndex = 29
        Me.BMaximizar.Text = "NsControlButton1"
        '
        'BClose
        '
        Me.BClose.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BClose.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BClose.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BClose.ForeColor2 = System.Drawing.Color.Black
        Me.BClose.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.Location = New System.Drawing.Point(1032, 3)
        Me.BClose.Margin = New System.Windows.Forms.Padding(0)
        Me.BClose.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BClose.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BClose.Name = "BClose"
        Me.BClose.Size = New System.Drawing.Size(18, 20)
        Me.BClose.TabIndex = 28
        Me.BClose.Text = "NsControlButton1"
        '
        'GPlayer
        '
        Me.GPlayer.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GPlayer.Controls.Add(Me.Panel1)
        Me.GPlayer.Controls.Add(Me.EditingPanel)
        Me.GPlayer.DrawSeperator = False
        Me.GPlayer.Location = New System.Drawing.Point(13, 41)
        Me.GPlayer.Name = "GPlayer"
        Me.GPlayer.Size = New System.Drawing.Size(1032, 603)
        Me.GPlayer.SubTitle = ""
        Me.GPlayer.TabIndex = 169
        Me.GPlayer.Title = ""
        '
        'Panel1
        '
        Me.Panel1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Panel1.Controls.Add(Me.NoVideoImage)
        Me.Panel1.Controls.Add(Me.Progreso)
        Me.Panel1.Controls.Add(Me.BPlay)
        Me.Panel1.Controls.Add(Me.LProgreso)
        Me.Panel1.Controls.Add(Me.Player1)
        Me.Panel1.Location = New System.Drawing.Point(3, 3)
        Me.Panel1.Name = "Panel1"
        Me.Panel1.Size = New System.Drawing.Size(1026, 597)
        Me.Panel1.TabIndex = 170
        '
        'NoVideoImage
        '
        Me.NoVideoImage.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.NoVideoImage.Image = Global.KDesktop.My.Resources.Resources.NoVideo
        Me.NoVideoImage.Location = New System.Drawing.Point(0, 0)
        Me.NoVideoImage.Name = "NoVideoImage"
        Me.NoVideoImage.Size = New System.Drawing.Size(1026, 597)
        Me.NoVideoImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.NoVideoImage.TabIndex = 172
        Me.NoVideoImage.TabStop = False
        Me.NoVideoImage.Visible = False
        '
        'Progreso
        '
        Me.Progreso.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Progreso.Location = New System.Drawing.Point(13, 580)
        Me.Progreso.Maximum = 100
        Me.Progreso.Minimum = 0
        Me.Progreso.Name = "Progreso"
        Me.Progreso.Size = New System.Drawing.Size(1011, 15)
        Me.Progreso.TabIndex = 171
        Me.Progreso.Text = "NsProgressBar1"
        Me.Progreso.Value = 0
        '
        'BPlay
        '
        Me.BPlay.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.BPlay.Font = New System.Drawing.Font("Webdings", 11.25!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(2, Byte))
        Me.BPlay.ForeColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BPlay.Location = New System.Drawing.Point(-5, 578)
        Me.BPlay.Name = "BPlay"
        Me.BPlay.Size = New System.Drawing.Size(19, 16)
        Me.BPlay.TabIndex = 170
        Me.BPlay.Text = "4"
        Me.BPlay.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'LProgreso
        '
        Me.LProgreso.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LProgreso.Location = New System.Drawing.Point(0, 577)
        Me.LProgreso.Name = "LProgreso"
        Me.LProgreso.Size = New System.Drawing.Size(1027, 20)
        Me.LProgreso.TabIndex = 170
        '
        'Player1
        '
        Me.Player1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Player1.Enabled = True
        Me.Player1.Location = New System.Drawing.Point(0, 0)
        Me.Player1.Name = "Player1"
        Me.Player1.OcxState = CType(resources.GetObject("Player1.OcxState"), System.Windows.Forms.AxHost.State)
        Me.Player1.Size = New System.Drawing.Size(1026, 642)
        Me.Player1.TabIndex = 1
        '
        'EditingPanel
        '
        Me.EditingPanel.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.EditingPanel.BackColor = System.Drawing.Color.Black
        Me.EditingPanel.Controls.Add(Me.EditingLabel)
        Me.EditingPanel.Location = New System.Drawing.Point(3, 3)
        Me.EditingPanel.Name = "EditingPanel"
        Me.EditingPanel.Size = New System.Drawing.Size(1026, 597)
        Me.EditingPanel.TabIndex = 131
        Me.EditingPanel.Visible = False
        '
        'EditingLabel
        '
        Me.EditingLabel.Font = New System.Drawing.Font("Segoe UI", 20.0!, System.Drawing.FontStyle.Bold)
        Me.EditingLabel.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.EditingLabel.Location = New System.Drawing.Point(387, 267)
        Me.EditingLabel.Name = "EditingLabel"
        Me.EditingLabel.Size = New System.Drawing.Size(197, 81)
        Me.EditingLabel.TabIndex = 0
        Me.EditingLabel.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.EditingLabel.Value1 = "P"
        Me.EditingLabel.Value2 = "rocesando..."
        Me.EditingLabel.Visible = False
        '
        'FVideoRecorder
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(1058, 681)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FVideoRecorder"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Grabar Pantalla"
        Me.FTheme.ResumeLayout(False)
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PEditar, System.ComponentModel.ISupportInitialize).EndInit()
        Me.GGif.ResumeLayout(False)
        Me.GSonido.ResumeLayout(False)
        Me.GPlayer.ResumeLayout(False)
        Me.Panel1.ResumeLayout(False)
        CType(Me.NoVideoImage, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.Player1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.EditingPanel.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BMaximizar As NSControlButton
    Friend WithEvents BClose As NSControlButton
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents Duracion As NSLabel
    Friend WithEvents LTamaño As NSLabel
    Friend WithEvents Tamaño As NSLabel
    Friend WithEvents LDuracion As NSLabel
    Friend WithEvents GGif As NSGroupBox
    Friend WithEvents CGif As NSOnOffBox
    Friend WithEvents LGif As NSLabel
    Friend WithEvents PEditar As PictureBox
    Friend WithEvents GrabacionPantalla_LHelp As NSLabel
    Friend WithEvents EditingPanel As Panel
    Friend WithEvents EditingLabel As NSLabel
    Friend WithEvents Player1 As AxWMPLib.AxWindowsMediaPlayer
    Friend WithEvents GPlayer As NSGroupBox
    Friend WithEvents Panel1 As Panel
    Friend WithEvents Progreso As NSProgressBar
    Friend WithEvents BPlay As Label
    Friend WithEvents LProgreso As Label
    Friend WithEvents NoVideoImage As PictureBox
    Friend WithEvents PGuardar As PictureBox
    Friend WithEvents PCerrar As PictureBox
    Friend WithEvents PTelegram As PictureBox
    Friend WithEvents GSonido As NSGroupBox
    Friend WithEvents CSonido As NSOnOffBox
    Friend WithEvents LSonido As NSLabel
    Friend WithEvents PCopiar As PictureBox
End Class
