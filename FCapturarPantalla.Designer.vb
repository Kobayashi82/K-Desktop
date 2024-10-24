<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FCapturarPantalla
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FCapturarPantalla))
        Me.FTheme = New KDesktop.NSTheme()
        Me.Lpor = New KDesktop.NSLabel()
        Me.BColor = New System.Windows.Forms.PictureBox()
        Me.TNombre = New KDesktop.NSTextBox()
        Me.PCerrar = New System.Windows.Forms.PictureBox()
        Me.PTexto = New System.Windows.Forms.PictureBox()
        Me.PTelegram = New System.Windows.Forms.PictureBox()
        Me.PGuardar = New System.Windows.Forms.PictureBox()
        Me.PUndo = New System.Windows.Forms.PictureBox()
        Me.PRedo = New System.Windows.Forms.PictureBox()
        Me.PCopiar = New System.Windows.Forms.PictureBox()
        Me.PEditar = New System.Windows.Forms.PictureBox()
        Me.LTamaño = New KDesktop.NSLabel()
        Me.LHeight = New KDesktop.NSLabel()
        Me.LWitdh = New KDesktop.NSLabel()
        Me.Tamaño = New KDesktop.NSLabel()
        Me.CapturaPantalla_LHelp = New KDesktop.NSLabel()
        Me.BMaximizar = New KDesktop.NSControlButton()
        Me.BClose = New KDesktop.NSControlButton()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.TabControl = New KDesktop.NSTabControl()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.MenuTexto = New KDesktop.NSContextMenu()
        Me.MTexto_Texto = New System.Windows.Forms.ToolStripMenuItem()
        Me.MTexto_Separador1 = New System.Windows.Forms.ToolStripSeparator()
        Me.MTexto_Fuente = New System.Windows.Forms.ToolStripMenuItem()
        Me.MTexto_Separador2 = New System.Windows.Forms.ToolStripSeparator()
        Me.MTexto_Eliminar = New System.Windows.Forms.ToolStripMenuItem()
        Me.FTheme.SuspendLayout()
        CType(Me.BColor, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PTexto, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PUndo, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PRedo, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PEditar, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.MenuTexto.SuspendLayout()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.Lpor)
        Me.FTheme.Controls.Add(Me.BColor)
        Me.FTheme.Controls.Add(Me.TNombre)
        Me.FTheme.Controls.Add(Me.PCerrar)
        Me.FTheme.Controls.Add(Me.PTexto)
        Me.FTheme.Controls.Add(Me.PTelegram)
        Me.FTheme.Controls.Add(Me.PGuardar)
        Me.FTheme.Controls.Add(Me.PUndo)
        Me.FTheme.Controls.Add(Me.PRedo)
        Me.FTheme.Controls.Add(Me.PCopiar)
        Me.FTheme.Controls.Add(Me.PEditar)
        Me.FTheme.Controls.Add(Me.LTamaño)
        Me.FTheme.Controls.Add(Me.LHeight)
        Me.FTheme.Controls.Add(Me.LWitdh)
        Me.FTheme.Controls.Add(Me.Tamaño)
        Me.FTheme.Controls.Add(Me.CapturaPantalla_LHelp)
        Me.FTheme.Controls.Add(Me.BMaximizar)
        Me.FTheme.Controls.Add(Me.BClose)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.TabControl)
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
        Me.FTheme.Sizable = True
        Me.FTheme.Size = New System.Drawing.Size(800, 450)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 1
        Me.FTheme.Text = "Capturar Pantalla"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'Lpor
        '
        Me.Lpor.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Lpor.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Lpor.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.Lpor.Location = New System.Drawing.Point(474, 432)
        Me.Lpor.Name = "Lpor"
        Me.Lpor.Size = New System.Drawing.Size(10, 12)
        Me.Lpor.TabIndex = 132
        Me.Lpor.Text = "x"
        Me.Lpor.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Lpor.Value1 = ""
        Me.Lpor.Value2 = ""
        '
        'BColor
        '
        Me.BColor.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BColor.BackColor = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BColor.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
        Me.BColor.Location = New System.Drawing.Point(345, 421)
        Me.BColor.Name = "BColor"
        Me.BColor.Size = New System.Drawing.Size(23, 23)
        Me.BColor.TabIndex = 139
        Me.BColor.TabStop = False
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
        Me.TNombre.Location = New System.Drawing.Point(531, 421)
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
        Me.TNombre.TabIndex = 131
        Me.TNombre.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TNombre.UseSystemPasswordChar = False
        Me.TNombre.WordWrap = False
        '
        'PCerrar
        '
        Me.PCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCerrar.Image = Global.KDesktop.My.Resources.Resources.Cerrar_Off
        Me.PCerrar.Location = New System.Drawing.Point(761, 421)
        Me.PCerrar.Name = "PCerrar"
        Me.PCerrar.Size = New System.Drawing.Size(23, 23)
        Me.PCerrar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCerrar.TabIndex = 134
        Me.PCerrar.TabStop = False
        '
        'PTexto
        '
        Me.PTexto.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PTexto.Image = Global.KDesktop.My.Resources.Resources.Copiar_Imagen_Off
        Me.PTexto.Location = New System.Drawing.Point(316, 421)
        Me.PTexto.Name = "PTexto"
        Me.PTexto.Size = New System.Drawing.Size(23, 23)
        Me.PTexto.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PTexto.TabIndex = 135
        Me.PTexto.TabStop = False
        '
        'PTelegram
        '
        Me.PTelegram.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PTelegram.Image = Global.KDesktop.My.Resources.Resources.Telegram_Off
        Me.PTelegram.Location = New System.Drawing.Point(645, 421)
        Me.PTelegram.Name = "PTelegram"
        Me.PTelegram.Size = New System.Drawing.Size(23, 23)
        Me.PTelegram.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PTelegram.TabIndex = 135
        Me.PTelegram.TabStop = False
        '
        'PGuardar
        '
        Me.PGuardar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PGuardar.Image = Global.KDesktop.My.Resources.Resources.Save_Off
        Me.PGuardar.Location = New System.Drawing.Point(732, 421)
        Me.PGuardar.Name = "PGuardar"
        Me.PGuardar.Size = New System.Drawing.Size(23, 23)
        Me.PGuardar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PGuardar.TabIndex = 136
        Me.PGuardar.TabStop = False
        '
        'PUndo
        '
        Me.PUndo.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PUndo.Image = Global.KDesktop.My.Resources.Resources.Undo_Off
        Me.PUndo.Location = New System.Drawing.Point(380, 421)
        Me.PUndo.Name = "PUndo"
        Me.PUndo.Size = New System.Drawing.Size(23, 23)
        Me.PUndo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PUndo.TabIndex = 137
        Me.PUndo.TabStop = False
        '
        'PRedo
        '
        Me.PRedo.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PRedo.Image = Global.KDesktop.My.Resources.Resources.Redo_Off
        Me.PRedo.Location = New System.Drawing.Point(409, 421)
        Me.PRedo.Name = "PRedo"
        Me.PRedo.Size = New System.Drawing.Size(23, 23)
        Me.PRedo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PRedo.TabIndex = 137
        Me.PRedo.TabStop = False
        '
        'PCopiar
        '
        Me.PCopiar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCopiar.Image = Global.KDesktop.My.Resources.Resources.Copiar_Imagen_Off
        Me.PCopiar.Location = New System.Drawing.Point(703, 421)
        Me.PCopiar.Name = "PCopiar"
        Me.PCopiar.Size = New System.Drawing.Size(23, 23)
        Me.PCopiar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCopiar.TabIndex = 137
        Me.PCopiar.TabStop = False
        '
        'PEditar
        '
        Me.PEditar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PEditar.Image = Global.KDesktop.My.Resources.Resources.Editar_Imagen_Off
        Me.PEditar.Location = New System.Drawing.Point(674, 421)
        Me.PEditar.Name = "PEditar"
        Me.PEditar.Size = New System.Drawing.Size(23, 23)
        Me.PEditar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PEditar.TabIndex = 137
        Me.PEditar.TabStop = False
        '
        'LTamaño
        '
        Me.LTamaño.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LTamaño.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LTamaño.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.LTamaño.Location = New System.Drawing.Point(442, 417)
        Me.LTamaño.Name = "LTamaño"
        Me.LTamaño.Size = New System.Drawing.Size(44, 17)
        Me.LTamaño.TabIndex = 132
        Me.LTamaño.Text = "Tamaño:"
        Me.LTamaño.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LTamaño.Value1 = ""
        Me.LTamaño.Value2 = ""
        '
        'LHeight
        '
        Me.LHeight.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LHeight.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LHeight.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LHeight.Location = New System.Drawing.Point(488, 431)
        Me.LHeight.Name = "LHeight"
        Me.LHeight.Size = New System.Drawing.Size(34, 17)
        Me.LHeight.TabIndex = 133
        Me.LHeight.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LHeight.Value1 = " "
        Me.LHeight.Value2 = "0"
        '
        'LWitdh
        '
        Me.LWitdh.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.LWitdh.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.LWitdh.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LWitdh.Location = New System.Drawing.Point(436, 430)
        Me.LWitdh.Name = "LWitdh"
        Me.LWitdh.Size = New System.Drawing.Size(34, 17)
        Me.LWitdh.TabIndex = 133
        Me.LWitdh.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        Me.LWitdh.Value1 = " "
        Me.LWitdh.Value2 = "0"
        '
        'Tamaño
        '
        Me.Tamaño.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Tamaño.Font = New System.Drawing.Font("Segoe UI", 6.75!, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.Tamaño.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.Tamaño.Location = New System.Drawing.Point(489, 418)
        Me.Tamaño.Name = "Tamaño"
        Me.Tamaño.Size = New System.Drawing.Size(40, 17)
        Me.Tamaño.TabIndex = 133
        Me.Tamaño.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.Tamaño.Value1 = "0 "
        Me.Tamaño.Value2 = " KB"
        '
        'CapturaPantalla_LHelp
        '
        Me.CapturaPantalla_LHelp.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.CapturaPantalla_LHelp.Font = New System.Drawing.Font("Segoe UI", 9.75!, System.Drawing.FontStyle.Bold)
        Me.CapturaPantalla_LHelp.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer), CType(CType(85, Byte), Integer))
        Me.CapturaPantalla_LHelp.Location = New System.Drawing.Point(725, 5)
        Me.CapturaPantalla_LHelp.Name = "CapturaPantalla_LHelp"
        Me.CapturaPantalla_LHelp.Size = New System.Drawing.Size(14, 17)
        Me.CapturaPantalla_LHelp.TabIndex = 130
        Me.CapturaPantalla_LHelp.Text = "?"
        Me.CapturaPantalla_LHelp.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.CapturaPantalla_LHelp.Value1 = ""
        Me.CapturaPantalla_LHelp.Value2 = ""
        '
        'BMaximizar
        '
        Me.BMaximizar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BMaximizar.ControlButton = KDesktop.NSControlButton.Button.MaximizeRestore
        Me.BMaximizar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BMaximizar.ForeColor2 = System.Drawing.Color.Black
        Me.BMaximizar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BMaximizar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BMaximizar.Location = New System.Drawing.Point(748, 3)
        Me.BMaximizar.Margin = New System.Windows.Forms.Padding(0)
        Me.BMaximizar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BMaximizar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BMaximizar.Name = "BMaximizar"
        Me.BMaximizar.Size = New System.Drawing.Size(18, 20)
        Me.BMaximizar.TabIndex = 27
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
        Me.BClose.Location = New System.Drawing.Point(774, 3)
        Me.BClose.Margin = New System.Windows.Forms.Padding(0)
        Me.BClose.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BClose.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BClose.Name = "BClose"
        Me.BClose.Size = New System.Drawing.Size(18, 20)
        Me.BClose.TabIndex = 26
        Me.BClose.Text = "NsControlButton1"
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.Green
        Me.BCerrar.ForeColor4 = System.Drawing.Color.DarkRed
        Me.BCerrar.Location = New System.Drawing.Point(905, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 11
        '
        'TabControl
        '
        Me.TabControl.Alignment = System.Windows.Forms.TabAlignment.Left
        Me.TabControl.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TabControl.DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed
        Me.TabControl.ItemSize = New System.Drawing.Size(50, 15)
        Me.TabControl.Location = New System.Drawing.Point(10, 36)
        Me.TabControl.Multiline = True
        Me.TabControl.Name = "TabControl"
        Me.TabControl.SelectedIndex = 0
        Me.TabControl.Size = New System.Drawing.Size(778, 380)
        Me.TabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed
        Me.TabControl.TabIndex = 22
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
        Me.HTexto.TabIndex = 21
        Me.HTexto.TabStop = False
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'MenuTexto
        '
        Me.MenuTexto.AutoSize = False
        Me.MenuTexto.ForeColor = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.MenuTexto.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer), CType(CType(130, Byte), Integer))
        Me.MenuTexto.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.MTexto_Texto, Me.MTexto_Separador1, Me.MTexto_Fuente, Me.MTexto_Separador2, Me.MTexto_Eliminar})
        Me.MenuTexto.Name = "MenuTexto"
        Me.MenuTexto.Size = New System.Drawing.Size(101, 90)
        '
        'MTexto_Texto
        '
        Me.MTexto_Texto.AutoSize = False
        Me.MTexto_Texto.Name = "MTexto_Texto"
        Me.MTexto_Texto.Size = New System.Drawing.Size(100, 22)
        Me.MTexto_Texto.Text = "Texto"
        '
        'MTexto_Separador1
        '
        Me.MTexto_Separador1.Name = "MTexto_Separador1"
        Me.MTexto_Separador1.Size = New System.Drawing.Size(97, 6)
        '
        'MTexto_Fuente
        '
        Me.MTexto_Fuente.AutoSize = False
        Me.MTexto_Fuente.Name = "MTexto_Fuente"
        Me.MTexto_Fuente.ShowShortcutKeys = False
        Me.MTexto_Fuente.Size = New System.Drawing.Size(100, 22)
        Me.MTexto_Fuente.Text = "Fuente"
        '
        'MTexto_Separador2
        '
        Me.MTexto_Separador2.Name = "MTexto_Separador2"
        Me.MTexto_Separador2.Size = New System.Drawing.Size(97, 6)
        '
        'MTexto_Eliminar
        '
        Me.MTexto_Eliminar.AutoSize = False
        Me.MTexto_Eliminar.Name = "MTexto_Eliminar"
        Me.MTexto_Eliminar.Size = New System.Drawing.Size(100, 22)
        Me.MTexto_Eliminar.Text = "Eliminar"
        '
        'FCapturarPantalla
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(800, 450)
        Me.Controls.Add(Me.FTheme)
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "FCapturarPantalla"
        Me.ShowInTaskbar = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Capturar Pantalla"
        Me.FTheme.ResumeLayout(False)
        CType(Me.BColor, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PTexto, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PUndo, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PRedo, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PEditar, System.ComponentModel.ISupportInitialize).EndInit()
        Me.MenuTexto.ResumeLayout(False)
        Me.ResumeLayout(False)

    End Sub

    Friend WithEvents FTheme As NSTheme
    Friend WithEvents CapturaPantalla_LHelp As NSLabel
    Friend WithEvents BMaximizar As NSControlButton
    Friend WithEvents BClose As NSControlButton
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents TabControl As NSTabControl
    Friend WithEvents HTexto As NSTextBox
    Friend WithEvents TNombre As NSTextBox
    Friend WithEvents PCerrar As PictureBox
    Friend WithEvents PTelegram As PictureBox
    Friend WithEvents PGuardar As PictureBox
    Friend WithEvents PCopiar As PictureBox
    Friend WithEvents PEditar As PictureBox
    Friend WithEvents LTamaño As NSLabel
    Friend WithEvents Tamaño As NSLabel
    Friend WithEvents PUndo As PictureBox
    Friend WithEvents PRedo As PictureBox
    Friend WithEvents BColor As PictureBox
    Friend WithEvents Lpor As NSLabel
    Friend WithEvents LHeight As NSLabel
    Friend WithEvents LWitdh As NSLabel
    Friend WithEvents PTexto As PictureBox
    Friend WithEvents MenuTexto As NSContextMenu
    Friend WithEvents MTexto_Texto As ToolStripMenuItem
    Friend WithEvents MTexto_Separador1 As ToolStripSeparator
    Friend WithEvents MTexto_Fuente As ToolStripMenuItem
    Friend WithEvents MTexto_Separador2 As ToolStripSeparator
    Friend WithEvents MTexto_Eliminar As ToolStripMenuItem
End Class
