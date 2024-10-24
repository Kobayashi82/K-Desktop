<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class FAudioRecorder
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FAudioRecorder))
        Me.FTheme = New KDesktop.NSTheme()
        Me.LTamaño = New KDesktop.NSLabel()
        Me.Tamaño = New KDesktop.NSLabel()
        Me.LDuracion = New KDesktop.NSLabel()
        Me.Duracion = New KDesktop.NSLabel()
        Me.NsSeperator1 = New KDesktop.NSSeperator()
        Me.PTelegram = New System.Windows.Forms.PictureBox()
        Me.PGuardar = New System.Windows.Forms.PictureBox()
        Me.PCopiar = New System.Windows.Forms.PictureBox()
        Me.PCerrar = New System.Windows.Forms.PictureBox()
        Me.Trim_Left = New KDesktop.NSSeperatorCrop()
        Me.GrabacionAudio_LHelp = New KDesktop.NSLabel()
        Me.Progreso = New KDesktop.NSProgressBar()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.GSonido = New KDesktop.NSGroupBox()
        Me.CSonido = New KDesktop.NSOnOffBox()
        Me.LSonido = New KDesktop.NSLabel()
        Me.GMicrofono = New KDesktop.NSGroupBox()
        Me.CMicrofono = New KDesktop.NSOnOffBox()
        Me.LMicrofono = New KDesktop.NSLabel()
        Me.TNombre = New KDesktop.NSTextBox()
        Me.BClose = New KDesktop.NSControlButton()
        Me.BPlay = New System.Windows.Forms.Label()
        Me.Trim_Right = New KDesktop.NSSeperatorCrop()
        Me.FTheme.SuspendLayout()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.GSonido.SuspendLayout()
        Me.GMicrofono.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.LTamaño)
        Me.FTheme.Controls.Add(Me.Tamaño)
        Me.FTheme.Controls.Add(Me.LDuracion)
        Me.FTheme.Controls.Add(Me.Duracion)
        Me.FTheme.Controls.Add(Me.NsSeperator1)
        Me.FTheme.Controls.Add(Me.PTelegram)
        Me.FTheme.Controls.Add(Me.PGuardar)
        Me.FTheme.Controls.Add(Me.PCopiar)
        Me.FTheme.Controls.Add(Me.PCerrar)
        Me.FTheme.Controls.Add(Me.Trim_Left)
        Me.FTheme.Controls.Add(Me.GrabacionAudio_LHelp)
        Me.FTheme.Controls.Add(Me.Progreso)
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Controls.Add(Me.GSonido)
        Me.FTheme.Controls.Add(Me.GMicrofono)
        Me.FTheme.Controls.Add(Me.TNombre)
        Me.FTheme.Controls.Add(Me.BClose)
        Me.FTheme.Controls.Add(Me.BPlay)
        Me.FTheme.Controls.Add(Me.Trim_Right)
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
        Me.FTheme.Size = New System.Drawing.Size(345, 135)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 0
        Me.FTheme.Text = "Grabar Audio"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'LTamaño
        '
        Me.LTamaño.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LTamaño.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LTamaño.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LTamaño.Location = New System.Drawing.Point(29, 113)
        Me.LTamaño.Name = "LTamaño"
        Me.LTamaño.Size = New System.Drawing.Size(40, 17)
        Me.LTamaño.TabIndex = 142
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
        Me.Tamaño.Location = New System.Drawing.Point(71, 114)
        Me.Tamaño.Name = "Tamaño"
        Me.Tamaño.Size = New System.Drawing.Size(42, 17)
        Me.Tamaño.TabIndex = 143
        Me.Tamaño.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Tamaño.Value1 = "0 "
        Me.Tamaño.Value2 = " MB"
        '
        'LDuracion
        '
        Me.LDuracion.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LDuracion.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LDuracion.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LDuracion.Location = New System.Drawing.Point(24, 98)
        Me.LDuracion.Name = "LDuracion"
        Me.LDuracion.Size = New System.Drawing.Size(46, 17)
        Me.LDuracion.TabIndex = 144
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
        Me.Duracion.Location = New System.Drawing.Point(71, 99)
        Me.Duracion.Name = "Duracion"
        Me.Duracion.Size = New System.Drawing.Size(35, 17)
        Me.Duracion.TabIndex = 145
        Me.Duracion.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Duracion.Value1 = "00 :"
        Me.Duracion.Value2 = "00"
        '
        'NsSeperator1
        '
        Me.NsSeperator1.Cross = False
        Me.NsSeperator1.GlowColor = System.Drawing.Color.DarkGreen
        Me.NsSeperator1.Glowing = False
        Me.NsSeperator1.LineColor = System.Drawing.Color.FromArgb(CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer), CType(CType(35, Byte), Integer))
        Me.NsSeperator1.Location = New System.Drawing.Point(164, 80)
        Me.NsSeperator1.Name = "NsSeperator1"
        Me.NsSeperator1.Size = New System.Drawing.Size(21, 1)
        Me.NsSeperator1.TabIndex = 141
        Me.NsSeperator1.Text = "NsSeperator1"
        Me.NsSeperator1.VerticalLine = False
        '
        'PTelegram
        '
        Me.PTelegram.Image = Global.KDesktop.My.Resources.Resources.Telegram_Off
        Me.PTelegram.Location = New System.Drawing.Point(227, 104)
        Me.PTelegram.Name = "PTelegram"
        Me.PTelegram.Size = New System.Drawing.Size(23, 23)
        Me.PTelegram.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PTelegram.TabIndex = 138
        Me.PTelegram.TabStop = False
        '
        'PGuardar
        '
        Me.PGuardar.Image = Global.KDesktop.My.Resources.Resources.Save_Off
        Me.PGuardar.Location = New System.Drawing.Point(285, 104)
        Me.PGuardar.Name = "PGuardar"
        Me.PGuardar.Size = New System.Drawing.Size(23, 23)
        Me.PGuardar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PGuardar.TabIndex = 139
        Me.PGuardar.TabStop = False
        '
        'PCopiar
        '
        Me.PCopiar.Image = Global.KDesktop.My.Resources.Resources.Copiar_Imagen_Off
        Me.PCopiar.Location = New System.Drawing.Point(256, 104)
        Me.PCopiar.Name = "PCopiar"
        Me.PCopiar.Size = New System.Drawing.Size(23, 23)
        Me.PCopiar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCopiar.TabIndex = 140
        Me.PCopiar.TabStop = False
        '
        'PCerrar
        '
        Me.PCerrar.Image = Global.KDesktop.My.Resources.Resources.Cerrar_Off
        Me.PCerrar.Location = New System.Drawing.Point(314, 104)
        Me.PCerrar.Name = "PCerrar"
        Me.PCerrar.Size = New System.Drawing.Size(23, 23)
        Me.PCerrar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCerrar.TabIndex = 137
        Me.PCerrar.TabStop = False
        '
        'Trim_Left
        '
        Me.Trim_Left.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Left.Cross = False
        Me.Trim_Left.Cursor = System.Windows.Forms.Cursors.VSplit
        Me.Trim_Left.GlowColor = System.Drawing.Color.DarkGreen
        Me.Trim_Left.Glowing = False
        Me.Trim_Left.InvertedLine = False
        Me.Trim_Left.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Left.Location = New System.Drawing.Point(23, 49)
        Me.Trim_Left.Name = "Trim_Left"
        Me.Trim_Left.Size = New System.Drawing.Size(3, 10)
        Me.Trim_Left.TabIndex = 132
        Me.Trim_Left.VerticalLine = True
        '
        'GrabacionAudio_LHelp
        '
        Me.GrabacionAudio_LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GrabacionAudio_LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.GrabacionAudio_LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.GrabacionAudio_LHelp.Location = New System.Drawing.Point(304, 5)
        Me.GrabacionAudio_LHelp.Name = "GrabacionAudio_LHelp"
        Me.GrabacionAudio_LHelp.Size = New System.Drawing.Size(14, 17)
        Me.GrabacionAudio_LHelp.TabIndex = 130
        Me.GrabacionAudio_LHelp.Text = "?"
        Me.GrabacionAudio_LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.GrabacionAudio_LHelp.Value1 = ""
        Me.GrabacionAudio_LHelp.Value2 = ""
        '
        'Progreso
        '
        Me.Progreso.Location = New System.Drawing.Point(23, 35)
        Me.Progreso.Maximum = 100
        Me.Progreso.Minimum = 0
        Me.Progreso.Name = "Progreso"
        Me.Progreso.Size = New System.Drawing.Size(310, 14)
        Me.Progreso.TabIndex = 50
        Me.Progreso.Text = "NsProgressBar1"
        Me.Progreso.Value = 0
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
        Me.HTexto.Location = New System.Drawing.Point(817, 213)
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
        Me.HTexto.TabIndex = 49
        Me.HTexto.TabStop = False
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'GSonido
        '
        Me.GSonido.Controls.Add(Me.CSonido)
        Me.GSonido.Controls.Add(Me.LSonido)
        Me.GSonido.DrawSeperator = False
        Me.GSonido.Location = New System.Drawing.Point(22, 66)
        Me.GSonido.Name = "GSonido"
        Me.GSonido.Size = New System.Drawing.Size(134, 30)
        Me.GSonido.SubTitle = ""
        Me.GSonido.TabIndex = 48
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
        Me.LSonido.Size = New System.Drawing.Size(65, 17)
        Me.LSonido.TabIndex = 11
        Me.LSonido.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LSonido.Value1 = "S"
        Me.LSonido.Value2 = "onido"
        '
        'GMicrofono
        '
        Me.GMicrofono.Controls.Add(Me.CMicrofono)
        Me.GMicrofono.Controls.Add(Me.LMicrofono)
        Me.GMicrofono.DrawSeperator = False
        Me.GMicrofono.Location = New System.Drawing.Point(193, 66)
        Me.GMicrofono.Name = "GMicrofono"
        Me.GMicrofono.Size = New System.Drawing.Size(134, 30)
        Me.GMicrofono.SubTitle = ""
        Me.GMicrofono.TabIndex = 48
        Me.GMicrofono.Title = ""
        '
        'CMicrofono
        '
        Me.CMicrofono.Checked = False
        Me.CMicrofono.Location = New System.Drawing.Point(3, 3)
        Me.CMicrofono.MaximumSize = New System.Drawing.Size(56, 24)
        Me.CMicrofono.MinimumSize = New System.Drawing.Size(56, 24)
        Me.CMicrofono.Name = "CMicrofono"
        Me.CMicrofono.Size = New System.Drawing.Size(56, 24)
        Me.CMicrofono.TabIndex = 3
        '
        'LMicrofono
        '
        Me.LMicrofono.Font = New System.Drawing.Font("Segoe UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LMicrofono.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LMicrofono.Location = New System.Drawing.Point(62, 6)
        Me.LMicrofono.Name = "LMicrofono"
        Me.LMicrofono.Size = New System.Drawing.Size(65, 17)
        Me.LMicrofono.TabIndex = 11
        Me.LMicrofono.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LMicrofono.Value1 = "M"
        Me.LMicrofono.Value2 = "icrófono"
        '
        'TNombre
        '
        Me.TNombre.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TNombre.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TNombre.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TNombre.Font = New System.Drawing.Font("Segoe UI", 8.0!)
        Me.TNombre.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer))
        Me.TNombre.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TNombre.HideSelection = False
        Me.TNombre.Location = New System.Drawing.Point(112, 104)
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
        Me.TNombre.TabIndex = 43
        Me.TNombre.Text = "Escriba un nombre"
        Me.TNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TNombre.UseSystemPasswordChar = False
        Me.TNombre.WordWrap = False
        '
        'BClose
        '
        Me.BClose.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BClose.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BClose.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BClose.ForeColor2 = System.Drawing.Color.Black
        Me.BClose.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BClose.Location = New System.Drawing.Point(319, 3)
        Me.BClose.Margin = New System.Windows.Forms.Padding(0)
        Me.BClose.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BClose.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BClose.Name = "BClose"
        Me.BClose.Size = New System.Drawing.Size(18, 20)
        Me.BClose.TabIndex = 29
        '
        'BPlay
        '
        Me.BPlay.AutoSize = True
        Me.BPlay.Font = New System.Drawing.Font("Webdings", 10.0!)
        Me.BPlay.ForeColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BPlay.Location = New System.Drawing.Point(4, 31)
        Me.BPlay.Name = "BPlay"
        Me.BPlay.Size = New System.Drawing.Size(23, 20)
        Me.BPlay.TabIndex = 51
        Me.BPlay.Text = "4"
        '
        'Trim_Right
        '
        Me.Trim_Right.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Right.Cross = False
        Me.Trim_Right.Cursor = System.Windows.Forms.Cursors.VSplit
        Me.Trim_Right.GlowColor = System.Drawing.Color.DarkGreen
        Me.Trim_Right.Glowing = False
        Me.Trim_Right.InvertedLine = False
        Me.Trim_Right.LineColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Trim_Right.Location = New System.Drawing.Point(329, 49)
        Me.Trim_Right.Name = "Trim_Right"
        Me.Trim_Right.Size = New System.Drawing.Size(3, 10)
        Me.Trim_Right.TabIndex = 132
        Me.Trim_Right.VerticalLine = True
        '
        'FAudioRecorder
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(345, 135)
        Me.Controls.Add(Me.FTheme)
        Me.DoubleBuffered = True
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FAudioRecorder"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Grabar Audio"
        Me.FTheme.ResumeLayout(False)
        Me.FTheme.PerformLayout()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).EndInit()
        Me.GSonido.ResumeLayout(False)
        Me.GMicrofono.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BClose As NSControlButton
    Friend WithEvents GMicrofono As NSGroupBox
    Friend WithEvents CMicrofono As NSOnOffBox
    Friend WithEvents LMicrofono As NSLabel
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents BPlay As Label
    Friend WithEvents Progreso As NSProgressBar
    Friend WithEvents GSonido As NSGroupBox
    Friend WithEvents CSonido As NSOnOffBox
    Friend WithEvents LSonido As NSLabel
    Friend WithEvents GrabacionAudio_LHelp As NSLabel
    Friend WithEvents Trim_Left As NSSeperatorCrop
    Friend WithEvents Trim_Right As NSSeperatorCrop
    Friend WithEvents PCerrar As PictureBox
    Friend WithEvents PTelegram As PictureBox
    Friend WithEvents PGuardar As PictureBox
    Friend WithEvents PCopiar As PictureBox
    Friend WithEvents NsSeperator1 As NSSeperator
    Friend WithEvents LTamaño As NSLabel
    Friend WithEvents Tamaño As NSLabel
    Friend WithEvents LDuracion As NSLabel
    Friend WithEvents Duracion As NSLabel
End Class
