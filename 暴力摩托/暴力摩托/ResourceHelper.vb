Imports System.IO
Imports System.Reflection
Imports System.Threading

Public Module ResourceHelper
    ''' <summary>把嵌入的英文 ENU.DLL 释放到游戏目录 TEXT 下（幂等）</summary>
    ''' <returns>成功返回 True，失败返回 False</returns>
    Public Function RestoreEnglishDll(gameDir As String) As Boolean
        Try
            Dim asm = Assembly.GetExecutingAssembly()
            Dim resName = "暴力摩托.ENU.DLL"
            Dim stream = asm.GetManifestResourceStream(resName)

            If stream Is Nothing Then
                Dim fileName = "ENU.DLL"
                resName = asm.GetManifestResourceNames().
                FirstOrDefault(Function(n) n.EndsWith("." & fileName, StringComparison.OrdinalIgnoreCase))
                If resName IsNot Nothing Then
                    stream = asm.GetManifestResourceStream(resName)
                End If
            End If

            If stream Is Nothing Then Return False

            Dim textDir = Path.Combine(gameDir, "TEXT")
            If Not Directory.Exists(textDir) Then Directory.CreateDirectory(textDir)

            Dim dest = Path.Combine(textDir, "ENU.DLL")

            Using stream
                Using output = File.Create(dest)
                    stream.CopyTo(output)
                End Using
            End Using

            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>结束 RoadRash95 进程；优先按 PID，回退按进程名</summary>
    Public Sub KillGameProcess(procName As String, Optional pid As Integer = 0)
        If pid > 0 Then
            Try
                Dim p = Process.GetProcessById(pid)
                p.Kill()
                p.WaitForExit(5000)
                Return
            Catch
            End Try
        End If

        Try
            For Each p In Process.GetProcessesByName(procName)
                Try
                    p.Kill()
                    p.WaitForExit(5000)
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub
End Module