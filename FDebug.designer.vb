<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FDebug
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(FDebug))
        Me.FTheme = New KDesktop.NSTheme()
        Me.HTexto = New KDesktop.NSTextBox()
        Me.TComentarios = New KDesktop.NSTextBox()
        Me.PCerrar = New System.Windows.Forms.PictureBox()
        Me.PTelegram = New System.Windows.Forms.PictureBox()
        Me.PGuardar = New System.Windows.Forms.PictureBox()
        Me.PCopiar = New System.Windows.Forms.PictureBox()
        Me.TError = New System.Windows.Forms.Label()
        Me.TMemoria = New System.Windows.Forms.Label()
        Me.TWindows = New System.Windows.Forms.Label()
        Me.LError = New KDesktop.NSLabel()
        Me.LMemoria = New KDesktop.NSLabel()
        Me.LWindows = New KDesktop.NSLabel()
        Me.TVersion = New System.Windows.Forms.Label()
        Me.LVersion = New KDesktop.NSLabel()
        Me.BCerrar = New KDesktop.NSControlButton()
        Me.TFecha = New KDesktop.NSLabel()
        Me.FTheme.SuspendLayout()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'FTheme
        '
        Me.FTheme.AccentOffset = 42
        Me.FTheme.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.FTheme.BorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.FTheme.Colors = New KDesktop.Bloom(-1) {}
        Me.FTheme.Controls.Add(Me.HTexto)
        Me.FTheme.Controls.Add(Me.TComentarios)
        Me.FTheme.Controls.Add(Me.PCerrar)
        Me.FTheme.Controls.Add(Me.PTelegram)
        Me.FTheme.Controls.Add(Me.PGuardar)
        Me.FTheme.Controls.Add(Me.PCopiar)
        Me.FTheme.Controls.Add(Me.TError)
        Me.FTheme.Controls.Add(Me.TMemoria)
        Me.FTheme.Controls.Add(Me.TWindows)
        Me.FTheme.Controls.Add(Me.LError)
        Me.FTheme.Controls.Add(Me.LMemoria)
        Me.FTheme.Controls.Add(Me.LWindows)
        Me.FTheme.Controls.Add(Me.TVersion)
        Me.FTheme.Controls.Add(Me.LVersion)
        Me.FTheme.Controls.Add(Me.BCerrar)
        Me.FTheme.Controls.Add(Me.TFecha)
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
        Me.FTheme.Size = New System.Drawing.Size(450, 150)
        Me.FTheme.SmartBounds = True
        Me.FTheme.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.FTheme.TabIndex = 4
        Me.FTheme.Text = "  ¡¡Ups!! Algo ha salido mal"
        Me.FTheme.TransparencyKey = System.Drawing.Color.Empty
        Me.FTheme.Transparent = False
        '
        'HTexto
        '
        Me.HTexto.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.HTexto.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.HTexto.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.HTexto.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.HTexto.HideSelection = False
        Me.HTexto.Location = New System.Drawing.Point(-99, 123)
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
        Me.HTexto.TabIndex = 143
        Me.HTexto.TabStop = False
        Me.HTexto.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.HTexto.UseSystemPasswordChar = False
        Me.HTexto.WordWrap = False
        '
        'TComentarios
        '
        Me.TComentarios.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TComentarios.BorderColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.TComentarios.ControlCursor = System.Windows.Forms.Cursors.Default
        Me.TComentarios.Cursor = System.Windows.Forms.Cursors.IBeam
        Me.TComentarios.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.TComentarios.ForeColor = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TComentarios.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer), CType(CType(72, Byte), Integer))
        Me.TComentarios.ForeColor2 = System.Drawing.Color.FromArgb(CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer), CType(CType(102, Byte), Integer))
        Me.TComentarios.HideSelection = False
        Me.TComentarios.Location = New System.Drawing.Point(14, 121)
        Me.TComentarios.MaxLength = 30
        Me.TComentarios.Multiline = False
        Me.TComentarios.Name = "TComentarios"
        Me.TComentarios.ReadOnly = False
        Me.TComentarios.Rounded = 7
        Me.TComentarios.ScrollBars = System.Windows.Forms.ScrollBars.None
        Me.TComentarios.SelectedText = ""
        Me.TComentarios.SelectionLength = 0
        Me.TComentarios.SelectionStart = 0
        Me.TComentarios.ShortcutsEnabled = True
        Me.TComentarios.Size = New System.Drawing.Size(310, 23)
        Me.TComentarios.TabIndex = 142
        Me.TComentarios.Text = "Escriba aquí sus comentarios"
        Me.TComentarios.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TComentarios.UseSystemPasswordChar = False
        Me.TComentarios.WordWrap = False
        '
        'PCerrar
        '
        Me.PCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCerrar.Image = Global.KDesktop.My.Resources.Resources.Cerrar_Off
        Me.PCerrar.Location = New System.Drawing.Point(415, 121)
        Me.PCerrar.Name = "PCerrar"
        Me.PCerrar.Size = New System.Drawing.Size(23, 23)
        Me.PCerrar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCerrar.TabIndex = 138
        Me.PCerrar.TabStop = False
        '
        'PTelegram
        '
        Me.PTelegram.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PTelegram.Image = Global.KDesktop.My.Resources.Resources.Telegram_Off
        Me.PTelegram.Location = New System.Drawing.Point(328, 121)
        Me.PTelegram.Name = "PTelegram"
        Me.PTelegram.Size = New System.Drawing.Size(23, 23)
        Me.PTelegram.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PTelegram.TabIndex = 139
        Me.PTelegram.TabStop = False
        '
        'PGuardar
        '
        Me.PGuardar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PGuardar.Image = Global.KDesktop.My.Resources.Resources.Save_Off
        Me.PGuardar.Location = New System.Drawing.Point(386, 121)
        Me.PGuardar.Name = "PGuardar"
        Me.PGuardar.Size = New System.Drawing.Size(23, 23)
        Me.PGuardar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PGuardar.TabIndex = 140
        Me.PGuardar.TabStop = False
        '
        'PCopiar
        '
        Me.PCopiar.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PCopiar.Image = Global.KDesktop.My.Resources.Resources.Copiar_Imagen_Off
        Me.PCopiar.Location = New System.Drawing.Point(357, 121)
        Me.PCopiar.Name = "PCopiar"
        Me.PCopiar.Size = New System.Drawing.Size(23, 23)
        Me.PCopiar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage
        Me.PCopiar.TabIndex = 141
        Me.PCopiar.TabStop = False
        '
        'TError
        '
        Me.TError.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TError.AutoSize = True
        Me.TError.FlatStyle = System.Windows.Forms.FlatStyle.Flat
        Me.TError.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.TError.ForeColor = System.Drawing.Color.Gray
        Me.TError.Location = New System.Drawing.Point(76, 106)
        Me.TError.Name = "TError"
        Me.TError.Size = New System.Drawing.Size(0, 12)
        Me.TError.TabIndex = 21
        '
        'TMemoria
        '
        Me.TMemoria.AutoSize = True
        Me.TMemoria.ForeColor = System.Drawing.Color.Gray
        Me.TMemoria.Location = New System.Drawing.Point(76, 83)
        Me.TMemoria.Name = "TMemoria"
        Me.TMemoria.Size = New System.Drawing.Size(0, 13)
        Me.TMemoria.TabIndex = 21
        Me.TMemoria.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'TWindows
        '
        Me.TWindows.AutoSize = True
        Me.TWindows.ForeColor = System.Drawing.Color.Gray
        Me.TWindows.Location = New System.Drawing.Point(76, 60)
        Me.TWindows.Name = "TWindows"
        Me.TWindows.Size = New System.Drawing.Size(0, 13)
        Me.TWindows.TabIndex = 21
        Me.TWindows.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'LError
        '
        Me.LError.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LError.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LError.Location = New System.Drawing.Point(34, 104)
        Me.LError.Name = "LError"
        Me.LError.Size = New System.Drawing.Size(36, 17)
        Me.LError.TabIndex = 20
        Me.LError.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LError.Value1 = "E"
        Me.LError.Value2 = "rror:"
        '
        'LMemoria
        '
        Me.LMemoria.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LMemoria.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LMemoria.Location = New System.Drawing.Point(14, 81)
        Me.LMemoria.Name = "LMemoria"
        Me.LMemoria.Size = New System.Drawing.Size(58, 17)
        Me.LMemoria.TabIndex = 20
        Me.LMemoria.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LMemoria.Value1 = "M"
        Me.LMemoria.Value2 = "emoria:"
        '
        'LWindows
        '
        Me.LWindows.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LWindows.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LWindows.Location = New System.Drawing.Point(12, 58)
        Me.LWindows.Name = "LWindows"
        Me.LWindows.Size = New System.Drawing.Size(58, 17)
        Me.LWindows.TabIndex = 20
        Me.LWindows.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LWindows.Value1 = "W"
        Me.LWindows.Value2 = "indows:"
        '
        'TVersion
        '
        Me.TVersion.AutoSize = True
        Me.TVersion.ForeColor = System.Drawing.Color.Gray
        Me.TVersion.Location = New System.Drawing.Point(76, 37)
        Me.TVersion.Name = "TVersion"
        Me.TVersion.Size = New System.Drawing.Size(0, 13)
        Me.TVersion.TabIndex = 21
        Me.TVersion.TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        '
        'LVersion
        '
        Me.LVersion.Font = New System.Drawing.Font("Verdana", 8.25!)
        Me.LVersion.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.LVersion.Location = New System.Drawing.Point(20, 35)
        Me.LVersion.Name = "LVersion"
        Me.LVersion.Size = New System.Drawing.Size(58, 17)
        Me.LVersion.TabIndex = 20
        Me.LVersion.TextAlign = System.Windows.Forms.HorizontalAlignment.Left
        Me.LVersion.Value1 = "V"
        Me.LVersion.Value2 = "ersion:"
        '
        'BCerrar
        '
        Me.BCerrar.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BCerrar.ControlButton = KDesktop.NSControlButton.Button.Close
        Me.BCerrar.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer), CType(CType(65, Byte), Integer))
        Me.BCerrar.ForeColor2 = System.Drawing.Color.Black
        Me.BCerrar.ForeColor3 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.ForeColor4 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.BCerrar.Location = New System.Drawing.Point(425, 3)
        Me.BCerrar.Margin = New System.Windows.Forms.Padding(0)
        Me.BCerrar.MaximumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.MinimumSize = New System.Drawing.Size(18, 20)
        Me.BCerrar.Name = "BCerrar"
        Me.BCerrar.Size = New System.Drawing.Size(18, 20)
        Me.BCerrar.TabIndex = 13
        Me.BCerrar.Text = "NsControlButton1"
        '
        'TFecha
        '
        Me.TFecha.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TFecha.Font = New System.Drawing.Font("Verdana", 6.75!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.TFecha.ForeColor1 = System.Drawing.Color.FromArgb(CType(CType(205, Byte), Integer), CType(CType(150, Byte), Integer), CType(CType(0, Byte), Integer))
        Me.TFecha.Location = New System.Drawing.Point(195, 4)
        Me.TFecha.Name = "TFecha"
        Me.TFecha.Size = New System.Drawing.Size(116, 11)
        Me.TFecha.TabIndex = 20
        Me.TFecha.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        Me.TFecha.Value1 = ""
        Me.TFecha.Value2 = ""
        '
        'FDebug
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(10.0!, 21.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.FromArgb(CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer), CType(CType(50, Byte), Integer))
        Me.ClientSize = New System.Drawing.Size(450, 150)
        Me.ControlBox = False
        Me.Controls.Add(Me.FTheme)
        Me.Font = New System.Drawing.Font("Century Gothic", 12.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(0, Byte))
        Me.ForeColor = System.Drawing.Color.White
        Me.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(5)
        Me.Name = "FDebug"
        Me.ShowIcon = False
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "Error Detectado"
        Me.FTheme.ResumeLayout(False)
        Me.FTheme.PerformLayout()
        CType(Me.PCerrar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PTelegram, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PGuardar, System.ComponentModel.ISupportInitialize).EndInit()
        CType(Me.PCopiar, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents FTheme As NSTheme
    Friend WithEvents BCerrar As NSControlButton
    Friend WithEvents TVersion As Label
    Friend WithEvents LVersion As NSLabel
    Friend WithEvents TMemoria As Label
    Friend WithEvents TWindows As Label
    Friend WithEvents LMemoria As NSLabel
    Friend WithEvents LWindows As NSLabel
    Friend WithEvents TError As Label
    Friend WithEvents LError As NSLabel
    Friend WithEvents PCerrar As PictureBox
    Friend WithEvents PTelegram As PictureBox
    Friend WithEvents PGuardar As PictureBox
    Friend WithEvents PCopiar As PictureBox
    Friend WithEvents TFecha As NSLabel
    Friend WithEvents TComentarios As NSTextBox
    Friend WithEvents HTexto As NSTextBox
End Class
