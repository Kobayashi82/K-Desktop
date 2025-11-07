
#Region " Ayuda "

Public Class FAyuda

#Region " Variables "

    Public Current_Help As String = "Introducción"
    Dim Current_Index As Integer
    Dim OpenedList As New List(Of String) From {""}

#End Region

#Region " Formulario "

    Private Sub FAyuda_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : TopMost = True : MinimumSize = New Size(666, 474) : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        ShowInTaskbar = False : ShowInTaskbar = True
        For Cuenta = 0 To ListaAyuda.Items.Length - 1 : ListaAyuda.Items(Cuenta).Tag = ListaAyuda.Items(Cuenta).Text : Next
        Text = "Ayuda de " + FMenu.Version : FTheme.Text = Text
        Show_Help(Current_Help, True)
    End Sub

    Private Sub FAyuda_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Application.DoEvents() : Opacity = 1 : TopMost = False : FMenu.FocusMe(Me)
    End Sub

    Private Sub FAyuda_KeyPress(sender As Object, e As KeyPressEventArgs) Handles MyBase.KeyPress
        If Control.ModifierKeys = Keys.Shift Or Control.ModifierKeys = Keys.Control Or Control.ModifierKeys = Keys.Alt Then Exit Sub
        If Asc(e.KeyChar) = 27 AndAlso Opacity = 1 Then e.Handled = True : Close()
    End Sub

#End Region

#Region " Titulo "

    Private Sub PLeftRight_MouseEnter(sender As Object, e As EventArgs) Handles PLeft.MouseEnter, PRight.MouseEnter
        Select Case sender.Name
            Case "PLeft" : PLeft.Image = My.Resources.Ayuda_Left
            Case "PRight" : PRight.Image = My.Resources.Ayuda_Right
        End Select
    End Sub

    Private Sub PLeftRight_MouseLeave(sender As Object, e As EventArgs) Handles PLeft.MouseLeave, PRight.MouseLeave
        Select Case sender.Name
            Case "PLeft" : PLeft.Image = My.Resources.Ayuda_Left_Off
            Case "PRight" : PRight.Image = My.Resources.Ayuda_Right_Off
        End Select
    End Sub

    Private Sub PLeftRight_Click(sender As Object, e As EventArgs) Handles PLeft.Click, PLeft.DoubleClick, PRight.Click, PRight.DoubleClick
        Select Case sender.Name
            Case "PLeft" : Show_Help("Prior")
            Case "PRight" : Show_Help("Next")
        End Select
    End Sub

#End Region

#Region " Lista "

    Private Sub ListaAyuda_SelectedIndexChanged(sender As Object, Indice As Integer) Handles ListaAyuda.SelectedIndexChanged
        If Indice = -1 Then ListaAyuda.SelectItem(Current_Index) : Exit Sub Else Current_Index = Indice
        If Indice < ListaAyuda.Items.Count AndAlso Current_Help <> ListaAyuda.Items(Indice).Tag Then Show_Help(ListaAyuda.Items(Indice).Tag, False)
    End Sub

    Private Sub ListaAyuda_MouseDown(sender As Object, e As MouseEventArgs) Handles ListaAyuda.MouseDown
        Dim Position As Integer : For Cuenta As Integer = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Area.Contains(e.Location) Then Position = Cuenta : GoTo Salta
        Next : Exit Sub
Salta:  If ListaAyuda.Items(Position).Principal = True Then
            If e.X > 5 AndAlso e.X < 21 Then
                If ListaAyuda.Items(Position).Opened = True Then ListaAyuda.Items(Position).Opened = False : OpenedList.Remove(ListaAyuda.Items(Position).Tag) : EliminarSecundario(ListaAyuda.Items(Position).Tag) Else ListaAyuda.Items(Position).Opened = True : OpenedList.Add(ListaAyuda.Items(Position).Tag) : AñadirSecundario(ListaAyuda.Items(Position).Tag)
            End If
        End If
        If ListaAyuda.Items(Position).Secundario = True Then
            If e.X > 10 AndAlso e.X < 26 Then
                If ListaAyuda.Items(Position).Opened = True Then ListaAyuda.Items(Position).Opened = False : OpenedList.Remove(ListaAyuda.Items(Position).Tag) : EliminarSecundarioChild(ListaAyuda.Items(Position).Tag) Else ListaAyuda.Items(Position).Opened = True : OpenedList.Add(ListaAyuda.Items(Position).Tag) : AñadirSecundarioChild(ListaAyuda.Items(Position).Tag, Position)
            End If
        End If
    End Sub

    Private Sub ListaAyuda_DobleClick(sender As Object, e As MouseEventArgs, Indice As Integer) Handles ListaAyuda.DobleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        If ListaAyuda.Items(Indice).Principal = True AndAlso (e.X < 6 Or e.X > 20) Then
            If ListaAyuda.Items(Indice).Opened = True Then ListaAyuda.Items(Indice).Opened = False : OpenedList.Remove(ListaAyuda.Items(Indice).Tag) : EliminarSecundario(ListaAyuda.Items(Indice).Tag) Else ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag) : AñadirSecundario(ListaAyuda.Items(Indice).Tag)
        End If
        If ListaAyuda.Items(Indice).Secundario = True AndAlso (e.X < 11 Or e.X > 25) Then
            If ListaAyuda.Items(Indice).Opened = True Then ListaAyuda.Items(Indice).Opened = False : OpenedList.Remove(ListaAyuda.Items(Indice).Tag) : EliminarSecundarioChild(ListaAyuda.Items(Indice).Tag) Else ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag) : AñadirSecundarioChild(ListaAyuda.Items(Indice).Tag, Indice)
        End If
    End Sub

#Region " Añadir / Eliminar "

    Private Sub AñadirSecundario(Help_Title As String)
        Dim Indice As Integer
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then Indice = Cuenta : Exit For
        Next : If ListaAyuda.Items(Indice).Principal = False Then Exit Sub
        Dim NewItem As New NSListViewHLP.NSListViewItem : Indice += 1
        Select Case Help_Title
            Case "Introducción"
                Indice = AñadirSecundario_CreateItem("Historía de KDesktop", "Historía de KDesktop", True, Indice, True)
                AñadirSecundario_CreateItem("Comandos y Acciones", "Comandos y Acciones", True, Indice, True)
            Case "Comandos"
                Indice = AñadirSecundario_CreateItem("Crear y Eliminar", "Crear y Eliminar", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Opciones del Comando (1)", "Opciones del Comando (1)", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Opciones del Comando (2)", "Opciones del Comando (2)", True, Indice, True)
                AñadirSecundario_CreateItem("Comandos Remotos (UDP)", "Comandos Remotos (UDP)", True, Indice, True)
            Case "Acciones"
                'Aplicaciones
                Indice = AñadirSecundario_CreateItem("Ejecutar Aplicación", "Ejecutar Aplicación", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Abrir Carpeta", "Abrir Carpeta", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Página Web", "Página Web", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Aplicaciones", "Aplicaciones", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Ejecutar BAT", "Ejecutar BAT", True, Indice, True)
                'Audio y Video
                Indice = AñadirSecundario_CreateItem("Capturar Pantalla", "Capturar Pantalla", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Grabar Pantalla", "Grabar Pantalla", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Grabar Audio", "Grabar Audio", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Reproducir Sonido", "Reproducir Sonido", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Salida de Audio", "Salida de Audio", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Volumen", "Volumen", True, Indice, True)
                'Utilidades
                Indice = AñadirSecundario_CreateItem("Macros", "Macros", True, Indice, True)
                'Indice = AñadirSecundario_CreateItem("K-Note", "K-Note", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("K-Board", "Accion K-Board", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("QuickMenu", "Accion QuickMenu", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Red Ad-Hoc", "Red Ad-Hoc", True, Indice, True)
                'Acciones
                Indice = AñadirSecundario_CreateItem("Enviar Pulsación", "Enviar Pulsación", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("TopMost", "TopMost", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Archivos Ocultos", "Archivos Ocultos", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Desactivar en", "Desactivar en", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Mostrar Mensaje", "Mostrar Mensaje", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Apagar Equipo", "Apagar Equipo", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Comandos", "Accion Comandos", True, Indice, True)
                AñadirSecundario_CreateItem("Esperar", "Esperar", True, Indice, True)
            Case "K-Board"

            Case "QuickMenu"
                Indice = AñadirSecundario_CreateItem("Crear un QuickMenu", "Crear un QuickMenu", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Crear un Item (1)", "Crear un Item (1)", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Crear un Item (2)", "Crear un Item (2)", True, Indice, True)
                Indice = AñadirSecundario_CreateItem("Editar y Eliminar un Item", "Editar y Eliminar un Item", True, Indice, True)
                AñadirSecundario_CreateItem("Comando Rápido", "Comando Rápido", True, Indice, True)
            Case "Opciones"
                Indice = AñadirSecundario_CreateItem("Iconos de KDesktop", "Iconos de KDesktop", True, Indice, True)
                AñadirSecundario_CreateItem("Licencia de KDesktop", "Licencia de KDesktop", True, Indice, True)
        End Select
    End Sub

    Private Function AñadirSecundarioChild(Help_Title As String, cIndice As Integer) As Integer
        Dim Indice As Integer
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then Indice = Cuenta : Exit For
        Next
        Dim NewItem As New NSListViewHLP.NSListViewItem : Indice += 1
        Select Case Help_Title
            Case "Ejecutar Aplicación"
                Indice = AñadirSecundarioChild_CreateItem("Ruta de la Aplicación", "Ruta de la Aplicación", Indice)
                Indice = AñadirSecundarioChild_CreateItem("Opciones de la Aplicación", "Opciones de la Aplicación", Indice)
                Return Indice
        End Select : Return cIndice
    End Function

#Region " Eliminar "

    Private Sub EliminarSecundario(Help_Title As String)
        Dim Indice As Integer
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then Indice = Cuenta : Exit For
        Next : If ListaAyuda.Items(Indice).Principal = False Then Exit Sub
        Indice += 1
        For Cuenta = Indice To ListaAyuda.Items.Count - 1
            If Indice < ListaAyuda.Items.Count AndAlso ListaAyuda.Items(Indice).Principal = False Then ListaAyuda.RemoveItemAt(Indice)
        Next
    End Sub

    Private Sub EliminarSecundarioChild(Help_Title As String)
        Dim Indice As Integer
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then Indice = Cuenta : Exit For
        Next
        Indice += 1
        For Cuenta = Indice To ListaAyuda.Items.Count - 1
            If Indice < ListaAyuda.Items.Count AndAlso ListaAyuda.Items(Indice).Principal = False AndAlso ListaAyuda.Items(Indice).Secundario = False Then ListaAyuda.RemoveItemAt(Indice)
        Next
    End Sub

#End Region

#Region " Funciones "

    Private Function AñadirSecundario_CreateItem(Help_Title As String, Title_Tag As String, Secundario As Boolean, Indice As Integer, Optional IgnorarIcono As Boolean = False) As Integer
        Dim NewItem As New NSListViewHLP.NSListViewItem
        NewItem.Text = Help_Title : NewItem.Tag = Title_Tag : NewItem.Secundario = Secundario : NewItem.IgnorarIcono = IgnorarIcono : If OpenedList.Contains(NewItem.Tag) Then NewItem.Opened = True
        Select Case Title_Tag
            'Introducción
            Case "Historía de KDesktop" : NewItem.Imagen = My.Resources.Ayuda_HistoriaKDesktop : NewItem.ImagenPNG = True
            Case "Comandos y Acciones" : NewItem.Imagen = My.Resources.Ayuda_ComandosAcciones : NewItem.ImagenPNG = True
            'Comandos
            Case "Crear y Eliminar" : NewItem.Imagen = My.Resources.Ayuda_CrearComando : NewItem.ImagenPNG = True
            Case "Opciones del Comando (1)" : NewItem.Imagen = My.Resources.Ayuda_OpcionesComandos : NewItem.ImagenPNG = True
            Case "Opciones del Comando (2)" : NewItem.Imagen = My.Resources.Ayuda_OpcionesComandos : NewItem.ImagenPNG = True
            Case "Comandos Remotos (UDP)" : NewItem.Imagen = My.Resources.Ayuda_ComandosRemotos : NewItem.ImagenPNG = True
            'Acciones
            Case "Ejecutar Aplicación" : NewItem.Imagen = My.Resources.Accion_Aplicacion.ToBitmap
            Case "Abrir Carpeta" : NewItem.Imagen = My.Resources.Accion_AbrirCarpeta.ToBitmap
            Case "Página Web" : NewItem.Imagen = My.Resources.WebPage.ToBitmap
            Case "Aplicaciones" : NewItem.Imagen = My.Resources.Accion_Aplicaciones.ToBitmap
            Case "Ejecutar BAT" : NewItem.Imagen = My.Resources.Accion_BAT.ToBitmap
                'Audio y Video
            Case "Capturar Pantalla" : NewItem.Imagen = My.Resources.Accion_Capturar_Pantalla.ToBitmap
            Case "Grabar Pantalla" : NewItem.Imagen = My.Resources.Accion_Grabar_Pantalla.ToBitmap
            Case "Grabar Audio" : NewItem.Imagen = My.Resources.Accion_Grabar_Audio.ToBitmap
            Case "Reproducir Sonido" : NewItem.Imagen = My.Resources.Accion_ReproducirSonido.ToBitmap
            Case "Salida de Audio" : NewItem.Imagen = My.Resources.Accion_Salida_Audio.ToBitmap
            Case "Volumen" : NewItem.Imagen = My.Resources.Accion_Volumen.ToBitmap
                'Utilidades
            Case "Macros" : NewItem.Imagen = My.Resources.Accion_Macros.ToBitmap
            Case "Accion K-Board" : NewItem.Imagen = My.Resources.Accion_K_Board.ToBitmap
            Case "Accion QuickMenu" : NewItem.Imagen = My.Resources.Accion_QuickMenu.ToBitmap
            Case "Red Ad-Hoc" : NewItem.Imagen = My.Resources.Accion_Ad_Hoc.ToBitmap
                'Acciones
            Case "Enviar Pulsación" : NewItem.Imagen = My.Resources.Accion_EnviarPulsacion.ToBitmap
            Case "TopMost" : NewItem.Imagen = My.Resources.Accion_TopMost.ToBitmap
            Case "Archivos Ocultos" : NewItem.Imagen = My.Resources.Accion_Archivos_Ocultos.ToBitmap
            Case "Desactivar en" : NewItem.Imagen = My.Resources.Accion_Desactivar.ToBitmap
            Case "Mostrar Mensaje" : NewItem.Imagen = My.Resources.Accion_Mensaje.ToBitmap
            Case "Apagar Equipo" : NewItem.Imagen = My.Resources.Accion_Apagar_Equipo.ToBitmap
            Case "Accion Comandos" : NewItem.Imagen = My.Resources.Accion_Comandos.ToBitmap
            Case "Esperar" : NewItem.Imagen = My.Resources.Accion_Esperar.ToBitmap
            'K-Board

            'QuickMenu
            Case "Crear un QuickMenu" : NewItem.Imagen = My.Resources.Ayuda_CrearQuickMenu : NewItem.ImagenPNG = True
            Case "Crear un Item (1)" : NewItem.Imagen = My.Resources.Ayuda_CrearItem : NewItem.ImagenPNG = True
            Case "Crear un Item (2)" : NewItem.Imagen = My.Resources.Ayuda_CrearItem : NewItem.ImagenPNG = True
            Case "Editar y Eliminar un Item" : NewItem.Imagen = My.Resources.Ayuda_EditarItem : NewItem.ImagenPNG = True
            Case "Comando Rápido" : NewItem.Imagen = My.Resources.Ayuda_ComandosRapidos : NewItem.ImagenPNG = True
            'Opciones
            Case "Iconos de KDesktop" : NewItem.Imagen = My.Resources.Ayuda_IconosKDesktop : NewItem.ImagenPNG = True
            Case "Licencia de KDesktop" : NewItem.Imagen = My.Resources.Ayuda_LicenciaKDesktop : NewItem.ImagenPNG = True
        End Select
        ListaAyuda.InsertItemAt(Indice, NewItem) : Indice += 1
        If NewItem.Secundario = True AndAlso OpenedList.Contains(NewItem.Tag) = True Then Indice = AñadirSecundarioChild(NewItem.Tag, Indice)
        Return Indice
    End Function

    Private Function AñadirSecundarioChild_CreateItem(Help_Title As String, Title_Tag As String, Indice As Integer) As Integer
        Dim NewItem As New NSListViewHLP.NSListViewItem
        NewItem.Text = Help_Title : NewItem.Tag = Title_Tag : NewItem.SecundarioChild = True : If OpenedList.Contains(NewItem.Tag) Then NewItem.Opened = True
        ListaAyuda.InsertItemAt(Indice, NewItem) : Indice += 1
        Return Indice
    End Function

#End Region

#End Region

#End Region

#Region " Show Help "

    Public Sub Show_Help(Help_Title As String, Optional Force As Boolean = False)
        If Help_Title = "Next" Then
            Dim Indice As Integer = ListaAyuda.SelectedIndex(0)
            If ListaAyuda.Items(Indice).Principal = True AndAlso ListaAyuda.Items(Indice).Opened = False Then
                ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag)
                AñadirSecundario(ListaAyuda.Items(Indice).Text)
                ListaAyuda.SelectItem(Indice + 1) : ListaAyuda.EnsureVisibleAt(Indice + 1)
            ElseIf ListaAyuda.Items(Indice).Secundario = True AndAlso ListaAyuda.Items(Indice).Opened = False AndAlso ListaAyuda.Items(Indice).IgnorarIcono = False Then
                ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag)
                AñadirSecundarioChild(ListaAyuda.Items(Indice).Text, Indice)
                ListaAyuda.SelectItem(Indice + 1) : ListaAyuda.EnsureVisibleAt(Indice + 1)
            Else
                ListaAyuda.SelectItem(Indice + 1)
                ListaAyuda.EnsureVisibleAt(Indice + 1)
            End If : Exit Sub
        End If
        If Help_Title = "Prior" Then
            Dim Indice As Integer = ListaAyuda.SelectedIndex(0) - 1
            If ListaAyuda.Items(Indice).Principal = True AndAlso ListaAyuda.Items(Indice).Opened = False Then
                ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag)
                AñadirSecundario(ListaAyuda.Items(Indice).Text)
                For Cuenta = 0 To ListaAyuda.Items.Count - 1
                    If ListaAyuda.Items(Cuenta).Text = Current_Help Then Indice = Cuenta - 1 : Exit For
                Next
                If ListaAyuda.Items(Indice).Secundario = True AndAlso ListaAyuda.Items(Indice).Opened = False AndAlso ListaAyuda.Items(Indice).IgnorarIcono = False Then
                    ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag)
                    AñadirSecundarioChild(ListaAyuda.Items(Indice).Text, Indice)
                End If
                For Cuenta = 0 To ListaAyuda.Items.Count - 1
                    If ListaAyuda.Items(Cuenta).Text = Current_Help Then ListaAyuda.SelectItem(Cuenta - 1) : ListaAyuda.EnsureVisibleAt(Cuenta - 1) : Exit For
                Next
            ElseIf ListaAyuda.Items(Indice).Secundario = True AndAlso ListaAyuda.Items(Indice).Opened = False AndAlso ListaAyuda.Items(Indice).IgnorarIcono = False Then
                ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Tag)
                AñadirSecundarioChild(ListaAyuda.Items(Indice).Text, Indice)
                For Cuenta = 0 To ListaAyuda.Items.Count - 1
                    If ListaAyuda.Items(Cuenta).Text = Current_Help Then ListaAyuda.SelectItem(Cuenta - 1) : ListaAyuda.EnsureVisibleAt(Cuenta - 1) : Exit For
                Next
            Else
                ListaAyuda.SelectItem(Indice)
                ListaAyuda.EnsureVisibleAt(Indice)
            End If : Exit Sub
        End If
        Expandir_SubItem(Help_Title)
        If Force = False AndAlso Help_Title = Current_Help Then Exit Sub
        OcultarPaneles()
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then LTitulo.Value1 = ListaAyuda.Items(Cuenta).Text.Substring(0, 1) : LTitulo.Value2 = ListaAyuda.Items(Cuenta).Text.Substring(1) : Exit For
        Next
        Select Case Help_Title
            'Introduccion
            Case "Introducción" : PIntroduccion.Visible = True
            Case "Historía de KDesktop" : PIntroduccion_Historia.Visible = True
            Case "Comandos y Acciones" : PIntroduccion_ComandosYAcciones.Visible = True
            'Comandos
            Case "Comandos" : PComandos.Visible = True
            Case "Crear y Eliminar" : PComandos_CrearComandos.Visible = True
            Case "Opciones del Comando (1)" : PComandos_OpcionesComando1.Visible = True
            Case "Opciones del Comando (2)" : PComandos_OpcionesComando2.Visible = True
            Case "Comandos Remotos (UDP)" : PComandos_ComandosRemotos.Visible = True
            'Acciones
            Case "Acciones" : PAcciones.Visible = True
                 'Aplicaciones
            Case "Ejecutar Aplicación" : PAcciones_EjecutarAplicacion.Visible = True
            Case "Abrir Carpeta" : PAcciones_AbrirCarpeta.Visible = True
            Case "Página Web" : PAcciones_PaginaWeb.Visible = True
            Case "Aplicaciones" : PAcciones_Aplicaciones.Visible = True
            Case "Ejecutar BAT" : PAcciones_EjecutarBAT.Visible = True
                 'Audio y Video
            Case "Capturar Pantalla" : PAcciones_CapturarPantalla.Visible = True
            Case "Grabar Pantalla" : PAcciones_GrabarPantalla.Visible = True
            Case "Grabar Audio" : PAcciones_GrabarAudio.Visible = True
            Case "Reproducir Sonido" : PAcciones_ReproducirSonido.Visible = True
            Case "Salida de Audio" : PAcciones_SalidaAudio.Visible = True
            Case "Volumen" : PAcciones_Volumen.Visible = True
                 'Utilidades
            Case "Macros" : PAcciones_Macros.Visible = True
            Case "K-Note" ': PAcciones_KNote.Visible = True
            Case "Accion K-Board" : PAcciones_KBoard.Visible = True
            Case "Accion QuickMenu" : PAcciones_QuickMenu.Visible = True
            Case "Red Ad-Hoc" : PAcciones_RedAdHoc.Visible = True
                 'Acciones
            Case "Enviar Pulsación" : PAcciones_EnviarPulsacion.Visible = True
            Case "TopMost" : PAcciones_TopMost.Visible = True
            Case "Archivos Ocultos" : PAcciones_ArchivosOcultos.Visible = True
            Case "Desactivar en" : PAcciones_DesactivarEn.Visible = True
            Case "Mostrar Mensaje" : PAcciones_MostrarMensaje.Visible = True
            Case "Apagar Equipo" : PAcciones_ApagarEquipo.Visible = True
            Case "Accion Comandos" : PAcciones_Comandos.Visible = True
            Case "Esperar" : PAcciones_Esperar.Visible = True
            'K-Board
            Case "K-Board" : PKBoard.Visible = True
            'QuickMenu
            Case "QuickMenu" : PQuickMenu.Visible = True
            Case "Crear un QuickMenu" : PQuickMenu_CrearQuickMenu.Visible = True
            Case "Crear un Item (1)" : PQuickMenu_CrearItem.Visible = True
            Case "Crear un Item (2)" : PQuickMenu_CrearItem2.Visible = True
            Case "Editar y Eliminar un Item" : PQuickMenu_EditarEliminar.Visible = True
            Case "Comando Rápido" : PQuickMenu_ComandoRapido.Visible = True
            'Opciones
            Case "Opciones" : POpciones.Visible = True
            Case "Iconos de KDesktop" : POpciones_IconosKDesktop.Visible = True
            Case "Licencia de KDesktop" : POpciones_Licencia.Visible = True
        End Select

        Current_Help = Help_Title
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then ListaAyuda.SelectItem(Cuenta) : ListaAyuda.EnsureVisibleAt(Cuenta) : Exit For
        Next
        If Current_Help = ListaAyuda.Items(0).Tag Then PLeft.Visible = False Else PLeft.Visible = True
        If ListaAyuda.SelectedIndex(0) = ListaAyuda.Items.Count - 1 Then
            If (ListaAyuda.SelectedItems(0).Principal = False AndAlso ListaAyuda.SelectedItems(0).Secundario = False) Or ListaAyuda.SelectedItems(0).IgnorarIcono = True Then PRight.Visible = False Else PRight.Visible = True
        Else
            PRight.Visible = True
        End If
    End Sub

    Private Sub OcultarPaneles()
        'Introduccion
        PIntroduccion.Visible = False
        PIntroduccion_Historia.Visible = False
        PIntroduccion_ComandosYAcciones.Visible = False
        'Comandos
        PComandos.Visible = False
        PComandos_CrearComandos.Visible = False
        PComandos_OpcionesComando1.Visible = False
        PComandos_OpcionesComando2.Visible = False
        PComandos_ComandosRemotos.Visible = False
        'Acciones
        PAcciones.Visible = False
        '    Aplicaciones
        PAcciones_EjecutarAplicacion.Visible = False
        PAcciones_AbrirCarpeta.Visible = False
        PAcciones_PaginaWeb.Visible = False
        PAcciones_Aplicaciones.Visible = False
        PAcciones_EjecutarBAT.Visible = False
        '    Multimedia
        PAcciones_CapturarPantalla.Visible = False
        PAcciones_GrabarPantalla.Visible = False
        PAcciones_GrabarAudio.Visible = False
        PAcciones_ReproducirSonido.Visible = False
        PAcciones_SalidaAudio.Visible = False
        PAcciones_Volumen.Visible = False
        '    Utilidades
        PAcciones_Macros.Visible = False
        'PAcciones_KNote.Visible = False
        PAcciones_KBoard.Visible = False
        PAcciones_QuickMenu.Visible = False
        PAcciones_RedAdHoc.Visible = False
        '    Acciones
        PAcciones_EnviarPulsacion.Visible = False
        PAcciones_TopMost.Visible = False
        PAcciones_ArchivosOcultos.Visible = False
        PAcciones_DesactivarEn.Visible = False
        PAcciones_MostrarMensaje.Visible = False
        PAcciones_ApagarEquipo.Visible = False
        PAcciones_Comandos.Visible = False
        PAcciones_Esperar.Visible = False
        'K-Board
        PKBoard.Visible = False
        'QuickMenu
        PQuickMenu.Visible = False
        PQuickMenu_CrearQuickMenu.Visible = False
        PQuickMenu_CrearItem.Visible = False
        PQuickMenu_CrearItem2.Visible = False
        PQuickMenu_EditarEliminar.Visible = False
        PQuickMenu_ComandoRapido.Visible = False
        'Opciones
        POpciones.Visible = False
        POpciones_IconosKDesktop.Visible = False
        POpciones_Licencia.Visible = False
    End Sub

    Private Sub Expandir_Item(Help_Title As String)
        Dim Indice As Integer
        For Cuenta = 0 To ListaAyuda.Items.Count - 1
            If ListaAyuda.Items(Cuenta).Tag = Help_Title Then Indice = Cuenta : Exit For
        Next
        If ListaAyuda.Items(Indice).Principal = True AndAlso ListaAyuda.Items(Indice).Opened = False Then
            ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Text)
            AñadirSecundario(ListaAyuda.Items(Indice).Text)
        ElseIf ListaAyuda.Items(Indice).Secundario = True AndAlso ListaAyuda.Items(Indice).Opened = False AndAlso ListaAyuda.Items(Indice).IgnorarIcono = False Then
            ListaAyuda.Items(Indice).Opened = True : OpenedList.Add(ListaAyuda.Items(Indice).Text)
            AñadirSecundarioChild(ListaAyuda.Items(Indice).Text, Indice)
        End If
    End Sub

    Private Sub ContraerTodo()
        ListaAyuda.Clear()
        ListaAyuda.AddItem("Introducción", "Introducción") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
        ListaAyuda.AddItem("Comandos", "Comandos") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
        ListaAyuda.AddItem("Acciones", "Acciones") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
        ListaAyuda.AddItem("K-Board", "K-Board") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
        ListaAyuda.AddItem("QuickMenu", "QuickMenu") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
        ListaAyuda.AddItem("Opciones", "Opciones") : ListaAyuda.Items(ListaAyuda.Items.Count - 1).Principal = True
    End Sub

    Private Sub Expandir_SubItem(SubItem As String)
        ContraerTodo()
        Select Case SubItem
            'Introduccion
            Case "Introducción" : Expandir_Item(SubItem)
            Case "Historía de KDesktop" : Expandir_Item("Introducción") : Expandir_Item(SubItem)
            Case "Comandos y Acciones" : Expandir_Item("Introducción") : Expandir_Item(SubItem)
            'Comandos
            Case "Comandos" : Expandir_Item(SubItem)
            Case "Crear y Eliminar" : Expandir_Item("Comandos") : Expandir_Item(SubItem)
            Case "Opciones del Comando (1)" : Expandir_Item("Comandos") : Expandir_Item(SubItem)
            Case "Opciones del Comando (2)" : Expandir_Item("Comandos") : Expandir_Item(SubItem)
            Case "Comandos Remotos (UDP)" : Expandir_Item("Comandos") : Expandir_Item(SubItem)
            'Acciones
            Case "Acciones" : Expandir_Item(SubItem)
                 'Aplicaciones
            Case "Ejecutar Aplicación" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Página Web" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Abrir Carpeta" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Aplicaciones" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Ejecutar BAT" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
                 'Audio y Video
            Case "Capturar Pantalla" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Grabar Pantalla" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Grabar Audio" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Salida de Audio" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Reproducir Sonido" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Volumen" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
                 'Utilidades
            Case "Red Ad-Hoc" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Accion K-Board" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "K-Note" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Macros" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Accion QuickMenu" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
                 'Acciones
            Case "Enviar Pulsación" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "TopMost" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Internet" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Archivos Ocultos" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Desactivar en" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Esperar" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Mostrar Mensaje" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Apagar Equipo" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            Case "Accion Comandos" : Expandir_Item("Acciones") : Expandir_Item(SubItem)
            'K-Board
            Case "K-Board" : Expandir_Item(SubItem)
            'QuickMenu
            Case "QuickMenu" : Expandir_Item(SubItem)
            Case "Crear un QuickMenu" : Expandir_Item("QuickMenu") : Expandir_Item(SubItem)
            Case "Crear un Item (1)" : Expandir_Item("QuickMenu") : Expandir_Item(SubItem)
            Case "Crear un Item (2)" : Expandir_Item("QuickMenu") : Expandir_Item(SubItem)
            Case "Editar y Eliminar un Item" : Expandir_Item("QuickMenu") : Expandir_Item(SubItem)
            Case "Comando Rápido" : Expandir_Item("QuickMenu") : Expandir_Item(SubItem)
            'Opciones
            Case "Opciones" : Expandir_Item(SubItem)
            Case "Iconos de KDesktop" : Expandir_Item("Opciones") : Expandir_Item(SubItem)
            Case "Licencia de KDesktop" : Expandir_Item("Opciones") : Expandir_Item(SubItem)
        End Select
    End Sub

    Private Sub Comandos_OpcionesComando1_NsLabel5_Click(sender As Object, e As MouseEventArgs) Handles Comandos_OpcionesComando1_NsLabel5.Click, Comandos_OpcionesComando1_NsLabel5.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Opciones del Comando (2)")
        Show_Help("Opciones del Comando (2)")
    End Sub

    Private Sub QuickMenu_CrearItem_NsLabel10_Click(sender As Object, e As MouseEventArgs) Handles QuickMenu_CrearItem_NsLabel10.Click, QuickMenu_CrearItem_NsLabel10.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Crear un Item (2)")
        Show_Help("Crear un Item (2)")
    End Sub

    Private Sub QuickMenu_CrearItem2_eLabel1_Click(sender As Object, e As MouseEventArgs) Handles QuickMenu_CrearItem2_eLabel1.Click, QuickMenu_CrearItem2_eLabel1.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Comando Rápido")
        Show_Help("Comando Rápido")
    End Sub

    Private Sub QuickMenu_ComandoRapido_eLabel1_Click(sender As Object, e As MouseEventArgs) Handles QuickMenu_ComandoRapido_eLabel1.Click, QuickMenu_ComandoRapido_eLabel1.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Iconos de KDesktop")
        Show_Help("Iconos de KDesktop")
    End Sub

    Private Sub POpciones_eLabel1_Click(sender As Object, e As MouseEventArgs) Handles POpciones_eLabel1.Click, POpciones_eLabel1.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Comandos Remotos (UDP)")
        Show_Help("Comandos Remotos (UDP)")
    End Sub

    Private Sub POpciones_eLabel2_Click(sender As Object, e As MouseEventArgs) Handles POpciones_eLabel2.Click, POpciones_eLabel2.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Licencia de KDeskto")
        Show_Help("Licencia de KDesktop")
    End Sub

    Private Sub Acciones_Macros_eLabel_Click(sender As Object, e As MouseEventArgs) Handles Acciones_Macros_eLabel1.Click, Acciones_Macros_eLabel1.DoubleClick, Acciones_Macros_eLabel2.Click, Acciones_Macros_eLabel2.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Expandir_SubItem("Enviar Pulsación")
        Show_Help("Enviar Pulsación")
    End Sub

    Private Sub Comandos_ComandosRemotos_eLabel1_Click(sender As Object, e As MouseEventArgs) Handles Comandos_ComandosRemotos_eLabel1.Click, Comandos_ComandosRemotos_eLabel1.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        Dim GuardarDir As New FolderBrowserDialog With {.RootFolder = Environment.SpecialFolder.Desktop, .SelectedPath = FMenu.GetDownloadsPath, .Description = "UDP Terminal 1.4"}
        If GuardarDir.ShowDialog <> DialogResult.Cancel Then IO.File.WriteAllBytes((GuardarDir.SelectedPath + "\UDP Terminal 1.4.apk").Replace("\\", "\"), My.Resources.UDP_Terminal_1_4)
    End Sub

#End Region

End Class

#End Region