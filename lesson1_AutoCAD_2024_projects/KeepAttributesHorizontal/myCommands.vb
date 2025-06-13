Imports Autodesk.AutoCAD.Runtime
Imports Autodesk.AutoCAD.ApplicationServices
Imports Autodesk.AutoCAD.EditorInput

Public Class MyCropCommand
    <CommandMethod("SayHello")>
    Public Sub Hello()
        Dim doc As Document = Application.DocumentManager.MdiActiveDocument
        Dim ed As Editor = doc.Editor
        ed.WriteMessage(vbLf & "Hello from your VB.NET plugin!")
    End Sub
End Class
