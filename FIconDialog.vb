
#Region " Icon Dialog "

Public Class FIconDialog

#Region " Variables "

    Public Icono As Icon = Nothing
    Public Cancelado As Boolean = True
    Dim ImageListSmall, ImageListLarge As ImageList

#End Region

#Region " Formulario "

    Private Sub FIconDialog_Load(sender As Object, e As EventArgs) Handles Me.Load
        Opacity = 0 : Location = New Point((Screen.FromControl(FMenu).Bounds.Width - Width) / 2 + Screen.FromControl(FMenu).Bounds.Left, (Screen.FromControl(FMenu).Bounds.Height - Height) / 2 + Screen.FromControl(FMenu).Bounds.Top)
        PCargando.Visible = True : LCargando.Visible = True : Application.DoEvents()
    End Sub

    Private Sub FIconDialog_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Try : Opacity = 1 : FMenu.FocusMe(Me) : Application.DoEvents()
            IO.Directory.CreateDirectory(FMenu.KobaPathTemp) : If IO.File.Exists(FMenu.KobaPathTemp + "KDesktopRes.dll") = False Then IO.File.WriteAllBytes(FMenu.KobaPathTemp + "KDesktopRes.dll", My.Resources.kdesktopres)
            If IO.File.Exists(FMenu.KobaPathTemp + "KDesktopRes.dll") = True Then ImageListLarge = GetImageList(FMenu.KobaPathTemp + "KDesktopRes.dll")
            Icono = Nothing : ImageListSmall = ImageListLarge : Lista.SmallImageList = ImageListSmall : Lista.LargeImageList = ImageListLarge : Lista.Clear()
            If ImageListSmall IsNot Nothing Then
                For Cuenta = 0 To ImageListSmall.Images.Count - 1
                    If ImageListSmall.Images(Cuenta) IsNot Nothing Then Lista.Items.Add("Icono " + Cuenta.ToString, Cuenta)
                Next
            End If : Lista.SelectedItems.Clear() : If Lista.Items.Count > 0 Then Lista.Items(0).Selected = True
        Catch : End Try : PCargando.Visible = False : LCargando.Visible = False
    End Sub

    Private Sub FIconDialog_KeyPress(sender As Object, e As KeyPressEventArgs) Handles Me.KeyPress
        If Asc(e.KeyChar) = 27 Then e.Handled = True : Close()
    End Sub

#End Region

#Region " Lista "

    Private Sub Lista_SelectedIndexChanged(sender As Object, e As EventArgs) Handles Lista.SelectedIndexChanged
        If Lista.SelectedItems.Count > 0 Then Icono = Icon.FromHandle(CType(ImageListLarge.Images(Lista.SelectedItems(0).ImageIndex), Bitmap).GetHicon)
    End Sub

    Private Sub Lista_DoubleClick(sender As Object, e As EventArgs) Handles Lista.DoubleClick
        If Icono Is Nothing Then Lista.Focus() : Exit Sub
        Cancelado = False : Close()
    End Sub

#Region " GetImageList "

    Public Function GetImageList(FileName As String) As ImageList
        Try : Dim Count As Integer = Iconos.ExtractIconEx(FileName, -1, Nothing, Nothing, 0)
            Dim ImgList As New ImageList With {.ImageSize = New Size(32, 32), .ColorDepth = ColorDepth.Depth32Bit, .TransparentColor = Color.Black}
            If Count > 0 Then
                Dim LargeIcons(Count - 1) As IntPtr : For Cuenta = 0 To LargeIcons.Length - 1
                    Dim MyIcon As Icon = Icon.FromHandle(Iconos.ExtractAssociatedIcon(Handle, FileName, Cuenta))
                    Dim bRes As Boolean = Iconos.DestroyIcon(LargeIcons(Cuenta))
                    ImgList.Images.Add(MyIcon.ToBitmap)
                Next
            End If : If ImgList.Images.Count > 0 Then Return ImgList
        Catch : End Try : Return Nothing
    End Function

#End Region

#End Region

#Region " Botones "

    Private Sub BIconos_Click(sender As Object, e As MouseEventArgs) Handles BIconosWindows.Click, BIconosSistema.Click, BIconosKDesktop.Click
        Try : If e.Button <> MouseButtons.Left Then Exit Sub
            Dim cBoton As NSButton = CType(sender, NSButton)
            IO.Directory.CreateDirectory(FMenu.KobaPathTemp) : If IO.File.Exists(FMenu.KobaPathTemp + "KDesktopRes.dll") = False Then IO.File.WriteAllBytes(FMenu.KobaPathTemp + "KDesktopRes.dll", My.Resources.kdesktopres)
            If cBoton.Name = "BIconosWindows" AndAlso IO.File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\SHELL32.dll") Then ImageListLarge = GetImageList(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\SHELL32.dll")
            If cBoton.Name = "BIconosSistema" AndAlso IO.File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\imageres.dll") Then ImageListLarge = GetImageList(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\imageres.dll")
            If cBoton.Name = "BIconosKDesktop" AndAlso IO.File.Exists(FMenu.KobaPathTemp + "KDesktopRes.dll") = True Then ImageListLarge = GetImageList(FMenu.KobaPathTemp + "KDesktopRes.dll")
            Icono = Nothing : ImageListSmall = ImageListLarge : Lista.SmallImageList = ImageListSmall : Lista.LargeImageList = ImageListLarge : Lista.Clear()
            If ImageListSmall IsNot Nothing Then
                For Cuenta = 0 To ImageListSmall.Images.Count - 1
                    If ImageListSmall.Images(Cuenta) IsNot Nothing Then Lista.Items.Add("Icono " + Cuenta.ToString, Cuenta)
                Next
            End If : Lista.SelectedItems.Clear() : If Lista.Items.Count > 0 Then Lista.Items(0).Selected = True
        Catch : End Try
    End Sub

    Private Sub BExaminar_Click(sender As Object, e As MouseEventArgs) Handles BExaminar.Click
        Try : If e.Button <> MouseButtons.Left Then Exit Sub
            Dim Abrir As New OpenFileDialog : Abrir.CheckFileExists = True : Abrir.CheckPathExists = True
            Abrir.Title = "Seleccionar icono..." : Abrir.Filter = "Archivos de Iconos|*.ico;*.exe;*.dll;*.lnk|Todos los Archivos|*.*"
            If Abrir.ShowDialog <> DialogResult.Cancel Then
                If Abrir.FileName.ToLower.EndsWith(".lnk") Then ImageListLarge = GetImageList(Rutas.GetTargetPath(Abrir.FileName)) Else ImageListLarge = GetImageList(Abrir.FileName)
                Icono = Nothing : ImageListSmall = ImageListLarge : Lista.SmallImageList = ImageListSmall : Lista.LargeImageList = ImageListLarge : Lista.Clear()
                If ImageListSmall IsNot Nothing Then
                    For Cuenta = 0 To ImageListSmall.Images.Count - 1
                        If ImageListSmall.Images(Cuenta) IsNot Nothing Then Lista.Items.Add("Icono " + Cuenta.ToString, Cuenta)
                    Next
                End If
                Lista.SelectedItems.Clear()
                If Lista.Items.Count > 0 Then : Lista.Items(0).Selected = True : Else
                    Try : If Abrir.FileName.ToLower.EndsWith(".lnk") Then Abrir.FileName = Rutas.GetTargetPath(Abrir.FileName)
                        Dim cIcono As Icon = Icon.ExtractAssociatedIcon(Abrir.FileName)
                        If cIcono IsNot Nothing Then
                            ImageListLarge = New ImageList : ImageListSmall = New ImageList
                            ImageListLarge.ImageSize = cIcono.Size : ImageListLarge.ColorDepth = ColorDepth.Depth32Bit
                            ImageListLarge.Images.Clear() : ImageListLarge.Images.Add(cIcono)
                            ImageListSmall = ImageListLarge : Lista.SmallImageList = ImageListSmall : Lista.LargeImageList = ImageListLarge : Lista.Clear()
                            Lista.Items.Add("Icono 0", 0) : Lista.Items(0).Selected = True
                        End If
                    Catch : End Try
                End If
            End If
        Catch : End Try : BExaminar.State = NSButton.MouseState.None : BExaminar.Invalidate()
    End Sub

#End Region

#Region " Aceptar / Cancelar "

    Private Sub BAceptar_Click(sender As Object, e As MouseEventArgs) Handles BAceptar.Click
        If e.Button <> MouseButtons.Left Then Exit Sub
        If Icono IsNot Nothing Then Cancelado = False
        Close()
    End Sub


    Private Sub BCancelar_Click(sender As Object, e As MouseEventArgs) Handles BCancelar.Click
        If e.Button = MouseButtons.Left Then Close()
    End Sub

#End Region

#Region " Help "

    Private Sub LHelp_Click(sender As Object, e As MouseEventArgs) Handles LHelp.Click, LHelp.DoubleClick
        If e.Button <> MouseButtons.Left Then Exit Sub
        FAyuda.Current_Help = "Iconos de KDesktop" : If FAyuda.Visible = False Then FAyuda.Show() Else FAyuda.Show_Help(FAyuda.Current_Help, True) : FAyuda.BringToFront()
    End Sub

    Private Sub LHelp_MouseEnter(sender As Object, e As EventArgs) Handles LHelp.MouseEnter
        LHelp.ForeColor1 = Color.FromArgb(205, 150, 0)
    End Sub

    Private Sub LHelp_MouseLeave(sender As Object, e As EventArgs) Handles LHelp.MouseLeave
        LHelp.ForeColor1 = Color.Gray
    End Sub


#End Region

End Class

#End Region