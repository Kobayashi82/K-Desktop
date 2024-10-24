
#Region " Identificar Pantalla "

Public Class FIdentificarPantalla

    Public NumberToDraw As Integer = 0
    Private pfc As New Text.PrivateFontCollection
    Private cTimer As New Timer With {.Interval = 3000, .Enabled = True}

    Private Sub FIdentificarPantalla_Load(sender As Object, e As EventArgs) Handles Me.Load
        AddHandler cTimer.Tick, AddressOf cTimer_Tick
        Try : Dim fontMemPointer As IntPtr = Runtime.InteropServices.Marshal.AllocCoTaskMem(My.Resources.Arialic_Hollow.Length)
            Runtime.InteropServices.Marshal.Copy(My.Resources.Arialic_Hollow, 0, fontMemPointer, My.Resources.Arialic_Hollow.Length)
            pfc.AddMemoryFont(fontMemPointer, My.Resources.Arialic_Hollow.Length)
            Runtime.InteropServices.Marshal.FreeCoTaskMem(fontMemPointer)
        Catch : End Try
    End Sub

    Private Sub CTimer_Tick(sender As Object, e As EventArgs)
        Close()
    End Sub

    Private Sub FIdentificarPantalla_Paint(sender As Object, e As PaintEventArgs) Handles Me.Paint
        e.Graphics.DrawString(NumberToDraw.ToString, New Font(pfc.Families(0), 600, FontStyle.Regular), Brushes.White, e.ClipRectangle, New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center})
    End Sub

End Class

#End Region