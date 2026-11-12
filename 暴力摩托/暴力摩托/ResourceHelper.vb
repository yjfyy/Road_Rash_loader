Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography

Public Module ResourceHelper
    ''' <summary>把嵌入的英文 ENU.DLL 释放到游戏目录 TEXT 下（幂等）</summary>
    ''' <returns>成功返回 True，失败返回 False</returns>
    Public Function WriteLanguageDll(gameDir As String, resName As String) As Boolean
        Try
            Dim asm = Assembly.GetExecutingAssembly()
            Using stream = asm.GetManifestResourceStream(resName)
                If stream Is Nothing Then Return False

                Dim textDir = Path.Combine(gameDir, "TEXT")
                If Not Directory.Exists(textDir) Then Return False

                Dim dest = Path.Combine(textDir, "ENU.DLL")
                Using output = File.Create(dest)
                    stream.CopyTo(output)
                End Using

                ' 把 ENU.DLL 的时间戳改成和 ESP.DLL 一致
                Try
                    Dim refFile = Path.Combine(textDir, "ESP.DLL")
                    If File.Exists(refFile) Then
                        File.SetCreationTimeUtc(dest, File.GetCreationTimeUtc(refFile))
                        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(refFile))
                        File.SetLastAccessTimeUtc(dest, File.GetLastAccessTimeUtc(refFile))
                    End If
                Catch
                    ' 时间戳设置失败不影响主流程
                End Try

                Return True
            End Using
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